using System;
using System.Collections.Generic;

namespace towers;

public static class HanoiSolver
{
    public static void Solve(int n, int from, int to, int aux, Action<int, int> onMove)
    {
        if (n <= 0) return;
        Solve(n - 1, from, aux, to, onMove);
        onMove(from, to);
        Solve(n - 1, aux, to, from, onMove);
    }

    public static List<(int from, int to)> GenerateMoves(int n, int from = 0, int to = 2, int aux = 1)
    {
        var moves = new List<(int, int)>();
        Solve(n, from, to, aux, (f, t) => moves.Add((f, t)));
        return moves;
    }
}