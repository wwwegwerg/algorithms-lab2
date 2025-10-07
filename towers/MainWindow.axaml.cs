using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Layout;

namespace towers;

public partial class MainWindow : Window
{
    private readonly List<List<int>> _pegs = [[], [], []];
    private int _moves;
    private bool _isSolving;
    private CancellationTokenSource? _solverCts;

    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) => NewGame((int)Math.Round(DiscSlider.Value));
        this.GetObservable(BoundsProperty).Subscribe(_ => RebuildAll());
    }

    private void OnNewGameClick(object? sender, RoutedEventArgs e)
    {
        if (_isSolving) _solverCts?.Cancel();
        NewGame((int)Math.Round(DiscSlider.Value));
    }

    private async void OnSolveClick(object? sender, RoutedEventArgs e)
    {
        if (_isSolving) return;

        NewGame((int)Math.Round(DiscSlider.Value));
        _isSolving = true;
        _solverCts = new CancellationTokenSource();
        try
        {
            SetStatus("Решаю…");
            await SolveAsync(_pegs[0].Count, 0, 2, 1, _solverCts.Token);
            SetStatus("Готово!");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Остановлено.");
        }
        finally
        {
            _isSolving = false;
            _solverCts?.Dispose();
            _solverCts = null;
        }
    }

    private void NewGame(int discs)
    {
        _solverCts?.Cancel();
        _isSolving = false;

        foreach (var p in _pegs) p.Clear();
        for (var size = discs; size >= 1; size--) _pegs[0].Add(size);

        _moves = 0;
        UpdateCounters(discs);
        SetStatus("Готово к решению.");
        RebuildAll();
    }

    private void UpdateCounters(int discs)
    {
        MovesText.Text = _moves.ToString();
        var optimal = BigInteger.Pow(2, discs) - 1;
        OptimalText.Text = $"(минимум: {optimal})";
    }

    private void SetStatus(string text)
    {
        StatusText.Text = text;
    }

    private void DoMove(int from, int to)
    {
        var source = _pegs[from];
        var target = _pegs[to];
        if (source.Count == 0) return;

        var moving = source[^1];
        if (target.Count != 0 && target[^1] < moving)
            throw new InvalidOperationException("Недопустимый ход");

        source.RemoveAt(source.Count - 1);
        target.Add(moving);
        _moves++;
        MovesText.Text = _moves.ToString();

        RebuildAll();
    }

    // Рекурсивный решатель с двумя режимами анимации.
    private async Task SolveAsync(int n, int from, int to, int aux, CancellationToken ct)
    {
        if (n <= 0) return;

        await SolveAsync(n - 1, from, aux, to, ct);
        ct.ThrowIfCancellationRequested();

        // задержка по тоглу: медленно ~220мс, быстро ~40мс (достаточно, чтобы видеть движение и не фризить UI)
        var delay = SlowCheck?.IsChecked ?? false ? 220 : 40;
        await Task.Delay(delay, ct);
        DoMove(from, to);

        await SolveAsync(n - 1, aux, to, from, ct);
    }

    private void RebuildAll()
    {
        RebuildPeg(0, PegHit0, PegPanel0);
        RebuildPeg(1, PegHit1, PegPanel1);
        RebuildPeg(2, PegHit2, PegPanel2);
    }

    private void RebuildPeg(int pegIndex, Border hitBox, DockPanel panel)
    {
        panel.Children.Clear();

        var tower = _pegs[pegIndex];
        var n = Math.Max(1, (int)Math.Round(DiscSlider.Value));
        var areaWidth = Math.Max(100, hitBox.Bounds.Width);
        var minWidth = Math.Min(120, areaWidth * 0.28);
        var maxWidth = Math.Min(areaWidth - 28, areaWidth * 0.92);
        var step = (maxWidth - minWidth) / Math.Max(1, n - 1);
        var discHeight = 26.0;

        foreach (var size in tower)
        {
            var width = minWidth + (size - 1) * step;

            var border = new Border
            {
                Height = discHeight,
                Width = width,
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 2, 0, 2),
                HorizontalAlignment = HorizontalAlignment.Center,
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

        brush.GradientStops.Add(new GradientStop(WithAlpha(baseColor, 230), 0));
        brush.GradientStops.Add(new GradientStop(WithAlpha(baseColor, 255), 1));
        return brush;
    }

    private static Color WithAlpha(Color c, byte a)
    {
        return Color.FromArgb(a, c.R, c.G, c.B);
    }

    private static Color HsvToRgb(double h, double s, double v)
    {
        h = (h % 360 + 360) % 360;
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
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