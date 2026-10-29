using System.Runtime.InteropServices;
using static SourisFacile.Native;

namespace SourisFacile;

// A connected display. `Id` identifies the physical display and stays the
// same when it's unplugged and plugged back in, or after a restart. `Scale`
// is 1 at 100%.
internal sealed record Display(string Id, string Name, bool IsBuiltIn, Rect Bounds, Rect WorkArea, double Scale)
{
    // The grid of this display in `settings`, which suits its size if it was never set.
    public (int Columns, int Rows) Grid(Settings settings) =>
        settings.Grid(Id, Bounds.Width / Scale, Bounds.Height / Scale);
}

internal static class Displays
{
    // Device names (\\.\DISPLAY1) to IDs and names, which take a while to
    // read: refreshed when the displays change.
    private static Dictionary<string, (string Id, string Name, bool IsBuiltIn)>? identities;

    public static void Invalidate() => identities = null;

    // The connected displays, primary first. The work area leaves out the taskbar.
    public static List<Display> All()
    {
        var known = identities ??= ReadIdentities();
        var displays = new List<Display>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr _, ref RECT _, IntPtr _) =>
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                var (id, name, builtIn) = known.TryGetValue(info.szDevice, out var identity)
                    ? identity
                    : (info.szDevice, info.szDevice.TrimStart('\\', '.'), false);
                var scale = GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpi, out _) == 0 ? dpi / 96.0 : 1;
                displays.Add(new Display(id, name, builtIn, info.rcMonitor.ToRect(), info.rcWork.ToRect(), scale));
            }
            return true;
        }, IntPtr.Zero);
        // The primary display is the one at the origin.
        return displays.OrderBy(d => d.Bounds.MinX == 0 && d.Bounds.MinY == 0 ? 0 : 1).ToList();
    }

    // The display containing `point`, or the nearest one.
    public static Display? At(Point point)
    {
        var displays = All();
        return displays.FirstOrDefault(d => d.Bounds.Contains(point))
            ?? displays.MinBy(d => Distance(d.Bounds, point));
    }

    private static double Distance(Rect rect, Point point)
    {
        var dx = Math.Max(Math.Max(rect.MinX - point.X, 0), point.X - rect.MaxX);
        var dy = Math.Max(Math.Max(rect.MinY - point.Y, 0), point.Y - rect.MaxY);
        return dx * dx + dy * dy;
    }

    // MARK: Display configuration

    private const uint QDC_ONLY_ACTIVE_PATHS = 2;
    private const int GET_SOURCE_NAME = 1;
    private const int GET_TARGET_NAME = 2;
    // Output technologies of a laptop's own screen.
    private const int OUTPUT_INTERNAL = unchecked((int)0x80000000);
    private const int OUTPUT_LVDS = 6;
    private const int OUTPUT_DISPLAYPORT_EMBEDDED = 11;
    private const int OUTPUT_UDI_EMBEDDED = 13;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathSourceInfo
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathTargetInfo
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public int outputTechnology;
        public int rotation;
        public int scaling;
        public uint refreshNumerator;
        public uint refreshDenominator;
        public int scanLineOrdering;
        public int targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathInfo
    {
        public PathSourceInfo sourceInfo;
        public PathTargetInfo targetInfo;
        public uint flags;
    }

    // Not read: only its size matters.
    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct ModeInfo
    {
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoHeader
    {
        public int type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SourceDeviceName
    {
        public DeviceInfoHeader header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TargetDeviceName
    {
        public DeviceInfoHeader header;
        public uint flags;
        public int outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string monitorDevicePath;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint numPaths, [Out] PathInfo[] paths,
        ref uint numModes, [Out] ModeInfo[] modes, IntPtr topologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref SourceDeviceName request);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref TargetDeviceName request);

    // The monitor's device path makes the ID, and its EDID gives the name.
    private static Dictionary<string, (string, string, bool)> ReadIdentities()
    {
        var result = new Dictionary<string, (string, string, bool)>(StringComparer.OrdinalIgnoreCase);
        if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount) != 0)
        {
            return result;
        }
        var paths = new PathInfo[pathCount];
        var modes = new ModeInfo[modeCount];
        if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
        {
            return result;
        }

        foreach (var path in paths.Take((int)pathCount))
        {
            var source = new SourceDeviceName
            {
                header = new DeviceInfoHeader
                {
                    type = GET_SOURCE_NAME,
                    size = (uint)Marshal.SizeOf<SourceDeviceName>(),
                    adapterId = path.sourceInfo.adapterId,
                    id = path.sourceInfo.id,
                },
            };
            var target = new TargetDeviceName
            {
                header = new DeviceInfoHeader
                {
                    type = GET_TARGET_NAME,
                    size = (uint)Marshal.SizeOf<TargetDeviceName>(),
                    adapterId = path.targetInfo.adapterId,
                    id = path.targetInfo.id,
                },
            };
            if (DisplayConfigGetDeviceInfo(ref source) != 0 || DisplayConfigGetDeviceInfo(ref target) != 0
                || string.IsNullOrEmpty(target.monitorDevicePath))
            {
                continue;
            }
            var builtIn = target.outputTechnology is OUTPUT_INTERNAL or OUTPUT_LVDS or OUTPUT_DISPLAYPORT_EMBEDDED or OUTPUT_UDI_EMBEDDED;
            var name = !string.IsNullOrWhiteSpace(target.monitorFriendlyDeviceName) ? target.monitorFriendlyDeviceName
                : builtIn ? "Built-in display"
                : "Display";
            result.TryAdd(source.viewGdiDeviceName, (target.monitorDevicePath, name, builtIn));
        }
        return result;
    }
}
