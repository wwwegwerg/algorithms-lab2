using System;
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
    private readonly TextBlock _dbgCplx;

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
        _dbgCplx = this.FindControl<TextBlock>("DbgCplx")!;

        _useCustomGen = this.FindControl<CheckBox>("UseCustomGen")!;
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
            var newVal = (int)e.NewValue!;
            if (newVal == 2)
            {
                newVal = e.OldValue < e.NewValue ? 3 : 1;
                _sidesUpDown.Value = newVal;
            }

            _canvas.BaseSides = newVal;
            UpdateDebug();
        };

        _resetBtn.Click += OnResetClicked;

        _applyGenBtn.Click += OnApplyGenClicked;
        _useCustomGen.IsCheckedChanged += (_, _) =>
        {
            if (_useCustomGen.IsChecked == true) TryApplyGenerator();
            else RestoreDefaultGenerator();
        };

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
            const int expandSteps = 1;

            var rules = (_rulesBox.Text ?? "")
                .Replace("\r\n", "\n")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);

            var gen = LSystemGenerator.BuildNormalizedGenerator(axiom, rules, angle, expandSteps);
            _canvas.SetGenerator(gen);
            _genInfo.Text = "OK";
        }
        catch (Exception ex)
        {
            _genInfo.Text = $"Ошибка: {ex.Message}";
        }

        UpdateDebug();
    }

    private void RestoreDefaultGenerator()
    {
        _canvas.SetGenerator(FractalCanvas.DefaultKochGenerator());
        _genInfo.Text = "Включите использование пользовательского генератора";
        UpdateDebug();
    }

    private void UpdateDebug()
    {
        _dbgIter.Text = $"Итерации: {_canvas.Iterations}";
        _dbgZoom.Text = $"Зум: ×{_canvas.ZoomFactor:0.00}";
        _dbgVerts.Text = $"Вершин: {_canvas.VertexCount:N0}";
        _dbgGenSeg.Text = $"Сегментов генератора: {_canvas.GeneratorSegments}";
        _dbgCplx.Text = $"Сложность: {_canvas.BaseSides} * {_canvas.GeneratorSegments}^N + 1";
    }
}