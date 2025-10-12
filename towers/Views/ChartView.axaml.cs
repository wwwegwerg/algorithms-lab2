using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using AvaloniaPlotView = OxyPlot.Avalonia.PlotView;

namespace towers.Views;

public partial class ChartView : UserControl
{
    private const int MinN = 1;
    private const int MaxN = 30; // 35 => 8 с небольшим минут
    private const int DefaultPasses = 5;

    private AvaloniaPlotView? _plot;

    private readonly PlotModel _model = new()
    {
        Title = "Время решения задачи в зависимости от числа дисков"
    };

    private readonly LineSeries _series = new()
    {
        Title = "Время, с",
        MarkerType = MarkerType.Circle,
        MarkerSize = 2,
        StrokeThickness = 2
    };

    private bool _hasResults;

    public ChartView()
    {
        AvaloniaXamlLoader.Load(this);
        AttachedToVisualTree += OnAttached;
    }

    private void OnAttached(object? s, VisualTreeAttachmentEventArgs e)
    {
        _plot = this.FindControl<AvaloniaPlotView>("Plot");

        _model.Axes.Clear();
        _model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = "Количество дисков",
            Minimum = double.NaN,
            Maximum = double.NaN
        });
        _model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "Время, с",
            Minimum = double.NaN,
            Maximum = double.NaN
        });

        _model.Series.Clear();
        _model.Series.Add(_series);

        if (_plot != null)
            _plot.Model = _model;

        this.GetObservable(IsVisibleProperty).Subscribe(visible =>
        {
            if (visible && !_hasResults)
                Benchmark(DefaultPasses);
        });

        if (IsVisible && !_hasResults)
            Benchmark(DefaultPasses);
    }

    private void Benchmark(int passesPerN)
    {
        Console.WriteLine($"Started at {DateTime.Now.TimeOfDay}");
        var globalSw = Stopwatch.StartNew();

        var pts = new List<DataPoint>(MaxN - MinN + 1);

        // for (var i = 0; i < passesPerN; i++)
        // {
        //     HanoiSolver.Solve(3, 0, 2, 1, static (_, _) => { });
        // }

        for (var n = MinN; n <= MaxN; n++)
        {
            for (var i = 0; i < passesPerN; i++)
            {
                HanoiSolver.Solve(3, 0, 2, 1, static (_, _) => { });
            }

            var sw = Stopwatch.StartNew();

            for (var i = 0; i < passesPerN; i++)
            {
                HanoiSolver.Solve(n, 0, 2, 1, static (_, _) => { });
            }

            sw.Stop();

            var avgSeconds = sw.Elapsed.TotalSeconds / passesPerN;
            pts.Add(new DataPoint(n, avgSeconds));
        }

        globalSw.Stop();
        Console.WriteLine($"Completed at {DateTime.Now.TimeOfDay}");
        Console.WriteLine($"Total time: {globalSw.Elapsed.TotalSeconds}");

        _series.ItemsSource = pts;
        _model.ResetAllAxes();
        _plot?.InvalidatePlot(true);

        _hasResults = true;
    }
}