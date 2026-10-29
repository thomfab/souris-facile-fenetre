using System.Drawing;
using static SourisFacile.Native;

namespace SourisFacile;

// A borderless, click-through window that never takes the focus.
internal class OverlayForm : Form
{
    public OverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= (int)(WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        // It's placed in pixels: don't let Windows Forms rescale it when it
        // lands on a display with another scale.
        if (m.Msg is WM_DPICHANGED or WM_GETDPISCALEDSIZE) return;
        base.WndProc(ref m);
    }

    // Shows it over `area`, just below `insertAfter` in the stacking order.
    public void ShowOver(Rect area, IntPtr insertAfter)
    {
        Bounds = new Rectangle((int)area.X, (int)area.Y, (int)area.Width, (int)area.Height);
        if (!Visible) Show();
        SetWindowPos(Handle, insertAfter, (int)area.X, (int)area.Y, (int)area.Width, (int)area.Height,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }
}

internal sealed class GridForm : OverlayForm
{
    private int columns = 1;
    private int rows = 1;

    public GridForm()
    {
        // Everything but the lines is see-through, and the lines are faint.
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        Opacity = 0.35;
        DoubleBuffered = true;
    }

    public void SetGrid(int columns, int rows)
    {
        this.columns = columns;
        this.rows = rows;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var scale = DeviceDpi / 96.0;
        var width = Math.Max(2, (int)Geometry.Round(2 * scale));
        var half = width / 2;
        var size = ClientSize;
        foreach (var x in Geometry.Divide(0, size.Width, columns).Skip(1).SkipLast(1))
        {
            e.Graphics.FillRectangle(Brushes.White, (int)x - half, 0, width, size.Height);
        }
        foreach (var y in Geometry.Divide(0, size.Height, rows).Skip(1).SkipLast(1))
        {
            e.Graphics.FillRectangle(Brushes.White, 0, (int)y - half, size.Width, width);
        }
    }
}

internal sealed class DimmerForm : OverlayForm
{
    public DimmerForm()
    {
        BackColor = Color.Black;
        Opacity = 0.5;
    }
}

internal sealed class Overlays
{
    private GridForm? grid;
    private (Rect Area, int Columns, int Rows)? gridShown;
    private readonly List<DimmerForm> dimmers = [];

    // Shows the grid lines over `area` (a display's work area), above all windows.
    public void ShowGrid(Rect area, int columns, int rows)
    {
        if (gridShown == (area, columns, rows)) return;
        grid ??= new GridForm();
        grid.SetGrid(columns, rows);
        grid.ShowOver(area, HWND_TOPMOST);
        gridShown = (area, columns, rows);
    }

    public void HideGrid()
    {
        grid?.Hide();
        gridShown = null;
    }

    // Darkens every display, right below the given window so it stays
    // bright. Windows stacked above it stay bright too: raise the window to
    // avoid that.
    public void ShowDimmer(IntPtr below)
    {
        var displays = Displays.All();
        while (dimmers.Count < displays.Count) dimmers.Add(new DimmerForm());
        for (var i = 0; i < dimmers.Count; i++)
        {
            if (i < displays.Count) dimmers[i].ShowOver(displays[i].Bounds, below);
            else dimmers[i].Hide();
        }
    }

    public void HideDimmer()
    {
        foreach (var dimmer in dimmers) dimmer.Hide();
    }

    public void HideAll()
    {
        HideGrid();
        HideDimmer();
    }
}
