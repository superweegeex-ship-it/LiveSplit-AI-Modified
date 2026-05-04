using System;
using System.Buffers;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

using LiveSplit.Options;

using static System.Windows.Forms.TextRenderer;

namespace LiveSplit.UI;

public class SimpleLabel
{
    public string Text { get; set; }
    public ICollection<string> AlternateText { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public Font Font { get; set; }
    public Brush Brush { get; set; }
    public StringAlignment HorizontalAlignment { get; set; }
    public StringAlignment VerticalAlignment { get; set; }
    public Color ShadowColor { get; set; }
    public Color OutlineColor { get; set; }

    public bool HasShadow { get; set; }
    public bool IsMonospaced { get; set; }

    /// <summary>South-east shadow offset in pixels (+x, +y).</summary>
    public float TextShadowOffset { get; set; } = 2f;

    /// <summary>0–100, scales <see cref="ShadowColor"/> alpha for the shadow.</summary>
    public float TextShadowTransparency { get; set; } = 100f;

    /// <summary>0–100, shadow blur (0 = sharp, 100 = soft); scales Gaussian spread linearly.</summary>
    public float TextShadowBlur { get; set; } = 28f;

    private StringFormat Format { get; set; }

    public float ActualWidth { get; set; }

    public Color ForeColor
    {
        get => ((SolidBrush)Brush).Color;
        set
        {
            try
            {
                if (Brush is SolidBrush brush)
                {
                    brush.Color = value;
                }
                else
                {
                    Brush = new SolidBrush(value);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }
    }

    public SimpleLabel(
        string text = "",
        float x = 0.0f, float y = 0.0f,
        Font font = null, Brush brush = null,
        float width = float.MaxValue, float height = float.MaxValue,
        StringAlignment horizontalAlignment = StringAlignment.Near,
        StringAlignment verticalAlignment = StringAlignment.Near,
        IEnumerable<string> alternateText = null)
    {
        Text = text;
        X = x;
        Y = y;
        Font = font ?? new Font("Arial", 1.0f);
        Brush = brush ?? new SolidBrush(Color.Black);
        Width = width;
        Height = height;
        HorizontalAlignment = horizontalAlignment;
        VerticalAlignment = verticalAlignment;
        IsMonospaced = false;
        HasShadow = true;
        ShadowColor = Color.FromArgb(128, 0, 0, 0);
        OutlineColor = Color.FromArgb(0, 0, 0, 0);
        ((List<string>)(AlternateText = [])).AddRange(alternateText ?? new string[0]);
        Format = new StringFormat
        {
            Alignment = HorizontalAlignment,
            LineAlignment = VerticalAlignment,
            FormatFlags = StringFormatFlags.NoWrap,
            Trimming = StringTrimming.EllipsisCharacter
        };
    }

    /// <summary>
    /// Width of the alternate text that would be drawn at <paramref name="layoutWidth"/>,
    /// after <see cref="CalculateAlternateText"/> (same rules as <see cref="Draw"/> for non-monospaced labels).
    /// </summary>
    public float MeasureDisplayedTextWidth(Graphics g, float layoutWidth)
    {
        if (IsMonospaced)
        {
            Format.Alignment = HorizontalAlignment;
            Format.LineAlignment = VerticalAlignment;
            SetActualWidth(g);
            string cutOffText = CutOff(g);
            return MeasureActualWidth(cutOffText, g);
        }

        Format.Alignment = HorizontalAlignment;
        Format.LineAlignment = VerticalAlignment;
        CalculateAlternateText(g, layoutWidth);
        return ActualWidth;
    }

    public void Draw(Graphics g)
    {
        long profilerStart = UiPaintProfiler.Begin();
        try
        {
            Format.Alignment = HorizontalAlignment;
            Format.LineAlignment = VerticalAlignment;

            if (!IsMonospaced)
            {
                string actualText = CalculateAlternateText(g, Width);
                DrawText(actualText, g, X, Y, Width, Height, Format);
            }
            else
            {
                var monoFormat = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = VerticalAlignment
                };

                int measurement = MeasureText(g, "0", Font, new Size((int)(Width + 0.5f), (int)(Height + 0.5f)), TextFormatFlags.NoPadding).Width;
                float offset = Width;
                int charIndex = 0;
                SetActualWidth(g);
                string cutOffText = CutOff(g);

                offset = Width - MeasureActualWidth(cutOffText, g);
                if (HorizontalAlignment != StringAlignment.Far)
                {
                    offset = 0f;
                }

                while (charIndex < cutOffText.Length)
                {
                    float curOffset = 0f;
                    char curChar = cutOffText[charIndex];

                    if (char.IsDigit(curChar))
                    {
                        curOffset = measurement;
                    }
                    else
                    {
                        curOffset = MeasureText(g, curChar.ToString(), Font, new Size((int)(Width + 0.5f), (int)(Height + 0.5f)), TextFormatFlags.NoPadding).Width;
                    }

                    DrawText(curChar.ToString(), g, X + offset - (curOffset / 2f), Y, curOffset * 2f, Height, monoFormat);

                    charIndex++;
                    offset += curOffset;
                }
            }
        }
        finally
        {
            UiPaintProfiler.Record(UiPaintProfilerSection.Labels, profilerStart);
        }
    }

    private Color GetEffectiveShadowTint()
    {
        float t = Math.Min(100f, Math.Max(0f, TextShadowTransparency)) / 100f;
        int a = (int)Math.Round(Math.Min(255f, Math.Max(0f, ShadowColor.A * t)));
        return Color.FromArgb(a, ShadowColor.R, ShadowColor.G, ShadowColor.B);
    }

    /// <summary>Same sigma curve as the legacy multi-draw Gaussian stack (0–100 blur slider).</summary>
    private static float ShadowSigmaFromBlurNorm(float blurNorm) => 0.15f + blurNorm * 2.45f;

    /// <summary>Box-blur radius (integral two-pass); tighter cap keeps worst-case paint cost bounded.</summary>
    private static int ShadowBoxBlurRadius(float sigma)
    {
        int r = (int)Math.Ceiling(sigma * 0.82);
        return Math.Max(1, Math.Min(10, r));
    }

    private const int ShadowBitmapMaxPixels = 450_000;
    private const int ShadowCacheMaxEntries = 192;
    private const int ShadowCacheMaxPixels = 4_000_000;
    private const int TextBitmapMaxPixels = 600_000;
    private const int TextCacheMaxEntries = 320;
    private const int TextCacheMaxPixels = 8_000_000;
    private static readonly object ShadowCacheLock = new();
    private static readonly Dictionary<ShadowBitmapCacheKey, ShadowBitmapCacheEntry> ShadowBitmapCache = [];
    private static readonly object TextCacheLock = new();
    private static readonly Dictionary<TextBitmapCacheKey, TextBitmapCacheEntry> TextBitmapCache = [];
    private static long shadowCacheClock;
    private static int shadowCachePixels;
    private static long textCacheClock;
    private static int textCachePixels;

    private sealed class ShadowBitmapCacheEntry
    {
        public Bitmap Bitmap { get; set; }
        public int Pixels { get; set; }
        public long LastUsed { get; set; }
    }

    private sealed class TextBitmapCacheEntry
    {
        public Bitmap Bitmap { get; set; }
        public int Pixels { get; set; }
        public long LastUsed { get; set; }
    }

    private readonly struct ShadowBitmapCacheKey : IEquatable<ShadowBitmapCacheKey>
    {
        private readonly bool usePath;
        private readonly string text;
        private readonly string fontFamily;
        private readonly int fontStyle;
        private readonly int fontUnit;
        private readonly int fontSize;
        private readonly int pathFontSize;
        private readonly int width;
        private readonly int height;
        private readonly int dpiX;
        private readonly int dpiY;
        private readonly int tintArgb;
        private readonly int alignment;
        private readonly int lineAlignment;
        private readonly int formatFlags;
        private readonly int trimming;
        private readonly int textRenderingHint;
        private readonly int smoothingMode;
        private readonly int radius;
        private readonly int pad;

        public ShadowBitmapCacheKey(
            bool usePath,
            string text,
            Font font,
            float pathFontSize,
            float width,
            float height,
            Graphics graphics,
            StringFormat format,
            Color tint,
            int radius,
            int pad)
        {
            this.usePath = usePath;
            this.text = text ?? string.Empty;
            fontFamily = font?.FontFamily?.Name ?? string.Empty;
            fontStyle = font == null ? 0 : (int)font.Style;
            fontUnit = font == null ? 0 : (int)font.Unit;
            fontSize = FloatCacheKey(font?.Size ?? 0f);
            this.pathFontSize = FloatCacheKey(pathFontSize);
            this.width = FloatCacheKey(width);
            this.height = FloatCacheKey(height);
            dpiX = FloatCacheKey(graphics.DpiX);
            dpiY = FloatCacheKey(graphics.DpiY);
            tintArgb = tint.ToArgb();
            alignment = (int)format.Alignment;
            lineAlignment = (int)format.LineAlignment;
            formatFlags = (int)format.FormatFlags;
            trimming = (int)format.Trimming;
            textRenderingHint = (int)graphics.TextRenderingHint;
            smoothingMode = (int)graphics.SmoothingMode;
            this.radius = radius;
            this.pad = pad;
        }

        public bool Equals(ShadowBitmapCacheKey other)
        {
            return usePath == other.usePath
                && text == other.text
                && fontFamily == other.fontFamily
                && fontStyle == other.fontStyle
                && fontUnit == other.fontUnit
                && fontSize == other.fontSize
                && pathFontSize == other.pathFontSize
                && width == other.width
                && height == other.height
                && dpiX == other.dpiX
                && dpiY == other.dpiY
                && tintArgb == other.tintArgb
                && alignment == other.alignment
                && lineAlignment == other.lineAlignment
                && formatFlags == other.formatFlags
                && trimming == other.trimming
                && textRenderingHint == other.textRenderingHint
                && smoothingMode == other.smoothingMode
                && radius == other.radius
                && pad == other.pad;
        }

        public override bool Equals(object obj) => obj is ShadowBitmapCacheKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = usePath ? 17 : 23;
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(text);
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(fontFamily);
                hash = (hash * 31) + fontStyle;
                hash = (hash * 31) + fontUnit;
                hash = (hash * 31) + fontSize;
                hash = (hash * 31) + pathFontSize;
                hash = (hash * 31) + width;
                hash = (hash * 31) + height;
                hash = (hash * 31) + dpiX;
                hash = (hash * 31) + dpiY;
                hash = (hash * 31) + tintArgb;
                hash = (hash * 31) + alignment;
                hash = (hash * 31) + lineAlignment;
                hash = (hash * 31) + formatFlags;
                hash = (hash * 31) + trimming;
                hash = (hash * 31) + textRenderingHint;
                hash = (hash * 31) + smoothingMode;
                hash = (hash * 31) + radius;
                hash = (hash * 31) + pad;
                return hash;
            }
        }
    }

    private readonly struct TextBitmapCacheKey : IEquatable<TextBitmapCacheKey>
    {
        private readonly string text;
        private readonly string fontFamily;
        private readonly int fontStyle;
        private readonly int fontUnit;
        private readonly int fontSize;
        private readonly int width;
        private readonly int height;
        private readonly int worldX;
        private readonly int worldY;
        private readonly int dpiX;
        private readonly int dpiY;
        private readonly int scaleX;
        private readonly int scaleY;
        private readonly int translateX;
        private readonly int translateY;
        private readonly int deviceLeft;
        private readonly int deviceTop;
        private readonly int foreArgb;
        private readonly int shadowArgb;
        private readonly int outlineArgb;
        private readonly int shadowOffset;
        private readonly int shadowTransparency;
        private readonly int shadowBlur;
        private readonly int hasShadow;
        private readonly int alignment;
        private readonly int lineAlignment;
        private readonly int formatFlags;
        private readonly int trimming;
        private readonly int textRenderingHint;
        private readonly int smoothingMode;
        private readonly int compositingQuality;

        public TextBitmapCacheKey(
            string text,
            Font font,
            float x,
            float y,
            float width,
            float height,
            Graphics graphics,
            StringFormat format,
            Color foreColor,
            Color shadowColor,
            Color outlineColor,
            float textShadowOffset,
            float textShadowTransparency,
            float textShadowBlur,
            bool hasShadow,
            float transformScaleX,
            float transformScaleY,
            float transformTranslateX,
            float transformTranslateY,
            int deviceLeft,
            int deviceTop)
        {
            this.text = text ?? string.Empty;
            fontFamily = font?.FontFamily?.Name ?? string.Empty;
            fontStyle = font == null ? 0 : (int)font.Style;
            fontUnit = font == null ? 0 : (int)font.Unit;
            fontSize = FloatCacheKey(font?.Size ?? 0f);
            this.width = FloatCacheKey(width);
            this.height = FloatCacheKey(height);
            worldX = FloatCacheKey(x);
            worldY = FloatCacheKey(y);
            dpiX = FloatCacheKey(graphics.DpiX);
            dpiY = FloatCacheKey(graphics.DpiY);
            scaleX = FloatCacheKey(transformScaleX);
            scaleY = FloatCacheKey(transformScaleY);
            translateX = FloatCacheKey(transformTranslateX);
            translateY = FloatCacheKey(transformTranslateY);
            this.deviceLeft = deviceLeft;
            this.deviceTop = deviceTop;
            foreArgb = foreColor.ToArgb();
            shadowArgb = shadowColor.ToArgb();
            outlineArgb = outlineColor.ToArgb();
            shadowOffset = FloatCacheKey(textShadowOffset);
            shadowTransparency = FloatCacheKey(textShadowTransparency);
            shadowBlur = FloatCacheKey(textShadowBlur);
            this.hasShadow = hasShadow ? 1 : 0;
            alignment = (int)format.Alignment;
            lineAlignment = (int)format.LineAlignment;
            formatFlags = (int)format.FormatFlags;
            trimming = (int)format.Trimming;
            textRenderingHint = (int)graphics.TextRenderingHint;
            smoothingMode = (int)graphics.SmoothingMode;
            compositingQuality = (int)graphics.CompositingQuality;
        }

        public bool Equals(TextBitmapCacheKey other)
        {
            return text == other.text
                && fontFamily == other.fontFamily
                && fontStyle == other.fontStyle
                && fontUnit == other.fontUnit
                && fontSize == other.fontSize
                && width == other.width
                && height == other.height
                && worldX == other.worldX
                && worldY == other.worldY
                && dpiX == other.dpiX
                && dpiY == other.dpiY
                && scaleX == other.scaleX
                && scaleY == other.scaleY
                && translateX == other.translateX
                && translateY == other.translateY
                && deviceLeft == other.deviceLeft
                && deviceTop == other.deviceTop
                && foreArgb == other.foreArgb
                && shadowArgb == other.shadowArgb
                && outlineArgb == other.outlineArgb
                && shadowOffset == other.shadowOffset
                && shadowTransparency == other.shadowTransparency
                && shadowBlur == other.shadowBlur
                && hasShadow == other.hasShadow
                && alignment == other.alignment
                && lineAlignment == other.lineAlignment
                && formatFlags == other.formatFlags
                && trimming == other.trimming
                && textRenderingHint == other.textRenderingHint
                && smoothingMode == other.smoothingMode
                && compositingQuality == other.compositingQuality;
        }

        public override bool Equals(object obj) => obj is TextBitmapCacheKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(text);
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(fontFamily);
                hash = (hash * 31) + fontStyle;
                hash = (hash * 31) + fontUnit;
                hash = (hash * 31) + fontSize;
                hash = (hash * 31) + width;
                hash = (hash * 31) + height;
                hash = (hash * 31) + worldX;
                hash = (hash * 31) + worldY;
                hash = (hash * 31) + dpiX;
                hash = (hash * 31) + dpiY;
                hash = (hash * 31) + scaleX;
                hash = (hash * 31) + scaleY;
                hash = (hash * 31) + translateX;
                hash = (hash * 31) + translateY;
                hash = (hash * 31) + deviceLeft;
                hash = (hash * 31) + deviceTop;
                hash = (hash * 31) + foreArgb;
                hash = (hash * 31) + shadowArgb;
                hash = (hash * 31) + outlineArgb;
                hash = (hash * 31) + shadowOffset;
                hash = (hash * 31) + shadowTransparency;
                hash = (hash * 31) + shadowBlur;
                hash = (hash * 31) + hasShadow;
                hash = (hash * 31) + alignment;
                hash = (hash * 31) + lineAlignment;
                hash = (hash * 31) + formatFlags;
                hash = (hash * 31) + trimming;
                hash = (hash * 31) + textRenderingHint;
                hash = (hash * 31) + smoothingMode;
                hash = (hash * 31) + compositingQuality;
                return hash;
            }
        }
    }

    private static int FloatCacheKey(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return 0;
        }

        return (int)Math.Round(value * 100f);
    }

    private static Bitmap GetOrCreateShadowBitmap(ShadowBitmapCacheKey key, int width, int height, Action<Bitmap> populate)
    {
        lock (ShadowCacheLock)
        {
            if (ShadowBitmapCache.TryGetValue(key, out ShadowBitmapCacheEntry entry)
                && entry.Bitmap != null
                && entry.Bitmap.Width == width
                && entry.Bitmap.Height == height)
            {
                entry.LastUsed = ++shadowCacheClock;
                return entry.Bitmap;
            }
        }

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        populate(bitmap);

        lock (ShadowCacheLock)
        {
            if (ShadowBitmapCache.TryGetValue(key, out ShadowBitmapCacheEntry existing)
                && existing.Bitmap != null
                && existing.Bitmap.Width == width
                && existing.Bitmap.Height == height)
            {
                existing.LastUsed = ++shadowCacheClock;
                bitmap.Dispose();
                return existing.Bitmap;
            }

            int pixels = width * height;
            ShadowBitmapCache[key] = new ShadowBitmapCacheEntry
            {
                Bitmap = bitmap,
                Pixels = pixels,
                LastUsed = ++shadowCacheClock
            };
            shadowCachePixels += pixels;
            TrimShadowBitmapCache();
            return bitmap;
        }
    }

    private static void TrimShadowBitmapCache()
    {
        while (ShadowBitmapCache.Count > ShadowCacheMaxEntries || shadowCachePixels > ShadowCacheMaxPixels)
        {
            ShadowBitmapCacheKey oldestKey = default;
            ShadowBitmapCacheEntry oldestEntry = null;
            foreach (KeyValuePair<ShadowBitmapCacheKey, ShadowBitmapCacheEntry> item in ShadowBitmapCache)
            {
                if (oldestEntry == null || item.Value.LastUsed < oldestEntry.LastUsed)
                {
                    oldestKey = item.Key;
                    oldestEntry = item.Value;
                }
            }

            if (oldestEntry == null)
            {
                return;
            }

            ShadowBitmapCache.Remove(oldestKey);
            shadowCachePixels -= oldestEntry.Pixels;
            oldestEntry.Bitmap?.Dispose();
        }
    }

    private static Bitmap GetOrCreateTextBitmap(TextBitmapCacheKey key, int width, int height, Action<Bitmap> populate)
    {
        lock (TextCacheLock)
        {
            if (TextBitmapCache.TryGetValue(key, out TextBitmapCacheEntry entry)
                && entry.Bitmap != null
                && entry.Bitmap.Width == width
                && entry.Bitmap.Height == height)
            {
                entry.LastUsed = ++textCacheClock;
                return entry.Bitmap;
            }
        }

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        populate(bitmap);

        lock (TextCacheLock)
        {
            if (TextBitmapCache.TryGetValue(key, out TextBitmapCacheEntry existing)
                && existing.Bitmap != null
                && existing.Bitmap.Width == width
                && existing.Bitmap.Height == height)
            {
                existing.LastUsed = ++textCacheClock;
                bitmap.Dispose();
                return existing.Bitmap;
            }

            int pixels = width * height;
            TextBitmapCache[key] = new TextBitmapCacheEntry
            {
                Bitmap = bitmap,
                Pixels = pixels,
                LastUsed = ++textCacheClock
            };
            textCachePixels += pixels;
            TrimTextBitmapCache();
            return bitmap;
        }
    }

    private static void TrimTextBitmapCache()
    {
        while (TextBitmapCache.Count > TextCacheMaxEntries || textCachePixels > TextCacheMaxPixels)
        {
            TextBitmapCacheKey oldestKey = default;
            TextBitmapCacheEntry oldestEntry = null;
            foreach (KeyValuePair<TextBitmapCacheKey, TextBitmapCacheEntry> item in TextBitmapCache)
            {
                if (oldestEntry == null || item.Value.LastUsed < oldestEntry.LastUsed)
                {
                    oldestKey = item.Key;
                    oldestEntry = item.Value;
                }
            }

            if (oldestEntry == null)
            {
                return;
            }

            TextBitmapCache.Remove(oldestKey);
            textCachePixels -= oldestEntry.Pixels;
            oldestEntry.Bitmap?.Dispose();
        }
    }

    private static void BgraStraightToPremultiplied(byte[] buf, int stride, int width, int height)
    {
        for (int y = 0; y < height; y++)
        {
            int row = y * stride;
            for (int x = 0; x < width; x++)
            {
                int i = row + x * 4;
                byte b = buf[i];
                byte g2 = buf[i + 1];
                byte r = buf[i + 2];
                byte a = buf[i + 3];
                if (a == 0)
                {
                    buf[i] = buf[i + 1] = buf[i + 2] = 0;
                    continue;
                }

                buf[i] = (byte)((b * a + 127) / 255);
                buf[i + 1] = (byte)((g2 * a + 127) / 255);
                buf[i + 2] = (byte)((r * a + 127) / 255);
            }
        }
    }

    private static void BgraPremultipliedToStraight(byte[] buf, int stride, int width, int height)
    {
        for (int y = 0; y < height; y++)
        {
            int row = y * stride;
            for (int x = 0; x < width; x++)
            {
                int i = row + x * 4;
                byte a = buf[i + 3];
                if (a == 0)
                {
                    buf[i] = buf[i + 1] = buf[i + 2] = 0;
                    continue;
                }

                int pr = buf[i + 2];
                int pg = buf[i + 1];
                int pb = buf[i];
                buf[i + 2] = (byte)Math.Min(255, (pr * 255 + a / 2) / a);
                buf[i + 1] = (byte)Math.Min(255, (pg * 255 + a / 2) / a);
                buf[i] = (byte)Math.Min(255, (pb * 255 + a / 2) / a);
            }
        }
    }

    /// <summary>O(width) separable box blur using prefix sums (edge-clamped window).</summary>
    private static void HorizontalBoxBlurPremultipliedIntegral(byte[] src, byte[] dst, int stride, int width, int height, int radius)
    {
        int[] pref = ArrayPool<int>.Shared.Rent((width + 1) * 4);
        try
        {
            for (int y = 0; y < height; y++)
            {
                int row = y * stride;
                for (int ch = 0; ch < 4; ch++)
                {
                    int b = ch * (width + 1);
                    pref[b] = 0;
                    for (int x = 0; x < width; x++)
                    {
                        pref[b + x + 1] = pref[b + x] + src[row + x * 4 + ch];
                    }
                }

                for (int x = 0; x < width; x++)
                {
                    int L = x - radius;
                    if (L < 0)
                    {
                        L = 0;
                    }

                    int R = x + radius;
                    if (R >= width)
                    {
                        R = width - 1;
                    }

                    int cnt = R - L + 1;
                    int di = row + x * 4;
                    for (int ch = 0; ch < 4; ch++)
                    {
                        int bb = ch * (width + 1);
                        int sum = pref[bb + R + 1] - pref[bb + L];
                        dst[di + ch] = (byte)(sum / cnt);
                    }
                }
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(pref, clearArray: false);
        }
    }

    private static void VerticalBoxBlurPremultipliedIntegral(byte[] src, byte[] dst, int stride, int width, int height, int radius)
    {
        int[] pref = ArrayPool<int>.Shared.Rent((height + 1) * 4);
        try
        {
            for (int x = 0; x < width; x++)
            {
                int x4 = x * 4;
                for (int ch = 0; ch < 4; ch++)
                {
                    int b = ch * (height + 1);
                    pref[b] = 0;
                    for (int y = 0; y < height; y++)
                    {
                        pref[b + y + 1] = pref[b + y] + src[y * stride + x4 + ch];
                    }
                }

                for (int y = 0; y < height; y++)
                {
                    int L = y - radius;
                    if (L < 0)
                    {
                        L = 0;
                    }

                    int R = y + radius;
                    if (R >= height)
                    {
                        R = height - 1;
                    }

                    int cnt = R - L + 1;
                    int di = y * stride + x4;
                    for (int ch = 0; ch < 4; ch++)
                    {
                        int bb = ch * (height + 1);
                        int sum = pref[bb + R + 1] - pref[bb + L];
                        dst[di + ch] = (byte)(sum / cnt);
                    }
                }
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(pref, clearArray: false);
        }
    }

    private static void BoxBlurPremultipliedTwoPassIntegral(byte[] work, byte[] scratch, int stride, int width, int height, int radius)
    {
        HorizontalBoxBlurPremultipliedIntegral(work, scratch, stride, width, height, radius);
        VerticalBoxBlurPremultipliedIntegral(scratch, work, stride, width, height, radius);
    }

    private void DrawTextShadowPath(Graphics g, GraphicsPath gp, string text, float fontSize, float x, float y, float width, float height, StringFormat format, Color tint)
    {
        float off = Math.Min(16f, Math.Max(0f, TextShadowOffset));
        float blurN = Math.Min(100f, Math.Max(0f, TextShadowBlur)) / 100f;

        var savedPixel = g.PixelOffsetMode;
        var savedInterp = g.InterpolationMode;
        var savedSmooth = g.SmoothingMode;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        try
        {
            if (blurN <= 0.0005f)
            {
                using var brush = new SolidBrush(tint);
                gp.AddString(text, Font.FontFamily, (int)Font.Style, fontSize, new RectangleF(x + off, y + off, width, height), format);
                g.FillPath(brush, gp);
                gp.Reset();
                return;
            }

            float sigma = ShadowSigmaFromBlurNorm(blurN);
            int radius = ShadowBoxBlurRadius(sigma);
            int pad = (int)Math.Ceiling(2.6 * sigma) + (int)Math.Ceiling(off) + 8;
            int bw = Math.Max(1, (int)Math.Ceiling(width) + 2 * pad);
            int bh = Math.Max(1, (int)Math.Ceiling(height) + 2 * pad);
            if ((long)bw * bh > ShadowBitmapMaxPixels)
            {
                using var brush = new SolidBrush(tint);
                gp.AddString(text, Font.FontFamily, (int)Font.Style, fontSize, new RectangleF(x + off, y + off, width, height), format);
                g.FillPath(brush, gp);
                gp.Reset();
                return;
            }

            var key = new ShadowBitmapCacheKey(
                usePath: true,
                text,
                Font,
                fontSize,
                width,
                height,
                g,
                format,
                tint,
                radius,
                pad);
            Bitmap bmp = GetOrCreateShadowBitmap(key, bw, bh, cachedBitmap =>
            {
                using (var g2 = Graphics.FromImage(cachedBitmap))
                {
                    g2.Clear(Color.Transparent);
                    g2.TextRenderingHint = g.TextRenderingHint;
                    g2.SmoothingMode = g.SmoothingMode;
                    g2.PixelOffsetMode = PixelOffsetMode.Half;
                    using var brush = new SolidBrush(tint);
                    using var shadowPath = new GraphicsPath();
                    shadowPath.AddString(
                        text,
                        Font.FontFamily,
                        (int)Font.Style,
                        fontSize,
                        new RectangleF(pad, pad, width, height),
                        format);
                    g2.FillPath(brush, shadowPath);
                }

                BlurShadowBitmapInPlace(cachedBitmap, radius);
            });

            g.InterpolationMode = InterpolationMode.Bilinear;
            g.DrawImage(
                bmp,
                new RectangleF(x + off - pad, y + off - pad, bw, bh),
                new RectangleF(0, 0, bw, bh),
                GraphicsUnit.Pixel);
        }
        finally
        {
            g.PixelOffsetMode = savedPixel;
            g.InterpolationMode = savedInterp;
            g.SmoothingMode = savedSmooth;
        }
    }

    private void DrawTextShadowString(Graphics g, string text, float x, float y, float width, float height, StringFormat format, Color tint)
    {
        float off = Math.Min(16f, Math.Max(0f, TextShadowOffset));
        float blurN = Math.Min(100f, Math.Max(0f, TextShadowBlur)) / 100f;

        var savedPixel = g.PixelOffsetMode;
        var savedInterp = g.InterpolationMode;
        var savedSmooth = g.SmoothingMode;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        try
        {
            if (blurN <= 0.0005f)
            {
                using var brush = new SolidBrush(tint);
                g.DrawString(text, Font, brush, new RectangleF(x + off, y + off, width, height), format);
                return;
            }

            float sigma = ShadowSigmaFromBlurNorm(blurN);
            int radius = ShadowBoxBlurRadius(sigma);
            int pad = (int)Math.Ceiling(2.6 * sigma) + (int)Math.Ceiling(off) + 8;
            int bw = Math.Max(1, (int)Math.Ceiling(width) + 2 * pad);
            int bh = Math.Max(1, (int)Math.Ceiling(height) + 2 * pad);
            if ((long)bw * bh > ShadowBitmapMaxPixels)
            {
                using var brush = new SolidBrush(tint);
                g.DrawString(text, Font, brush, new RectangleF(x + off, y + off, width, height), format);
                return;
            }

            var key = new ShadowBitmapCacheKey(
                usePath: false,
                text,
                Font,
                GetFontSize(g),
                width,
                height,
                g,
                format,
                tint,
                radius,
                pad);
            Bitmap bmp = GetOrCreateShadowBitmap(key, bw, bh, cachedBitmap =>
            {
                using (var g2 = Graphics.FromImage(cachedBitmap))
                {
                    g2.Clear(Color.Transparent);
                    g2.TextRenderingHint = g.TextRenderingHint;
                    g2.SmoothingMode = g.SmoothingMode;
                    g2.PixelOffsetMode = PixelOffsetMode.Half;
                    using var brush = new SolidBrush(tint);
                    g2.DrawString(text, Font, brush, new RectangleF(pad, pad, width, height), format);
                }

                BlurShadowBitmapInPlace(cachedBitmap, radius);
            });

            g.InterpolationMode = InterpolationMode.Bilinear;
            g.DrawImage(
                bmp,
                new RectangleF(x + off - pad, y + off - pad, bw, bh),
                new RectangleF(0, 0, bw, bh),
                GraphicsUnit.Pixel);
        }
        finally
        {
            g.PixelOffsetMode = savedPixel;
            g.InterpolationMode = savedInterp;
            g.SmoothingMode = savedSmooth;
        }
    }

    private static void BlurShadowBitmapInPlace(Bitmap bmp, int radius)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride;
            int w = bmp.Width;
            int h = bmp.Height;
            int len = stride * h;
            byte[] work = ArrayPool<byte>.Shared.Rent(len);
            byte[] scratch = ArrayPool<byte>.Shared.Rent(len);
            try
            {
                Marshal.Copy(data.Scan0, work, 0, len);
                BgraStraightToPremultiplied(work, stride, w, h);
                BoxBlurPremultipliedTwoPassIntegral(work, scratch, stride, w, h, radius);
                BgraPremultipliedToStraight(work, stride, w, h);
                Marshal.Copy(work, 0, data.Scan0, len);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(work, clearArray: false);
                ArrayPool<byte>.Shared.Return(scratch, clearArray: false);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    private float GetTextCachePaddingWorld(Graphics g)
    {
        float pad = 2f;
        if (OutlineColor.A > 0)
        {
            pad += GetOutlineSize(GetFontSize(g)) + 2f;
        }

        if (HasShadow)
        {
            Color tint = GetEffectiveShadowTint();
            if (tint.A > 0)
            {
                float off = Math.Min(16f, Math.Max(0f, TextShadowOffset));
                float blurN = Math.Min(100f, Math.Max(0f, TextShadowBlur)) / 100f;
                if (blurN <= 0.0005f)
                {
                    pad += off + 2f;
                }
                else
                {
                    float sigma = ShadowSigmaFromBlurNorm(blurN);
                    pad += (float)Math.Ceiling(2.6 * sigma) + (float)Math.Ceiling(off) + 8f;
                }
            }
        }

        return Math.Min(128f, Math.Max(2f, pad));
    }

    private bool TryDrawCachedText(string text, Graphics g, float x, float y, float width, float height, StringFormat format)
    {
        if (string.IsNullOrEmpty(text)
            || Font == null
            || Brush is not SolidBrush solidBrush
            || width <= 0f
            || height <= 0f
            || width > 4096f
            || height > 2048f)
        {
            return false;
        }

        using Matrix transform = g.Transform;
        float[] elements = transform.Elements;
        float scaleX = elements[0];
        float shearY = elements[1];
        float shearX = elements[2];
        float scaleY = elements[3];
        float translateX = elements[4];
        float translateY = elements[5];
        if (Math.Abs(shearX) > 0.0001f
            || Math.Abs(shearY) > 0.0001f
            || Math.Abs(scaleX) < 0.05f
            || Math.Abs(scaleY) < 0.05f)
        {
            return false;
        }

        float padWorld = GetTextCachePaddingWorld(g);
        float scaleMax = Math.Max(Math.Abs(scaleX), Math.Abs(scaleY));
        int padDevice = (int)Math.Ceiling(padWorld * scaleMax) + 3;
        var points = new[]
        {
            new PointF(x, y),
            new PointF(x + width, y + height)
        };
        transform.TransformPoints(points);
        float minX = Math.Min(points[0].X, points[1].X);
        float maxX = Math.Max(points[0].X, points[1].X);
        float minY = Math.Min(points[0].Y, points[1].Y);
        float maxY = Math.Max(points[0].Y, points[1].Y);
        int left = (int)Math.Floor(minX) - padDevice;
        int top = (int)Math.Floor(minY) - padDevice;
        int right = (int)Math.Ceiling(maxX) + padDevice;
        int bottom = (int)Math.Ceiling(maxY) + padDevice;
        int bitmapWidth = Math.Max(1, right - left);
        int bitmapHeight = Math.Max(1, bottom - top);
        if ((long)bitmapWidth * bitmapHeight > TextBitmapMaxPixels)
        {
            return false;
        }

        Color foreColor = solidBrush.Color;
        var key = new TextBitmapCacheKey(
            text,
            Font,
            x,
            y,
            width,
            height,
            g,
            format,
            foreColor,
            ShadowColor,
            OutlineColor,
            TextShadowOffset,
            TextShadowTransparency,
            TextShadowBlur,
            HasShadow,
            scaleX,
            scaleY,
            translateX,
            translateY,
            left,
            top);

        Bitmap bitmap = GetOrCreateTextBitmap(key, bitmapWidth, bitmapHeight, cachedBitmap =>
        {
            using var g2 = Graphics.FromImage(cachedBitmap);
            g2.Clear(Color.Transparent);
            g2.TextRenderingHint = g.TextRenderingHint;
            g2.SmoothingMode = g.SmoothingMode;
            g2.PixelOffsetMode = g.PixelOffsetMode;
            g2.InterpolationMode = g.InterpolationMode;
            g2.CompositingQuality = g.CompositingQuality;
            using Matrix cacheTransform = transform.Clone();
            cacheTransform.Translate(-left, -top, MatrixOrder.Append);
            g2.Transform = cacheTransform;
            DrawTextDirect(text, g2, x, y, width, height, format);
        });

        GraphicsState state = g.Save();
        try
        {
            g.ResetTransform();
            g.CompositingQuality = CompositingQuality.HighSpeed;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImageUnscaled(bitmap, left, top);
        }
        finally
        {
            g.Restore(state);
        }

        return true;
    }

    private void DrawText(string text, Graphics g, float x, float y, float width, float height, StringFormat format)
    {
        if (text == null)
        {
            return;
        }

        if (TryDrawCachedText(text, g, x, y, width, height, format))
        {
            return;
        }

        DrawTextDirect(text, g, x, y, width, height, format);
    }

    private void DrawTextDirect(string text, Graphics g, float x, float y, float width, float height, StringFormat format)
    {
        if (g.TextRenderingHint == TextRenderingHint.AntiAlias && OutlineColor.A > 0)
        {
            float fontSize = GetFontSize(g);
            using var gp = new GraphicsPath();
            using var outline = new Pen(OutlineColor, GetOutlineSize(fontSize)) { LineJoin = LineJoin.Round };
            if (HasShadow)
            {
                Color tint = GetEffectiveShadowTint();
                if (tint.A > 0)
                {
                    DrawTextShadowPath(g, gp, text, fontSize, x, y, width, height, format, tint);
                }
            }

            gp.AddString(text, Font.FontFamily, (int)Font.Style, fontSize, new RectangleF(x, y, width, height), format);
            g.DrawPath(outline, gp);
            g.FillPath(Brush, gp);
        }
        else
        {
            if (HasShadow)
            {
                Color tint = GetEffectiveShadowTint();
                if (tint.A > 0)
                {
                    DrawTextShadowString(g, text, x, y, width, height, format, tint);
                }
            }

            g.DrawString(text, Font, Brush, new RectangleF(x, y, width, height), format);
        }
    }

    private float GetOutlineSize(float fontSize)
    {
        return 2.1f + (fontSize * 0.055f);
    }

    private float GetFontSize(Graphics g)
    {
        if (Font.Unit == GraphicsUnit.Point)
        {
            return Font.Size * g.DpiY / 72;
        }

        return Font.Size;
    }

    public void SetActualWidth(Graphics g)
    {
        Format.Alignment = HorizontalAlignment;
        Format.LineAlignment = VerticalAlignment;

        if (!IsMonospaced)
        {
            ActualWidth = g.MeasureString(Text, Font, 9999, Format).Width;
        }
        else
        {
            ActualWidth = MeasureActualWidth(Text, g);
        }
    }

    public string CalculateAlternateText(Graphics g, float width)
    {
        string actualText = Text;
        ActualWidth = g.MeasureString(Text, Font, 9999, Format).Width;
        foreach (string curText in AlternateText.OrderByDescending(x => x.Length))
        {
            if (width < ActualWidth)
            {
                actualText = curText;
                ActualWidth = g.MeasureString(actualText, Font, 9999, Format).Width;
            }
            else
            {
                break;
            }
        }

        return actualText;
    }

    private float MeasureActualWidth(string text, Graphics g)
    {
        int charIndex = 0;
        int measurement = MeasureText(g, "0", Font, new Size((int)(Width + 0.5f), (int)(Height + 0.5f)), TextFormatFlags.NoPadding).Width;
        int offset = 0;

        while (charIndex < text.Length)
        {
            char curChar = text[charIndex];

            if (char.IsDigit(curChar))
            {
                offset += measurement;
            }
            else
            {
                offset += MeasureText(g, curChar.ToString(), Font, new Size((int)(Width + 0.5f), (int)(Height + 0.5f)), TextFormatFlags.NoPadding).Width;
            }

            charIndex++;
        }

        return offset;
    }

    private string CutOff(Graphics g)
    {
        if (ActualWidth < Width)
        {
            return Text;
        }

        string cutOffText = Text;
        while (ActualWidth >= Width && !string.IsNullOrEmpty(cutOffText))
        {
            cutOffText = cutOffText.Remove(cutOffText.Length - 1, 1);
            ActualWidth = MeasureActualWidth(cutOffText + "...", g);
        }

        if (ActualWidth >= Width)
        {
            return "";
        }

        return cutOffText + "...";
    }
}
