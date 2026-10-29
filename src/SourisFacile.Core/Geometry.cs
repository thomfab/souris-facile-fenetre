namespace SourisFacile;

// Everything works in virtual screen coordinates, in physical pixels: origin
// at the top-left of the primary display, y going down.

public readonly record struct Point(double X, double Y);

public readonly record struct Rect(double X, double Y, double Width, double Height)
{
    public double MinX => X;
    public double MinY => Y;
    public double MaxX => X + Width;
    public double MaxY => Y + Height;
    public double MidX => X + Width / 2;
    public double MidY => Y + Height / 2;
    public Point Origin => new(X, Y);

    public static Rect FromEdges(double left, double top, double right, double bottom) =>
        new(left, top, right - left, bottom - top);

    public bool Contains(Point p) => p.X >= MinX && p.X < MaxX && p.Y >= MinY && p.Y < MaxY;
}

public static class Geometry
{
    // Rounds half away from zero, like Swift's rounded().
    public static double Round(double value) => Math.Round(value, MidpointRounding.AwayFromZero);

    // Evenly spaced positions from start to start + length, both included.
    public static double[] Divide(double start, double length, int count) =>
        Enumerable.Range(0, count + 1).Select(i => Round(start + i * length / count)).ToArray();

    // How far apart two frames are: the largest difference of any edge or size.
    public static double FrameDistance(Rect a, Rect b) =>
        new[] { Math.Abs(a.MinX - b.MinX), Math.Abs(a.MinY - b.MinY),
                Math.Abs(a.Width - b.Width), Math.Abs(a.Height - b.Height) }.Max();

    // The shift that brings the closest of `edges` onto one of `lines`,
    // or 0 if none is within `threshold`.
    public static double SnapDelta(IEnumerable<double> edges, IReadOnlyList<double> lines, double threshold)
    {
        double best = 0;
        double bestDist = threshold;
        foreach (var edge in edges)
        {
            foreach (var line in lines)
            {
                var dist = Math.Abs(line - edge);
                if (dist <= bestDist)
                {
                    best = line - edge;
                    bestDist = dist;
                }
            }
        }
        return best;
    }

    // The display in `direction` from display `source`, among display frames:
    // the closest one that way, preferring those lined up with it.
    public static int? DisplayToward(Direction direction, int source, IReadOnlyList<Rect> displays)
    {
        var from = displays[source];
        var v = direction.Vector();
        int? best = null;
        var bestScore = double.PositiveInfinity;
        for (var index = 0; index < displays.Count; index++)
        {
            if (index == source) continue;
            var frame = displays[index];
            var dx = frame.MidX - from.MidX;
            var dy = frame.MidY - from.MidY;
            var along = dx * v.X + dy * v.Y;
            if (along <= 0) continue;
            var across = Math.Abs(dx * v.Y - dy * v.X);
            var score = along + 2 * across;
            if (score < bestScore)
            {
                best = index;
                bestScore = score;
            }
        }
        return best;
    }

    // The display `step` places after display `index`, going left to right
    // (then top to bottom) and wrapping around.
    public static int? DisplayAfter(int index, int step, IReadOnlyList<Rect> displays)
    {
        if (displays.Count <= 1) return null;
        var order = Enumerable.Range(0, displays.Count)
            .OrderBy(i => displays[i].MinX).ThenBy(i => displays[i].MinY).ToList();
        var position = order.IndexOf(index);
        var count = order.Count;
        return order[((position + step) % count + count) % count];
    }

    // Where `frame` lands when it goes from area `from` to area `to` (displays'
    // work areas): same relative place and size, fitted inside `to`.
    // With `keepSize`, for windows that can't be resized, only the place changes.
    public static Rect Transfer(Rect frame, Rect from, Rect to, bool keepSize)
    {
        var sx = to.Width / from.Width;
        var sy = to.Height / from.Height;
        double width = frame.Width, height = frame.Height;
        if (!keepSize)
        {
            width = Math.Min(width * sx, to.Width);
            height = Math.Min(height * sy, to.Height);
        }
        // The center keeps its relative place.
        var x = to.MinX + (frame.MidX - from.MinX) * sx - width / 2;
        var y = to.MinY + (frame.MidY - from.MinY) * sy - height / 2;
        x = Math.Max(Math.Min(x, to.MaxX - width), to.MinX);
        y = Math.Max(Math.Min(y, to.MaxY - height), to.MinY);
        return new Rect(Round(x), Round(y), Round(width), Round(height));
    }

    // Moves each edge of `frame` to the nearest line of the grid over `area`,
    // keeping it at least one cell wide and tall.
    public static Rect FitToGrid(Rect frame, Rect area, int columns, int rows)
    {
        static int Nearest(double value, double[] lines)
        {
            var best = 0;
            for (var i = 1; i < lines.Length; i++)
            {
                if (Math.Abs(lines[i] - value) < Math.Abs(lines[best] - value)) best = i;
            }
            return best;
        }
        static (double, double) Span(double low, double high, double[] lines)
        {
            int first = Nearest(low, lines), last = Nearest(high, lines);
            if (first == last)
            {
                if (last < lines.Length - 1) last += 1; else first -= 1;
            }
            return (lines[first], lines[last]);
        }
        var (left, right) = Span(frame.MinX, frame.MaxX, Divide(area.MinX, area.Width, columns));
        var (top, bottom) = Span(frame.MinY, frame.MaxY, Divide(area.MinY, area.Height, rows));
        return Rect.FromEdges(left, top, right, bottom);
    }
}
