using System.Drawing;
using Microsoft.Win32;

namespace SourisFacile;

// The settings, with a page per topic in a sidebar, then one per connected
// display and one for unplugged displays.
internal sealed class SettingsForm : Form
{
    private const int ContentWidth = 470;

    private abstract record Page
    {
        public sealed record General : Page;
        public sealed record Move : Page;
        public sealed record Resize : Page;
        public sealed record Snapping : Page;
        public sealed record Send : Page;
        public sealed record OfDisplay(string Id) : Page;
        public sealed record Unplugged : Page;
    }

    private readonly SettingsStore store;
    private readonly WindowMemoryStore memory;
    private readonly GridPreview preview = new();
    private readonly TreeView sidebar;
    private readonly FlowLayoutPanel content;
    private Page page = new Page.General();
    private List<Display> displays = Displays.All();
    // Controls whose state follows the settings while the page is shown.
    private Label? warnings;
    private Control? sendSection;

    public SettingsForm(SettingsStore store, WindowMemoryStore memory)
    {
        this.store = store;
        this.memory = memory;

        Text = $"{App.Name} Settings";
        Icon = App.Icon;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(ContentWidth + 260, 540);
        Font = SystemFonts.MessageBoxFont ?? Font;

        content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(12),
        };
        sidebar = new TreeView
        {
            Dock = DockStyle.Left,
            Width = 210,
            FullRowSelect = true,
            HideSelection = false,
            ShowLines = false,
            ShowPlusMinus = false,
            ShowRootLines = false,
            ItemHeight = 26,
            BorderStyle = BorderStyle.None,
            BackColor = SystemColors.Control,
        };
        sidebar.BeforeCollapse += (_, e) => e.Cancel = true;
        sidebar.AfterSelect += (_, e) =>
        {
            if (e.Node?.Tag is Page selected)
            {
                if (selected != page || content.Controls.Count == 0) Show(selected);
            }
            else if (e.Node?.FirstNode is { } first) sidebar.SelectedNode = first;
        };
        Controls.Add(content);
        Controls.Add(sidebar);

        store.Changed += OnSettingsChanged;
        memory.Changed += OnMemoryChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
        RebuildSidebar();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            store.Changed -= OnSettingsChanged;
            memory.Changed -= OnMemoryChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
            preview.Dispose();
        }
        base.Dispose(disposing);
    }

    private void OnSettingsChanged() => RunOnUi(RefreshStates);

    // Moving windows around changes the remembered sizes shown.
    private void OnMemoryChanged() => RunOnUi(() =>
    {
        if (page is Page.Send or Page.OfDisplay && !content.ContainsFocus) Show(page);
    });

    private void OnDisplaysChanged(object? sender, EventArgs e) => RunOnUi(() =>
    {
        displays = Displays.All();
        RebuildSidebar();
    });

    private void RunOnUi(Action action)
    {
        if (!IsHandleCreated || IsDisposed) return;
        if (InvokeRequired) BeginInvoke(action); else action();
    }

    // MARK: Sidebar

    private void RebuildSidebar()
    {
        sidebar.BeginUpdate();
        sidebar.Nodes.Clear();
        TreeNode? selected = null;
        TreeNode Node(TreeNodeCollection nodes, string text, Page tag)
        {
            var node = nodes.Add(text);
            node.Tag = tag;
            if (tag == page) selected = node;
            return node;
        }
        Node(sidebar.Nodes, "General", new Page.General());
        Node(sidebar.Nodes, "Move", new Page.Move());
        Node(sidebar.Nodes, "Resize", new Page.Resize());
        Node(sidebar.Nodes, "Snapping", new Page.Snapping());
        Node(sidebar.Nodes, "Send to Display", new Page.Send());
        var displaysNode = sidebar.Nodes.Add("Displays");
        displaysNode.NodeFont = new Font(sidebar.Font, FontStyle.Bold);
        foreach (var display in displays)
        {
            Node(displaysNode.Nodes, display.Name, new Page.OfDisplay(display.Id));
        }
        if (Unplugged().Count > 0) Node(displaysNode.Nodes, "Unplugged Displays", new Page.Unplugged());
        sidebar.ExpandAll();
        sidebar.EndUpdate();

        // The display shown may have been unplugged.
        sidebar.SelectedNode = selected ?? sidebar.Nodes[0];
        Show((Page)sidebar.SelectedNode.Tag);
    }

    private void Show(Page newPage)
    {
        page = newPage;
        warnings = null;
        sendSection = null;
        content.SuspendLayout();
        foreach (var control in content.Controls.Cast<Control>().ToList()) control.Dispose();
        content.Controls.Clear();
        var s = store.Current;
        switch (page)
        {
            case Page.General: GeneralPage(s); break;
            case Page.Move: MovePage(s); break;
            case Page.Resize: ResizePage(s); break;
            case Page.Snapping: SnappingPage(s); break;
            case Page.Send: SendPage(s); break;
            case Page.OfDisplay(var id):
                if (displays.FirstOrDefault(d => d.Id == id) is { } display) DisplayPage(display);
                else Add(Hint("This display isn't plugged in anymore."));
                break;
            case Page.Unplugged: UnpluggedPage(); break;
        }
        content.ResumeLayout();
        RefreshStates();
    }

    private void RefreshStates()
    {
        var s = store.Current;
        if (warnings != null)
        {
            var list = Modifiers.Warnings(s.MoveModifiers, s.ResizeModifiers);
            warnings.Text = string.Join("\n", list.Select(w => "⚠ " + w));
            warnings.Visible = list.Count > 0;
        }
        if (sendSection != null) sendSection.Enabled = s.MoveKeys != Modifier.None;
    }

    // MARK: Pages

    private void GeneralPage(Settings s)
    {
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
        Add(Section(null, null,
            Toggle("Enabled", s.Enabled, (settings, on) => settings with { Enabled = on }),
            login,
            Toggle("Raise window", s.RaiseWindow, (settings, on) => settings with { RaiseWindow = on }),
            Hint("Bring the window to the front when you start moving or resizing it.")));
    }

    private void MovePage(Settings s)
    {
        Add(Section("Keys",
            "Hold these keys and move the mouse to move the window under the pointer. Turn all off to disable.",
            ModifierBoxes(s.MoveModifiers, (settings, value) => settings with { MoveModifiers = value })));
        AddWarnings();
    }

    private void ResizePage(Settings s)
    {
        Add(Section("Keys",
            "Hold these keys and move the mouse to resize the window under the pointer. Turn all off to disable.",
            ModifierBoxes(s.ResizeModifiers, (settings, value) => settings with { ResizeModifiers = value })));
        AddWarnings();
        Add(Section(null, null,
            Toggle("Resize from nearest corner", s.ResizeNearestCorner,
                (settings, on) => settings with { ResizeNearestCorner = on }),
            Hint("Otherwise always from the bottom-right corner.")));
    }

    private void AddWarnings()
    {
        warnings = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0),
            ForeColor = Color.DarkOrange,
            Margin = new Padding(3, 0, 3, 10),
        };
        Add(warnings);
    }

    private void SnappingPage(Settings s)
    {
        var distance = new NumericUpDown
        {
            Minimum = 0,
            Maximum = Defaults.SnapMax,
            Value = s.SnapThreshold,
            Width = 70,
        };
        distance.ValueChanged += (_, _) => store.Update(settings => settings with { SnapThreshold = (int)distance.Value });
        Add(Section("Edge Snapping",
            "Window edges snap to display edges when they come close. The distance is in pixels at 100% scale, and 0 turns edge snapping off.",
            Row(new Label { Text = "Snap distance:", AutoSize = true, Anchor = AnchorStyles.Left }, distance),
            Toggle("Snap to other windows", s.SnapToWindows, (settings, on) => settings with { SnapToWindows = on })));

        Add(Section("Grid",
            "While moving or resizing, press and hold the click (with the keys still held) to snap window edges to the grid of the display under the pointer. Each display's grid is set on its own page.",
            Toggle("Show grid", s.ShowGrid, (settings, on) => settings with { ShowGrid = on }),
            Toggle("Dim other windows", s.DimWindows, (settings, on) => settings with { DimWindows = on })));
    }

    private void SendPage(Settings s)
    {
        sendSection = Section(null,
            "Hold the move keys over a window and swipe, scroll or press an arrow key. The pointer goes with the window.",
            Toggle("Swipe or scroll", s.SendWithScroll, (settings, on) => settings with { SendWithScroll = on }),
            Hint("Swipe two fingers on a touchpad toward a display. A mouse wheel goes to the next or previous display."),
            Toggle("Reverse swipes", s.ReverseSwipes, (settings, on) => settings with { ReverseSwipes = on }),
            Hint("Turn on if the touchpad is set to \"Down motion scrolls down\" and windows go the wrong way."),
            Toggle("Arrow keys", s.SendWithArrows, (settings, on) => settings with { SendWithArrows = on }),
            Hint("Press an arrow key to go to the display in that direction."));
        Add(sendSection);

        var forget = new Button { Text = "Forget All Remembered Sizes", AutoSize = true, Enabled = !memory.Current.IsEmpty };
        forget.Click += (_, _) => memory.Update(_ => new WindowMemory());
        Add(Section("Window Sizes", null,
            Toggle("Remember window sizes per display", s.RememberFrames,
                (settings, on) => settings with { RememberFrames = on }),
            Hint("A window sent or dragged to a display gets the size and place its app's windows last had there. Otherwise it keeps its relative place and size."),
            forget));
    }

    private void DisplayPage(Display display)
    {
        var (columns, rows) = display.Grid(store.Current);
        var fields = new GridSizeFields("Grid size", columns, rows);
        fields.Changed += (newColumns, newRows) =>
        {
            store.Update(settings => settings.WithDisplayGrid(display.Id,
                new DisplayGrid(display.Name, newColumns, newRows)));
            PreviewGrid(display);
        };
        var previewButton = new Button { Text = "Preview Grid", AutoSize = true };
        previewButton.Click += (_, _) => PreviewGrid(display);
        Add(Section("Grid",
            "Windows snap to it while you hold the click. Remembered for this display, and used again whenever it's plugged in. A new display gets a grid suiting its size.",
            fields, previewButton));

        var count = memory.Current.Count(display.Id);
        var forget = new Button { Text = "Forget", AutoSize = true, Enabled = count > 0 };
        forget.Click += (_, _) => memory.Update(m => m.Forget(display.Id));
        Add(Section("Remembered Window Sizes",
            "Where apps' windows last were on this display, used when a window is sent or dragged here.",
            Row(new Label { Text = count == 1 ? "1 app" : $"{count} apps", AutoSize = true, Anchor = AnchorStyles.Left, Width = 120 },
                forget)));
    }

    private void UnpluggedPage()
    {
        var rows = Unplugged().Select(entry =>
        {
            var forget = new Button { Text = "Forget", AutoSize = true };
            forget.Click += (_, _) =>
            {
                store.Update(settings => settings.WithDisplayGrid(entry.Key, null));
                memory.Update(m => m.Forget(entry.Key));
                BeginInvoke(RebuildSidebar);
            };
            var label = new Label
            {
                Text = $"{entry.Value.Name} — Grid {entry.Value.Columns} × {entry.Value.Rows}",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
            };
            return (Control)Row(label, forget);
        }).ToArray();
        Add(Section(null,
            "Settings of displays that aren't plugged in. They apply again as soon as the display is. Forgetting a display also forgets its remembered window sizes.",
            rows));
    }

    // MARK: Displays

    private List<KeyValuePair<string, DisplayGrid>> Unplugged() =>
        store.Current.DisplayGrids
            .Where(entry => displays.All(d => d.Id != entry.Key))
            .OrderBy(entry => entry.Value.Name)
            .ToList();

    // Briefly shows the display's grid on it, to tell displays apart.
    private void PreviewGrid(Display display)
    {
        var (columns, rows) = display.Grid(store.Current);
        preview.Show(display.WorkArea, columns, rows);
    }

    // MARK: Building blocks

    private void Add(Control control) => content.Controls.Add(control);

    private static Control Section(string? title, string? footer, params Control[] controls)
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
            Text = title ?? "",
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

    private Control ModifierBoxes(string value, Func<Settings, string, Settings> change)
    {
        var current = Modifiers.Parse(value);
        var boxes = new Dictionary<Modifier, CheckBox>();
        var column = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Margin = Padding.Empty,
        };
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
            column.Controls.Add(box);
        }
        return column;
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
