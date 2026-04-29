using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LiveSplit.View.BackgroundVideo;

/// <summary>
/// Overlay that paints the timer layout above embedded native video and uses
/// <see cref="SetWindowRgn"/> based on actual pixel coverage to make unpainted areas
/// pass through to the sibling video surface (works on all Windows versions, no layered child).
/// Region rebuild is throttled and cached to keep per-paint cost low.
/// Drag and right-click are forwarded to the parent <see cref="Form"/>.
/// </summary>
internal sealed class EmbeddedLayoutOverlayPanel : Panel
{
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HT_CAPTION = 0x2;
    /// <summary>Alpha threshold for treating a pixel as opaque enough to be part of the region.</summary>
    private const byte OpaquePixelAlphaThreshold = 16;
    /// <summary>Minimum interval between window-region rebuilds (ms). Higher = less CPU, more lag.</summary>
    private const int RegionRebuildMinIntervalMs = 100;

    private bool useFullClientHitRegion;

    /// <summary>
    /// When true, the hit-test region is the full client rectangle (skips the per-pixel buffer scan).
    /// Do not enable while showing native video under transparent layout pixels: a full rectangular region
    /// removes hole-punching, and GDI+ transparent pixels on this HWND do not reveal a sibling mpv surface
    /// (background appears black). Prefer the alpha scan path for embedded compositor layouts.
    /// </summary>
    public bool UseFullClientHitRegion
    {
        get => useFullClientHitRegion;
        set
        {
            if (useFullClientHitRegion == value)
            {
                return;
            }

            useFullClientHitRegion = value;
            lastRegionWidth = lastRegionHeight = -1;
            lastFullHitRegionW = lastFullHitRegionH = -1;
        }
    }

    /// <summary>Raised when the overlay receives a right-click; the form should show its context menu.</summary>
    public event EventHandler<MouseEventArgs> RightClickRequested;

    private Bitmap paintBuffer;
    private readonly Stopwatch regionUpdateClock = Stopwatch.StartNew();
    private long lastRegionUpdateMs;
    private int lastRegionWidth;
    private int lastRegionHeight;
    private int lastFullHitRegionW;
    private int lastFullHitRegionH;

    public EmbeddedLayoutOverlayPanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw,
            true);
        UpdateStyles();
        BackColor = Color.Transparent;
        TabStop = false;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Background is rendered into the offscreen ARGB buffer in OnPaint instead.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        int w = Math.Max(1, ClientSize.Width);
        int h = Math.Max(1, ClientSize.Height);

        EnsurePaintBuffer(w, h);
        if (paintBuffer == null)
        {
            base.OnPaint(e);
            return;
        }

        using (Graphics bg = Graphics.FromImage(paintBuffer))
        {
            bg.Clear(Color.Transparent);
            var args = new PaintEventArgs(bg, new Rectangle(0, 0, w, h));
            base.OnPaint(args);
        }

        bool sizeChanged = w != lastRegionWidth || h != lastRegionHeight;
        long nowMs = regionUpdateClock.ElapsedMilliseconds;
        if (UseFullClientHitRegion)
        {
            if (w != lastFullHitRegionW || h != lastFullHitRegionH)
            {
                ApplyFullClientHitRegion(w, h);
                lastFullHitRegionW = w;
                lastFullHitRegionH = h;
            }
        }
        else if (sizeChanged || nowMs - lastRegionUpdateMs >= RegionRebuildMinIntervalMs)
        {
            UpdateWindowRegionFromBuffer(w, h);
            lastRegionUpdateMs = nowMs;
            lastRegionWidth = w;
            lastRegionHeight = h;
        }

        e.Graphics.DrawImageUnscaled(paintBuffer, 0, 0);
    }

    private void ApplyFullClientHitRegion(int w, int h)
    {
        if (!IsHandleCreated)
        {
            return;
        }

        IntPtr rgn = CreateRectRgn(0, 0, w, h);
        if (rgn == IntPtr.Zero)
        {
            return;
        }

        // SetWindowRgn takes ownership of the region handle on success.
        _ = SetWindowRgn(Handle, rgn, redraw: true);
        lastRegionWidth = w;
        lastRegionHeight = h;
        lastRegionUpdateMs = regionUpdateClock.ElapsedMilliseconds;
    }

    private void EnsurePaintBuffer(int w, int h)
    {
        if (paintBuffer != null && paintBuffer.Width == w && paintBuffer.Height == h)
        {
            return;
        }

        paintBuffer?.Dispose();
        paintBuffer = new Bitmap(w, h, PixelFormat.Format32bppArgb);
    }

    private unsafe void UpdateWindowRegionFromBuffer(int w, int h)
    {
        if (!IsHandleCreated)
        {
            return;
        }

        BitmapData data = paintBuffer.LockBits(
            new Rectangle(0, 0, w, h),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);

        IntPtr unionRegion = IntPtr.Zero;
        try
        {
            byte* basePtr = (byte*)data.Scan0;
            int stride = data.Stride;
            unionRegion = CreateRectRgn(0, 0, 0, 0);
            var rowRanges = new List<(int Start, int End)>(8);
            var prevRanges = new List<(int Start, int End)>(8);
            int rowStart = -1;
            bool havePrev = false;

            for (int y = 0; y < h; y++)
            {
                rowRanges.Clear();
                byte* rowPtr = basePtr + (y * stride);
                int x = 0;
                while (x < w)
                {
                    while (x < w && rowPtr[(x * 4) + 3] < OpaquePixelAlphaThreshold)
                    {
                        x++;
                    }

                    if (x >= w)
                    {
                        break;
                    }

                    int start = x;
                    while (x < w && rowPtr[(x * 4) + 3] >= OpaquePixelAlphaThreshold)
                    {
                        x++;
                    }

                    rowRanges.Add((start, x));
                }

                bool sameAsPrev = havePrev && AreRangesEqual(prevRanges, rowRanges);
                if (!sameAsPrev)
                {
                    if (havePrev && rowStart >= 0)
                    {
                        AddRowsToRegion(unionRegion, prevRanges, rowStart, y);
                    }

                    if (rowRanges.Count > 0)
                    {
                        prevRanges.Clear();
                        prevRanges.AddRange(rowRanges);
                        rowStart = y;
                        havePrev = true;
                    }
                    else
                    {
                        havePrev = false;
                        rowStart = -1;
                    }
                }
            }

            if (havePrev && rowStart >= 0)
            {
                AddRowsToRegion(unionRegion, prevRanges, rowStart, h);
            }

            _ = SetWindowRgn(Handle, unionRegion, redraw: false);
            unionRegion = IntPtr.Zero;
        }
        catch
        {
            // Best effort; on failure the previous region (if any) stays in effect.
        }
        finally
        {
            paintBuffer.UnlockBits(data);
            if (unionRegion != IntPtr.Zero)
            {
                _ = DeleteObject(unionRegion);
            }
        }
    }

    private static bool AreRangesEqual(List<(int Start, int End)> a, List<(int Start, int End)> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Start != b[i].Start || a[i].End != b[i].End)
            {
                return false;
            }
        }

        return true;
    }

    private static void AddRowsToRegion(IntPtr unionRegion, List<(int Start, int End)> ranges, int y0, int y1)
    {
        foreach ((int start, int end) in ranges)
        {
            IntPtr rect = CreateRectRgn(start, y0, end, y1);
            try
            {
                _ = CombineRgn(unionRegion, unionRegion, rect, RGN_OR);
            }
            finally
            {
                _ = DeleteObject(rect);
            }
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        Form owner = FindForm();
        if (owner == null || !owner.IsHandleCreated || owner.IsDisposed)
        {
            return;
        }

        _ = NativeReleaseCapture();
        _ = NativeSendMessage(owner.Handle, WM_NCLBUTTONDOWN, (IntPtr)HT_CAPTION, IntPtr.Zero);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Right)
        {
            RightClickRequested?.Invoke(this, e);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            paintBuffer?.Dispose();
            paintBuffer = null;
        }

        base.Dispose(disposing);
    }

    private const int RGN_OR = 2;

    [DllImport("user32.dll", EntryPoint = "ReleaseCapture")]
    private static extern bool NativeReleaseCapture();

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    private static extern IntPtr NativeSendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(IntPtr hrgnDest, IntPtr hrgnSrc1, IntPtr hrgnSrc2, int fnCombineMode);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll", EntryPoint = "SetWindowRgn")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);
}
