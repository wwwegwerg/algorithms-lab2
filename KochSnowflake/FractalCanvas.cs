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

    private double _scale = 1.0; // текущий масштаб (world -> screen)
    private double _minScale = 1.0;
    private Vector _offset; // смещение в экранных координатах

    private bool _panning;
    private Point _last;

    private int _iterations = 4;
    private bool _viewInitialized; // уже выполнялся первичный fit

    public event EventHandler? ViewChanged;
    public event EventHandler? IterationsChanged;

    // Внутренний отступ при вписывании
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

        // Пересчёт minScale/offset при изменении размеров и padding — БЕЗ сброса вида
        this.GetObservable(BoundsProperty).Subscribe(_ =>
        {
            // Если размеров ещё нет — ничего не делаем
            if (Bounds.Width <= 0 || Bounds.Height <= 0) return;

            if (!_viewInitialized)
            {
                // Первый раз — полноценное вписывание
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
            if (!_viewInitialized)
                // Паддинг поменяли до инициализации — отложим fit
                return;

            UpdateMinScalePreserveView();
            InvalidateVisual();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        });

        Regenerate();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Гарантированный fit ПОСЛЕ того, как лэйаут посчитает размеры
        Dispatcher.UIThread.Post(() =>
        {
            if (_viewInitialized) return;
            if (Bounds.Width <= 0 || Bounds.Height <= 0) return;

            FitToView();
            _viewInitialized = true;
            InvalidateVisual();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }, DispatcherPriority.Background);
    }

    public int Iterations
    {
        get => _iterations;
        set
        {
            var v = Math.Clamp(value, 0, 7);
            if (v == _iterations) return;
            _iterations = v;
            Regenerate(); // пересчитать геометрию (вид не сбрасываем)
            IterationsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Zoom = 1.0 соответствует виду «вся снежинка вписана с учётом FitPadding».</summary>
    public double ZoomFactor => _minScale > 0 ? _scale / _minScale : 1.0;

    public int VertexCount => _world.Count - 1;

    public void ResetView()
    {
        FitToView();
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
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

        // Мировая точка под курсором ДО зума
        var worldAtCursor = ScreenToWorld(sp);

        // Новый масштаб (не меньше минимального)
        var factor = Math.Pow(1.1, e.Delta.Y);
        var newScale = Math.Max(_minScale, _scale * factor);
        if (Math.Abs(newScale - _scale) < 1e-9) return;

        _scale = newScale;

        // Та же мировая точка остаётся под курсором ПОСЛЕ зума
        _offset = new Vector(
            sp.X - worldAtCursor.X * _scale,
            sp.Y - worldAtCursor.Y * _scale
        );

        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);

        // Фон
        ctx.FillRectangle(Brushes.White, new Rect(Bounds.Size));

        if (_world.Count < 2) return;

        // Если по каким-то причинам первичный fit ещё не выполнен, сделаем подстраховку
        if (!_viewInitialized && Bounds.Width > 0 && Bounds.Height > 0)
        {
            FitToView();
            _viewInitialized = true;
        }

        // Чёрная линия, ~1px
        var pen = new Pen(Brushes.Black, 1.0, lineCap: PenLineCap.Round);

        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            var p0 = WorldToScreen(_world[0]);
            g.BeginFigure(p0, false);

            for (var i = 1; i < _world.Count; i++)
                g.LineTo(WorldToScreen(_world[i]));

            g.EndFigure(true);
        }

        ctx.DrawGeometry(null, pen, geo);
    }

    private void Regenerate()
    {
        _world = KochSnowflake.Generate(_iterations);
        _boundsWorld = ComputeBounds(_world);

        // Вид не трогаем; пересчитаем только минимальный масштаб и при необходимости
        // мягко подтянем текущий масштаб, сохранив точку в центре экрана.
        UpdateMinScalePreserveView();

        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void FitToView()
    {
        if (_world.Count == 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var pad = Math.Max(0, FitPadding);
        var vw = Math.Max(1, Bounds.Width - 2 * pad);
        var vh = Math.Max(1, Bounds.Height - 2 * pad);

        _minScale = _scale = Math.Min(vw / _boundsWorld.Width, vh / _boundsWorld.Height);

        _offset = new Vector(
            pad + (vw - _boundsWorld.Width * _scale) / 2.0 - _boundsWorld.X * _scale,
            pad + (vh - _boundsWorld.Height * _scale) / 2.0 - _boundsWorld.Y * _scale
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

        // Если первый раз — нормальный fit
        if (!_viewInitialized)
        {
            FitToView();
            _viewInitialized = true;
            return;
        }

        // Сохраним мировую точку в центре экрана
        var centerScreen = new Point(Bounds.Width / 2.0, Bounds.Height / 2.0);
        var worldAtCenter = ScreenToWorld(centerScreen);

        _minScale = newMin;

        if (!(_scale < _minScale)) return;
        _scale = _minScale;
        _offset = centerScreen - new Vector(worldAtCenter.X * _scale, worldAtCenter.Y * _scale);
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

        if (double.IsInfinity(minX)) return new Rect(0, 0, 1, 1);
        return new Rect(minX, minY, Math.Max(1e-9, maxX - minX), Math.Max(1e-9, maxY - minY));
    }

    private Point WorldToScreen(Point w)
    {
        return new Point(w.X * _scale + _offset.X, w.Y * _scale + _offset.Y);
    }

    private Point ScreenToWorld(Point s)
    {
        return new Point((s.X - _offset.X) / _scale, (s.Y - _offset.Y) / _scale);
    }
}