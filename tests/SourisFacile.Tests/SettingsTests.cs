namespace SourisFacile.Tests;

public class SettingsTests
{
    [Fact]
    public void ParseIgnoresUnknownNames()
    {
        Assert.Equal(Modifier.Control | Modifier.Alt, Modifiers.Parse("control,bogus,alt"));
        Assert.Equal(Modifier.None, Modifiers.Parse(""));
    }

    [Fact]
    public void FormatUsesAStableOrder()
    {
        Assert.Equal("control,alt,win", Modifiers.Format(Modifier.Win | Modifier.Control | Modifier.Alt));
        Assert.Equal("", Modifiers.Format(Modifier.None));
    }

    [Fact]
    public void DefaultsAreAltWindowsToMove()
    {
        Assert.Equal(Modifier.Alt | Modifier.Win, Modifiers.Parse(Defaults.MoveModifiers));
        Assert.Equal(Modifier.Control | Modifier.Alt | Modifier.Win, Modifiers.Parse(Defaults.ResizeModifiers));
        Assert.Empty(Modifiers.Warnings(Defaults.MoveModifiers, Defaults.ResizeModifiers));
    }

    [Fact]
    public void WarnsAboutIdenticalCombinations()
    {
        Assert.Single(Modifiers.Warnings("alt,win", "win,alt"));
        // Both turned off is a way to disable the feature, not a conflict.
        Assert.Empty(Modifiers.Warnings("", ""));
    }

    [Fact]
    public void WarnsAboutASingleKey()
    {
        Assert.Single(Modifiers.Warnings("win", "control,alt,win"));
        Assert.Equal(3, Modifiers.Warnings("shift", "shift").Count);
    }

    [Fact]
    public void WarnsAboutAltGr()
    {
        Assert.Single(Modifiers.Warnings("control,alt", "control,alt,win"));
        Assert.Equal(2, Modifiers.Warnings("control,alt", "shift,control,alt").Count);
    }

    [Fact]
    public void KeyActionsMatchExactly()
    {
        var s = new Settings();
        Assert.Equal(KeyAction.Move, s.Action(Modifier.Alt | Modifier.Win));
        Assert.Equal(KeyAction.Resize, s.Action(Modifier.Control | Modifier.Alt | Modifier.Win));
        Assert.Equal(KeyAction.None, s.Action(Modifier.Shift | Modifier.Alt | Modifier.Win));
        Assert.Equal(KeyAction.None, s.Action(Modifier.Win));
        Assert.Equal(KeyAction.None, s.Action(Modifier.None));
    }

    [Fact]
    public void TurnedOffCombinationsNeverMatch()
    {
        Assert.Equal(KeyAction.None, new Settings { MoveModifiers = "", ResizeModifiers = "" }.Action(Modifier.None));
        Assert.Equal(KeyAction.Move, new Settings { ResizeModifiers = "" }.Action(Modifier.Alt | Modifier.Win));
    }

    [Fact]
    public void ADisplayUsesItsOwnGrid()
    {
        var settings = new Settings().WithDisplayGrid("wide", new DisplayGrid("DELL U3421WE", 6, 2));
        Assert.Equal((6, 2), settings.Grid("wide", 3440, 1440));
        Assert.Equal((Defaults.GridMax, 1),
            new Settings().WithDisplayGrid("broken", new DisplayGrid("Broken", 500, 0)).Grid("broken", 1920, 1080));
    }

    [Fact]
    public void OtherDisplaysGetAGridSuitingTheirSize()
    {
        var settings = new Settings().WithDisplayGrid("wide", new DisplayGrid("DELL U3421WE", 6, 2));
        Assert.Equal((8, 4), settings.Grid("another display", 3440, 1440));
        Assert.Equal((2, 2), settings.Grid(null, 1440, 900));
        Assert.Equal((8, 4), settings.WithDisplayGrid("wide", null).Grid("wide", 3440, 1440));
    }

    [Theory]
    [InlineData(1440, 900, 2, 2)]    // 13–14″ laptop at 200%
    [InlineData(1536, 864, 2, 2)]    // 1080p laptop at 125%
    [InlineData(1707, 1067, 3, 2)]   // 16″ laptop at 150%
    [InlineData(1920, 1080, 3, 2)]
    [InlineData(2560, 1440, 4, 3)]
    [InlineData(3440, 1440, 8, 4)]   // 21:9
    [InlineData(5120, 1440, 8, 4)]   // 32:9
    [InlineData(1440, 2560, 2, 3)]   // Portrait
    [InlineData(0, 0, 4, 3)]
    public void SensibleGrids(double width, double height, int columns, int rows)
    {
        Assert.Equal((columns, rows), GridPreset.Sensible(width, height));
    }

    [Fact]
    public void SensibleGridsAreAmongThePresets()
    {
        foreach (var (width, height) in new[] { (1440, 900), (1920, 1080), (2560, 1440), (3440, 1440), (1440, 2560) })
        {
            var (columns, rows) = GridPreset.Sensible(width, height);
            Assert.Contains(GridPreset.All, p => p.Columns == columns && p.Rows == rows);
        }
    }

    [Fact]
    public void SettingsSurviveARoundTrip()
    {
        var settings = new Settings { RememberFrames = false, MoveModifiers = "shift,win" }
            .WithDisplayGrid("wide", new DisplayGrid("Ultrawide", 8, 4));
        var parsed = Settings.Parse(settings.ToJson());
        Assert.False(parsed.RememberFrames);
        Assert.Equal("shift,win", parsed.MoveModifiers);
        Assert.Equal(new DisplayGrid("Ultrawide", 8, 4), parsed.DisplayGrids["wide"]);
    }

    [Fact]
    public void SettingsFromVersion01StillLoad()
    {
        var parsed = Settings.Parse("""
            { "grid-enabled": true, "grid-columns": 6, "grid-rows": 2, "show-grid": false,
              "display-grids": { "wide": { "name": "Ultrawide", "columns": 6, "rows": 2 } } }
            """);
        Assert.False(parsed.ShowGrid);
        Assert.True(parsed.RememberFrames);
        Assert.Equal((6, 2), parsed.Grid("wide", 3440, 1440));
    }

    [Fact]
    public void ParsingClampsAndFallsBack()
    {
        var parsed = Settings.Parse("""{ "snap-threshold": -3 }""");
        Assert.Equal(0, parsed.SnapThreshold);
        Assert.True(parsed.Enabled);
        Assert.Equal(new Settings().MoveModifiers, Settings.Parse("not json").MoveModifiers);
    }
}
