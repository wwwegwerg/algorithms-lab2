using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace fractal;

public static class FractalRenderer
{
    public static Task<WriteableBitmap> RenderSierpinskiCarpetAsync(
        int width, int height,
        decimal centerX, decimal centerY,
        decimal scale, int levels,
        CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var bmp = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);

            using var fb = bmp.Lock();
            unsafe
            {
                byte* basePtr = (byte*)fb.Address.ToPointer();
                int stride = fb.RowBytes;

                decimal halfW = width / 2m;
                decimal halfH = height / 2m;

                int effLevels = FractalMath.EffectiveLevels(scale, levels);

                Parallel.For(0, height, new ParallelOptions { CancellationToken = ct }, y =>
                {
                    ct.ThrowIfCancellationRequested();
                    byte* row = basePtr + y * stride;
                    decimal wy = centerY + ((y + 0.5m) - halfH) * scale;

                    for (int x = 0; x < width; x++)
                    {
                        decimal wx = centerX + ((x + 0.5m) - halfW) * scale;

                        int depthHit = FractalMath.HoleDepth(wx, wy, effLevels);

                        byte r, g, b;
                        if (depthHit >= 0)
                        {
                            r = 16;
                            g = 18;
                            b = 24;
                        }
                        else
                        {
                            int dither = ((x ^ y) & 3) - 1;
                            int val = Math.Clamp(235 + dither, 0, 255);
                            r = g = b = (byte)val;
                        }

                        int off = x * 4;
                        row[off + 0] = b;
                        row[off + 1] = g;
                        row[off + 2] = r;
                        row[off + 3] = 255;
                    }
                });
            }

            return bmp;
        }, ct);
    }
}