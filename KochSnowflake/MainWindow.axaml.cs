using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace KochSnowflake;

public partial class MainWindow : Window
{
    private readonly FractalCanvas _canvas;
    private readonly NumericUpDown _iterUpDown;
    private readonly NumericUpDown _sidesUpDown;
    private readonly Button _resetBtn;

    private readonly TextBlock _dbgIter;
    private readonly TextBlock _dbgZoom;
    private readonly TextBlock _dbgVerts;
    private readonly TextBlock _dbgGenSeg;

    // Пользовательский генератор
    private readonly CheckBox _useCustomGen;
    private readonly TextBox _axiomBox;
    private readonly NumericUpDown _angleBox;
    private readonly TextBox _rulesBox;
    private readonly Button _applyGenBtn;
    private readonly TextBlock _genInfo;

    public MainWindow()
    {
        InitializeComponent();

        _canvas = this.FindControl<FractalCanvas>("Canvas")!;
        _iterUpDown = this.FindControl<NumericUpDown>("IterUpDown")!;
        _sidesUpDown = this.FindControl<NumericUpDown>("SidesUpDown")!;
        _resetBtn = this.FindControl<Button>("ResetBtn")!;

        _dbgIter = this.FindControl<TextBlock>("DbgIter")!;
        _dbgZoom = this.FindControl<TextBlock>("DbgZoom")!;
        _dbgVerts = this.FindControl<TextBlock>("DbgVerts")!;
        _dbgGenSeg = this.FindControl<TextBlock>("DbgGenSeg")!;

        _useCustomGen = this.FindControl<CheckBox>("UseCustomGen")!;
        _angleBox = null!;
        _axiomBox = this.FindControl<TextBox>("AxiomBox")!;
        _angleBox = this.FindControl<NumericUpDown>("AngleBox")!;
        _rulesBox = this.FindControl<TextBox>("RulesBox")!;
        _applyGenBtn = this.FindControl<Button>("ApplyGenBtn")!;
        _genInfo = this.FindControl<TextBlock>("GenInfo")!;

        _canvas.Iterations = (int)_iterUpDown.Value!;
        _canvas.BaseSides = (int)_sidesUpDown.Value!;

        _iterUpDown.ValueChanged += (_, e) =>
        {
            _canvas.Iterations = (int)e.NewValue!;
            UpdateDebug();
        };

        _sidesUpDown.ValueChanged += (_, e) =>
        {
            _canvas.BaseSides = (int)e.NewValue!;
            UpdateDebug();
        };

        _resetBtn.Click += OnResetClicked;

        _applyGenBtn.Click += OnApplyGenClicked;
        _useCustomGen.IsCheckedChanged += (_, _) =>
        {
            if (_useCustomGen.IsChecked == true) TryApplyGenerator();
            else RestoreDefaultGenerator();
        };

        if (_useCustomGen.IsChecked == true) TryApplyGenerator();
        else RestoreDefaultGenerator();

        _canvas.ViewChanged += (_, _) => UpdateDebug();
        _canvas.IterationsChanged += (_, _) => UpdateDebug();
        _canvas.GeneratorChanged += (_, _) => UpdateDebug();

        UpdateDebug();
    }

    private void OnResetClicked(object? sender, RoutedEventArgs e)
    {
        _canvas.ResetView();
        UpdateDebug();
    }

    private void OnApplyGenClicked(object? sender, RoutedEventArgs e) => TryApplyGenerator();

    private void TryApplyGenerator()
    {
        _genInfo.Text = "";
        if (_useCustomGen.IsChecked != true)
        {
            RestoreDefaultGenerator();
            return;
        }

        try
        {
            var axiom = _axiomBox.Text ?? "F";
            var angle = (double)_angleBox.Value!;
            // шаг развёртки фиксируем = 1 (по твоему требованию)
            var expandSteps = 1;

            var rules = (_rulesBox.Text ?? "")
                .Replace("\r\n", "\n")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);

            var gen = LSystemGenerator.BuildNormalizedGenerator(axiom, rules, angle, expandSteps);
            _canvas.SetGenerator(gen);
            _genInfo.Text = $"OK • сегментов: {Math.Max(0, gen.Count - 1)}";
        }
        catch (Exception ex)
        {
            _genInfo.Text = $"Ошибка: {ex.Message}";
        }

        UpdateDebug();
    }

    private void RestoreDefaultGenerator()
    {
        var h = Math.Sqrt(3) / 6.0;
        var def = new List<Point>
        {
            new(0, 0),
            new(1.0 / 3.0, 0),
            new(0.5, -h),
            new(2.0 / 3.0, 0),
            new(1, 0),
        };
        _canvas.SetGenerator(def);
        _genInfo.Text = "Стандартный Кох";
        UpdateDebug();
    }

    private void UpdateDebug()
    {
        _dbgIter.Text = $"Итерации: {_canvas.Iterations}";
        _dbgZoom.Text = $"Зум: ×{_canvas.ZoomFactor:0.00}";
        _dbgVerts.Text = $"Вершин: {_canvas.VertexCount:N0}";
        _dbgGenSeg.Text = $"Сегментов генератора: {_canvas.GeneratorSegments}";
    }
}