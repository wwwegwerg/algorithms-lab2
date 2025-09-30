using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace towers;

public partial class MainWindow : Window
{
    private readonly List<List<int>> _pegs = new()
    {
        new List<int>(),
        new List<int>(),
        new List<int>()
    };

    private int _moves;
    private (int fromPeg, int size)? _selected;
    private bool _isSolving;
    private CancellationTokenSource? _solverCts;

    public MainWindow()
    {
        InitializeComponent();

        this.Opened += (_, _) => NewGame((int)Math.Round((double)DiscSlider.Value));
        this.GetObservable(BoundsProperty).Subscribe(_ => RebuildAll());
    }

    private void OnNewGameClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isSolving) return;
        NewGame((int)Math.Round((double)DiscSlider.Value));
    }

    private async void OnSolveClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isSolving) return;

        NewGame((int)Math.Round((double)DiscSlider.Value));

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
            _selected = null;
            RebuildAll();
        }
    }

    private void Peg_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_isSolving) return;
        if (sender is not Control { Tag: int targetPeg }) return;

        if (_selected is null) return;

        var (fromPeg, _) = _selected.Value;
        TryMove(fromPeg, targetPeg);
    }

    private void Disc_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_isSolving) return;
        if (sender is not Border discBorder) return;
        if (discBorder.Tag is not (int pegIndex, int size)) return;

        var tower = _pegs[pegIndex];
        if (tower.Count == 0 || tower[^1] != size) return; // не верхний

        if (_selected is { fromPeg: var fp, size: var s } && fp == pegIndex && s == size)
            _selected = null;
        else
            _selected = (pegIndex, size);

        RebuildAll();
    }

    private void NewGame(int discs)
    {
        _solverCts?.Cancel();
        _isSolving = false;
        _selected = null;

        foreach (var p in _pegs) p.Clear();

        for (int size = discs; size >= 1; size--)
            _pegs[0].Add(size);

        _moves = 0;
        UpdateCounters(discs);
        SetStatus("Новая игра. Кликните верхний диск, затем — целевую башню.");
        RebuildAll();
    }

    private void UpdateCounters(int discs)
    {
        MovesText.Text = _moves.ToString();
        var optimal = BigInteger.Pow(2, discs) - 1;
        OptimalText.Text = $"(минимум: {optimal})";
    }

    private void SetStatus(string text) => StatusText.Text = text;

    private bool TryMove(int from, int to, bool silent = false)
    {
        if (from == to) return false;

        var source = _pegs[from];
        var target = _pegs[to];
        if (source.Count == 0) return false;

        var moving = source[^1];
        var ok = target.Count == 0 || target[^1] > moving;
        if (!ok) return false;

        source.RemoveAt(source.Count - 1);
        target.Add(moving);
        _moves++;
        MovesText.Text = _moves.ToString();

        _selected = null;
        RebuildAll();

        if (!silent)
        {
            // победа: все диски на C (2), A и B пустые
            var total = (int)Math.Round((double)DiscSlider.Value);
            if (_pegs[2].Count == total && _pegs[0].Count == 0 && _pegs[1].Count == 0)
                SetStatus($"Собрано! Ходов: {_moves}");
        }

        return true;
    }

    private async Task SolveAsync(int n, int from, int to, int aux, CancellationToken ct)
    {
        if (n <= 0) return;

        await SolveAsync(n - 1, from, aux, to, ct);
        ct.ThrowIfCancellationRequested();

        var delay = SlowCheck.IsChecked == true ? 220 : 40;
        await Task.Delay(delay, ct);
        TryMove(from, to, silent: true);

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
        var n = Math.Max(1, (int)Math.Round((double)DiscSlider.Value));
        var areaWidth = Math.Max(100, hitBox.Bounds.Width);
        var minWidth = Math.Min(120, areaWidth * 0.28);
        var maxWidth = Math.Min(areaWidth - 28, areaWidth * 0.92);
        var step = (maxWidth - minWidth) / Math.Max(1, n - 1);
        var discHeight = 26.0;

        for (var i = 0; i < tower.Count; i++)
        {
            var size = tower[i];
            var width = minWidth + (size - 1) * step;

            var isTop = i == tower.Count - 1;
            var isSelected = _selected is { fromPeg: var fp, size: var s } && fp == pegIndex && s == size;

            var border = new Border
            {
                Height = discHeight,
                Width = width,
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 2, 0, 2),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Background = MakeDiscBrush(size, n, isSelected),
                BorderBrush = isSelected ? Brushes.Gold : new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)),
                BorderThickness = isSelected ? new Thickness(3) : new Thickness(0),
                Tag = (pegIndex, size),
                Cursor = isTop ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Arrow)
            };

            // 🔧 ToolTip — это attached-property
            ToolTip.SetTip(border, isTop
                ? $"Диск {size} (верхний). Клик — взять/положить."
                : $"Диск {size}");

            border.PointerPressed += Disc_PointerPressed;

            DockPanel.SetDock(border, Dock.Bottom);
            panel.Children.Add(border);
        }
    }

    private static IBrush MakeDiscBrush(int size, int n, bool emphasized)
    {
        double t = (double)(size - 1) / Math.Max(1, n - 1);
        var baseColor = HsvToRgb(220 * (1 - t) + 20 * t, 0.65, emphasized ? 0.95 : 0.85);

        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative)
        };

        brush.GradientStops.Add(new GradientStop(WithAlpha(baseColor, 230), 0));
        brush.GradientStops.Add(new GradientStop(WithAlpha(baseColor, 255), 1));

        return brush;
    }

    private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    private static Color HsvToRgb(double h, double s, double v)
    {
        h = (h % 360 + 360) % 360;
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        double m = v - c;

        (double r1, double g1, double b1) = (0, 0, 0);
        if (h < 60) (r1, g1, b1) = (c, x, 0);
        else if (h < 120) (r1, g1, b1) = (x, c, 0);
        else if (h < 180) (r1, g1, b1) = (0, c, x);
        else if (h < 240) (r1, g1, b1) = (0, x, c);
        else if (h < 300) (r1, g1, b1) = (x, 0, c);
        else (r1, g1, b1) = (c, 0, x);

        byte R = (byte)Math.Round((r1 + m) * 255);
        byte G = (byte)Math.Round((g1 + m) * 255);
        byte B = (byte)Math.Round((b1 + m) * 255);
        return Color.FromRgb(R, G, B);
    }
}