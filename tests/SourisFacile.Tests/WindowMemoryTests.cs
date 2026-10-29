namespace SourisFacile.Tests;

public class WindowMemoryTests
{
    private const string Laptop = "laptop";
    private const string Office = "office";
    private static readonly Rect Area = new(0, 0, 1920, 1152);

    [Fact]
    public void RemembersPerAppAndDisplay()
    {
        var memory = new WindowMemory();
        Assert.True(memory.IsEmpty);
        var frame = new Rect(0, 0, 960, 1152);
        memory = memory.Remember(frame, Area, "chrome.exe", Laptop);
        Assert.False(memory.IsEmpty);
        Assert.Equal(new RememberedFrame(frame, Area), memory.Frame("chrome.exe", Laptop));
        Assert.Null(memory.Frame("chrome.exe", Office));
        Assert.Null(memory.Frame("outlook.exe", Laptop));
        Assert.Equal(1, memory.Count(Laptop));
        Assert.Equal(0, memory.Count(Office));
    }

    [Fact]
    public void TheLastFrameWins()
    {
        var memory = new WindowMemory()
            .Remember(new Rect(0, 0, 100, 100), Area, "app.exe", Laptop)
            .Remember(new Rect(50, 50, 200, 200), Area, "app.exe", Laptop);
        Assert.Equal(new Rect(50, 50, 200, 200), memory.Frame("app.exe", Laptop)?.Frame);
    }

    [Fact]
    public void ChangesLeaveTheOriginalAlone()
    {
        var memory = new WindowMemory().Remember(new Rect(0, 0, 100, 100), Area, "app.exe", Laptop);
        Assert.True(memory.Forget(Laptop).IsEmpty);
        Assert.False(memory.IsEmpty);
    }

    [Fact]
    public void EncodingRoundTrips()
    {
        var memory = new WindowMemory().Remember(new Rect(10, 10, 700, 500), Area, "app.exe", Office);
        var parsed = WindowMemory.Parse(memory.ToJson());
        Assert.Equal(memory.Frame("app.exe", Office), parsed.Frame("app.exe", Office));
        Assert.DoesNotContain("MinX", memory.ToJson());
        Assert.True(WindowMemory.Parse("garbage").IsEmpty);
    }

    [Fact]
    public void RestoringOnAnUnchangedDisplayGivesTheSameFrame()
    {
        var remembered = new RememberedFrame(new Rect(100, 100, 700, 500), Area);
        Assert.Equal(remembered.Frame, remembered.Restored(Area, keepSize: false));
    }

    [Fact]
    public void RestoringFollowsAChangedDisplay()
    {
        // The taskbar went from the bottom (1152 px high) to the left (80 px wide).
        var remembered = new RememberedFrame(new Rect(0, 0, 960, 1152), Area);
        var now = new Rect(80, 0, 1840, 1200);
        var restored = remembered.Restored(now, keepSize: false);
        Assert.Equal(80, restored.MinX);
        Assert.Equal(920, restored.Width);
        Assert.Equal(1200, restored.Height);
    }
}
