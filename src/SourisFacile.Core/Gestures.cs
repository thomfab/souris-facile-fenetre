namespace SourisFacile;

// A direction on screen, in virtual screen coordinates (y going down).
public enum Direction { Left, Right, Up, Down }

public static class Directions
{
    // The main direction of a movement.
    public static Direction FromMovement(double dx, double dy) =>
        Math.Abs(dx) >= Math.Abs(dy)
            ? (dx > 0 ? Direction.Right : Direction.Left)
            : (dy > 0 ? Direction.Down : Direction.Up);

    // From a Windows virtual-key code.
    public static Direction? FromArrowKey(int vk) => vk switch
    {
        0x25 => Direction.Left,
        0x26 => Direction.Up,
        0x27 => Direction.Right,
        0x28 => Direction.Down,
        _ => null,
    };

    public static Point Vector(this Direction direction) => direction switch
    {
        Direction.Left => new Point(-1, 0),
        Direction.Right => new Point(1, 0),
        Direction.Up => new Point(0, -1),
        _ => new Point(0, 1),
    };
}

// Turns a swipe, a stream of small scroll events from a touchpad, into a
// single direction once the fingers have travelled far enough. Windows
// doesn't say when a swipe begins or ends, so a pause separates them.
public sealed class SwipeTracker
{
    public const double Distance = 60;
    public const double Pause = 0.3;

    private double dx;
    private double dy;
    private bool fired;
    private double lastTime = double.NegativeInfinity;

    // `dx` and `dy` follow the fingers. Returns a direction at most once per swipe.
    public Direction? Feed(double dx, double dy, double time)
    {
        if (time - lastTime > Pause) Reset();
        lastTime = time;

        if (fired) return null;
        this.dx += dx;
        this.dy += dy;
        if (Math.Sqrt(this.dx * this.dx + this.dy * this.dy) < Distance) return null;
        fired = true;
        return Directions.FromMovement(this.dx, this.dy);
    }

    public void Reset()
    {
        dx = 0;
        dy = 0;
        fired = false;
        lastTime = double.NegativeInfinity;
    }
}
