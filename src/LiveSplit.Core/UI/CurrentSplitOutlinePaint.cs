using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace LiveSplit.UI;

public static class CurrentSplitOutlinePaint
{
    /// <summary>Monotonic clock for smooth gradient scroll; avoids 16 ms <see cref="Environment.TickCount"/> steps that look like broken transparency at high speed.</summary>
    private static readonly long AnimationEpochTicks = Stopwatch.GetTimestamp();

    private static float AnimationPhasePixels(float waveSpeed)
    {
        if (waveSpeed <= 0.0001f)
        {
            return 0f;
        }

        double elapsed = (Stopwatch.GetTimestamp() - AnimationEpochTicks) / (double)Stopwatch.Frequency;
        return (float)(elapsed * waveSpeed * 35.0);
    }

    private static float WrappedPhaseOffset(float waveSpeed, int tile)
    {
        if (tile <= 0)
        {
            return 0f;
        }

        double phase = AnimationPhasePixels(waveSpeed);
        double wrapped = phase - Math.Floor(phase / tile) * tile;
        return (float)wrapped;
    }

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

    /// <summary>
    /// Brush for the current-split row background when using a non-image gradient. Lets the fill scroll on an axis
    /// independent of whether the row gradient runs horizontally or vertically (unlike <see cref="CreateOutlineBrush"/>,
    /// which ties both to one wave axis).
    /// </summary>
    public static Brush CreateRowFillBrush(
        float width,
        float height,
        int alpha255,
        bool rgbWave,
        CurrentSplitOutlineWaveAxis motionAxis,
        int waveSpeed,
        bool rowGradientHorizontal,
        Color topColor,
        Color bottomColor)
    {
        float speed = waveSpeed;
        bool motionHorizontal = motionAxis == CurrentSplitOutlineWaveAxis.Horizontal;

        if (rgbWave)
        {
            return CreateRainbowBrush(width, height, alpha255, motionHorizontal, speed);
        }

        if (speed > 0.0001f)
        {
            return CreateTwoColorTiledWaveBrush(
                width,
                height,
                alpha255,
                rowGradientHorizontal,
                motionHorizontal,
                speed,
                topColor,
                bottomColor);
        }

        return CreateTwoPointGradient(width, height, alpha255, topColor, bottomColor, rowGradientHorizontal);
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

        brush.GammaCorrection = true;

        const int hueStops = 32;
        var positions = new float[hueStops];
        var colors = new Color[hueStops];
        for (int i = 0; i < hueStops; i++)
        {
            positions[i] = i / (float)(hueStops - 1);
            colors[i] = MultiplyAlpha(ColorFromHsv(i * 360f / (hueStops - 1), 1f, 1f), alpha255);
        }

        colors[^1] = colors[0];
        brush.InterpolationColors = new ColorBlend
        {
            Positions = positions,
            Colors = colors
        };
        brush.WrapMode = WrapMode.Tile;

        float offset = WrappedPhaseOffset(waveSpeed, tile);
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

        float offset = WrappedPhaseOffset(waveSpeed, tile);
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
            const int stopCount = 32;
            var positions = new float[stopCount];
            var colors = new Color[stopCount];
            for (int i = 0; i < stopCount; i++)
            {
                float t = i / (float)(stopCount - 1);
                float tri = t <= 0.5f ? t * 2f : (1f - t) * 2f;
                float s = tri * tri * (3f - 2f * tri);
                positions[i] = t;
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
            const int stopCount = 32;
            var positions = new float[stopCount];
            var colors = new Color[stopCount];
            for (int i = 0; i < stopCount; i++)
            {
                float t = i / (float)(stopCount - 1);
                positions[i] = t;
                colors[i] = LerpRgb(a, b, t);
            }

            colors[0] = a;
            colors[^1] = b;
            brush.InterpolationColors = new ColorBlend
            {
                Positions = positions,
                Colors = colors
            };
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
