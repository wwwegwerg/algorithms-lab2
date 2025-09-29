using System;
using Avalonia;
using Avalonia.Controls;

namespace fractal;

static class DpiHelpers
{
    // Берём масштаб рендеринга (DPI) с TopLevel (окна)
    public static double GetRenderScaling(this Visual v) =>
        TopLevel.GetTopLevel(v)?.RenderScaling ?? 1.0;

    // Переводим координаты в пиксели с учётом DPI
    public static PixelPoint ToPixelPoint(this Point p, Visual v)
    {
        var s = v.GetRenderScaling();
        return new PixelPoint((int)Math.Round(p.X * s), (int)Math.Round(p.Y * s));
    }
}

public static class FractalMath
{
    // Нормализуем координату к диапазону [-1.5; 1.5) в decimal
    public static decimal Wrap3(decimal v)
    {
        decimal t = v + 1.5m;
        t -= 3m * Math.Floor(t / 3m);
        return t - 1.5m;
    }

    public static int HoleDepth(decimal x, decimal y, int depthLeft)
    {
        if (depthLeft <= 0) return -1;

        x = Wrap3(x);
        y = Wrap3(y);

        if (Math.Abs(x) < 0.5m && Math.Abs(y) < 0.5m)
            return 0;

        int sub = HoleDepth(x * 3m, y * 3m, depthLeft - 1);
        return sub < 0 ? -1 : sub + 1;
    }

    public static int EffectiveLevels(decimal scale, int requested)
    {
        if (scale <= 0) return Math.Clamp(requested, 1, 60);
        // Логарифм только для оценки уровня; переводим в double — это НЕ влияет на точность рендера
        double vis = Math.Floor(Math.Log(1.0 / (double)scale, 3.0));
        int eff = (int)vis + 1;
        return Math.Clamp(eff, 1, requested);
    }
}