namespace SourisFacile;

// Moves or resizes the window under the pointer while modifier keys are held.
internal sealed class WindowMover
{
    private const double MinWidth = 100;
    private const double MinHeight = 60;
    // Touchpads report scrolls in wheel units (120 a notch): this turns
    // them into roughly the distance the fingers travelled.
    private const double SwipeScale = 0.25;

    private enum Op { None, Move, Resize }

    private abstract record SendTarget
    {
        public sealed record Toward(Direction Direction) : SendTarget;
        // The next (1) or previous (-1) display.
        public sealed record Cycle(int Step) : SendTarget;
    }

    private readonly SettingsStore store;
    private readonly SynchronizationContext ui;
    private readonly Overlays overlays = new();

    // MARK: State on the UI thread

    private Settings settings;
    private Op op = Op.None;
    private ManagedWindow? window;
    private Point start;
    private Rect startRect;
    // Where the pointer sits inside the window, as a fraction of its size.
    private Point grabRatio;
    // Which edges a resize moves: -1 for left/top, 1 for right/bottom.
    private double resizeDirX = 1;
    private double resizeDirY = 1;
    private Rect? lastFrame;
    // The display scale where the operation started.
    private double scale = 1;
    private List<Rect> workAreas = [];
    private List<Rect> otherRects = [];

    // MARK: State on the hook thread

    private int updatePending;
    private readonly SwipeTracker swipe = new();
    private double lastWheelSend = double.NegativeInfinity;
    private double lastSwipeEvent = double.NegativeInfinity;
    // Arrow keys whose key down was swallowed, so their key up is too.
    private readonly HashSet<int> swallowedKeys = [];

    public WindowMover(SettingsStore store, SynchronizationContext ui)
    {
        this.store = store;
        this.ui = ui;
        settings = store.Current;
    }

    // Called by the hooks for every pointer or modifier change. Bursts of
    // events are coalesced into one update, so a slow app doesn't make the
    // pointer lag.
    public void ScheduleUpdate()
    {
        if (Interlocked.Exchange(ref updatePending, 1) == 1) return;
        ui.Post(_ =>
        {
            Volatile.Write(ref updatePending, 0);
            Update();
        }, null);
    }

    public void Stop() => EndOperation();

    private void Update()
    {
        if (op == Op.None) settings = store.Current;

        var point = Native.CursorPosition();
        var newOp = settings.Enabled ? OpForModifiers(InputHooks.CurrentModifiers()) : Op.None;

        if (newOp != op)
        {
            EndOperation();
            if (newOp != Op.None) BeginOperation(newOp, point);
            return;
        }

        switch (op)
        {
            case Op.Move: UpdateMove(point); break;
            case Op.Resize: UpdateResize(point); break;
        }
    }

    private Op OpForModifiers(Modifier held)
    {
        if (settings.ResizeKeys != Modifier.None && held == settings.ResizeKeys) return Op.Resize;
        if (settings.MoveKeys != Modifier.None && held == settings.MoveKeys) return Op.Move;
        return Op.None;
    }

    private void BeginOperation(Op newOp, Point point, ManagedWindow? target = null)
    {
        var w = target ?? ManagedWindow.At(point);
        if (w == null || !(newOp == Op.Move ? w.CanMove : w.CanResize)) return;
        if (w.Frame is not { Width: > 0, Height: > 0 } grabbed) return;

        // The Start menu or the menu bar would open when the keys are released.
        if ((InputHooks.CurrentModifiers() & (Modifier.Win | Modifier.Alt)) != 0)
        {
            InputHooks.PressMaskKey();
        }

        var rect = grabbed;
        if (w.IsMaximized)
        {
            // Moving takes it back to its own size, under the pointer;
            // resizing starts from the size it has now.
            w.Restore();
            if (newOp == Op.Resize) w.SetFrame(grabbed);
            if (w.Frame is not { Width: > 0, Height: > 0 } restored) return;
            rect = restored;
        }

        op = newOp;
        window = w;
        start = point;
        startRect = rect;
        lastFrame = rect;
        grabRatio = new Point((point.X - grabbed.MinX) / grabbed.Width, (point.Y - grabbed.MinY) / grabbed.Height);
        scale = Native.ScaleAt(point);

        if (settings.ResizeNearestCorner)
        {
            resizeDirX = grabRatio.X < 0.5 ? -1 : 1;
            resizeDirY = grabRatio.Y < 0.5 ? -1 : 1;
        }
        else
        {
            resizeDirX = 1;
            resizeDirY = 1;
        }

        CollectSnapTargets(w);

        if (settings.RaiseWindow) w.Raise();

        if (settings.GridEnabled && settings.DimWindows) overlays.ShowDimmer(below: w.Handle);

        // A maximized window being moved jumps under the pointer right away.
        if (op == Op.Move && rect != grabbed) UpdateMove(point);
    }

    private void EndOperation()
    {
        op = Op.None;
        window = null;
        workAreas = [];
        otherRects = [];
        overlays.HideAll();
    }

    private void UpdateMove(Point point)
    {
        if (window == null) return;
        // The app may change its size on a display with another scale.
        var current = window.Frame ?? startRect;
        double width = current.Width, height = current.Height;
        var x = Geometry.Round(point.X - grabRatio.X * width);
        var y = Geometry.Round(point.Y - grabRatio.Y * height);

        x += Snap([x, x + width], SnapLinesX(y, height, point));
        y += Snap([y, y + height], SnapLinesY(x, width, point));

        var frame = new Rect(x, y, width, height);
        if (frame == lastFrame) return;
        lastFrame = frame;
        if (!window.SetOrigin(frame.Origin)) EndOperation();
    }

    private void UpdateResize(Point point)
    {
        if (window == null) return;
        var dx = point.X - start.X;
        var dy = point.Y - start.Y;

        double left = startRect.MinX, right = startRect.MaxX;
        double top = startRect.MinY, bottom = startRect.MaxY;

        if (resizeDirX > 0) right += dx; else left += dx;
        if (resizeDirY > 0) bottom += dy; else top += dy;

        var linesX = SnapLinesX(top, bottom - top, point);
        if (resizeDirX > 0) right += Snap([right], linesX); else left += Snap([left], linesX);

        var linesY = SnapLinesY(left, right - left, point);
        if (resizeDirY > 0) bottom += Snap([bottom], linesY); else top += Snap([top], linesY);

        var minWidth = MinWidth * scale;
        var minHeight = MinHeight * scale;
        if (right - left < minWidth)
        {
            if (resizeDirX > 0) right = left + minWidth; else left = right - minWidth;
        }
        if (bottom - top < minHeight)
        {
            if (resizeDirY > 0) bottom = top + minHeight; else top = bottom - minHeight;
        }

        var frame = new Rect(Geometry.Round(left), Geometry.Round(top),
            Geometry.Round(right - left), Geometry.Round(bottom - top));
        if (frame == lastFrame) return;
        lastFrame = frame;
        if (!window.SetFrame(frame))
        {
            EndOperation();
            return;
        }

        // An app can refuse a size (below its minimum, above its maximum).
        // Put back the edges that shouldn't move.
        if ((resizeDirX >= 0 && resizeDirY >= 0) || window.Frame is not { } actual
            || (actual.Width == frame.Width && actual.Height == frame.Height))
        {
            return;
        }
        var origin = new Point(resizeDirX < 0 ? frame.MaxX - actual.Width : frame.MinX,
            resizeDirY < 0 ? frame.MaxY - actual.Height : frame.MinY);
        if (origin != actual.Origin) window.SetOrigin(origin);
    }

    // MARK: Sending to another display (hook thread)

    private bool HoldsMoveKeys()
    {
        var current = store.Current;
        return current.Enabled && current.MoveKeys != Modifier.None
            && InputHooks.CurrentModifiers() == current.MoveKeys;
    }

    private static double Now => Environment.TickCount64 / 1000.0;

    public bool HandleWheel(int delta, bool horizontal, Point _)
    {
        if (!HoldsMoveKeys() || !store.Current.SendWithScroll)
        {
            swipe.Reset();
            return false;
        }
        var now = Now;
        // A mouse wheel moves by whole notches, a touchpad by small steps.
        // Touchpads also send whole notches at times, in the middle of a swipe.
        var continuous = delta % Native.WHEEL_DELTA != 0 || now - lastSwipeEvent < SwipeTracker.Pause;

        if (continuous)
        {
            lastSwipeEvent = now;
            // Make the deltas follow the fingers: x going right, y going
            // down. By default, a touchpad scrolls the content along with
            // the fingers ("Down motion scrolls up").
            var sign = store.Current.ReverseSwipes ? -1 : 1;
            var dx = horizontal ? -sign * delta * SwipeScale : 0;
            var dy = horizontal ? 0 : sign * delta * SwipeScale;
            if (swipe.Feed(dx, dy, now) is { } direction) SendLater(new SendTarget.Toward(direction));
        }
        else if (now - lastWheelSend > 0.25)
        {
            // A wheel notch goes to the next or previous display. A fast
            // spin sends a burst of events: only the first one counts.
            lastWheelSend = now;
            var forward = horizontal ? delta > 0 : delta < 0;
            SendLater(new SendTarget.Cycle(forward ? 1 : -1));
        }
        return true;
    }

    public bool HandleKey(int vk, bool down)
    {
        if (!down) return swallowedKeys.Remove(vk);
        if (Directions.FromArrowKey(vk) is not { } direction || !HoldsMoveKeys() || !store.Current.SendWithArrows)
        {
            return false;
        }
        // Holding the key down doesn't keep sending.
        if (swallowedKeys.Add(vk)) SendLater(new SendTarget.Toward(direction));
        return true;
    }

    // Out of the hook thread, which must answer quickly.
    private void SendLater(SendTarget target) => ui.Post(_ => Send(target), null);

    // MARK: Sending to another display (UI thread)

    // Sends the window under the pointer (or the one being moved) to another
    // display, at the same relative place and size, and the pointer with it.
    private void Send(SendTarget target)
    {
        if (op == Op.None) settings = store.Current;
        var point = Native.CursorPosition();
        var moving = op == Op.Move;
        var w = moving ? window : ManagedWindow.At(point);
        if (w == null || !w.CanMove || w.Frame is not { Width: > 0, Height: > 0 } frame) return;

        if ((InputHooks.CurrentModifiers() & (Modifier.Win | Modifier.Alt)) != 0)
        {
            InputHooks.PressMaskKey();
        }

        var displays = Displays.All();
        var center = new Point(frame.MidX, frame.MidY);
        var from = displays.FindIndex(d => d.Bounds.Contains(center));
        if (from < 0) from = displays.IndexOf(Displays.At(center)!);
        if (from < 0) return;
        var frames = displays.Select(d => d.Bounds).ToList();
        var to = target switch
        {
            SendTarget.Toward t => Geometry.DisplayToward(t.Direction, from, frames),
            SendTarget.Cycle c => Geometry.DisplayAfter(from, c.Step, frames),
            _ => null,
        };
        if (to is not { } index) return;

        EndOperation();
        if (w.IsMaximized)
        {
            w.Restore();
            if (w.Frame is not { Width: > 0, Height: > 0 } restored) return;
            frame = restored;
        }

        var area = displays[index].WorkArea;
        var resizable = w.CanResize;
        var newFrame = Geometry.Transfer(frame, displays[from].WorkArea, area, keepSize: !resizable);
        if (settings.GridEnabled && resizable)
        {
            var (columns, rows) = settings.GridSize(displays[index].Id);
            newFrame = Geometry.FitToGrid(newFrame, area, columns, rows);
        }

        if (resizable)
        {
            if (!w.SetFrame(newFrame)) return;
            // Apps adjust their size to the scale of the display they land on: insist once.
            if (w.Frame != newFrame) w.SetFrame(newFrame);
        }
        else if (!w.SetOrigin(newFrame.Origin))
        {
            return;
        }

        // The pointer follows, at the same spot in the window.
        var landed = w.Frame ?? newFrame;
        var ratioX = Math.Clamp((point.X - frame.MinX) / frame.Width, 0, 1);
        var ratioY = Math.Clamp((point.Y - frame.MinY) / frame.Height, 0, 1);
        var newPoint = new Point(Geometry.Round(landed.MinX + ratioX * landed.Width),
            Geometry.Round(landed.MinY + ratioY * landed.Height));
        Native.SetCursorPos((int)newPoint.X, (int)newPoint.Y);

        // Carry on moving it from there, even if other windows cover that spot.
        if (moving) BeginOperation(Op.Move, newPoint, w);
    }

    // MARK: Snapping

    // Records the displays' work areas and the other windows' frames.
    private void CollectSnapTargets(ManagedWindow target)
    {
        workAreas = Displays.All().Select(d => d.WorkArea).ToList();
        otherRects = ManagedWindow.OnScreen()
            .Where(w => w.Handle != target.Handle)
            .Select(w => w.Frame)
            .ToList();
    }

    private double SnapThreshold => settings.SnapThreshold * scale;

    private double Snap(double[] edges, IReadOnlyList<double> lines)
    {
        // The grid always snaps; edge snapping only within the threshold.
        var threshold = settings.GridEnabled ? double.PositiveInfinity : SnapThreshold;
        return threshold > 0 ? Geometry.SnapDelta(edges, lines, threshold) : 0;
    }

    // Vertical lines a window spanning [top, top + height] can snap to.
    private List<double> SnapLinesX(double top, double height, Point point)
    {
        if (settings.GridEnabled)
        {
            var (area, columns, _) = CurrentGrid(point);
            return [.. Geometry.Divide(area.MinX, area.Width, columns)];
        }

        var lines = workAreas.SelectMany(a => new[] { a.MinX, a.MaxX }).ToList();
        if (settings.SnapToWindows)
        {
            var t = SnapThreshold;
            foreach (var r in otherRects.Where(r => r.MinY - t < top + height && r.MaxY + t > top))
            {
                lines.Add(r.MinX);
                lines.Add(r.MaxX);
            }
        }
        return lines;
    }

    // Horizontal lines a window spanning [left, left + width] can snap to.
    private List<double> SnapLinesY(double left, double width, Point point)
    {
        if (settings.GridEnabled)
        {
            var (area, _, rows) = CurrentGrid(point);
            return [.. Geometry.Divide(area.MinY, area.Height, rows)];
        }

        var lines = workAreas.SelectMany(a => new[] { a.MinY, a.MaxY }).ToList();
        if (settings.SnapToWindows)
        {
            var t = SnapThreshold;
            foreach (var r in otherRects.Where(r => r.MinX - t < left + width && r.MaxX + t > left))
            {
                lines.Add(r.MinY);
                lines.Add(r.MaxY);
            }
        }
        return lines;
    }

    // The grid follows the display under the pointer, with that display's size.
    private (Rect Area, int Columns, int Rows) CurrentGrid(Point point)
    {
        var display = Displays.At(point);
        var area = display?.WorkArea ?? workAreas.FirstOrDefault();
        var (columns, rows) = settings.GridSize(display?.Id);
        if (settings.ShowGrid) overlays.ShowGrid(area, columns, rows);
        return (area, columns, rows);
    }
}
