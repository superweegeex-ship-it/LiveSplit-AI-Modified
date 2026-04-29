using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LiveSplit.View.BackgroundVideo;

/// <summary>
/// Top-level borderless layered window that paints the LiveSplit UI as a per-pixel-alpha surface
/// floating above the main form's video host. Uses UpdateLayeredWindow so anti-aliased edges and
/// translucent shadows compose correctly over native GPU video below.
/// Mouse input is fully transparent (WS_EX_TRANSPARENT) so it always falls through to the main form.
/// </summary>
internal sealed class LayoutOverlayWindow : Form
{
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int ULW_ALPHA = 0x00000002;
    private const byte AC_SRC_OVER = 0x00;
    private const byte AC_SRC_ALPHA = 0x01;

    public LayoutOverlayWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = false;
        AllowTransparency = false;
        DoubleBuffered = false;
        Text = string.Empty;
        ControlBox = false;
        MinimizeBox = false;
        MaximizeBox = false;
        Visible = false;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            cp.Style = WS_POPUP;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    /// <summary>Pushes a 32bpp ARGB bitmap to the layered window with per-pixel alpha.</summary>
    public void RenderArgbBitmap(Bitmap argbBitmap)
    {
        if (argbBitmap == null || !IsHandleCreated || IsDisposed)
        {
            return;
        }

        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memoryDc = CreateCompatibleDC(screenDc);
        IntPtr previousBitmap = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        try
        {
            hBitmap = argbBitmap.GetHbitmap(Color.FromArgb(0));
            previousBitmap = SelectObject(memoryDc, hBitmap);

            var topLeft = new POINT { X = Left, Y = Top };
            var size = new SIZE { cx = argbBitmap.Width, cy = argbBitmap.Height };
            var srcOrigin = new POINT { X = 0, Y = 0 };
            var blend = new BLENDFUNCTION
            {
                BlendOp = AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AC_SRC_ALPHA
            };

            _ = UpdateLayeredWindow(
                Handle,
                screenDc,
                ref topLeft,
                ref size,
                memoryDc,
                ref srcOrigin,
                0,
                ref blend,
                ULW_ALPHA);
        }
        finally
        {
            if (previousBitmap != IntPtr.Zero)
            {
                _ = SelectObject(memoryDc, previousBitmap);
            }

            if (hBitmap != IntPtr.Zero)
            {
                _ = DeleteObject(hBitmap);
            }

            _ = DeleteDC(memoryDc);
            _ = ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd,
        IntPtr hdcDst,
        ref POINT pptDst,
        ref SIZE psize,
        IntPtr hdcSrc,
        ref POINT pprSrc,
        uint crKey,
        ref BLENDFUNCTION pblend,
        uint dwFlags);
}
