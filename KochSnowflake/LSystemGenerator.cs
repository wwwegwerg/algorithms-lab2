using System;
using System.Collections.Generic;
using Avalonia;

namespace KochSnowflake;

/// <summary>
/// Простой L-системный конструктор генераторного полилиния.
/// Поддерживает символы:
///   F — шаг вперёд с рисованием;
///   + — поворот влево на угол;
///   - — поворот вправо на угол;
///   Любые другие символы участвуют в переписывании, но игнорируются при рисовании.
/// </summary>
public static class LSystemGenerator
{
    public static List<Point> BuildNormalizedGenerator(
        string axiom,
        IEnumerable<string> ruleLines,
        double angleDegrees,
        int expandSteps)
    {
        if (expandSteps < 1) expandSteps = 1;

        var rules = ParseRules(ruleLines);
        string current = axiom ?? "F";

        for (var i = 0; i < expandSteps; i++)
            current = Rewrite(current, rules);

        var poly = TurtleToPolyline(current, angleDegrees);
        var normalized = Normalize(poly);

        // Генератор должен быть хотя бы из двух точек
        if (normalized.Count < 2)
            throw new InvalidOperationException("Порожденный генератор пуст или вырожден.");

        return normalized;
    }

    private static Dictionary<char, string> ParseRules(IEnumerable<string> lines)
    {
        var dict = new Dictionary<char, string>();
        foreach (var raw in lines ?? [])
        {
            var line = (raw ?? "").Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // Поддержка форматов: F=F+F--F+F ИЛИ F -> F+F--F+F
            var parts = line.Split(["->", "="], StringSplitOptions.None);
            if (parts.Length != 2) continue;

            var left = parts[0].Trim();
            var right = parts[1].Trim();

            if (left.Length != 1) continue;
            var key = left[0];

            dict[key] = right;
        }

        return dict;
    }

    private static string Rewrite(string s, Dictionary<char, string> rules)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var result = new System.Text.StringBuilder(s.Length * 2);
        foreach (var ch in s)
        {
            if (rules.TryGetValue(ch, out var repl)) result.Append(repl);
            else result.Append(ch);
        }

        return result.ToString();
    }

    private static List<Point> TurtleToPolyline(string commands, double angleDeg)
    {
        var ang = 0.0; // направление в радианах, 0 — вдоль +X
        var step = 1.0;
        var angle = angleDeg * Math.PI / 180.0;

        var p = new Point(0, 0);
        var pts = new List<Point> { p };

        foreach (var ch in commands)
        {
            switch (ch)
            {
                case 'F':
                    p = new Point(p.X + step * Math.Cos(ang), p.Y + step * Math.Sin(ang));
                    pts.Add(p);
                    break;
                case '+':
                    ang -= angle;
                    break;
                case '-':
                    ang += angle;
                    break;
                default:
                    // прочие символы игнорируем на этапе рисования
                    break;
            }
        }

        return pts;
    }

    /// <summary>
    /// Переносит начало в (0,0), поворачивает так, чтобы конец лёг на ось X,
    /// и масштабирует так, чтобы длина была 1. Конец становится близко к (1,0).
    /// </summary>
    private static List<Point> Normalize(IReadOnlyList<Point> pts)
    {
        if (pts.Count < 2)
            return [];

        var a = pts[0];
        var b = pts[^1];
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-12)
            throw new InvalidOperationException("Генератор замкнут или вырожден (длина = 0).");

        double cos = dx / len;
        double sin = dy / len;

        var outPts = new List<Point>(pts.Count);
        foreach (var p in pts)
        {
            // перенос
            double x = p.X - a.X;
            double y = p.Y - a.Y;
            // поворот на -theta (чтобы вектор лег на +X)
            double xr = x * cos + y * sin;
            double yr = -x * sin + y * cos;
            // масштаб к длине 1
            outPts.Add(new Point(xr / len, yr / len));
        }

        // Принудительно "почистим" плавающие артефакты конца
        outPts[^1] = new Point(1.0, 0.0);
        outPts[0] = new Point(0.0, 0.0);
        return outPts;
    }
}