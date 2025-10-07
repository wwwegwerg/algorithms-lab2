using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace towers.Views;

public partial class GameView : UserControl
{
    // --- UI refs (через FindControl) ---
    private Slider? _discSlider;
    private CheckBox? _slowCheck;
    private TextBlock? _movesText, _optimalText, _statusText;
    private Border? _pegHit0, _pegHit1, _pegHit2;
    private DockPanel? _pegPanel0, _pegPanel1, _pegPanel2;

    // --- state ---
    private readonly List<List<int>> _pegs = [new(), new(), new()];
    private int _moves;
    private CancellationTokenSource? _cts;

    public GameView()
    {
        AvaloniaXamlLoader.Load(this); // <- вместо InitializeComponent()
        AttachedToVisualTree += OnAttached; // берём ссылки на элементы, когда контрол оказался в визуальном дереве
    }

    private void OnAttached(object? s, VisualTreeAttachmentEventArgs e)
    {
        // resolve controls by x:Name
        _discSlider = this.FindControl<Slider>("DiscSlider");
        _slowCheck = this.FindControl<CheckBox>("SlowCheck");
        _movesText = this.FindControl<TextBlock>("MovesText");
        _optimalText = this.FindControl<TextBlock>("OptimalText");
        _statusText = this.FindControl<TextBlock>("StatusText");

        _pegHit0 = this.FindControl<Border>("PegHit0");
        _pegHit1 = this.FindControl<Border>("PegHit1");
        _pegHit2 = this.FindControl<Border>("PegHit2");
        _pegPanel0 = this.FindControl<DockPanel>("PegPanel0");
        _pegPanel1 = this.FindControl<DockPanel>("PegPanel1");
        _pegPanel2 = this.FindControl<DockPanel>("PegPanel2");

        // пересборка при ресайзе
        this.GetObservable(BoundsProperty).Subscribe(_ => RebuildAll());

        NewGame((int)Math.Round(_discSlider?.Value ?? 5));
    }

    // wired from XAML
    private void OnNewGameClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        NewGame((int)Math.Round(_discSlider?.Value ?? 5));

    // wired from XAML
    // 1) Принудительно начинаем с чистого старта перед решением
    private async void OnSolveClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_cts != null) return;

        var n = (int)Math.Round(_discSlider?.Value ?? 5);

        // ВАЖНО: сбросить поле в исходное состояние под текущее n,
        // иначе ходы не соответствуют реальному состоянию башен.
        NewGame(n);

        var moves = HanoiSolver.GenerateMoves(n, 0, 2, 1);

        _cts = new CancellationTokenSource();
        try
        {
            SetStatus("Решаю…");
            var delay = (_slowCheck?.IsChecked ?? false) ? 220 : 40;

            foreach (var (from, to) in moves)
            {
                _cts.Token.ThrowIfCancellationRequested();
                await Task.Delay(delay, _cts.Token);
                DoMove(from, to);
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
        for (int size = discs; size >= 1; size--) _pegs[0].Add(size);

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

    // 2) Защита в DoMove
    private void DoMove(int from, int to)
    {
        // границы индексов
        if (from < 0 || from > 2 || to < 0 || to > 2) return;

        var source = _pegs[from];
        var target = _pegs[to];

        // если источник пуст — это признак несоответствия состояния и хода;
        // просто игнорируем, чтобы не падать (но при корректном запуске через NewGame это не произойдёт)
        if (source.Count == 0)
        {
            SetStatus("Попытка хода с пустой башни — пропущено.");
            return;
        }

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
        if (_pegHit0 == null || _pegHit1 == null || _pegHit2 == null ||
            _pegPanel0 == null || _pegPanel1 == null || _pegPanel2 == null) return;

        RebuildPeg(0, _pegHit0, _pegPanel0);
        RebuildPeg(1, _pegHit1, _pegPanel1);
        RebuildPeg(2, _pegHit2, _pegPanel2);
    }

    private void RebuildPeg(int pegIndex, Border hitBox, DockPanel panel)
    {
        panel.Children.Clear();

        var tower = _pegs[pegIndex];
        var n = Math.Max(1, (int)Math.Round(_discSlider?.Value ?? 5));
        var areaWidth = Math.Max(100, hitBox.Bounds.Width);
        var minWidth = Math.Min(120, areaWidth * 0.28);
        var maxWidth = Math.Min(areaWidth - 28, areaWidth * 0.92);
        var step = (maxWidth - minWidth) / Math.Max(1, n - 1);
        var discHeight = 26.0;

        for (var i = 0; i < tower.Count; i++)
        {
            var size = tower[i];
            var width = minWidth + (size - 1) * step;

            var border = new Border
            {
                Height = discHeight,
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
        double t = (double)(size - 1) / Math.Max(1, n - 1);
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
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        double m = v - c;

        double r1 = 0, g1 = 0, b1 = 0;
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