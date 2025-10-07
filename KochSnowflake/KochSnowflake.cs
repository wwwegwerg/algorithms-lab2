using System;
using System.Collections.Generic;
using Avalonia;

namespace KochSnowflake;

public static class KochSnowflake
{
    private const double Cos60 = 0.5;
    private static readonly double Sin60 = Math.Sqrt(3) / 2.0;

    public static List<Point> Generate(int iterations)
    {
        // CCW равносторонний треугольник (база)
        var a = new Point(0, 0);
        var b = new Point(1, 0);
        var c = new Point(0.5, Math.Sqrt(3) / 2.0);

        var pts = new List<Point>(capacity: 3 * (int)Math.Pow(4, Math.Max(iterations, 0)) + 1);

        SubdivideEdge(a, b, iterations, pts);
        SubdivideEdge(b, c, iterations, pts);
        SubdivideEdge(c, a, iterations, pts);
        pts.Add(a); // замкнуть
        return pts;
    }

    private static void SubdivideEdge(Point a, Point b, int depth, List<Point> output)
    {
        if (depth == 0)
        {
            output.Add(a);
            return;
        }

        var vx = (b.X - a.X) / 3.0;
        var vy = (b.Y - a.Y) / 3.0;

        var p1 = new Point(a.X + vx, a.Y + vy);
        var p2 = new Point(a.X + 2 * vx, a.Y + 2 * vy);

        // Повернуть (vx,vy) на -60°, чтобы "бугорок" был СНАРУЖИ CCW-полигона
        var rx = vx * Cos60 + vy * Sin60; // cos*vx - (-sin)*vy
        var ry = -vx * Sin60 + vy * Cos60; // (-sin)*vx + cos*vy
        var peak = new Point(p1.X + rx, p1.Y + ry);

        SubdivideEdge(a, p1, depth - 1, output);
        SubdivideEdge(p1, peak, depth - 1, output);
        SubdivideEdge(peak, p2, depth - 1, output);
        SubdivideEdge(p2, b, depth - 1, output);
    }
}