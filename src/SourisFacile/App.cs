using System.Drawing;
using Microsoft.Win32;

namespace SourisFacile;

// The notification area icon and its menu. There's no other window, except
// the settings.
internal sealed class App : ApplicationContext
{
    public const string Name = "Souris facile fenêtre";
    public const string Url = "https://github.com/thomfab/souris-facile-fenetre";

    public static Icon Icon { get; } = LoadIcon(32);

    private readonly SettingsStore store;
    private readonly WindowMover mover;
    private readonly InputHooks hooks = new();
    private readonly NotifyIcon trayIcon;
    private readonly ToolStripMenuItem enabledItem;
    private SettingsForm? settingsForm;

    public App(SettingsStore store)
    {
        this.store = store;
        mover = new WindowMover(store, SynchronizationContext.Current!);

        enabledItem = new ToolStripMenuItem("Enabled", null, (_, _) => ToggleEnabled());
        var menu = new ContextMenuStrip();
        menu.Items.Add($"About {Name}", null, (_, _) => ShowAbout());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(enabledItem);
        menu.Items.Add(new ToolStripSeparator());
        var settingsItem = new ToolStripMenuItem("Settings…", null, (_, _) => ShowSettings());
        settingsItem.Font = new Font(settingsItem.Font, FontStyle.Bold);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add($"Quit {Name}", null, (_, _) => ExitThread());
        menu.Opening += (_, _) => enabledItem.Checked = store.Current.Enabled;

        trayIcon = new NotifyIcon
        {
            Icon = LoadIcon(SystemInformation.SmallIconSize.Width),
            Text = Name,
            ContextMenuStrip = menu,
            Visible = true,
        };
        trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowSettings();
        };

        hooks.Changed = mover.ScheduleUpdate;
        hooks.Key = mover.HandleKey;
        hooks.Wheel = mover.HandleWheel;
        hooks.Start();

        SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;

        if (store.IsNew)
        {
            // Saves the defaults, so this shows only on the first launch.
            store.Update(settings => settings);
            ShowSettings();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
            hooks.Dispose();
            mover.Stop();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            settingsForm?.Dispose();
        }
        base.Dispose(disposing);
    }

    private static void OnDisplaysChanged(object? sender, EventArgs e) => Displays.Invalidate();

    private void ToggleEnabled()
    {
        store.Update(settings => settings with { Enabled = !settings.Enabled });
        if (!store.Current.Enabled) mover.Stop();
    }

    public void ShowSettings()
    {
        if (settingsForm == null || settingsForm.IsDisposed)
        {
            settingsForm = new SettingsForm(store);
        }
        settingsForm.Show();
        if (settingsForm.WindowState == FormWindowState.Minimized) settingsForm.WindowState = FormWindowState.Normal;
        settingsForm.Activate();
    }

    private static void ShowAbout()
    {
        var version = Application.ProductVersion;
        var page = new TaskDialogPage
        {
            Caption = Name,
            Heading = $"{Name} {version}",
            Text = $"Move and resize windows by holding modifier keys.\n\n<a href=\"{Url}\">{Url}</a>",
            EnableLinks = true,
            Icon = new TaskDialogIcon(Icon),
            Buttons = { TaskDialogButton.OK },
        };
        page.LinkClicked += (_, e) => Shell.Open(e.LinkHref);
        TaskDialog.ShowDialog(page);
    }

    private static Icon LoadIcon(int size)
    {
        using var stream = typeof(App).Assembly.GetManifestResourceStream("AppIcon.ico")!;
        return new Icon(stream, size, size);
    }
}
