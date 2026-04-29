using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

using LiveSplit.Model;
using LiveSplit.TimeFormatters;

namespace LiveSplit.UI.Components;

[GlobalFontConsumer(GlobalFont.TimesFont)]
public class SplitsComponent : IComponent
{
    public ComponentRendererComponent InternalComponent { get; protected set; }

    public float PaddingTop => InternalComponent.PaddingTop;
    public float PaddingLeft => InternalComponent.PaddingLeft;
    public float PaddingBottom => InternalComponent.PaddingBottom;
    public float PaddingRight => InternalComponent.PaddingRight;

    protected IList<IComponent> Components { get; set; }
    protected IList<SplitComponent> SplitComponents { get; set; }

    protected SplitsSettings Settings { get; set; }

    protected SimpleLabel MeasureTimeLabel { get; set; }
    protected SimpleLabel MeasureDeltaLabel { get; set; }
    protected SimpleLabel MeasureCharLabel { get; set; }

    protected TimeAccuracy CurrentAccuracy { get; set; }
    protected TimeAccuracy CurrentDeltaAccuracy { get; set; }
    protected bool CurrentDropDecimals { get; set; }

    protected ITimeFormatter TimeFormatter { get; set; }
    protected ITimeFormatter DeltaTimeFormatter { get; set; }

    private Dictionary<Image, Image> ShadowImages { get; set; }

    private int visualSplitCount;
    private int settingsSplitCount;

    protected bool PreviousShowLabels { get; set; }

    protected int ScrollOffset { get; set; }
    protected int LastSplitSeparatorIndex { get; set; }

    protected LiveSplitState CurrentState { get; set; }
    protected LiveSplitState OldState { get; set; }
    protected LayoutMode OldLayoutMode { get; set; }
    protected Color OldShadowsColor { get; set; }

    protected IEnumerable<ColumnData> ColumnsList => Settings.ColumnsList.Select(x => x.Data);
    protected List<(int exLength, float exWidth, float width)> ColumnWidths { get; set; }

    public string ComponentName => "Splits";

    public float VerticalHeight => InternalComponent.VerticalHeight;

    public float MinimumWidth => InternalComponent.MinimumWidth;

    public float HorizontalWidth => InternalComponent.HorizontalWidth;

    public float MinimumHeight => InternalComponent.MinimumHeight;

    public IDictionary<string, Action> ContextMenuControls => null;

    public SplitsComponent(LiveSplitState state)
    {
        CurrentState = state;
        Settings = new SplitsSettings(state);
        InternalComponent = new ComponentRendererComponent();

        MeasureTimeLabel = new SimpleLabel();
        MeasureDeltaLabel = new SimpleLabel();
        MeasureCharLabel = new SimpleLabel();
        CurrentAccuracy = Settings.SplitTimesAccuracy;
        CurrentDeltaAccuracy = Settings.DeltasAccuracy;
        CurrentDropDecimals = Settings.DropDecimals;
        TimeFormatter = new SplitTimeFormatter(CurrentAccuracy);
        DeltaTimeFormatter = new DeltaSplitTimeFormatter(CurrentDeltaAccuracy, CurrentDropDecimals);

        ShadowImages = [];
        visualSplitCount = Settings.VisualSplitCount;
        settingsSplitCount = Settings.VisualSplitCount;
        Settings.SplitLayoutChanged += Settings_SplitLayoutChanged;
        ColumnWidths = Settings.ColumnsList.Select(_ => (0, 0f, 0f)).ToList();
        ScrollOffset = 0;
        RebuildVisualSplits();
        state.ComparisonRenamed += state_ComparisonRenamed;
    }

    private void state_ComparisonRenamed(object sender, EventArgs e)
    {
        var args = (RenameEventArgs)e;
        foreach (ColumnData column in ColumnsList)
        {
            if (column.Comparison == args.OldName)
            {
                column.Comparison = args.NewName;
                ((LiveSplitState)sender).Layout.HasChanged = true;
            }
        }
    }

    private void Settings_SplitLayoutChanged(object sender, EventArgs e)
    {
        RebuildVisualSplits();
    }

    private void RebuildVisualSplits()
    {
        Components = [];
        SplitComponents = [];
        InternalComponent.VisibleComponents = Components;

        int totalSplits = Settings.ShowBlankSplits ? Math.Max(Settings.VisualSplitCount, visualSplitCount) : visualSplitCount;

        if (Settings.ShowColumnLabels && CurrentState.Layout?.Mode == LayoutMode.Vertical)
        {
            Components.Add(new LabelsComponent(Settings, ColumnsList, ColumnWidths));
            Components.Add(new SeparatorComponent());
        }

        for (int i = 0; i < totalSplits; ++i)
        {
            if (i == totalSplits - 1 && i > 0)
            {
                LastSplitSeparatorIndex = Components.Count;
                if (Settings.AlwaysShowLastSplit && Settings.SeparatorLastSplit)
                {
                    Components.Add(new SeparatorComponent());
                }
                else if (Settings.ShowThinSeparators)
                {
                    Components.Add(new ThinSeparatorComponent());
                }
            }

            var splitComponent = new SplitComponent(Settings, ColumnsList, ColumnWidths);
            Components.Add(splitComponent);
            if (i < visualSplitCount - 1 || i == (Settings.LockLastSplit ? totalSplits - 1 : visualSplitCount - 1))
            {
                SplitComponents.Add(splitComponent);
            }

            if (Settings.ShowThinSeparators && i < totalSplits - 2)
            {
                Components.Add(new ThinSeparatorComponent());
            }
        }
    }

    private void Prepare(LiveSplitState state)
    {
        if (state != OldState)
        {
            state.OnScrollDown += state_OnScrollDown;
            state.OnScrollUp += state_OnScrollUp;
            state.OnStart += state_OnStart;
            state.OnReset += state_OnReset;
            state.OnSplit += state_OnSplit;
            state.OnSkipSplit += state_OnSkipSplit;
            state.OnUndoSplit += state_OnUndoSplit;
            OldState = state;
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

        int previousSplitCount = visualSplitCount;
        visualSplitCount = Math.Min(state.Run.Count, Settings.VisualSplitCount);
        if (previousSplitCount != visualSplitCount
            || (Settings.ShowBlankSplits && settingsSplitCount != Settings.VisualSplitCount)
            || Settings.ShowColumnLabels != PreviousShowLabels
            || (Settings.ShowColumnLabels && state.Layout.Mode != OldLayoutMode))
        {
            PreviousShowLabels = Settings.ShowColumnLabels;
            OldLayoutMode = state.Layout.Mode;
            RebuildVisualSplits();
        }

        settingsSplitCount = Settings.VisualSplitCount;

        int skipCount = Math.Min(
            Math.Max(
                0,
                state.CurrentSplitIndex - (visualSplitCount - 2 - Settings.SplitPreviewCount + (Settings.AlwaysShowLastSplit ? 0 : 1))),
            state.Run.Count - visualSplitCount);
        ScrollOffset = Math.Min(Math.Max(ScrollOffset, -skipCount), state.Run.Count - skipCount - visualSplitCount);
        skipCount += ScrollOffset;

        if (OldShadowsColor != state.LayoutSettings.ShadowsColor)
        {
            ShadowImages.Clear();
        }

        foreach (ISegment split in state.Run)
        {
            if (split.Icon != null && (!ShadowImages.ContainsKey(split.Icon) || OldShadowsColor != state.LayoutSettings.ShadowsColor))
            {
                ShadowImages.Add(split.Icon, IconShadow.Generate(split.Icon, state.LayoutSettings.ShadowsColor));
            }
        }

        bool iconsNotBlank = state.Run.Count(x => x.Icon != null) > 0;
        foreach (SplitComponent split in SplitComponents)
        {
            split.DisplayIcon = iconsNotBlank && Settings.DisplayIcons;

            if (split.Split != null && split.Split.Icon != null)
            {
                split.ShadowImage = ShadowImages[split.Split.Icon];
            }
            else
            {
                split.ShadowImage = null;
            }
        }

        OldShadowsColor = state.LayoutSettings.ShadowsColor;

        foreach (IComponent component in Components)
        {
            if (component is SeparatorComponent separator)
            {
                int index = Components.IndexOf(separator);
                if (state.CurrentPhase is TimerPhase.Running or TimerPhase.Paused)
                {
                    if (((SplitComponent)Components[index + 1]).Split == state.CurrentSplit)
                    {
                        separator.LockToBottom = true;
                    }
                    else if (Components[index - 1] is SplitComponent splits && splits.Split == state.CurrentSplit)
                    {
                        separator.LockToBottom = false;
                    }
                }

                if (Settings.AlwaysShowLastSplit && Settings.SeparatorLastSplit && index == LastSplitSeparatorIndex)
                {
                    if (skipCount >= state.Run.Count - visualSplitCount)
                    {
                        if (Settings.ShowThinSeparators)
                        {
                            separator.DisplayedSize = 1f;
                        }
                        else
                        {
                            separator.DisplayedSize = 0f;
                        }

                        separator.UseSeparatorColor = false;
                    }
                    else
                    {
                        separator.DisplayedSize = 2f;
                        separator.UseSeparatorColor = true;
                    }
                }
            }
            else if (component is ThinSeparatorComponent thinSeparator)
            {
                int index = Components.IndexOf(thinSeparator);
                if (state.CurrentPhase is TimerPhase.Running or TimerPhase.Paused)
                {
                    if (((SplitComponent)Components[index + 1]).Split == state.CurrentSplit)
                    {
                        thinSeparator.LockToBottom = true;
                    }
                    else if (((SplitComponent)Components[index - 1]).Split == state.CurrentSplit)
                    {
                        thinSeparator.LockToBottom = false;
                    }
                }
            }
        }
    }

    private void state_OnUndoSplit(object sender, EventArgs e)
    {
        ScrollOffset = 0;
    }

    private void state_OnSkipSplit(object sender, EventArgs e)
    {
        ScrollOffset = 0;
    }

    private void state_OnSplit(object sender, EventArgs e)
    {
        ScrollOffset = 0;
    }

    private void state_OnReset(object sender, TimerPhase e)
    {
        ScrollOffset = 0;
    }

    private void state_OnStart(object sender, EventArgs e)
    {
        ScrollOffset = 0;
    }

    private void state_OnScrollUp(object sender, EventArgs e)
    {
        ScrollOffset--;
    }

    private void state_OnScrollDown(object sender, EventArgs e)
    {
        ScrollOffset++;
    }

    private void DrawBackground(Graphics g, float width, float height)
    {
        if (Settings.BackgroundGradient != ExtendedGradientType.Alternating
            && (Settings.BackgroundColor.A > 0
            || (Settings.BackgroundGradient != ExtendedGradientType.Plain
            && Settings.BackgroundColor2.A > 0)))
        {
            var gradientBrush = new LinearGradientBrush(
                        new PointF(0, 0),
                        Settings.BackgroundGradient == ExtendedGradientType.Horizontal
                        ? new PointF(width, 0)
                        : new PointF(0, height),
                        Settings.BackgroundColor,
                        Settings.BackgroundGradient == ExtendedGradientType.Plain
                        ? Settings.BackgroundColor
                        : Settings.BackgroundColor2);
            g.FillRectangle(gradientBrush, 0, 0, width, height);
        }
    }

    private void SetMeasureLabels(Graphics g, LiveSplitState state)
    {
        MeasureTimeLabel.Text = TimeFormatter.Format(new TimeSpan(24, 0, 0));
        MeasureDeltaLabel.Text = DeltaTimeFormatter.Format(new TimeSpan(0, 9, 0, 0));
        MeasureCharLabel.Text = "W";

        MeasureTimeLabel.Font = state.LayoutSettings.TimesFont;
        MeasureTimeLabel.IsMonospaced = true;
        MeasureDeltaLabel.Font = state.LayoutSettings.TimesFont;
        MeasureDeltaLabel.IsMonospaced = true;
        MeasureCharLabel.Font = state.LayoutSettings.TimesFont;
        MeasureCharLabel.IsMonospaced = true;

        MeasureTimeLabel.SetActualWidth(g);
        MeasureDeltaLabel.SetActualWidth(g);
        MeasureCharLabel.SetActualWidth(g);
    }

    public void DrawVertical(Graphics g, LiveSplitState state, float width, Region clipRegion)
    {
        Prepare(state);
        DrawBackground(g, width, VerticalHeight);
        SetMeasureLabels(g, state);
        InternalComponent.DrawVertical(g, state, width, clipRegion);
    }

    public void DrawHorizontal(Graphics g, LiveSplitState state, float height, Region clipRegion)
    {
        Prepare(state);
        DrawBackground(g, HorizontalWidth, height);
        SetMeasureLabels(g, state);
        InternalComponent.DrawHorizontal(g, state, height, clipRegion);
    }

    public Control GetSettingsControl(LayoutMode mode)
    {
        Settings.Mode = mode;
        return Settings;
    }

    public void SetSettings(System.Xml.XmlNode settings)
    {
        Settings.SetSettings(settings);
        RebuildVisualSplits();
    }

    public System.Xml.XmlNode GetSettings(System.Xml.XmlDocument document)
    {
        return Settings.GetSettings(document);
    }

    public void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode)
    {
        int skipCount = Math.Min(
            Math.Max(
                0,
                state.CurrentSplitIndex - (visualSplitCount - 2 - Settings.SplitPreviewCount + (Settings.AlwaysShowLastSplit ? 0 : 1))),
            state.Run.Count - visualSplitCount);
        ScrollOffset = Math.Min(Math.Max(ScrollOffset, -skipCount), state.Run.Count - skipCount - visualSplitCount);
        skipCount += ScrollOffset;

        int i = 0;
        if (SplitComponents.Count >= visualSplitCount)
        {
            foreach (ISegment split in state.Run.Skip(skipCount).Take(visualSplitCount - 1 + (Settings.AlwaysShowLastSplit ? 0 : 1)))
            {
                SplitComponents[i].Split = split;
                i++;
            }

            if (Settings.AlwaysShowLastSplit)
            {
                SplitComponents[i].Split = state.Run.Last();
            }
        }

        CalculateColumnWidths(state.Run);

        if (invalidator != null)
        {
            InternalComponent.Update(invalidator, state, width, height, mode);
        }
    }

    private void CalculateColumnWidths(IRun run)
    {
        if (ColumnsList != null)
        {
            while (ColumnWidths.Count < ColumnsList.Count())
            {
                ColumnWidths.Add((0, 0f, 0f));
            }

            TimeSpan longestTime = new TimeSpan(9, 0, 0);
            TimeSpan longestDelta = new TimeSpan(0, 0, 59, 0);
            foreach (ISegment split in run.Reverse())
            {
                if (split.SplitTime.RealTime is TimeSpan splitRealTime && longestTime < splitRealTime)
                {
                    longestTime = splitRealTime;
                }

                foreach (KeyValuePair<string, Time> kv in split.Comparisons)
                {
                    if (kv.Value.RealTime is TimeSpan cmpRealTime && longestTime < cmpRealTime)
                    {
                        longestTime = cmpRealTime;
                    }

                    if (split.SplitTime.RealTime - kv.Value.RealTime is TimeSpan deltaRealTime)
                    {
                        if (longestDelta < deltaRealTime)
                        {
                            longestDelta = deltaRealTime;
                        }
                        else if (longestDelta < (- deltaRealTime))
                        {
                            longestDelta = - deltaRealTime;
                        }
                    }
                }
            }

            int timeLength = TimeFormatter.Format(longestTime).Length;
            int deltaLength = DeltaTimeFormatter.Format(longestDelta).Length;
            float timeCharWidth = MeasureTimeLabel.Text.Length > 0 ? MeasureTimeLabel.ActualWidth / MeasureTimeLabel.Text.Length : MeasureCharLabel.ActualWidth;
            float timeWidth = Math.Max(MeasureTimeLabel.ActualWidth, timeCharWidth * (timeLength + 1));
            float deltaWidth = Math.Max(MeasureDeltaLabel.ActualWidth, timeCharWidth * (deltaLength + 1));

            for (int i = 0; i < ColumnsList.Count(); i++)
            {
                ColumnData column = ColumnsList.ElementAt(i);

                float labelWidth = 0f;
                if (column.Type is ColumnType.DeltaorSplitTime or ColumnType.SegmentDeltaorSegmentTime)
                {
                    labelWidth = Math.Max(deltaWidth, timeWidth);
                }
                else if (column.Type is ColumnType.Delta or ColumnType.SegmentDelta)
                {
                    labelWidth = deltaWidth;
                }
                else if (column.Type is ColumnType.SplitTime or ColumnType.SegmentTime)
                {
                    labelWidth = timeWidth;
                }
                else if (column.Type is ColumnType.CustomVariable)
                {
                    int longestLength = run.Metadata.CustomVariableValue(column.Name)?.Length ?? 0;
                    foreach (ISegment split in run)
                    {
                        if (split.CustomVariableValues.TryGetValue(column.Name, out string value) && !string.IsNullOrEmpty(value))
                        {
                            longestLength = Math.Max(longestLength, value.Length);
                        }
                    }

                    float exCharWidth = ColumnWidths[i].exLength > 0 ? ColumnWidths[i].exWidth / ColumnWidths[i].exLength : MeasureCharLabel.ActualWidth;
                    labelWidth = exCharWidth * (longestLength + 1);
                }

                ColumnWidths[i] = (ColumnWidths[i].exLength, ColumnWidths[i].exWidth, labelWidth);
            }
        }
    }

    public void Dispose()
    {
    }

    public int GetSettingsHashCode()
    {
        return Settings.GetSettingsHashCode();
    }
}
