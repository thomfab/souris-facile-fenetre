using System.Drawing;
using Microsoft.Win32;

namespace SourisFacile;

internal sealed class SettingsForm : Form
{
    private const int ContentWidth = 470;

    private readonly SettingsStore store;
    private readonly GridPreview preview = new();
    private readonly FlowLayoutPanel root;
    private readonly Dictionary<Modifier, CheckBox> moveBoxes = [];
    private readonly Dictionary<Modifier, CheckBox> resizeBoxes = [];
    private readonly Label warnings;
    private readonly GroupBox sendGroup;
    private readonly GroupBox snapGroup;
    private readonly CheckBox gridEnabled;
    private readonly GridSizeFields defaultGrid;
    private readonly CheckBox showGrid;
    private readonly CheckBox dimWindows;
    private readonly FlowLayoutPanel displaysPanel;

    public SettingsForm(SettingsStore store)
    {
        this.store = store;
        var s = store.Current;

        Text = $"{App.Name} Settings";
        Icon = App.Icon;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(ContentWidth + 40, 720);
        Font = SystemFonts.MessageBoxFont ?? Font;

        root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(10),
        };
        Controls.Add(root);

        // Move and resize keys
        root.Controls.Add(Section("Move",
            "Hold these keys and move the mouse to move the window under the pointer. Turn all off to disable.",
            ModifierBoxes(moveBoxes, s.MoveModifiers, (settings, value) => settings with { MoveModifiers = value })));
        root.Controls.Add(Section("Resize",
            "Hold these keys and move the mouse to resize the window under the pointer. Turn all off to disable.",
            ModifierBoxes(resizeBoxes, s.ResizeModifiers, (settings, value) => settings with { ResizeModifiers = value })));

        warnings = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0),
            ForeColor = Color.DarkOrange,
            Margin = new Padding(3, 0, 3, 10),
        };
        root.Controls.Add(warnings);

        // Sending to another display
        sendGroup = Section("Send to Another Display",
            "Hold the move keys over a window and swipe, scroll or press an arrow key. The window keeps its relative place and size, fits the grid of the display it lands on, and the pointer goes with it.",
            Toggle("Swipe or scroll", s.SendWithScroll, (settings, on) => settings with { SendWithScroll = on }),
            Hint("Swipe two fingers on a touchpad toward a display. A mouse wheel goes to the next or previous display."),
            Toggle("Reverse swipes", s.ReverseSwipes, (settings, on) => settings with { ReverseSwipes = on }),
            Hint("Turn on if the touchpad is set to \"Down motion scrolls down\" and windows go the wrong way."),
            Toggle("Arrow keys", s.SendWithArrows, (settings, on) => settings with { SendWithArrows = on }),
            Hint("Press an arrow key to go to the display in that direction."));
        root.Controls.Add(sendGroup);

        // Behavior
        var login = new CheckBox { Text = "Launch at login", AutoSize = true, Checked = Startup.IsEnabled };
        login.CheckedChanged += (_, _) =>
        {
            try
            {
                Startup.SetEnabled(login.Checked);
            }
            catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException)
            {
                login.Checked = Startup.IsEnabled;
            }
        };
        root.Controls.Add(Section("Behavior", null,
            Toggle("Raise window", s.RaiseWindow, (settings, on) => settings with { RaiseWindow = on }),
            Toggle("Resize from nearest corner", s.ResizeNearestCorner,
                (settings, on) => settings with { ResizeNearestCorner = on }),
            login));

        // Edge snapping
        var distance = new NumericUpDown
        {
            Minimum = 0,
            Maximum = Defaults.SnapMax,
            Value = s.SnapThreshold,
            Width = 70,
        };
        distance.ValueChanged += (_, _) => store.Update(settings => settings with { SnapThreshold = (int)distance.Value });
        snapGroup = Section("Edge Snapping",
            "Window edges snap to display edges when they come close. The distance is in pixels at 100% scale, and 0 turns edge snapping off. Not used while the grid is on.",
            Row(new Label { Text = "Snap distance:", AutoSize = true, Anchor = AnchorStyles.Left }, distance),
            Toggle("Snap to other windows", s.SnapToWindows, (settings, on) => settings with { SnapToWindows = on }));
        root.Controls.Add(snapGroup);

        // Grid
        gridEnabled = Toggle("Snap to grid", s.GridEnabled, (settings, on) => settings with { GridEnabled = on });
        defaultGrid = new GridSizeFields("Default grid size", s.GridColumns, s.GridRows);
        defaultGrid.Changed += (columns, rows) =>
        {
            store.Update(settings => settings with { GridColumns = columns, GridRows = rows });
            RebuildDisplays();
        };
        showGrid = Toggle("Show grid", s.ShowGrid, (settings, on) => settings with { ShowGrid = on });
        dimWindows = Toggle("Dim other windows", s.DimWindows, (settings, on) => settings with { DimWindows = on });
        root.Controls.Add(Section("Grid",
            "Divide each display into a grid. Moved and resized edges always snap to its lines. Displays without their own grid size use the default one.",
            gridEnabled, defaultGrid, showGrid, dimWindows));

        displaysPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Margin = Padding.Empty,
        };
        root.Controls.Add(displaysPanel);

        store.Changed += OnSettingsChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
        RebuildDisplays();
        RefreshStates();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            store.Changed -= OnSettingsChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
            preview.Dispose();
        }
        base.Dispose(disposing);
    }

    private void OnSettingsChanged()
    {
        if (InvokeRequired) BeginInvoke(RefreshStates); else RefreshStates();
    }

    private void OnDisplaysChanged(object? sender, EventArgs e)
    {
        if (IsHandleCreated) BeginInvoke(RebuildDisplays);
    }

    private void RefreshStates()
    {
        var s = store.Current;
        var list = Modifiers.Warnings(s.MoveModifiers, s.ResizeModifiers);
        warnings.Text = string.Join("\n", list.Select(w => "⚠ " + w));
        warnings.Visible = list.Count > 0;
        sendGroup.Enabled = s.MoveKeys != Modifier.None;
        snapGroup.Enabled = !s.GridEnabled;
        defaultGrid.Enabled = showGrid.Enabled = dimWindows.Enabled = s.GridEnabled;
        displaysPanel.Enabled = s.GridEnabled;
    }

    // MARK: Displays

    private void RebuildDisplays()
    {
        var s = store.Current;
        var displays = Displays.All();
        displaysPanel.SuspendLayout();
        foreach (Control control in displaysPanel.Controls.Cast<Control>().ToList()) control.Dispose();
        displaysPanel.Controls.Clear();

        foreach (var display in displays)
        {
            var ownGrid = s.DisplayGrids.GetValueOrDefault(display.Id);
            var own = new CheckBox { Text = "Own grid size", AutoSize = true, Checked = ownGrid != null };
            own.CheckedChanged += (_, _) =>
            {
                store.Update(settings => settings.WithDisplayGrid(display.Id,
                    own.Checked ? new DisplayGrid(display.Name, settings.GridColumns, settings.GridRows) : null));
                BeginInvoke(RebuildDisplays);
            };
            var controls = new List<Control>
            {
                own,
                Hint($"Otherwise uses the default, {s.GridColumns} × {s.GridRows}."),
            };
            if (ownGrid != null)
            {
                var fields = new GridSizeFields("Grid size", ownGrid.Columns, ownGrid.Rows);
                fields.Changed += (columns, rows) =>
                {
                    store.Update(settings => settings.WithDisplayGrid(display.Id,
                        new DisplayGrid(display.Name, columns, rows)));
                    PreviewGrid(display);
                };
                controls.Add(fields);
            }
            var previewButton = new Button { Text = "Preview Grid", AutoSize = true };
            previewButton.Click += (_, _) => PreviewGrid(display);
            controls.Add(previewButton);
            displaysPanel.Controls.Add(Section(display.Name, null, [.. controls]));
        }

        var unplugged = s.DisplayGrids
            .Where(entry => displays.All(d => d.Id != entry.Key))
            .OrderBy(entry => entry.Value.Name)
            .ToList();
        if (unplugged.Count > 0)
        {
            var rows = unplugged.Select(entry =>
            {
                var forget = new Button { Text = "Forget", AutoSize = true };
                forget.Click += (_, _) =>
                {
                    store.Update(settings => settings.WithDisplayGrid(entry.Key, null));
                    BeginInvoke(RebuildDisplays);
                };
                var label = new Label
                {
                    Text = $"{entry.Value.Name} — {entry.Value.Columns} × {entry.Value.Rows}",
                    AutoSize = true,
                    Anchor = AnchorStyles.Left,
                };
                return (Control)Row(label, forget);
            }).ToArray();
            displaysPanel.Controls.Add(Section("Unplugged Displays",
                "Remembered grid sizes of displays that aren't plugged in. They apply again as soon as the display is.",
                rows));
        }
        displaysPanel.ResumeLayout();
    }

    // Briefly shows the display's grid on it, to tell displays apart.
    private void PreviewGrid(Display display)
    {
        var (columns, rows) = store.Current.GridSize(display.Id);
        preview.Show(display.WorkArea, columns, rows);
    }

    // MARK: Building blocks

    private static GroupBox Section(string title, string? footer, params Control[] controls)
    {
        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Padding = new Padding(4),
        };
        panel.Controls.AddRange(controls);
        if (footer != null) panel.Controls.Add(Hint(footer));
        var group = new GroupBox
        {
            Text = title,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(ContentWidth, 0),
            MaximumSize = new Size(ContentWidth, 0),
            Margin = new Padding(3, 3, 3, 10),
        };
        group.Controls.Add(panel);
        return group;
    }

    private static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(ContentWidth - 30, 0),
        ForeColor = SystemColors.GrayText,
        Margin = new Padding(3, 0, 3, 6),
    };

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        row.Controls.AddRange(controls);
        return row;
    }

    private CheckBox Toggle(string text, bool value, Func<Settings, bool, Settings> change)
    {
        var box = new CheckBox { Text = text, AutoSize = true, Checked = value };
        box.CheckedChanged += (_, _) => store.Update(settings => change(settings, box.Checked));
        return box;
    }

    private Control ModifierBoxes(Dictionary<Modifier, CheckBox> boxes, string value,
        Func<Settings, string, Settings> change)
    {
        var current = Modifiers.Parse(value);
        var row = Row();
        foreach (var modifier in Modifiers.All)
        {
            var box = new CheckBox
            {
                Text = Modifiers.Label(modifier),
                AutoSize = true,
                Checked = current.HasFlag(modifier),
            };
            box.CheckedChanged += (_, _) =>
            {
                var keys = boxes.Where(b => b.Value.Checked).Aggregate(Modifier.None, (all, b) => all | b.Key);
                store.Update(settings => change(settings, Modifiers.Format(keys)));
            };
            boxes[modifier] = box;
            row.Controls.Add(box);
        }
        return row;
    }
}

// A preset picker with columns and rows.
internal sealed class GridSizeFields : FlowLayoutPanel
{
    private readonly ComboBox presets;
    private readonly NumericUpDown columns;
    private readonly NumericUpDown rows;
    private bool updating;

    public event Action<int, int>? Changed;

    public GridSizeFields(string title, int columnCount, int rowCount)
    {
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        AutoSize = true;
        Margin = Padding.Empty;

        presets = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        presets.Items.AddRange([.. GridPreset.All.Select(p => p.Label), "Custom"]);
        columns = Number(columnCount);
        rows = Number(rowCount);

        Controls.Add(Line(title, presets));
        Controls.Add(Line("Columns:", columns));
        Controls.Add(Line("Rows:", rows));
        SelectPreset();

        presets.SelectedIndexChanged += (_, _) =>
        {
            if (updating || presets.SelectedIndex >= GridPreset.All.Length) return;
            var preset = GridPreset.All[presets.SelectedIndex];
            updating = true;
            columns.Value = preset.Columns;
            rows.Value = preset.Rows;
            updating = false;
            Changed?.Invoke(preset.Columns, preset.Rows);
        };
        columns.ValueChanged += (_, _) => OnNumberChanged();
        rows.ValueChanged += (_, _) => OnNumberChanged();
    }

    private void OnNumberChanged()
    {
        if (updating) return;
        SelectPreset();
        Changed?.Invoke((int)columns.Value, (int)rows.Value);
    }

    // The preset matching the current size, or "Custom".
    private void SelectPreset()
    {
        updating = true;
        var index = Array.FindIndex(GridPreset.All, p => p.Columns == columns.Value && p.Rows == rows.Value);
        presets.SelectedIndex = index >= 0 ? index : GridPreset.All.Length;
        updating = false;
    }

    private static NumericUpDown Number(int value) => new()
    {
        Minimum = Defaults.GridMin,
        Maximum = Defaults.GridMax,
        Value = Defaults.ClampGrid(value),
        Width = 70,
    };

    private static FlowLayoutPanel Line(string label, Control control)
    {
        var line = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        line.Controls.Add(new Label { Text = label, AutoSize = false, Width = 130, Height = 23, TextAlign = ContentAlignment.MiddleLeft });
        line.Controls.Add(control);
        return line;
    }
}

// Shows a grid for 2 seconds. Showing it again keeps it up for another 2 seconds.
internal sealed class GridPreview : IDisposable
{
    private readonly Overlays overlays = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 2000 };

    public GridPreview()
    {
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            overlays.HideGrid();
        };
    }

    public void Show(Rect area, int columns, int rows)
    {
        overlays.HideGrid();
        overlays.ShowGrid(area, columns, rows);
        timer.Stop();
        timer.Start();
    }

    public void Dispose()
    {
        timer.Dispose();
        overlays.HideAll();
    }
}
