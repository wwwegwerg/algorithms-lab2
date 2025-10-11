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

    private readonly List<List<int>> _pegs = [new(), new(), new()];
    private int _moves;
    private CancellationTokenSource? _cts;

    private const double DiscHeight = 26.0;
    private const double DiscMarginV = 4.0; // Margin(Top=2, Bottom=2) => суммарно 4

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

        this.GetObservable(BoundsProperty).Subscribe(_ => RebuildAll());
        NewGame((int)Math.Round(_discSlider?.Value ?? 5));
    }

    private void OnNewGameClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        NewGame((int)Math.Round(_discSlider?.Value ?? 5));

    private async void OnSolveClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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
        foreach (var p in _pegs) p.Clear();
        for (var size = discs; size >= 1; size--) _pegs[0].Add(size);

        _moves = 0;
        if (_movesText != null) _movesText.Text = "0";
        if (_optimalText != null)
        {
            var optimal = BigInteger.Pow(2, discs) - 1;
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
        if (_overlay == null)
        {
            DoMove(from, to);
            return;
        }

        if (from < 0 || from > 2 || to < 0 || to > 2) return;

        var source = _pegs[from];
        var target = _pegs[to];
        if (source.Count == 0) return;

        var size = source[^1];

        var sourceCountBefore = source.Count;
        var targetCountBefore = target.Count;

        var (srcCenterX, srcBaseY) = PegCenterAndBaseY(from);
        var (dstCenterX, dstBaseY) = PegCenterAndBaseY(to);

        var (minW, maxW, step) = DiscWidthParamsForPeg(from);
        var width = minW + (size - 1) * step;

        var x0 = srcCenterX - width / 2.0;
        var x1 = dstCenterX - width / 2.0;

        var y0 = srcBaseY - DiscHeight - (sourceCountBefore - 1) * (DiscHeight + DiscMarginV);
        var y1 = dstBaseY - DiscHeight - (targetCountBefore) * (DiscHeight + DiscMarginV);

        var dx = Math.Abs(dstCenterX - srcCenterX);
        var arc = Math.Clamp(dx * 0.35, 60, 180);

        source.RemoveAt(source.Count - 1);
        RebuildAll();

        var ghost = new Border
        {
            Width = width,
            Height = DiscHeight,
            CornerRadius = new CornerRadius(10),
            Background = MakeDiscBrush(size, Math.Max(1, (int)Math.Round(_discSlider?.Value ?? 5))),
            BorderBrush = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)),
            BorderThickness = new Thickness(0)
        };
        Canvas.SetLeft(ghost, x0);
        Canvas.SetTop(ghost, y0);
        _overlay.Children.Add(ghost);

        var tList = new[] { 0.0, 0.25, 0.5, 0.75, 1.0 };
        var anim = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(500),
            Easing = new SineEaseInOut(),
            FillMode = FillMode.Forward
        };

        foreach (var t in tList)
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

        await anim.RunAsync(ghost, ct);

        target.Add(size);
        _moves++;
        _movesText!.Text = _moves.ToString();

        RebuildAll();
        _overlay.Children.Remove(ghost);
    }


    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private (double centerX, double baseY) PegCenterAndBaseY(int peg)
    {
        Border? hit = peg switch { 0 => _pegHit0, 1 => _pegHit1, _ => _pegHit2 };
        if (hit == null || _overlay == null) return (0, 0);

        var local = new Point(hit.Bounds.Width / 2, hit.Bounds.Bottom - 40);
        var pt = hit.TranslatePoint(local, _overlay) ?? new Point(0, 0);
        return (pt.X, pt.Y);
    }

    private (double minW, double maxW, double step) DiscWidthParamsForPeg(int peg)
    {
        var hit = peg switch { 0 => _pegHit0, 1 => _pegHit1, _ => _pegHit2 };
        var areaWidth = Math.Max(100, hit?.Bounds.Width ?? 200);
        var minW = Math.Min(120, areaWidth * 0.28);
        var maxW = Math.Min(areaWidth - 28, areaWidth * 0.92);
        var n = Math.Max(1, (int)Math.Round(_discSlider?.Value ?? 5));
        var step = (maxW - minW) / Math.Max(1, n - 1);
        return (minW, maxW, step);
    }

    private void DoMove(int from, int to)
    {
        if (from < 0 || from > 2 || to < 0 || to > 2) return;

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
        var n = Math.Max(1, (int)Math.Round(_discSlider?.Value ?? 5));
        var areaWidth = Math.Max(100, hitBox.Bounds.Width);
        var minWidth = Math.Min(120, areaWidth * 0.28);
        var maxWidth = Math.Min(areaWidth - 28, areaWidth * 0.92);
        var step = (maxWidth - minWidth) / Math.Max(1, n - 1);

        foreach (var size in tower)
        {
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
}