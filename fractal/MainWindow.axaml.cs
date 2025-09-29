using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace fractal;

public partial class MainWindow : Window
{
    private decimal _centerX;
    private decimal _centerY;
    private decimal _scale; // мировых ед. на 1 пиксель
    private int _levels = 6;

    private bool _isPanning;
    private PixelPoint _lastPointerPx;

    private WriteableBitmap? _bitmap;
    private CancellationTokenSource? _renderCts;
    private readonly object _renderLock = new();

    public MainWindow()
    {
        InitializeComponent();

        Opened += (_, _) =>
        {
            Focus();
            IterationsUpDown.Value = _levels;
            IterationsUpDown.PropertyChanged += (_, e) =>
            {
                if (e.Property != NumericUpDown.ValueProperty) return;

                var v = IterationsUpDown.Value ?? _levels;
                _levels = (int)Math.Clamp(Math.Round(v), 1, 60);
                QueueRender();
            };
            ResetView();
        };
    }

    private void OnResetClicked(object? sender, RoutedEventArgs e) => ResetView();
    private void OnSizeChanged(object? sender, SizeChangedEventArgs e) => QueueRender();

    private void ResetView()
    {
        _centerX = 0.0m;
        _centerY = 0.0m;

        // стартовая ширина области = 3.0 (центральная 3x3 плитка)
        var s = (decimal)this.GetRenderScaling();
        var wDip = (decimal)FractalImage.Bounds.Width;
        if (wDip <= 1) wDip = Math.Max(1m, (decimal)this.ClientSize.Width - (decimal)Sidebar.Bounds.Width);
        var wPx = Math.Max(1m, Math.Round(wDip * s));
        _scale = 3.0m / wPx; // ширина области = 3.0

        QueueRender();
    }

    private bool IsPointInsideFractal(Point pOnFractal)
    {
        var w = FractalImage.Bounds.Width;
        var h = FractalImage.Bounds.Height;
        return pOnFractal.X >= 0 && pOnFractal.Y >= 0 && pOnFractal.X < w && pOnFractal.Y < h;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var posOnFractal = e.GetPosition(FractalImage);
        if (!IsPointInsideFractal(posOnFractal))
            return;

        _isPanning = true;
        _lastPointerPx = posOnFractal.ToPixelPoint(FractalImage);
        Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Pointer.Capture(this);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPanning) return;

        var posOnFractal = e.GetPosition(FractalImage);
        var ptPx = posOnFractal.ToPixelPoint(FractalImage);

        var dx = ptPx.X - _lastPointerPx.X;
        var dy = ptPx.Y - _lastPointerPx.Y;
        _lastPointerPx = ptPx;

        _centerX -= dx * _scale;
        _centerY -= dy * _scale;
        _centerX = FractalMath.Wrap3(_centerX);
        _centerY = FractalMath.Wrap3(_centerY);

        QueueRender();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isPanning) return;

        _isPanning = false;
        Cursor = new Cursor(StandardCursorType.Arrow);
        e.Pointer.Capture(null);
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var posOnFractal = e.GetPosition(FractalImage);
        if (!IsPointInsideFractal(posOnFractal)) return;

        var s = (decimal)this.GetRenderScaling();

        var pos = e.GetPosition(FractalImage);
        decimal posPxX = (decimal)pos.X * s;
        decimal posPxY = (decimal)pos.Y * s;

        decimal wDip = (decimal)FractalImage.Bounds.Width;
        decimal hDip = (decimal)FractalImage.Bounds.Height;
        if (wDip <= 1 || hDip <= 1)
        {
            wDip = Math.Max(1m, (decimal)ClientSize.Width - (decimal)Sidebar.Bounds.Width);
            hDip = Math.Max(1m, (decimal)ClientSize.Height);
        }

        decimal widthPx = Math.Max(1m, Math.Round(wDip * s));
        decimal heightPx = Math.Max(1m, Math.Round(hDip * s));

        decimal worldX = _centerX + (posPxX - widthPx / 2m) * _scale;
        decimal worldY = _centerY + (posPxY - heightPx / 2m) * _scale;

        decimal factor = e.Delta.Y > 0 ? 0.8m : 1.25m;
        decimal newScale = _scale * factor;

        _centerX = worldX - (posPxX - widthPx / 2m) * newScale;
        _centerY = worldY - (posPxY - heightPx / 2m) * newScale;
        _scale = newScale;

        _centerX = FractalMath.Wrap3(_centerX);
        _centerY = FractalMath.Wrap3(_centerY);

        QueueRender();
    }

    private void QueueRender()
    {
        // 1) Считаем реальные размеры области фрактала (в пикселях)
        var s = (decimal)this.GetRenderScaling();

        decimal wDip = (decimal)FractalImage.Bounds.Width;
        decimal hDip = (decimal)FractalImage.Bounds.Height;
        if (wDip <= 1 || hDip <= 1)
        {
            wDip = Math.Max(1m, (decimal)ClientSize.Width - (decimal)Sidebar.Bounds.Width);
            hDip = Math.Max(1m, (decimal)ClientSize.Height);
        }

        int widthPx = (int)Math.Max(1m, Math.Round(wDip * s));
        int heightPx = (int)Math.Max(1m, Math.Round(hDip * s));
        if (widthPx <= 1 || heightPx <= 1) return;

        // 2) Отменяем предыдущий рендер и создаём новый CTS (используем _renderLock!)
        CancellationToken token;
        CancellationTokenSource cts;
        lock (_renderLock)
        {
            _renderCts?.Cancel();
            _renderCts?.Dispose();
            _renderCts = cts = new CancellationTokenSource();
            token = cts.Token;
        }

        // 3) Захватываем текущее состояние (чтобы зум/пан не дёргали в процессе)
        var cx = _centerX;
        var cy = _centerY;
        var sc = _scale;
        var lv = _levels;

        InfoText.Text = $"Центр: ({cx:F6}, {cy:F6})\nМасштаб: {sc:E2}\nУровни: {lv}\n{widthPx}×{heightPx}px";

        // 4) Фоновой задачей считаем битмап и публикуем его на UI-поток
        _ = Task.Run(async () =>
        {
            try
            {
                var bmp = await FractalRenderer.RenderSierpinskiCarpetAsync(
                    widthPx, heightPx, cx, cy, sc, lv, token);

                if (token.IsCancellationRequested) return;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _bitmap?.Dispose();
                    _bitmap = bmp;
                    FractalImage.Source = bmp;
                });
            }
            catch (OperationCanceledException)
            {
                // нормальная ситуация — пришёл новый рендер
            }
            catch (Exception ex)
            {
                // покажем ошибку на экране, чтобы не гадать
                await Dispatcher.UIThread.InvokeAsync(() => { InfoText.Text = "Render error: " + ex.Message; });
            }
        }, token);
    }
}