using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace LiveSplit.UI;

public static class CurrentSplitOutlinePaint
{
    /// <summary>
    /// Upper bound for outline wave speed in layout and splits UI (0 = static). Same numeric scale as passed to
    /// <see cref="CreateOutlineBrush"/>; previously sliders went to 100, which was faster than needed for most layouts.
    /// </summary>
    public const int OutlineWaveSpeedSliderMaximum = 40;

    public static Brush CreateOutlineBrush(
        float width,
        float height,
        int alpha255,
        bool rgbWave,
        CurrentSplitOutlineWaveAxis waveAxis,
        int waveSpeed,
        CurrentSplitOutlineFillMode fillMode,
        Color outlineColor,
        Color gradientEndColor)
    {
        float speed = waveSpeed;
        // Solid must win over the RGB wave checkbox so the outline is actually one color.
        if (fillMode == CurrentSplitOutlineFillMode.Solid)
        {
            return new SolidBrush(MultiplyAlpha(outlineColor, alpha255));
        }

        if (rgbWave)
        {
            return CreateRainbowBrush(width, height, alpha255, waveAxis == CurrentSplitOutlineWaveAxis.Horizontal, speed);
        }

        if (fillMode == CurrentSplitOutlineFillMode.Gradient)
        {
            bool gradientAlongX = waveAxis == CurrentSplitOutlineWaveAxis.Horizontal;
            if (speed > 0.0001f)
            {
                return CreateTwoColorTiledWaveBrush(
                    width,
                    height,
                    alpha255,
                    gradientAlongX,
                    waveAxis == CurrentSplitOutlineWaveAxis.Horizontal,
                    speed,
                    outlineColor,
                    gradientEndColor);
            }

            return CreateTwoPointGradient(
                width,
                height,
                alpha255,
                outlineColor,
                gradientEndColor,
                gradientAlongX);
        }

        return new SolidBrush(MultiplyAlpha(outlineColor, alpha255));
    }

    private static Brush CreateTwoPointGradient(
        float width,
        float height,
        int alpha255,
        Color c1,
        Color c2,
        bool horizontal)
    {
        Color a = MultiplyAlpha(c1, alpha255);
        Color b = MultiplyAlpha(c2, alpha255);
        PointF p1 = new PointF(0f, 0f);
        PointF p2 = horizontal ? new PointF(width, 0f) : new PointF(0f, height);
        var brush = new LinearGradientBrush(p1, p2, a, b);
        brush.GammaCorrection = true;
        ApplySoftTwoColorBlend(brush, a, b);
        return brush;
    }

    private static Brush CreateRainbowBrush(float width, float height, int alpha255, bool horizontal, float waveSpeed)
    {
        const int MinimumTile = 128;
        int tile = Math.Max(MinimumTile, (int)Math.Ceiling(Math.Max(width, height)));

        int band = Math.Max(2, (int)Math.Ceiling(horizontal ? height : width));
        var rect = new Rectangle(0, 0, tile, band);

        var brush = new LinearGradientBrush(
            rect,
            Color.White,
            Color.Black,
            LinearGradientMode.Horizontal);

        var blend = new ColorBlend();
        blend.Positions = new float[] { 0f, 1f / 6f, 2f / 6f, 3f / 6f, 4f / 6f, 5f / 6f, 1f };
        var colors = new Color[7];
        for (int i = 0; i < 7; i++)
        {
            colors[i] = MultiplyAlpha(ColorFromHsv(i * 60f, 1f, 1f), alpha255);
        }

        blend.Colors = colors;
        brush.InterpolationColors = blend;
        brush.WrapMode = WrapMode.Tile;

        float phase = 0f;
        if (waveSpeed > 0.0001f)
        {
            phase = Environment.TickCount * 0.001f * waveSpeed * 35f;
        }

        float offset = phase % tile;
        brush.ResetTransform();
        brush.TranslateTransform(-offset, 0f, MatrixOrder.Append);
        if (!horizontal)
        {
            brush.RotateTransform(90f, MatrixOrder.Append);
            brush.TranslateTransform(0f, band, MatrixOrder.Append);
        }

        return brush;
    }

    private static Brush CreateTwoColorTiledWaveBrush(
        float width,
        float height,
        int alpha255,
        bool gradientAlongX,
        bool waveMotionHorizontal,
        float waveSpeed,
        Color outlineColor,
        Color gradientEndColor)
    {
        const int MinimumTile = 128;
        int tile = Math.Max(MinimumTile, (int)Math.Ceiling(Math.Max(width, height)));

        int band = Math.Max(2, (int)Math.Ceiling(gradientAlongX ? height : width));
        var rect = new Rectangle(0, 0, tile, band);

        Color a = MultiplyAlpha(outlineColor, alpha255);
        Color b = MultiplyAlpha(gradientEndColor, alpha255);

        var brush = new LinearGradientBrush(
            rect,
            a,
            b,
            LinearGradientMode.Horizontal);

        brush.GammaCorrection = true;
        // Tiled A→B alone repeats B|A at the seam (hard snap back to the first color). Use A→B→A with identical ends.
        ApplySeamlessSymmetricTwoColorTileBlend(brush, a, b);
        brush.WrapMode = WrapMode.Tile;

        float phase = 0f;
        if (waveSpeed > 0.0001f)
        {
            phase = Environment.TickCount * 0.001f * waveSpeed * 35f;
        }

        float offset = phase % tile;
        brush.ResetTransform();
        brush.TranslateTransform(-offset, 0f, MatrixOrder.Append);
        if (!waveMotionHorizontal)
        {
            brush.RotateTransform(90f, MatrixOrder.Append);
            brush.TranslateTransform(0f, band, MatrixOrder.Append);
        }

        return brush;
    }

    /// <summary>
    /// One tile period is A→B→A with smooth ramps so WrapMode.Tile has no visible discontinuity.
    /// </summary>
    private static void ApplySeamlessSymmetricTwoColorTileBlend(LinearGradientBrush brush, Color a, Color b)
    {
        try
        {
            var positions = new float[]
            {
                0f, 0.06f, 0.12f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.88f, 0.94f, 1f
            };
            var colors = new Color[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                float t = positions[i];
                float tri = t <= 0.5f ? t * 2f : (1f - t) * 2f;
                float s = tri * tri * (3f - 2f * tri);
                colors[i] = LerpRgb(a, b, s);
            }

            colors[0] = a;
            colors[^1] = a;

            brush.InterpolationColors = new ColorBlend
            {
                Positions = positions,
                Colors = colors
            };
        }
        catch (ArgumentException)
        {
            ApplySoftTwoColorBlend(brush, a, b);
        }
    }

    private static void ApplySoftTwoColorBlend(LinearGradientBrush brush, Color a, Color b)
    {
        try
        {
            var blend = new ColorBlend
            {
                Positions = new float[]
                {
                    0f, 0.06f, 0.14f, 0.24f, 0.36f, 0.5f, 0.64f, 0.76f, 0.86f, 0.94f, 1f
                },
                Colors = new[]
                {
                    a,
                    LerpRgb(a, b, 0.05f),
                    LerpRgb(a, b, 0.12f),
                    LerpRgb(a, b, 0.22f),
                    LerpRgb(a, b, 0.35f),
                    LerpRgb(a, b, 0.5f),
                    LerpRgb(a, b, 0.65f),
                    LerpRgb(a, b, 0.78f),
                    LerpRgb(a, b, 0.88f),
                    LerpRgb(a, b, 0.95f),
                    b
                }
            };
            brush.InterpolationColors = blend;
        }
        catch (ArgumentException)
        {
            // Brush may reject blend for degenerate geometry; keep the two-stop default.
        }
    }

    private static Color LerpRgb(Color x, Color y, float t)
    {
        if (t < 0f)
        {
            t = 0f;
        }
        else if (t > 1f)
        {
            t = 1f;
        }

        return Color.FromArgb(
            (int)Math.Round(x.A + (y.A - x.A) * t),
            (int)Math.Round(x.R + (y.R - x.R) * t),
            (int)Math.Round(x.G + (y.G - x.G) * t),
            (int)Math.Round(x.B + (y.B - x.B) * t));
    }

    private static Color MultiplyAlpha(Color rgb, int alpha255)
    {
        int a = (int)Math.Round(rgb.A / 255.0 * alpha255);
        if (a < 0)
        {
            a = 0;
        }
        else if (a > 255)
        {
            a = 255;
        }

        return Color.FromArgb(a, rgb.R, rgb.G, rgb.B);
    }

    private static Color ColorFromHsv(float hDegrees, float s, float v)
    {
        float h = hDegrees % 360f;
        if (h < 0)
        {
            h += 360f;
        }

        if (s <= 0f)
        {
            byte vv = (byte)Math.Round(v * 255f);
            return Color.FromArgb(vv, vv, vv);
        }

        float hf = h / 60f;
        int sector = (int)Math.Floor(hf);
        float f = hf - sector;
        float p = v * (1f - s);
        float q = v * (1f - s * f);
        float t = v * (1f - s * (1f - f));

        switch (sector % 6)
        {
            case 0:
                return RgbByte(v, t, p);
            case 1:
                return RgbByte(q, v, p);
            case 2:
                return RgbByte(p, v, t);
            case 3:
                return RgbByte(p, q, v);
            case 4:
                return RgbByte(t, p, v);
            default:
                return RgbByte(v, p, q);
        }
    }

    private static Color RgbByte(float r, float g, float b)
    {
        static byte B(float x)
        {
            if (x < 0f)
            {
                return 0;
            }

            if (x > 1f)
            {
                return 255;
            }

            return (byte)Math.Round(x * 255f);
        }

        return Color.FromArgb(B(r), B(g), B(b));
    }
}
