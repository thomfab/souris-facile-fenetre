namespace SourisFacile.Tests;

public class SendToDisplayTests
{
    // A laptop, a wide monitor to its right, and a monitor above it.
    private static readonly Rect Laptop = new(0, 0, 1920, 1200);
    private static readonly Rect Wide = new(1920, -400, 3440, 1440);
    private static readonly Rect Above = new(0, -1080, 1920, 1080);
    private static readonly Rect[] Displays = [Laptop, Wide, Above];

    [Fact]
    public void DirectionFollowsTheMainAxis()
    {
        Assert.Equal(Direction.Right, Directions.FromMovement(50, -20));
        Assert.Equal(Direction.Down, Directions.FromMovement(-10, 40));
        Assert.Equal(Direction.Up, Directions.FromArrowKey(0x26));
        Assert.Null(Directions.FromArrowKey(0x41));
    }

    [Fact]
    public void DisplayTowardADirection()
    {
        Assert.Equal(1, Geometry.DisplayToward(Direction.Right, 0, Displays));
        Assert.Equal(2, Geometry.DisplayToward(Direction.Up, 0, Displays));
        Assert.Null(Geometry.DisplayToward(Direction.Left, 0, Displays));
        Assert.Equal(0, Geometry.DisplayToward(Direction.Left, 1, Displays));
        Assert.Equal(0, Geometry.DisplayToward(Direction.Down, 2, Displays));
    }

    [Fact]
    public void NextAndPreviousDisplaysWrapAround()
    {
        // Left to right, then top to bottom: above, laptop, wide.
        Assert.Equal(1, Geometry.DisplayAfter(0, 1, Displays));
        Assert.Equal(2, Geometry.DisplayAfter(1, 1, Displays));
        Assert.Equal(2, Geometry.DisplayAfter(0, -1, Displays));
        Assert.Null(Geometry.DisplayAfter(0, 1, [Laptop]));
    }

    [Fact]
    public void TransferKeepsTheRelativePlaceAndSize()
    {
        var from = new Rect(0, 0, 1000, 800);
        var to = new Rect(1000, 0, 2000, 1000);
        var leftHalf = new Rect(0, 0, 500, 800);
        Assert.Equal(new Rect(1000, 0, 1000, 1000), Geometry.Transfer(leftHalf, from, to, keepSize: false));
    }

    [Fact]
    public void TransferCanKeepTheSize()
    {
        var from = new Rect(0, 0, 1000, 800);
        var to = new Rect(1000, 0, 2000, 1000);
        Assert.Equal(new Rect(1350, 150, 300, 200),
            Geometry.Transfer(new Rect(100, 100, 300, 200), from, to, keepSize: true));
    }

    [Fact]
    public void TransferKeepsTheTopLeftOfAWindowTooBigToFit()
    {
        var from = new Rect(0, 0, 1000, 800);
        var small = new Rect(1000, 0, 500, 400);
        Assert.Equal(new Rect(1000, 0, 1000, 800), Geometry.Transfer(from, from, small, keepSize: true));
    }

    [Fact]
    public void FitToGridMovesEdgesToTheNearestLines()
    {
        var area = new Rect(0, 0, 1200, 900);
        Assert.Equal(new Rect(300, 0, 300, 600), Geometry.FitToGrid(new Rect(290, 10, 320, 580), area, 4, 3));
    }

    [Fact]
    public void FitToGridKeepsAtLeastOneCell()
    {
        var area = new Rect(0, 0, 1200, 900);
        Assert.Equal(new Rect(300, 300, 300, 300), Geometry.FitToGrid(new Rect(310, 310, 20, 20), area, 4, 3));
        Assert.Equal(new Rect(900, 600, 300, 300), Geometry.FitToGrid(new Rect(1190, 890, 5, 5), area, 4, 3));
    }

    [Fact]
    public void ASwipeGivesOneDirectionOnceFarEnough()
    {
        var swipe = new SwipeTracker();
        Assert.Null(swipe.Feed(5, 0, 0));
        Assert.Null(swipe.Feed(30, 10, 0.01));
        Assert.Equal(Direction.Right, swipe.Feed(40, 0, 0.02));
        // The rest of the same swipe does nothing.
        Assert.Null(swipe.Feed(100, 0, 0.03));
        // A new swipe, after a pause, counts again.
        Assert.Null(swipe.Feed(0, -30, 1));
        Assert.Equal(Direction.Up, swipe.Feed(0, -40, 1.01));
    }

    [Fact]
    public void SwipesAreSeparatedByAPause()
    {
        var swipe = new SwipeTracker();
        Assert.Equal(Direction.Left, swipe.Feed(-70, 0, 0));
        Assert.Null(swipe.Feed(-70, 0, 0.1));
        Assert.Equal(Direction.Left, swipe.Feed(-70, 0, 1));
    }
}
