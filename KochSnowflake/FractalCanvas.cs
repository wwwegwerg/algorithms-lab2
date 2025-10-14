using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace KochSnowflake;

public class FractalCanvas : Control
{
    private List<Point> _world = [];
    private Rect _boundsWorld;

    private double _scale = 1.0;
    private double _minScale = 1.0;
    private Vector _offset;
    private bool _viewInitialized;
    private bool _panning;
    private Point _last;

    private int _iterations = 4;

    private static readonly StyledProperty<int> BaseSidesProperty =
        AvaloniaProperty.Register<FractalCanvas, int>(nameof(BaseSides), 3);

    public int BaseSides
    {
        get => GetValue(BaseSidesProperty);
        set
        {
            var v = Math.Clamp(value, 1, 10);
            if (v == 2) v = 1;
            SetValue(BaseSidesProperty, v);
        }
    }

    private List<Point> _generator = DefaultKochGenerator();

    public event EventHandler? ViewChanged;
    public event EventHandler? IterationsChanged;
    public event EventHandler? GeneratorChanged;

    public static readonly StyledProperty<double> FitPaddingProperty =
        AvaloniaProperty.Register<FractalCanvas, double>(nameof(FitPadding), 16.0);

    public double FitPadding
    {
        get => GetValue(FitPaddingProperty);
        set => SetValue(FitPaddingProperty, value);
    }

    public FractalCanvas()
    {
        Focusable = true;
        ClipToBounds = true;

        this.GetObservable(BoundsProperty).Subscribe(_ =>
        {
            if (Bounds.Width <= 0 || Bounds.Height <= 0) return;

            if (!_viewInitialized)
            {
                FitToView();
                _viewInitialized = true;
            }
            else
            {
                UpdateMinScalePreserveView();
            }

            InvalidateVisual();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        });

        this.GetObservable(FitPaddingProperty).Subscribe(_ =>
        {
            if (!_viewInitialized) return;
            UpdateMinScalePreserveView();
            InvalidateVisual();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        });

        this.GetObservable(BaseSidesProperty).Subscribe(_ =>
        {
            RebuildWorld();
            InvalidateVisual();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        });

        RebuildWorld();

        AttachedToVisualTree += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_viewInitialized) return;
                if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
                FitToView();
                _viewInitialized = true;
                InvalidateVisual();
                ViewChanged?.Invoke(this, EventArgs.Empty);
            }, DispatcherPriority.Background);
        };
    }

    public int Iterations
    {
        get => _iterations;
        set
        {
            var v = Math.Clamp(value, 0, 7);
            if (v == _iterations) return;
            _iterations = v;
            RebuildWorld(preserveView: true);
            IterationsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double ZoomFactor => _minScale > 0 ? _scale / _minScale : 1.0;
    public int VertexCount => _world.Count;
    public int GeneratorSegments => Math.Max(0, _generator.Count - 1);

    public void SetGenerator(List<Point> normalizedGenerator)
    {
        if (normalizedGenerator.Count < 2) return;

        _generator = normalizedGenerator;
        RebuildWorld();
        GeneratorChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetView()
    {
        FitToView();
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public static List<Point> DefaultKochGenerator()
    {
        var h = Math.Sqrt(3) / 6.0;
        return
        [
            new Point(0, 0),
            new Point(1.0 / 3.0, 0),
            new Point(0.5, -h),
            new Point(2.0 / 3.0, 0),
            new Point(1, 0)
        ];
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _panning = true;
        _last = e.GetPosition(this);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _panning = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_panning) return;
        var p = e.GetPosition(this);
        var d = p - _last;
        _offset += new Vector(d.X, d.Y);
        _last = p;
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_world.Count == 0) return;

        var sp = e.GetPosition(this);
        var worldAtCursor = ScreenToWorld(sp);

        var factor = Math.Pow(1.1, e.Delta.Y);
        var newScale = Math.Max(_minScale, _scale * factor);
        if (Math.Abs(newScale - _scale) < 1e-9) return;

        _scale = newScale;
        _offset = new Vector(
            sp.X - worldAtCursor.X * _scale,
            sp.Y + worldAtCursor.Y * _scale
        );

        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);

        ctx.FillRectangle(Brushes.White, new Rect(Bounds.Size));
        if (_world.Count < 2) return;

        if (!_viewInitialized && Bounds.Width > 0 && Bounds.Height > 0)
        {
            FitToView();
            _viewInitialized = true;
        }

        var pen = new Pen(Brushes.Black, 1.0, lineCap: PenLineCap.Round);
        var isClosed = BaseSides > 1;

        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            var p0 = WorldToScreen(_world[0]);
            g.BeginFigure(p0, isFilled: false);

            for (var i = 1; i < _world.Count; i++)
                g.LineTo(WorldToScreen(_world[i]));

            g.EndFigure(isClosed: isClosed);
        }

        ctx.DrawGeometry(null, pen, geo);
    }

    private void RebuildWorld(bool preserveView = false)
    {
        _world = BuildSnowflake(_iterations, BaseSides);
        _boundsWorld = ComputeBounds(_world);

        if (!preserveView || !_viewInitialized)
            FitToView();
        else
            UpdateMinScalePreserveView();

        InvalidateVisual();
    }

    private List<Point> BuildSnowflake(int iterations, int sides)
    {
        if (sides <= 1)
        {
            var a = new Point(1, 0);
            var b = new Point(0, 0);
            var pts = new List<Point>();
            ExpandEdge(a, b, iterations, pts);
            pts.Add(b); // добавляем конец
            return pts;
        }
        else
        {
            var poly = BuildRegularPolygon(sides);
            var gSeg = Math.Max(1, GeneratorSegments);

            var pts = new List<Point>(sides * (int)Math.Pow(gSeg, Math.Max(0, iterations)) + 1);

            for (var i = 0; i < poly.Count; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Count];
                ExpandEdge(a, b, iterations, pts);
            }

            pts.Add(poly[0]); // добавляем конец
            return pts;
        }
    }

    private static List<Point> BuildRegularPolygon(int sides)
    {
        const double R = 1.0; // радиус описанной окружности
        const double start = -Math.PI / 2.0; // вершина вверх
        var list = new List<Point>(sides);
        for (var i = 0; i < sides; i++)
        {
            var ang = start + 2 * Math.PI * i / sides; // CCW
            list.Add(new Point(R * Math.Cos(ang), R * Math.Sin(ang)));
        }

        return list;
    }

    private void ExpandEdge(Point a, Point b, int depth, List<Point> output)
    {
        if (depth == 0)
        {
            output.Add(a);
            return;
        }

        var segs = TransformGeneratorToSegment(a, b, _generator);
        for (var i = 0; i < segs.Count - 1; i++)
            ExpandEdge(segs[i], segs[i + 1], depth - 1, output);
    }

    private static List<Point> TransformGeneratorToSegment(Point a, Point b, IReadOnlyList<Point> gen)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-12) len = 1e-12;
        double cos = dx / len, sin = dy / len;

        var res = new List<Point>(gen.Count);
        foreach (var g in gen)
        {
            var x = g.X * len;
            var y = g.Y * len;
            var xr = x * cos - y * sin;
            var yr = x * sin + y * cos;
            res.Add(new Point(a.X + xr, a.Y + yr));
        }

        return res;
    }

    private void FitToView()
    {
        if (_world.Count == 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var pad = Math.Max(0, FitPadding);
        var vw = Math.Max(1, Bounds.Width - 2 * pad);
        var vh = Math.Max(1, Bounds.Height - 2 * pad);

        _minScale = _scale = Math.Min(vw / _boundsWorld.Width, vh / _boundsWorld.Height);

        var wc = _boundsWorld.Center;
        var sc = new Point(pad + vw / 2.0, pad + vh / 2.0);

        _offset = new Vector(
            sc.X - wc.X * _scale,
            sc.Y + wc.Y * _scale
        );
    }

    private void UpdateMinScalePreserveView()
    {
        if (_world.Count == 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var pad = Math.Max(0, FitPadding);
        var vw = Math.Max(1, Bounds.Width - 2 * pad);
        var vh = Math.Max(1, Bounds.Height - 2 * pad);
        var newMin = Math.Min(vw / _boundsWorld.Width, vh / _boundsWorld.Height);

        var centerScreen = new Point(Bounds.Width / 2.0, Bounds.Height / 2.0);
        var worldAtCenter = ScreenToWorld(centerScreen);

        _minScale = newMin;

        if (!(_scale < _minScale)) return;
        _scale = _minScale;

        _offset = new Vector(
            centerScreen.X - worldAtCenter.X * _scale,
            centerScreen.Y + worldAtCenter.Y * _scale
        );
    }

    private static Rect ComputeBounds(List<Point> pts)
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;

        foreach (var p in pts)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }

        if (double.IsInfinity(minX))
            return new Rect(0, 0, 1, 1);
        return new Rect(minX, minY, Math.Max(1e-9, maxX - minX), Math.Max(1e-9, maxY - minY));
    }

    // переворачиваем ось Y т.к. в avalonia она смотрит вниз
    private Point WorldToScreen(Point w) => new(
        w.X * _scale + _offset.X,
        -w.Y * _scale + _offset.Y
    );

    private Point ScreenToWorld(Point s) => new(
        (s.X - _offset.X) / _scale,
        -(s.Y - _offset.Y) / _scale
    );
}