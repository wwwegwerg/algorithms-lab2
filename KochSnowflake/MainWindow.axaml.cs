using System.Globalization;
using Avalonia.Controls;

namespace KochSnowflake;

public partial class MainWindow : Window
{
    private readonly FractalCanvas _canvas;
    private readonly NumericUpDown _iterUpDown;
    private readonly Button _resetBtn;

    private readonly TextBlock _dbgIter;
    private readonly TextBlock _dbgZoom;
    private readonly TextBlock _dbgVerts;

    public MainWindow()
    {
        InitializeComponent();

        _canvas = this.FindControl<FractalCanvas>("Canvas")!;
        _iterUpDown = this.FindControl<NumericUpDown>("IterUpDown")!;
        _resetBtn = this.FindControl<Button>("ResetBtn")!;

        _dbgIter = this.FindControl<TextBlock>("DbgIter")!;
        _dbgZoom = this.FindControl<TextBlock>("DbgZoom")!;
        _dbgVerts = this.FindControl<TextBlock>("DbgVerts")!;

        // стартовые значения
        _canvas.Iterations = (int)_iterUpDown.Value!;

        // события UI
        _iterUpDown.ValueChanged += (_, e) =>
        {
            _canvas.Iterations = (int)e.NewValue!;
            UpdateDebug();
        };

        _resetBtn.Click += (_, _) =>
        {
            _canvas.ResetView();
            UpdateDebug();
        };

        // обновление инфы при изменении вида/итераций
        _canvas.ViewChanged += (_, _) => UpdateDebug();
        _canvas.IterationsChanged += (_, _) => UpdateDebug();

        UpdateDebug();
    }

    private void UpdateDebug()
    {
        var ci = CultureInfo.InvariantCulture;
        _dbgIter.Text = $"Итерации: {_canvas.Iterations}";
        _dbgZoom.Text = $"Зум: ×{_canvas.ZoomFactor.ToString("0.00", ci)}";
        _dbgVerts.Text = $"Вершин: {_canvas.VertexCount.ToString("N0", ci)}";
    }
}