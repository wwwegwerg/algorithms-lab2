using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Interactivity;
using Avalonia.Styling;

namespace towers.Views;

public partial class GameView : UserControl
{
    private Slider? _discSlider;
    private ToggleSwitch? _animSwitch;
    private TextBlock? _movesText, _optimalText, _statusText;
    private Border? _pegHit0, _pegHit1, _pegHit2;
    private DockPanel? _pegPanel0, _pegPanel1, _pegPanel2;
    private Canvas? _overlay;

    private readonly List<List<int>> _pegs = [[], [], []];
    private readonly int[] _hiddenTopCount = new int[3]; // скрытые верхние диски по кегам (для анимации)
    private int _moves;
    private int _discCount; // зафиксированное число дисков для текущей партии
    private CancellationTokenSource? _cts;

    private IDisposable? _boundsSub;

    private const double DiscHeight = 26.0;
    private const double DiscMarginV = 4.0;

    private const double ArcMin = 60.0;
    private const double ArcMax = 180.0;
    private const double PegBaseOffset = 40.0;

    private static readonly double[] ArcKeyTimes = [0.0, 0.25, 0.5, 0.75, 1.0];

    public GameView()
    {
        AvaloniaXamlLoader.Load(this);
        AttachedToVisualTree += OnAttached;
    }

    private void OnAttached(object? s, VisualTreeAttachmentEventArgs e)
    {
        _discSlider = this.FindControl<Slider>("DiscSlider");
        _animSwitch = this.FindControl<ToggleSwitch>("AnimSwitch");
        _movesText = this.FindControl<TextBlock>("MovesText");
        _optimalText = this.FindControl<TextBlock>("OptimalText");
        _statusText = this.FindControl<TextBlock>("StatusText");

        _pegHit0 = this.FindControl<Border>("PegHit0");
        _pegHit1 = this.FindControl<Border>("PegHit1");
        _pegHit2 = this.FindControl<Border>("PegHit2");
        _pegPanel0 = this.FindControl<DockPanel>("PegPanel0");
        _pegPanel1 = this.FindControl<DockPanel>("PegPanel1");
        _pegPanel2 = this.FindControl<DockPanel>("PegPanel2");

        _overlay = this.FindControl<Canvas>("Overlay");

        _boundsSub = this.GetObservable(BoundsProperty).Subscribe(_ => RebuildAll());
        NewGame((int)Math.Round(_discSlider?.Value ?? 5));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _boundsSub?.Dispose();
        _boundsSub = null;
    }

    private void OnNewGameClick(object? sender, RoutedEventArgs e) =>
        NewGame((int)Math.Round(_discSlider?.Value ?? 5));

    private async void OnSolveClick(object? sender, RoutedEventArgs e)
    {
        if (_cts != null) return;

        var n = (int)Math.Round(_discSlider?.Value ?? 5);
        NewGame(n);

        var moves = HanoiSolver.GenerateMoves(n, 0, 2, 1);

        _cts = new CancellationTokenSource();
        try
        {
            SetStatus("Решаю…");
            var useAnim = _animSwitch?.IsChecked != false;

            foreach (var (from, to) in moves)
            {
                _cts.Token.ThrowIfCancellationRequested();

                if (useAnim)
                    await AnimateMoveAsync(from, to, _cts.Token);
                else
                {
                    DoMove(from, to);
                    await Task.Delay(300, _cts.Token);
                }
            }

            SetStatus("Готово!");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Остановлено.");
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void NewGame(int discs)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        Array.Fill(_hiddenTopCount, 0);

        _discCount = Math.Max(1, discs);

        foreach (var p in _pegs) p.Clear();
        for (var size = _discCount; size >= 1; size--) _pegs[0].Add(size);

        _moves = 0;
        if (_movesText != null) _movesText.Text = "0";
        if (_optimalText != null)
        {
            var optimal = BigInteger.Pow(2, _discCount) - 1;
            _optimalText.Text = $"(минимум: {optimal})";
        }

        SetStatus("Готово к решению.");
        RebuildAll();
    }

    private void SetStatus(string text)
    {
        if (_statusText != null) _statusText.Text = text;
    }

    private async Task AnimateMoveAsync(int from, int to, CancellationToken ct)
    {
        if (_overlay is null)
        {
            DoMove(from, to);
            return;
        }

        if (!IsValidPeg(from) || !IsValidPeg(to)) return;

        var source = _pegs[from];
        var target = _pegs[to];
        if (source.Count == 0) return;

        var size = source[^1];

        if (target.Count != 0 && target[^1] < size)
            throw new InvalidOperationException("Недопустимый ход");

        var (srcCenterX, srcBaseY) = PegCenterAndBaseY(from);
        var (dstCenterX, dstBaseY) = PegCenterAndBaseY(to);

        var (minW, step) = DiscWidthParamsForPeg(from);
        var n = _discCount;
        var width = minW + (size - 1) * step;

        var x0 = srcCenterX - width / 2.0;
        var y0 = srcBaseY - DiscTotalHeight(source.Count); // верх исходной стопки
        var x1 = dstCenterX - width / 2.0;
        var y1 = dstBaseY - DiscTotalHeight(target.Count + 1); // будущая позиция
        var dx = Math.Abs(dstCenterX - srcCenterX);
        var arc = Math.Clamp(dx * 0.35, ArcMin, ArcMax);

        var ghost = MakeGhost(width, size, n);
        Canvas.SetLeft(ghost, x0);
        Canvas.SetTop(ghost, y0);
        _overlay.Children.Add(ghost);

        // визуально скрываем верхний диск исходной кеги на время анимации
        _hiddenTopCount[from]++;
        RebuildPeg(from, GetHit(from), GetPanel(from));

        try
        {
            var anim = BuildArcAnimation(x0, y0, x1, y1, arc);
            await anim.RunAsync(ghost, ct);

            // после успешной анимации — применяем изменение модели
            source.RemoveAt(source.Count - 1);
            target.Add(size);
            _moves++;
            if (_movesText != null) _movesText.Text = _moves.ToString();
            RebuildAll();
        }
        finally
        {
            _overlay.Children.Remove(ghost);
            // возвращаем визуально скрытый диск (если отменилось — он снова появится на исходной кеге)
            _hiddenTopCount[from] = Math.Max(0, _hiddenTopCount[from] - 1);
            RebuildPeg(from, GetHit(from), GetPanel(from));
        }
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private (double centerX, double baseY) PegCenterAndBaseY(int peg)
    {
        Border? hit = GetHit(peg);
        if (hit == null || _overlay == null) return (0, 0);

        var local = new Point(hit.Bounds.Width / 2, hit.Bounds.Bottom - PegBaseOffset);
        var pt = hit.TranslatePoint(local, _overlay) ?? new Point(0, 0);
        return (pt.X, pt.Y);
    }

    private static bool IsValidPeg(int peg) => (uint)peg < 3;

    private static double DiscTotalHeight(int count)
    {
        if (count <= 0) return 0;
        return DiscHeight + (count - 1) * (DiscHeight + DiscMarginV);
    }

    private static double GetAreaWidth(Border hit) => Math.Max(100, hit.Bounds.Width);

    private (double minW, double step) DiscWidthParamsForPeg(int peg)
    {
        var hit = GetHit(peg);
        var areaWidth = GetAreaWidth(hit!);
        var minW = Math.Min(120, areaWidth * 0.28);
        var maxW = Math.Min(areaWidth - 28, areaWidth * 0.92);
        var step = (maxW - minW) / Math.Max(1, _discCount - 1);
        return (minW, step);
    }

    private void DoMove(int from, int to)
    {
        if (!IsValidPeg(from) || !IsValidPeg(to)) return;

        var source = _pegs[from];
        var target = _pegs[to];
        if (source.Count == 0) return;

        var moving = source[^1];
        if (target.Count != 0 && target[^1] < moving)
            throw new InvalidOperationException("Недопустимый ход");

        source.RemoveAt(source.Count - 1);
        target.Add(moving);
        _moves++;
        if (_movesText != null) _movesText.Text = _moves.ToString();
        RebuildAll();
    }

    private void RebuildAll()
    {
        RebuildPeg(0, _pegHit0, _pegPanel0);
        RebuildPeg(1, _pegHit1, _pegPanel1);
        RebuildPeg(2, _pegHit2, _pegPanel2);
    }

    private void RebuildPeg(int pegIndex, Border? hitBox, DockPanel? panel)
    {
        if (hitBox == null || panel == null) return;

        panel.Children.Clear();

        var tower = _pegs[pegIndex];
        var n = _discCount;

        var (minWidth, step) = DiscWidthParamsForPeg(pegIndex);

        var skipTop = Math.Clamp(_hiddenTopCount[pegIndex], 0, tower.Count);
        var renderCount = tower.Count - skipTop;

        for (var i = 0; i < renderCount; i++)
        {
            var size = tower[i];
            var width = minWidth + (size - 1) * step;

            var border = new Border
            {
                Height = DiscHeight,
                Width = width,
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 2, 0, 2),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Background = MakeDiscBrush(size, n),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)),
                BorderThickness = new Thickness(0)
            };

            DockPanel.SetDock(border, Dock.Bottom);
            panel.Children.Add(border);
        }
    }

    private static IBrush MakeDiscBrush(int size, int n)
    {
        var t = (double)(size - 1) / Math.Max(1, n - 1);
        var baseColor = HsvToRgb(220 * (1 - t) + 20 * t, 0.65, 0.9);

        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative)
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(230, baseColor.R, baseColor.G, baseColor.B), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(255, baseColor.R, baseColor.G, baseColor.B), 1));
        return brush;
    }

    private static Color HsvToRgb(double h, double s, double v)
    {
        h = (h % 360 + 360) % 360;
        var c = v * s;
        var x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        var m = v - c;

        double r1, g1, b1;
        if (h < 60) (r1, g1, b1) = (c, x, 0);
        else if (h < 120) (r1, g1, b1) = (x, c, 0);
        else if (h < 180) (r1, g1, b1) = (0, c, x);
        else if (h < 240) (r1, g1, b1) = (0, x, c);
        else if (h < 300) (r1, g1, b1) = (x, 0, c);
        else (r1, g1, b1) = (c, 0, x);

        var R = (byte)Math.Round((r1 + m) * 255);
        var G = (byte)Math.Round((g1 + m) * 255);
        var B = (byte)Math.Round((b1 + m) * 255);
        return Color.FromRgb(R, G, B);
    }

    private static Animation BuildArcAnimation(double x0, double y0, double x1, double y1, double arc)
    {
        var anim = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(700),
            Easing = new SineEaseInOut(),
            FillMode = FillMode.Forward
        };

        foreach (var t in ArcKeyTimes)
        {
            var x = Lerp(x0, x1, t);
            var y = Lerp(y0, y1, t) - arc * 4 * t * (1 - t);

            anim.Children.Add(new KeyFrame
            {
                Cue = new Cue(t),
                Setters =
                {
                    new Setter(Canvas.LeftProperty, x),
                    new Setter(Canvas.TopProperty, y)
                }
            });
        }

        return anim;
    }

    private Border MakeGhost(double width, int size, int n) => new()
    {
        Width = width,
        Height = DiscHeight,
        CornerRadius = new CornerRadius(10),
        Background = MakeDiscBrush(size, n),
        BorderBrush = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)),
        BorderThickness = new Thickness(0)
    };

    private Border? GetHit(int peg) => peg switch { 0 => _pegHit0, 1 => _pegHit1, _ => _pegHit2 };
    private DockPanel? GetPanel(int peg) => peg switch { 0 => _pegPanel0, 1 => _pegPanel1, _ => _pegPanel2 };
}