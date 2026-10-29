namespace SourisFacile.Tests;

public class GeometryTests
{
    [Fact]
    public void DivideIncludesBothEnds()
    {
        Assert.Equal([0, 25, 50, 75, 100], Geometry.Divide(0, 100, 4));
        Assert.Equal([10, 100], Geometry.Divide(10, 90, 1));
    }

    [Fact]
    public void DivideRoundsToWholePixels()
    {
        Assert.Equal([0, 33, 67, 100], Geometry.Divide(0, 100, 3));
    }

    [Fact]
    public void SnapDeltaPicksTheClosestLine()
    {
        Assert.Equal(-10, Geometry.SnapDelta([10, 110], [0, 200], 16));
        Assert.Equal(5, Geometry.SnapDelta([10, 195], [0, 200], 16));
    }

    [Fact]
    public void SnapDeltaIgnoresLinesBeyondTheThreshold()
    {
        Assert.Equal(0, Geometry.SnapDelta([50], [0, 100], 16));
        Assert.Equal(-16, Geometry.SnapDelta([16], [0], 16));
    }

    [Fact]
    public void FrameDistanceIsTheLargestDifference()
    {
        var a = new Rect(0, 0, 100, 100);
        Assert.Equal(0, Geometry.FrameDistance(a, a));
        Assert.Equal(3, Geometry.FrameDistance(a, new Rect(1, -3, 102, 100)));
    }
}
