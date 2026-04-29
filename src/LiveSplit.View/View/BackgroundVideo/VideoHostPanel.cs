using System.Drawing;
using System.Windows.Forms;

namespace LiveSplit.View.BackgroundVideo;

/// <summary>
/// Host panel for embedded mpv: avoids painting an opaque background over native child video windows.
/// Forwards mouse hit-tests in the outer edge band to the parent form so users can resize the
/// LiveSplit window from its borders even when mpv covers the whole client area.
/// </summary>
internal sealed class VideoHostPanel : Panel
{
    private const int WS_CLIPCHILDREN = 0x02000000;
    private const int WS_CLIPSIBLINGS = 0x04000000;
    private const int WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;
    /// <summary>Width of the edge zone (in client pixels) that forwards hit-tests to the parent for resize.</summary>
    private const int EdgeResizeBandPx = 6;

    public VideoHostPanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw
            | ControlStyles.Opaque,
            false);
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Black;
        TabStop = false;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            // Clip GDI output to sibling native HWNDs (mpv vs layout overlay); reduces wrong paint order.
            cp.Style |= WS_CLIPCHILDREN | WS_CLIPSIBLINGS;
            return cp;
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Intentionally skip default erase so a reparented VO window is not covered by GDI fills.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // Native video child draws itself; nothing to paint here.
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCHITTEST)
        {
            int lparam = m.LParam.ToInt32();
            int sx = (short)(lparam & 0xFFFF);
            int sy = (short)((lparam >> 16) & 0xFFFF);
            Point clientPt = PointToClient(new Point(sx, sy));
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            bool nearLeft = clientPt.X < EdgeResizeBandPx;
            bool nearRight = clientPt.X >= w - EdgeResizeBandPx;
            bool nearTop = clientPt.Y < EdgeResizeBandPx;
            bool nearBottom = clientPt.Y >= h - EdgeResizeBandPx;
            if (nearLeft || nearRight || nearTop || nearBottom)
            {
                // HTTRANSPARENT bubbles the hit test to the next window (the form), which then
                // returns HTLEFT/HTRIGHT/HTTOP/HTBOTTOM/HTTOPLEFT/etc. and Windows handles resize.
                m.Result = (System.IntPtr)HTTRANSPARENT;
                return;
            }
        }

        base.WndProc(ref m);
    }
}
