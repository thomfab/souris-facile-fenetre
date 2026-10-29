using System.Text.Json;

namespace SourisFacile;

// Where an app's windows last were on a display, with the display's work
// area at the time, to adapt the frame if the display changed since (a new
// resolution or scale, the taskbar moved).
public sealed record RememberedFrame(Rect Frame, Rect Area)
{
    // The frame on the same display, whose work area is now `area`.
    public Rect Restored(Rect area, bool keepSize) => Geometry.Transfer(Frame, Area, area, keepSize);
}

// The remembered frames, by display ID then by app. Windows gives windows no
// ID that survives an app restart, so they're per app (its executable name).
public sealed class WindowMemory
{
    private readonly Dictionary<string, Dictionary<string, RememberedFrame>> frames;

    public WindowMemory() : this(new Dictionary<string, Dictionary<string, RememberedFrame>>()) { }

    private WindowMemory(Dictionary<string, Dictionary<string, RememberedFrame>> frames)
    {
        this.frames = frames;
    }

    public bool IsEmpty => frames.Values.All(apps => apps.Count == 0);

    public RememberedFrame? Frame(string app, string display) =>
        frames.TryGetValue(display, out var apps) && apps.TryGetValue(app, out var frame) ? frame : null;

    // How many apps have a remembered frame on the display.
    public int Count(string display) => frames.TryGetValue(display, out var apps) ? apps.Count : 0;

    // Each returns a new memory, leaving this one as it was.
    public WindowMemory Forget(string display)
    {
        var copy = Copy();
        copy.Remove(display);
        return new WindowMemory(copy);
    }

    public WindowMemory Remember(Rect frame, Rect area, string app, string display)
    {
        var copy = Copy();
        if (!copy.TryGetValue(display, out var apps)) copy[display] = apps = [];
        apps[app] = new RememberedFrame(frame, area);
        return new WindowMemory(copy);
    }

    private Dictionary<string, Dictionary<string, RememberedFrame>> Copy() =>
        frames.ToDictionary(entry => entry.Key, entry => new Dictionary<string, RememberedFrame>(entry.Value));

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static WindowMemory Parse(string json)
    {
        try
        {
            var frames = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, RememberedFrame>>>(json, Json);
            return frames == null ? new WindowMemory() : new WindowMemory(frames);
        }
        catch (JsonException)
        {
            return new WindowMemory();
        }
    }

    public string ToJson() => JsonSerializer.Serialize(frames, Json);
}

public sealed class WindowMemoryStore(string path)
    : Store<WindowMemory>(path, WindowMemory.Parse, m => m.ToJson(), () => new WindowMemory());
