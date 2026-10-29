# Souris facile fenêtre

A Windows 10 and 11 app to move or resize the window under the pointer by
holding modifier keys and moving the mouse: no clicking, no dragging, no
aiming for a title bar or a window border.

It's the Windows version of [Souris facile
pomme](https://github.com/thomfab/souris-facile-pomme) for macOS and of the
[Souris Facile GNOME Shell extension](https://github.com/thomfab/souris-facile).

(Disclaimer: Claude Code helped me)

## How it works

Hold a key combination and move the mouse:

| Keys (default)                 | Action                              |
| ------------------------------ | ----------------------------------- |
| `Alt` + `Windows`              | Move the window under the pointer   |
| `Ctrl` + `Alt` + `Windows`     | Resize the window under the pointer |

Press and hold the click (a normal click on a touchpad) while holding the
keys to snap to the grid, and release it to move or resize freely again.

Release the keys to stop. The combination must match exactly, and both can be
changed (or disabled) in the settings, which warn about combinations that
can't work well: the same keys for both, a single key held for ordinary
shortcuts, or `Ctrl` + `Alt` without the Windows key, which many keyboard
layouts (French AZERTY among them) also report for `AltGr`. Since the resize
keys include the move keys, adding `Ctrl` while moving switches to resizing,
and releasing it switches back.

Releasing the Windows key or `Alt` after moving a window doesn't open the
Start menu or an app's menu bar.

- **Moving** keeps the window at the same spot under the pointer. A maximized
  window goes back to its own size first.
- **Resizing** pulls the corner nearest the pointer, so you can grow or shrink
  a window from any side. It can also be set to always use the bottom-right
  corner.
- Fullscreen and minimized windows, the desktop and the taskbar are left
  alone.
- Windows of apps running as administrator can't be moved, unless Souris
  facile fenêtre runs as administrator too: Windows doesn't let a regular app
  move them.

### Snapping

Window edges snap to display edges (minus the taskbar) and to the edges of
neighbouring windows when they come within 16 pixels (at 100% scale,
more on displays scaled up). The distance is configurable, and `0` turns
snapping off.

### Grid

Each display is divided into a grid of columns and rows. While you hold the
click, moved and resized edges always land on a grid line; the click itself
doesn't reach the app under the pointer. The grid follows the display under
the pointer, and its lines are shown while you hold the click, with the other
windows dimmed. Windows stacked above the one you are moving stay bright, so
turn on "Raise window" if you want everything else dimmed.

Each display has its own grid size, e.g. 2 × 2 on the laptop screen and
8 × 4 on an ultrawide monitor. A display seen for the first time gets a grid
suiting its size (at its scale, so a 2880 × 1800 laptop screen at 200% counts
as 1440 × 900): 2 × 2 for 13–14″ laptops, 3 × 2 for 15–16″ laptops and
1080p, 4 × 3 for larger displays, 8 × 4 for ultrawides and 2 × 3 for
portrait displays.

Grids are remembered per physical display, so plugging the laptop into your
monitors at home or at the office brings their grids back. In the settings,
**Preview Grid** briefly draws a display's grid on it to tell displays apart,
and displays that aren't plugged in are listed under **Unplugged Displays**,
where they can be forgotten.

### Sending a window to another display

While holding the move keys over a window:

- **Swipe two fingers** on a touchpad toward another display: the window
  goes to the display in that direction, following the arrangement in
  Settings → System → Display. One swipe moves it once. If windows go the
  wrong way, the touchpad is probably set to "Down motion scrolls down": turn
  on **Reverse swipes**.
- **Scroll the mouse wheel**: the window goes to the next or previous
  display, left to right.
- **Press an arrow key**: the window goes to the display in that direction.

The window keeps its relative place and size (a window on the left half of
the laptop screen lands on the left half of the monitor), fits the grid of
that display if you hold the click, and the pointer goes with it, so you can
keep moving it. Swipes, scrolls and arrow keys used this way don't reach the
app under the pointer. Both can be turned off in the settings.

### Window sizes per display

Each display remembers where each app's windows last were on it. A window
sent to a display, or dragged onto it, gets that size and place back: for
example Chrome half the laptop screen and a third of the office monitor.
Sizes are remembered when you move or resize a window with Souris facile
fenêtre, and when a window leaves a display. Windows gives windows no ID that
survives an app restart, so this is per app (per executable, e.g.
`chrome.exe`): an app's windows share the same remembered size on a display.
It can be turned off, and the remembered sizes forgotten, in the settings.

## Notification area

Souris facile fenêtre has no window: everything is in the mouse icon in the
notification area, next to the clock. Windows 11 may hide it under the `^`
arrow: drag it onto the taskbar, or turn it on in Settings → Personalization
→ Taskbar → Other system tray icons.

Click the icon to open the settings, or right-click it for:

- **About Souris facile fenêtre**: version and project page.
- **Enabled**: turns moving and resizing on or off.
- **Settings…**
- **Quit Souris facile fenêtre**

Launching the app again while it's running opens its settings.

## Settings

Click the icon in the notification area to open the settings. The sidebar
has a page per topic, then one per connected display, and **Unplugged
Displays** when some are remembered.

| Page            | Setting                         | Default                    |
| --------------- | ------------------------------- | -------------------------- |
| General         | Enabled                         | On                         |
|                 | Launch at login                 | Off                        |
|                 | Raise window                    | Off                        |
| Move            | Keys                            | `Alt` + `Windows`          |
| Resize          | Keys                            | `Ctrl` + `Alt` + `Windows` |
|                 | Resize from nearest corner      | On                         |
| Snapping        | Snap distance                   | 16 px                      |
|                 | Snap to other windows           | On                         |
|                 | Show grid                       | On                         |
|                 | Dim other windows               | On                         |
| Send to Display | Swipe or scroll                 | On                         |
|                 | Reverse swipes                  | Off                        |
|                 | Arrow keys                      | On                         |
|                 | Remember window sizes           | On                         |
| Each display    | Grid size                       | Suits the display          |
|                 | Remembered window sizes, forget |                            |

They're saved in `%APPDATA%\SourisFacileFenetre`: `settings.json` and
`window-frames.json`.

## Installing

Each [release](https://github.com/thomfab/souris-facile-fenetre/releases) has:

- **`SourisFacileFenetre-<version>-setup.exe`**: the installer. It asks
  whether to install for you only, in `%LOCALAPPDATA%\Programs` with no
  administrator rights needed, or for all users, in Program Files, which asks
  for them. It can also turn on launching at login. Updating quits the
  running app first.
- **`SourisFacileFenetre-<version>-x64-portable.exe`** and
  **`…-arm64-portable.exe`**: a single executable to run from anywhere.

The executables aren't signed, so SmartScreen may warn the first time: click
**More info**, then **Run anyway**.

## Building

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download), and [Inno
Setup 6](https://jrsoftware.org/isinfo.php) for the installer.

```powershell
dotnet run --project src/SourisFacile   # build and launch
dotnet test                              # run the unit tests
./build.ps1                              # portable executables and installer in build/
./build.ps1 -NoInstaller                 # portable executables only
```

The app itself builds on Linux and macOS too (`dotnet build`), and the unit
tests, which cover everything that doesn't touch Windows, run there.

### Releasing

Set the version in `Directory.Build.props`, then push a matching tag (e.g.
`v0.1`): GitHub Actions builds the installer and the portable executables
and publishes them as a GitHub release.

## License

GPL-2.0-or-later, see [LICENSE](LICENSE).
