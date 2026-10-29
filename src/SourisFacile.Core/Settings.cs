using System.Text.Json;
using System.Text.Json.Serialization;

namespace SourisFacile;

[Flags]
public enum Modifier
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
    Win = 8,
}

public static class Modifiers
{
    public static readonly Modifier[] All = [Modifier.Shift, Modifier.Control, Modifier.Alt, Modifier.Win];

    public static string Label(Modifier modifier) => modifier switch
    {
        Modifier.Shift => "Shift",
        Modifier.Control => "Ctrl",
        Modifier.Alt => "Alt",
        Modifier.Win => "Windows key",
        _ => modifier.ToString(),
    };

    private static string Name(Modifier modifier) => modifier.ToString().ToLowerInvariant();

    // Modifier sets are stored as comma-separated names, e.g. "win,alt".
    public static Modifier Parse(string? value)
    {
        var result = Modifier.None;
        foreach (var name in (value ?? "").Split(','))
        {
            var match = All.FirstOrDefault(m => Name(m) == name.Trim());
            result |= match;
        }
        return result;
    }

    public static string Format(Modifier modifiers) =>
        string.Join(",", All.Where(m => modifiers.HasFlag(m)).Select(Name));

    private static int Count(Modifier modifiers) => All.Count(m => modifiers.HasFlag(m));

    // Problems with the move and resize combinations, to show in the settings.
    public static List<string> Warnings(string move, string resize)
    {
        var moveKeys = Parse(move);
        var resizeKeys = Parse(resize);
        var warnings = new List<string>();
        if (moveKeys != Modifier.None && moveKeys == resizeKeys)
        {
            warnings.Add("Move and Resize use the same keys, so only resizing works.");
        }
        foreach (var (name, keys) in new[] { ("Move", moveKeys), ("Resize", resizeKeys) })
        {
            if (Count(keys) == 1)
            {
                warnings.Add($"{name} uses {Label(keys)} alone, which is held for ordinary shortcuts and typing. Add another key.");
            }
        }
        foreach (var (name, keys) in new[] { ("Move", moveKeys), ("Resize", resizeKeys) })
        {
            // AltGr is reported as Ctrl + Alt.
            if (keys.HasFlag(Modifier.Control | Modifier.Alt) && !keys.HasFlag(Modifier.Win))
            {
                warnings.Add($"{name} uses Ctrl + Alt, which is also AltGr on many keyboard layouts: typing characters such as @ or € would grab windows. Add the Windows key.");
            }
        }
        return warnings;
    }
}

public static class Defaults
{
    public const string MoveModifiers = "alt,win";
    public const string ResizeModifiers = "control,alt,win";
    public const int SnapThreshold = 16;
    public const int GridMin = 1;
    public const int GridMax = 48;
    public const int SnapMax = 200;

    public static int ClampGrid(int count) => Math.Clamp(count, GridMin, GridMax);
}

public sealed record GridPreset(string Name, int Columns, int Rows)
{
    public string Label => $"{Name} — {Columns} × {Rows}";

    public static readonly GridPreset[] All =
    [
        new("Laptop 13–14″", 2, 2),
        new("Laptop 15–16″", 3, 2),
        new("24–27″", 4, 3),
        new("Ultrawide", 6, 2),
        new("Ultrawide, fine", 8, 4),
        new("Portrait", 2, 3),
    ];

    // A grid that suits a display of this size (in pixels at 100% scale),
    // for displays whose grid was never set.
    public static (int Columns, int Rows) Sensible(double width, double height)
    {
        if (width <= 0 || height <= 0) return (4, 3);
        if (height > width) return (2, 3);
        if (width / height >= 2.1) return (8, 4);  // Ultrawide
        if (width < 1600) return (2, 2);  // 13–14″ laptops
        if (width < 2000) return (3, 2);  // 15–16″ laptops, 1080p
        return (4, 3);
    }
}

// What the held modifier keys do.
public enum KeyAction { None, Move, Resize }

// The grid of one display, remembered by the display's ID so it applies
// again whenever that display is plugged back in.
public sealed record DisplayGrid(string Name, int Columns, int Rows);

// A snapshot of the settings. Changes make a new one.
public sealed record Settings
{
    public bool Enabled { get; init; } = true;
    public string MoveModifiers { get; init; } = Defaults.MoveModifiers;
    public string ResizeModifiers { get; init; } = Defaults.ResizeModifiers;
    public bool RaiseWindow { get; init; }
    public bool ResizeNearestCorner { get; init; } = true;
    // In pixels at 100% scale.
    public int SnapThreshold { get; init; } = Defaults.SnapThreshold;
    public bool SnapToWindows { get; init; } = true;
    // Keyed by display ID. Version 0.1 also saved a grid switch and a
    // default grid size, ignored now that every display has a grid.
    public IReadOnlyDictionary<string, DisplayGrid> DisplayGrids { get; init; } = new Dictionary<string, DisplayGrid>();
    public bool ShowGrid { get; init; } = true;
    public bool DimWindows { get; init; } = true;
    public bool SendWithScroll { get; init; } = true;
    public bool SendWithArrows { get; init; } = true;
    // For touchpads set to "Down motion scrolls down".
    public bool ReverseSwipes { get; init; }
    public bool RememberFrames { get; init; } = true;

    [JsonIgnore] public Modifier MoveKeys => Modifiers.Parse(MoveModifiers);
    [JsonIgnore] public Modifier ResizeKeys => Modifiers.Parse(ResizeModifiers);

    // `held` must match a combination exactly.
    public KeyAction Action(Modifier held)
    {
        if (ResizeKeys != Modifier.None && held == ResizeKeys) return KeyAction.Resize;
        if (MoveKeys != Modifier.None && held == MoveKeys) return KeyAction.Move;
        return KeyAction.None;
    }

    // The grid of a display `width` × `height` large (in pixels at 100%
    // scale), which suits its size if it was never set.
    public (int Columns, int Rows) Grid(string? displayId, double width, double height)
    {
        if (displayId != null && DisplayGrids.TryGetValue(displayId, out var grid))
        {
            return (Defaults.ClampGrid(grid.Columns), Defaults.ClampGrid(grid.Rows));
        }
        return GridPreset.Sensible(width, height);
    }

    public Settings WithDisplayGrid(string displayId, DisplayGrid? grid)
    {
        var grids = new Dictionary<string, DisplayGrid>(DisplayGrids);
        if (grid == null) grids.Remove(displayId); else grids[displayId] = grid;
        return this with { DisplayGrids = grids };
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower,
    };

    public static Settings Parse(string json)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(json, Json) ?? new Settings();
            return settings with
            {
                SnapThreshold = Math.Clamp(settings.SnapThreshold, 0, Defaults.SnapMax),
                DisplayGrids = settings.DisplayGrids ?? new Dictionary<string, DisplayGrid>(),
            };
        }
        catch (JsonException)
        {
            return new Settings();
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);
}

// Holds a value, saved as JSON in a file.
public class Store<T> where T : class
{
    private readonly string path;
    private readonly Func<T, string> serialize;
    private T current;

    public Store(string path, Func<string, T> parse, Func<T, string> serialize, Func<T> empty)
    {
        this.path = path;
        this.serialize = serialize;
        IsNew = !File.Exists(path);
        current = empty();
        if (!IsNew)
        {
            try
            {
                current = parse(File.ReadAllText(path));
            }
            catch (IOException) { }
        }
    }

    // Whether there was no file yet, i.e. the first launch.
    public bool IsNew { get; }

    // Safe to read from any thread.
    public T Current => Volatile.Read(ref current);

    public event Action? Changed;

    public void Update(Func<T, T> change)
    {
        Volatile.Write(ref current, change(Current));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, serialize(Current));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        Changed?.Invoke();
    }
}

public sealed class SettingsStore(string path)
    : Store<Settings>(path, Settings.Parse, s => s.ToJson(), () => new Settings());
