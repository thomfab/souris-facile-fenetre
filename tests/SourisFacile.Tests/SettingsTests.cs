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
    public void DisplayGridsOverrideTheDefault()
    {
        var settings = new Settings().WithDisplayGrid("wide", new DisplayGrid("Ultrawide", 6, 2));
        Assert.Equal((6, 2), settings.GridSize("wide"));
        Assert.Equal((4, 3), settings.GridSize("laptop"));
        Assert.Equal((4, 3), settings.GridSize(null));
        Assert.Equal((4, 3), settings.WithDisplayGrid("wide", null).GridSize("wide"));
    }

    [Fact]
    public void SettingsSurviveARoundTrip()
    {
        var settings = new Settings { GridEnabled = true, MoveModifiers = "shift,win" }
            .WithDisplayGrid("wide", new DisplayGrid("Ultrawide", 8, 3));
        var parsed = Settings.Parse(settings.ToJson());
        Assert.True(parsed.GridEnabled);
        Assert.Equal("shift,win", parsed.MoveModifiers);
        Assert.Equal(new DisplayGrid("Ultrawide", 8, 3), parsed.DisplayGrids["wide"]);
    }

    [Fact]
    public void ParsingClampsAndFallsBack()
    {
        var parsed = Settings.Parse("""{ "grid-columns": 500, "snap-threshold": -3 }""");
        Assert.Equal(Defaults.GridMax, parsed.GridColumns);
        Assert.Equal(0, parsed.SnapThreshold);
        Assert.True(parsed.Enabled);
        Assert.Equal(new Settings().MoveModifiers, Settings.Parse("not json").MoveModifiers);
    }
}
