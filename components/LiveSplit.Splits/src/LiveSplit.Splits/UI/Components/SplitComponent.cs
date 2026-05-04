using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;

using LiveSplit.Model;
using LiveSplit.TimeFormatters;
using LiveSplit.UI;

namespace LiveSplit.UI.Components;

[GlobalFontConsumer(GlobalFont.TimesFont | GlobalFont.TextFont)]
public class SplitComponent : IComponent
{
    public ISegment Split { get; set; }

    protected SimpleLabel NameLabel { get; set; }
    public SplitsSettings Settings { get; set; }

    protected int FrameCount { get; set; }

    public GraphicsCache Cache { get; set; }
    protected bool NeedUpdateAll { get; set; }
    protected bool IsActive { get; set; }

    protected TimeAccuracy CurrentAccuracy { get; set; }
    protected TimeAccuracy CurrentDeltaAccuracy { get; set; }
    protected bool CurrentDropDecimals { get; set; }

    protected ITimeFormatter TimeFormatter { get; set; }
    protected ITimeFormatter DeltaTimeFormatter { get; set; }

    protected int IconWidth => DisplayIcon ? (int)(Settings.IconSize + 7.5f) : 0;

    public bool DisplayIcon { get; set; }

    public Image ShadowImage { get; set; }
    protected Image OldImage { get; set; }

    public float PaddingTop => 0f;
    public float PaddingLeft => 0f;
    public float PaddingBottom => 0f;
    public float PaddingRight => 0f;

    public IEnumerable<ColumnData> ColumnsList { get; set; }
    public IList<SimpleLabel> LabelsList { get; set; }
    protected List<(int exLength, float exWidth, float width)> ColumnWidths { get; }

    public float VerticalHeight { get; set; }

    public float MinimumWidth
        => CalculateLabelsWidth() + IconWidth + 10;

    public float HorizontalWidth
        => Settings.SplitWidth + CalculateLabelsWidth() + IconWidth;

    public float MinimumHeight { get; set; }

    public IDictionary<string, Action> ContextMenuControls => null;

    public SplitComponent(SplitsSettings settings, IEnumerable<ColumnData> columnsList, List<(int exLength, float exWidth, float width)> columnWidths)
    {
        NameLabel = new SimpleLabel()
        {
            HorizontalAlignment = StringAlignment.Near,
            X = 8,
        };
        Settings = settings;
        ColumnsList = columnsList;
        ColumnWidths = columnWidths;
        TimeFormatter = new SplitTimeFormatter(Settings.SplitTimesAccuracy);
        DeltaTimeFormatter = new DeltaSplitTimeFormatter(Settings.DeltasAccuracy, Settings.DropDecimals);
        MinimumHeight = 25;
        VerticalHeight = 31;

        NeedUpdateAll = true;
        IsActive = false;

        Cache = new GraphicsCache();
        LabelsList = [];
    }

    private void DrawGeneral(Graphics g, LiveSplitState state, float width, float height, LayoutMode mode)
    {
        if (NeedUpdateAll)
        {
            UpdateAll(state);
        }

        if (Settings.BackgroundGradient == ExtendedGradientType.Alternating)
        {
            g.FillRectangle(new SolidBrush(
                (state.Run.IndexOf(Split) % 2) + (Settings.ShowColumnLabels ? 1 : 0) == 1
                ? Settings.BackgroundColor2
                : Settings.BackgroundColor
                ), 0, 0, width, height);
        }

        state.LayoutSettings.ApplyTextShadowTo(NameLabel);
        NameLabel.OutlineColor = state.LayoutSettings.TextOutlineColor;
        foreach (SimpleLabel label in LabelsList)
        {
            state.LayoutSettings.ApplyTextShadowTo(label);
            label.OutlineColor = state.LayoutSettings.TextOutlineColor;
        }

        if (Settings.SplitTimesAccuracy != CurrentAccuracy)
        {
            TimeFormatter = new SplitTimeFormatter(Settings.SplitTimesAccuracy);
            CurrentAccuracy = Settings.SplitTimesAccuracy;
        }

        if (Settings.DeltasAccuracy != CurrentDeltaAccuracy || Settings.DropDecimals != CurrentDropDecimals)
        {
            DeltaTimeFormatter = new DeltaSplitTimeFormatter(Settings.DeltasAccuracy, Settings.DropDecimals);
            CurrentDeltaAccuracy = Settings.DeltasAccuracy;
            CurrentDropDecimals = Settings.DropDecimals;
        }

        if (Split != null)
        {

            if (mode == LayoutMode.Vertical)
            {
                NameLabel.VerticalAlignment = StringAlignment.Center;
                NameLabel.Y = 0;
                NameLabel.Height = height;
                foreach (SimpleLabel label in LabelsList)
                {
                    label.VerticalAlignment = StringAlignment.Center;
                    label.Y = 0;
                    label.Height = height;
                }
            }
            else
            {
                NameLabel.VerticalAlignment = StringAlignment.Near;
                NameLabel.Y = 0;
                NameLabel.Height = 50;
                foreach (SimpleLabel label in LabelsList)
                {
                    label.VerticalAlignment = StringAlignment.Far;
                    label.Y = height - 50;
                    label.Height = 50;
                }
            }

            bool isCurrentSplitVisual = Split == state.CurrentSplit
                && (state.CurrentPhase == TimerPhase.Running || state.CurrentPhase == TimerPhase.Paused);
            if (isCurrentSplitVisual)
            {
                if (Settings.CurrentSplitGradient == GradientType.Image)
                {
                    // Always paint an opaque gradient base first so the split row is fully
                    // visible in the embedded-overlay path (which uses SetWindowRgn based on
                    // alpha >= 16). Without this, a GIF with transparent pixels would leave
                    // holes in the overlay and the current-split area would be indistinguishable
                    // from the video background.
                    using var baseBrush = new LinearGradientBrush(
                        new PointF(0, 0),
                        new PointF(0, height),
                        Settings.CurrentSplitTopColor,
                        Settings.CurrentSplitBottomColor);
                    g.FillRectangle(baseBrush, 0, 0, width, height);

                    Image bg = Settings.GetCurrentSplitBackgroundImageForRendering();
                    if (bg != null)
                    {
                        DrawCurrentSplitBackgroundImageScaled(g, bg, width, height, Settings.CurrentSplitImageInterpolation);
                    }
                }
                else
                {
                    bool rowGradientHorizontal = Settings.CurrentSplitGradient == GradientType.Horizontal;
                    Color fillEnd = Settings.CurrentSplitGradient == GradientType.Plain
                        ? Settings.CurrentSplitTopColor
                        : Settings.CurrentSplitBottomColor;
                    bool animatedFill = Settings.CurrentSplitGradientFillRgbWave
                        || Settings.CurrentSplitGradientFillWaveSpeed > 0;

                    if (animatedFill)
                    {
                        using Brush fillBrush = CurrentSplitOutlinePaint.CreateRowFillBrush(
                            width,
                            height,
                            255,
                            Settings.CurrentSplitGradientFillRgbWave,
                            Settings.CurrentSplitGradientFillWaveAxis,
                            Settings.CurrentSplitGradientFillWaveSpeed,
                            rowGradientHorizontal,
                            Settings.CurrentSplitTopColor,
                            fillEnd);
                        CompositingQuality oldQuality = g.CompositingQuality;
                        PixelOffsetMode oldPixelOffset = g.PixelOffsetMode;
                        SmoothingMode oldSmoothing = g.SmoothingMode;
                        try
                        {
                            g.CompositingQuality = CompositingQuality.HighQuality;
                            g.PixelOffsetMode = PixelOffsetMode.Half;
                            g.SmoothingMode = SmoothingMode.None;
                            g.FillRectangle(fillBrush, 0, 0, width, height);
                        }
                        finally
                        {
                            g.CompositingQuality = oldQuality;
                            g.PixelOffsetMode = oldPixelOffset;
                            g.SmoothingMode = oldSmoothing;
                        }
                    }
                    else
                    {
                        using var currentSplitBrush = new LinearGradientBrush(
                            new PointF(0, 0),
                            rowGradientHorizontal ? new PointF(width, 0) : new PointF(0, height),
                            Settings.CurrentSplitTopColor,
                            fillEnd);
                        g.FillRectangle(currentSplitBrush, 0, 0, width, height);
                    }
                }

                DrawCurrentSplitOutline(g, width, height);
            }

            Image icon = Split.Icon;
            if (DisplayIcon && icon != null)
            {
                Image shadow = ShadowImage;

                if (OldImage != icon)
                {
                    ImageAnimator.Animate(icon, (s, o) => { });
                    ImageAnimator.Animate(shadow, (s, o) => { });
                    OldImage = icon;
                }

                float drawWidth = Settings.IconSize;
                float drawHeight = Settings.IconSize;
                float shadowWidth = Settings.IconSize * (5 / 4f);
                float shadowHeight = Settings.IconSize * (5 / 4f);
                if (icon.Width > icon.Height)
                {
                    float ratio = icon.Height / (float)icon.Width;
                    drawHeight *= ratio;
                    shadowHeight *= ratio;
                }
                else
                {
                    float ratio = icon.Width / (float)icon.Height;
                    drawWidth *= ratio;
                    shadowWidth *= ratio;
                }

                ImageAnimator.UpdateFrames(shadow);
                if (Settings.IconShadows && shadow != null)
                {
                    g.DrawImage(
                        shadow,
                        7 + (((Settings.IconSize * (5 / 4f)) - shadowWidth) / 2) - 0.7f,
                        ((height - Settings.IconSize) / 2.0f) + (((Settings.IconSize * (5 / 4f)) - shadowHeight) / 2) - 0.7f,
                        shadowWidth,
                        shadowHeight);
                }

                ImageAnimator.UpdateFrames(icon);

                g.DrawImage(
                    icon,
                    7 + ((Settings.IconSize - drawWidth) / 2),
                    ((height - Settings.IconSize) / 2.0f) + ((Settings.IconSize - drawHeight) / 2),
                    drawWidth,
                    drawHeight);
            }

            NameLabel.Font = state.LayoutSettings.TextFont;
            NameLabel.X = 5 + IconWidth;
            NameLabel.HasShadow = state.LayoutSettings.DropShadows;

            if (ColumnsList.Count() == LabelsList.Count)
            {
                while (ColumnWidths.Count < LabelsList.Count)
                {
                    ColumnWidths.Add((0, 0f, 0f));
                }

                float curX = width - 7;
                float nameX = width - 7;
                foreach (SimpleLabel label in LabelsList.Reverse())
                {
                    int i = LabelsList.IndexOf(label);
                    float labelWidth = ColumnWidths[i].width;

                    label.Width = labelWidth + 20;
                    curX -= labelWidth + 5;
                    label.X = curX - 15;

                    label.Font = state.LayoutSettings.TimesFont;
                    label.HasShadow = state.LayoutSettings.DropShadows;
                    label.IsMonospaced = true;
                    label.Draw(g);

                    if (!string.IsNullOrEmpty(label.Text))
                    {
                        nameX = curX + labelWidth + 5 - label.ActualWidth;
                        if (ColumnWidths[i].exWidth < label.ActualWidth)
                        {
                            ColumnWidths[i] = (label.Text.Length, label.ActualWidth, labelWidth);
                        }
                    }
                }

                NameLabel.Width = (mode == LayoutMode.Horizontal ? width - 10 : nameX) - IconWidth;
                NameLabel.Draw(g);
            }
        }
        else
        {
            DisplayIcon = Settings.DisplayIcons;
        }
    }

    public void DrawVertical(Graphics g, LiveSplitState state, float width, Region clipRegion)
    {
        if (Settings.Display2Rows)
        {
            VerticalHeight = Settings.SplitHeight + (0.85f * (g.MeasureString("A", state.LayoutSettings.TimesFont).Height + g.MeasureString("A", state.LayoutSettings.TextFont).Height));
            DrawGeneral(g, state, width, VerticalHeight, LayoutMode.Horizontal);
        }
        else
        {
            VerticalHeight = Settings.SplitHeight + 25;
            DrawGeneral(g, state, width, VerticalHeight, LayoutMode.Vertical);
        }
    }

    public void DrawHorizontal(Graphics g, LiveSplitState state, float height, Region clipRegion)
    {
        MinimumHeight = 0.85f * (g.MeasureString("A", state.LayoutSettings.TimesFont).Height + g.MeasureString("A", state.LayoutSettings.TextFont).Height);
        DrawGeneral(g, state, HorizontalWidth, height, LayoutMode.Horizontal);
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

    private static void DrawCurrentSplitBackgroundImageScaled(Graphics g, Image bg, float width, float height, CurrentSplitImageInterpolationFilter filter)
    {
        ImageAnimator.UpdateFrames(bg);
        var dest = new RectangleF(0, 0, width, height);
        InterpolationMode previous = g.InterpolationMode;
        try
        {
            g.InterpolationMode = MapImageInterpolation(filter);
            g.DrawImage(bg, dest);
        }
        finally
        {
            g.InterpolationMode = previous;
        }
    }

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
        // Half can leave a 1px hairline between stacked split rows; Default keeps AA without that gap.
        g.PixelOffsetMode = filter == CurrentSplitImageInterpolationFilter.Bicubic
            || filter == CurrentSplitImageInterpolationFilter.Lanczos
            ? PixelOffsetMode.HighQuality
            : PixelOffsetMode.Default;
        g.InterpolationMode = MapImageInterpolation(filter);
    }

    private static bool OutlineBrushUsesSmoothColorSampling(SplitsSettings settings) =>
        settings.CurrentSplitOutlineRgbWave
        || settings.CurrentSplitOutlineFillMode == CurrentSplitOutlineFillMode.Gradient;

    private void DrawCurrentSplitOutline(Graphics g, float width, float height)
    {
        if (!Settings.CurrentSplitOutlineEnabled || width <= 1f || height <= 1f)
        {
            return;
        }

        decimal t = Settings.CurrentSplitOutlineTransparency;
        if (t < 0m)
        {
            t = 0m;
        }
        else if (t > 100m)
        {
            t = 100m;
        }

        int alpha = (int)Math.Round(255.0 * (double)(1m - t / 100m));
        if (alpha <= 0)
        {
            return;
        }

        float thickness = (float)Math.Max(0.1m, Settings.CurrentSplitOutlineThickness);

        using (Brush outlineBrush = CurrentSplitOutlinePaint.CreateOutlineBrush(
            width,
            height,
            alpha,
            Settings.CurrentSplitOutlineRgbWave,
            Settings.CurrentSplitOutlineWaveAxis,
            Settings.CurrentSplitOutlineWaveSpeed,
            Settings.CurrentSplitOutlineFillMode,
            Settings.CurrentSplitOutlineColor,
            Settings.CurrentSplitOutlineGradientEndColor))
        {
            if (OutlineUsesSmoothStroke(Settings.CurrentSplitOutlineInterpolation))
            {
                // Center-aligned stroke on a rectangle inset by pen/2 so the stroke meets the cell edge evenly on all sides.
                // GDI+ anti-aliasing nearly erases sub-1px widths with Inset + integer rect; use at least 1px width and scale alpha.
                float requestedW = thickness;
                float penW = requestedW;
                int drawAlpha = alpha;
                if (penW < 1f)
                {
                    drawAlpha = alpha > 0 ? Math.Max(1, (int)Math.Round(alpha * penW)) : 0;
                    penW = 1f;
                }

                float sizeLimit = Math.Max(0.1f, Math.Min(width, height) - 0.01f);
                penW = Math.Min(penW, sizeLimit);
                if (penW < 0.1f || drawAlpha <= 0)
                {
                    return;
                }

                float inset = penW * 0.5f;
                float rectW = width - penW;
                float rectH = height - penW;
                if (rectW < 0.01f || rectH < 0.01f)
                {
                    return;
                }

                using var penAa = new Pen(outlineBrush, penW)
                {
                    Alignment = PenAlignment.Center,
                    LineJoin = LineJoin.Round
                };

                GraphicsState gs = g.Save();
                try
                {
                    ApplyOutlineGraphicsQuality(g, Settings.CurrentSplitOutlineInterpolation);
                    // NearestNeighbor on the Graphics object also quantizes gradient-brush lookups, which
                    // makes two-color outlines look stepped; keep stroke quality settings but smooth brush sampling.
                    if (OutlineBrushUsesSmoothColorSampling(Settings))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    }

                    // Slight height extension covers the common 1px seam between this row and the next.
                    g.DrawRectangle(penAa, inset, inset, rectW, rectH + 0.5f);
                }
                finally
                {
                    g.Restore(gs);
                }
            }
            else
            {
                using var pen = new Pen(outlineBrush, thickness) { Alignment = PenAlignment.Inset };
                GraphicsState gs = g.Save();
                try
                {
                    g.SmoothingMode = SmoothingMode.None;
                    g.PixelOffsetMode = PixelOffsetMode.None;
                    if (OutlineBrushUsesSmoothColorSampling(Settings))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    }

                    if (thickness <= 1.05f)
                    {
                        g.DrawRectangle(pen, 0.5f, 0.5f, width - 1f, height - 1f);
                    }
                    else
                    {
                        g.DrawRectangle(pen, 0, 0, width - 1, height - 1);
                    }
                }
                finally
                {
                    g.Restore(gs);
                }
            }
        }
    }

    public string ComponentName => "Split";

    public Control GetSettingsControl(LayoutMode mode)
    {
        throw new NotSupportedException();
    }

    public void SetSettings(System.Xml.XmlNode settings)
    {
        throw new NotSupportedException();
    }

    public System.Xml.XmlNode GetSettings(System.Xml.XmlDocument document)
    {
        throw new NotSupportedException();
    }

    public string UpdateName => throw new NotSupportedException();

    public string XMLURL => throw new NotSupportedException();

    public string UpdateURL => throw new NotSupportedException();

    public Version Version => throw new NotSupportedException();

    protected void UpdateAll(LiveSplitState state)
    {
        if (Split != null)
        {
            RecreateLabels();

            if (Settings.AutomaticAbbreviations)
            {
                if (NameLabel.Text != Split.Name || NameLabel.AlternateText == null || !NameLabel.AlternateText.Any())
                {
                    NameLabel.AlternateText = Split.Name.GetAbbreviations().ToList();
                }
            }
            else if (NameLabel.AlternateText != null && NameLabel.AlternateText.Any())
            {
                NameLabel.AlternateText.Clear();
            }

            NameLabel.Text = Split.Name;

            int splitIndex = state.Run.IndexOf(Split);
            if (splitIndex < state.CurrentSplitIndex)
            {
                NameLabel.ForeColor = Settings.OverrideTextColor ? Settings.BeforeNamesColor : state.LayoutSettings.TextColor;
            }
            else
            {
                if (Split == state.CurrentSplit)
                {
                    NameLabel.ForeColor = Settings.OverrideTextColor ? Settings.CurrentNamesColor : state.LayoutSettings.TextColor;
                }
                else
                {
                    NameLabel.ForeColor = Settings.OverrideTextColor ? Settings.AfterNamesColor : state.LayoutSettings.TextColor;
                }
            }

            foreach (SimpleLabel label in LabelsList)
            {
                ColumnData column = ColumnsList.ElementAt(LabelsList.IndexOf(label));
                UpdateColumn(state, label, column);
            }
        }
    }

    protected void UpdateColumn(LiveSplitState state, SimpleLabel label, ColumnData data)
    {
        string comparison = data.Comparison == "Current Comparison" ? state.CurrentComparison : data.Comparison;
        if (!state.Run.Comparisons.Contains(comparison))
        {
            comparison = state.CurrentComparison;
        }

        TimingMethod timingMethod = state.CurrentTimingMethod;
        if (data.TimingMethod == "Real Time")
        {
            timingMethod = TimingMethod.RealTime;
        }
        else if (data.TimingMethod == "Game Time")
        {
            timingMethod = TimingMethod.GameTime;
        }

        ColumnType type = data.Type;

        int splitIndex = state.Run.IndexOf(Split);
        if (splitIndex < state.CurrentSplitIndex)
        {
            if (type is ColumnType.SplitTime or ColumnType.SegmentTime or ColumnType.CustomVariable)
            {
                label.ForeColor = Settings.OverrideTimesColor ? Settings.BeforeTimesColor : state.LayoutSettings.TextColor;

                if (type == ColumnType.SplitTime)
                {
                    label.Text = TimeFormatter.Format(Split.SplitTime[timingMethod]);
                }
                else if (type == ColumnType.SegmentTime)
                {
                    TimeSpan? segmentTime = LiveSplitStateHelper.GetPreviousSegmentTime(state, splitIndex, timingMethod);
                    label.Text = TimeFormatter.Format(segmentTime);
                }
                else if (type == ColumnType.CustomVariable)
                {
                    Split.CustomVariableValues.TryGetValue(data.Name, out string text);
                    label.Text = text ?? "";
                }
            }

            if (type is ColumnType.DeltaorSplitTime or ColumnType.Delta)
            {
                TimeSpan? deltaTime = Split.SplitTime[timingMethod] - Split.Comparisons[comparison][timingMethod];
                Color? color = LiveSplitStateHelper.GetSplitColor(state, deltaTime, splitIndex, true, true, comparison, timingMethod);
                if (color == null)
                {
                    color = Settings.OverrideTimesColor ? Settings.BeforeTimesColor : state.LayoutSettings.TextColor;
                }

                label.ForeColor = color.Value;

                if (type == ColumnType.DeltaorSplitTime)
                {
                    if (deltaTime != null)
                    {
                        label.Text = DeltaTimeFormatter.Format(deltaTime);
                    }
                    else
                    {
                        label.Text = TimeFormatter.Format(Split.SplitTime[timingMethod]);
                    }
                }

                else if (type == ColumnType.Delta)
                {
                    label.Text = DeltaTimeFormatter.Format(deltaTime);
                }
            }

            else if (type is ColumnType.SegmentDeltaorSegmentTime or ColumnType.SegmentDelta)
            {
                TimeSpan? segmentDelta = LiveSplitStateHelper.GetPreviousSegmentDelta(state, splitIndex, comparison, timingMethod);
                Color? color = LiveSplitStateHelper.GetSplitColor(state, segmentDelta, splitIndex, false, true, comparison, timingMethod);
                if (color == null)
                {
                    color = Settings.OverrideTimesColor ? Settings.BeforeTimesColor : state.LayoutSettings.TextColor;
                }

                label.ForeColor = color.Value;

                if (type == ColumnType.SegmentDeltaorSegmentTime)
                {
                    if (segmentDelta != null)
                    {
                        label.Text = DeltaTimeFormatter.Format(segmentDelta);
                    }
                    else
                    {
                        label.Text = TimeFormatter.Format(LiveSplitStateHelper.GetPreviousSegmentTime(state, splitIndex, timingMethod));
                    }
                }
                else if (type == ColumnType.SegmentDelta)
                {
                    label.Text = DeltaTimeFormatter.Format(segmentDelta);
                }
            }
        }
        else
        {
            if (type is ColumnType.SplitTime or ColumnType.SegmentTime or ColumnType.DeltaorSplitTime or ColumnType.SegmentDeltaorSegmentTime or ColumnType.CustomVariable)
            {
                if (Split == state.CurrentSplit)
                {
                    label.ForeColor = Settings.OverrideTimesColor ? Settings.CurrentTimesColor : state.LayoutSettings.TextColor;
                }
                else
                {
                    label.ForeColor = Settings.OverrideTimesColor ? Settings.AfterTimesColor : state.LayoutSettings.TextColor;
                }

                if (type is ColumnType.SplitTime or ColumnType.DeltaorSplitTime)
                {
                    label.Text = TimeFormatter.Format(Split.Comparisons[comparison][timingMethod]);
                }
                else if (type is ColumnType.SegmentTime or ColumnType.SegmentDeltaorSegmentTime)
                {
                    TimeSpan previousTime = TimeSpan.Zero;
                    for (int index = splitIndex - 1; index >= 0; index--)
                    {
                        TimeSpan? comparisonTime = state.Run[index].Comparisons[comparison][timingMethod];
                        if (comparisonTime != null)
                        {
                            previousTime = comparisonTime.Value;
                            break;
                        }
                    }

                    label.Text = TimeFormatter.Format(Split.Comparisons[comparison][timingMethod] - previousTime);
                }
                else if (type is ColumnType.CustomVariable)
                {
                    if (splitIndex == state.CurrentSplitIndex)
                    {
                        label.Text = state.Run.Metadata.CustomVariableValue(data.Name) ?? "";
                    }
                    else if (splitIndex > state.CurrentSplitIndex)
                    {
                        label.Text = "";
                    }
                }
            }

            //Live Delta
            bool splitDelta = type is ColumnType.DeltaorSplitTime or ColumnType.Delta;
            TimeSpan? bestDelta = LiveSplitStateHelper.CheckLiveDelta(state, splitDelta, comparison, timingMethod);
            if (bestDelta != null && Split == state.CurrentSplit &&
                (type == ColumnType.DeltaorSplitTime || type == ColumnType.Delta || type == ColumnType.SegmentDeltaorSegmentTime || type == ColumnType.SegmentDelta))
            {
                label.Text = DeltaTimeFormatter.Format(bestDelta);
                label.ForeColor = Settings.OverrideDeltasColor ? Settings.DeltasColor : state.LayoutSettings.TextColor;
            }
            else if (type is ColumnType.Delta or ColumnType.SegmentDelta)
            {
                label.Text = "";
            }
        }
    }

    protected float CalculateLabelsWidth()
    {
        if (ColumnWidths != null)
        {
            return ColumnWidths.Sum(e => e.width) + (5 * ColumnWidths.Count());
        }

        return 0f;
    }

    protected void RecreateLabels()
    {
        if (ColumnsList != null && LabelsList.Count != ColumnsList.Count())
        {
            LabelsList.Clear();
            foreach (ColumnData column in ColumnsList)
            {
                LabelsList.Add(new SimpleLabel
                {
                    HorizontalAlignment = StringAlignment.Far
                });
            }
        }
    }

    private int CurrentSplitBackgroundFrameCount()
    {
        if (!IsActive || Settings.CurrentSplitGradient != GradientType.Image)
        {
            return 1;
        }

        Image bg = Settings.GetCurrentSplitBackgroundImageForRendering();
        if (bg == null || bg.FrameDimensionsList.Length == 0)
        {
            return 1;
        }

        return Math.Max(1, bg.GetFrameCount(new FrameDimension(bg.FrameDimensionsList[0])));
    }

    public void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode)
    {
        if (Split != null)
        {
            UpdateAll(state);
            NeedUpdateAll = false;

            // Also highlight the current split when the timer hasn't started yet so the runner
            // can see which split they're on at all times (especially important in embedded-video
            // mode where the split row is otherwise fully transparent against the video).
            IsActive = (state.CurrentPhase == TimerPhase.Running
                        || state.CurrentPhase == TimerPhase.Paused
                        || state.CurrentPhase == TimerPhase.NotRunning) &&
                                                state.CurrentSplit == Split;

            Cache.Restart();
            Cache["Icon"] = Split.Icon;
            if (Cache.HasChanged)
            {
                if (Split.Icon == null)
                {
                    FrameCount = 0;
                }
                else
                {
                    FrameCount = Split.Icon.GetFrameCount(new FrameDimension(Split.Icon.FrameDimensionsList[0]));
                }
            }

            Cache["DisplayIcon"] = DisplayIcon;
            Cache["SplitName"] = NameLabel.Text;
            Cache["IsActive"] = IsActive;
            Cache["CurrentSplitGradient"] = Settings.CurrentSplitGradient;
            Cache["CurrentSplitBgPath"] = Settings.CurrentSplitBackgroundImagePath ?? string.Empty;
            Cache["CurrentSplitTopColor"] = Settings.CurrentSplitTopColor.ToArgb();
            Cache["CurrentSplitBottomColor"] = Settings.CurrentSplitBottomColor.ToArgb();
            Cache["CurrentSplitOutlineEnabled"] = Settings.CurrentSplitOutlineEnabled;
            Cache["CurrentSplitOutlineThickness"] = Settings.CurrentSplitOutlineThickness;
            Cache["CurrentSplitOutlineColor"] = Settings.CurrentSplitOutlineColor.ToArgb();
            Cache["CurrentSplitOutlineTransparency"] = Settings.CurrentSplitOutlineTransparency;
            Cache["CurrentSplitImageInterpolation"] = Settings.CurrentSplitImageInterpolation;
            Cache["CurrentSplitOutlineInterpolation"] = Settings.CurrentSplitOutlineInterpolation;
            Cache["CurrentSplitOutlineFillMode"] = Settings.CurrentSplitOutlineFillMode;
            Cache["CurrentSplitOutlineGradientEndColor"] = Settings.CurrentSplitOutlineGradientEndColor.ToArgb();
            Cache["CurrentSplitOutlineRgbWave"] = Settings.CurrentSplitOutlineRgbWave;
            Cache["CurrentSplitOutlineWaveAxis"] = Settings.CurrentSplitOutlineWaveAxis;
            Cache["CurrentSplitOutlineWaveSpeed"] = Settings.CurrentSplitOutlineWaveSpeed;
            Cache["CurrentSplitGradientFillRgbWave"] = Settings.CurrentSplitGradientFillRgbWave;
            Cache["CurrentSplitGradientFillWaveAxis"] = Settings.CurrentSplitGradientFillWaveAxis;
            Cache["CurrentSplitGradientFillWaveSpeed"] = Settings.CurrentSplitGradientFillWaveSpeed;
            Cache["NameColor"] = NameLabel.ForeColor.ToArgb();
            Cache["ColumnsCount"] = ColumnsList.Count();
            for (int index = 0; index < LabelsList.Count; index++)
            {
                SimpleLabel label = LabelsList[index];
                Cache["Columns" + index + "Text"] = label.Text;
                Cache["Columns" + index + "Color"] = label.ForeColor.ToArgb();
                if (index < ColumnWidths.Count)
                {
                    Cache["Columns" + index + "Width"] = ColumnWidths[index].width;
                }
            }

            int currentSplitBgFrames = CurrentSplitBackgroundFrameCount();
            bool outlineTwoColorWave = !Settings.CurrentSplitOutlineRgbWave
                && Settings.CurrentSplitOutlineWaveSpeed > 0
                && Settings.CurrentSplitOutlineFillMode == CurrentSplitOutlineFillMode.Gradient;
            bool outlineRgbAnim = Settings.CurrentSplitOutlineRgbWave
                && Settings.CurrentSplitOutlineFillMode != CurrentSplitOutlineFillMode.Solid;
            bool outlineWaveAnim = IsActive &&
                Settings.CurrentSplitOutlineEnabled &&
                Settings.CurrentSplitOutlineWaveSpeed > 0
                && (outlineRgbAnim || outlineTwoColorWave);
            bool fillTwoColorWave = !Settings.CurrentSplitGradientFillRgbWave
                && Settings.CurrentSplitGradientFillWaveSpeed > 0
                && Settings.CurrentSplitGradient != GradientType.Plain;
            bool fillRgbAnim = Settings.CurrentSplitGradientFillRgbWave;
            bool fillWaveAnim = IsActive
                && Settings.CurrentSplitGradient != GradientType.Image
                && Settings.CurrentSplitGradientFillWaveSpeed > 0
                && (fillRgbAnim || fillTwoColorWave);
            if (invalidator != null && (Cache.HasChanged || FrameCount > 1 || currentSplitBgFrames > 1 || outlineWaveAnim || fillWaveAnim))
            {
                invalidator.Invalidate(0, 0, width, height);
            }
        }
    }

    public void Dispose()
    {
    }
}
