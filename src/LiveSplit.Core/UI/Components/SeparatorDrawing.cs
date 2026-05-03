using System;
using System.Drawing;
using System.Drawing.Drawing2D;

using LiveSplit.Options;
using LiveSplit.UI;

namespace LiveSplit.UI.Components;

/// <summary>
/// Fills layout separator strips using the same gradient / wave model as the current-split outline.
/// </summary>
internal static class SeparatorDrawing
{
    internal static void FillSeparatorRect(
        Graphics g,
        LayoutSettings layout,
        float x,
        float y,
        float w,
        float h,
        Color primaryColor)
    {
        w = Math.Max(0.0001f, w);
        h = Math.Max(0.0001f, h);
        CurrentSplitOutlineWaveAxis waveAxis = MapWaveAxisForStrip(w >= h, layout.SeparatorWaveAxis);
        if (layout.SeparatorFillMode == CurrentSplitOutlineFillMode.Solid && !layout.SeparatorRgbWave)
        {
            using var brush = new SolidBrush(primaryColor);
            g.FillRectangle(brush, x, y, w, h);
            return;
        }

        using Brush brush2 = CurrentSplitOutlinePaint.CreateOutlineBrush(
            w,
            h,
            255,
            layout.SeparatorRgbWave,
            waveAxis,
            layout.SeparatorWaveSpeed,
            layout.SeparatorFillMode,
            primaryColor,
            layout.SeparatorGradientEndColor);
        g.FillRectangle(brush2, x, y, w, h);
    }

    internal static void DrawSeparatorOutline(
        Graphics g,
        LayoutSettings layout,
        float x,
        float y,
        float width,
        float height)
    {
        if (!layout.SeparatorOutlineEnabled || width <= 1f || height <= 1f)
        {
            return;
        }

        float t = layout.SeparatorOutlineTransparency;
        if (t < 0f)
        {
            t = 0f;
        }
        else if (t > 100f)
        {
            t = 100f;
        }

        int alpha = (int)Math.Round(255.0 * (1.0 - t / 100.0));
        if (alpha <= 0)
        {
            return;
        }

        float w = Math.Max(0.0001f, width);
        float h = Math.Max(0.0001f, height);
        CurrentSplitOutlineWaveAxis waveAxis = MapWaveAxisForStrip(w >= h, layout.SeparatorOutlineWaveAxis);
        float thickness = Math.Max(0.1f, layout.SeparatorOutlineThickness);

        using Brush outlineBrush = CurrentSplitOutlinePaint.CreateOutlineBrush(
            w,
            h,
            alpha,
            layout.SeparatorOutlineRgbWave,
            waveAxis,
            layout.SeparatorOutlineWaveSpeed,
            layout.SeparatorOutlineFillMode,
            layout.SeparatorOutlineColor,
            layout.SeparatorOutlineGradientEndColor);
        if (OutlineUsesSmoothStroke(layout.SeparatorOutlineInterpolation))
        {
            float requestedW = thickness;
            float penW = requestedW;
            int drawAlpha = alpha;
            if (penW < 1f)
            {
                drawAlpha = alpha > 0 ? Math.Max(1, (int)Math.Round(alpha * penW)) : 0;
                penW = 1f;
            }

            float sizeLimit = Math.Max(0.1f, Math.Min(w, h) - 0.01f);
            penW = Math.Min(penW, sizeLimit);
            if (penW < 0.1f || drawAlpha <= 0)
            {
                return;
            }

            float inset = penW * 0.5f;
            float rectW = w - penW;
            float rectH = h - penW;
            if (rectW < 0.01f || rectH < 0.01f)
            {
                return;
            }

            using var penAa = new Pen(outlineBrush, penW)
            {
                Alignment = PenAlignment.Center,
                LineJoin = LineJoin.Round
            };

            GraphicsState gsOuter = g.Save();
            try
            {
                g.TranslateTransform(x, y);
                GraphicsState gs = g.Save();
                try
                {
                    ApplyOutlineGraphicsQuality(g, layout.SeparatorOutlineInterpolation);
                    if (OutlineBrushUsesSmoothColorSampling(layout))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    }

                    g.DrawRectangle(penAa, inset, inset, rectW, rectH + 0.5f);
                }
                finally
                {
                    g.Restore(gs);
                }
            }
            finally
            {
                g.Restore(gsOuter);
            }
        }
        else
        {
            using var pen = new Pen(outlineBrush, thickness) { Alignment = PenAlignment.Inset };
            GraphicsState gsOuter = g.Save();
            try
            {
                g.TranslateTransform(x, y);
                GraphicsState gs = g.Save();
                try
                {
                    g.SmoothingMode = SmoothingMode.None;
                    g.PixelOffsetMode = PixelOffsetMode.None;
                    if (OutlineBrushUsesSmoothColorSampling(layout))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    }

                    if (thickness <= 1.05f)
                    {
                        g.DrawRectangle(pen, 0.5f, 0.5f, w - 1f, h - 1f);
                    }
                    else
                    {
                        g.DrawRectangle(pen, 0, 0, w - 1, h - 1);
                    }
                }
                finally
                {
                    g.Restore(gs);
                }
            }
            finally
            {
                g.Restore(gsOuter);
            }
        }
    }

    /// <summary>
    /// For a strip that runs primarily along X (horizontal bar), use the user's axis as-is.
    /// For a strip along Y (vertical bar), swap horizontal/vertical so "horizontal" still means along the layout's horizontal axis in screen space.
    /// </summary>
    private static CurrentSplitOutlineWaveAxis MapWaveAxisForStrip(bool stripRunsAlongX, CurrentSplitOutlineWaveAxis user)
    {
        if (stripRunsAlongX)
        {
            return user;
        }

        return user == CurrentSplitOutlineWaveAxis.Horizontal
            ? CurrentSplitOutlineWaveAxis.Vertical
            : CurrentSplitOutlineWaveAxis.Horizontal;
    }

    internal static bool SeparatorAppearanceIsAnimated(LayoutSettings layout) =>
        layout.SeparatorRgbWave
        || (layout.SeparatorFillMode == CurrentSplitOutlineFillMode.Gradient && layout.SeparatorWaveSpeed > 0);

    internal static bool SeparatorOutlineIsAnimated(LayoutSettings layout)
    {
        if (!layout.SeparatorOutlineEnabled)
        {
            return false;
        }

        bool outlineTwoColorWave = !layout.SeparatorOutlineRgbWave
            && layout.SeparatorOutlineWaveSpeed > 0
            && layout.SeparatorOutlineFillMode == CurrentSplitOutlineFillMode.Gradient;
        bool outlineRgbAnim = layout.SeparatorOutlineRgbWave
            && layout.SeparatorOutlineFillMode != CurrentSplitOutlineFillMode.Solid;
        return layout.SeparatorOutlineWaveSpeed > 0 && (outlineRgbAnim || outlineTwoColorWave);
    }

    private static InterpolationMode MapImageInterpolation(CurrentSplitImageInterpolationFilter filter) =>
        filter switch
        {
            CurrentSplitImageInterpolationFilter.Nearest => InterpolationMode.NearestNeighbor,
            CurrentSplitImageInterpolationFilter.Bilinear => InterpolationMode.Bilinear,
            CurrentSplitImageInterpolationFilter.Bicubic => InterpolationMode.Bicubic,
            CurrentSplitImageInterpolationFilter.Area => InterpolationMode.Low,
            CurrentSplitImageInterpolationFilter.Lanczos => InterpolationMode.HighQualityBicubic,
            _ => InterpolationMode.Default
        };

    private static bool OutlineUsesSmoothStroke(CurrentSplitImageInterpolationFilter filter) =>
        filter != CurrentSplitImageInterpolationFilter.Nearest;

    private static void ApplyOutlineGraphicsQuality(Graphics g, CurrentSplitImageInterpolationFilter filter)
    {
        if (filter == CurrentSplitImageInterpolationFilter.Nearest)
        {
            g.SmoothingMode = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.None;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            return;
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = filter == CurrentSplitImageInterpolationFilter.Bicubic
            || filter == CurrentSplitImageInterpolationFilter.Lanczos
            ? PixelOffsetMode.HighQuality
            : PixelOffsetMode.Default;
        g.InterpolationMode = MapImageInterpolation(filter);
    }

    private static bool OutlineBrushUsesSmoothColorSampling(LayoutSettings layout) =>
        layout.SeparatorOutlineRgbWave
        || layout.SeparatorOutlineFillMode == CurrentSplitOutlineFillMode.Gradient;
}
