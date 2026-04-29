using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

using LiveSplit.Model;
using LiveSplit.Model.Comparisons;

namespace LiveSplit.UI.Components;

[GlobalFontConsumer(GlobalFont.TextFont)]
public class LabelsComponent : IComponent
{
    public SplitsSettings Settings { get; set; }

    protected int FrameCount { get; set; }

    public GraphicsCache Cache { get; set; }

    public IEnumerable<ColumnData> ColumnsList { get; set; }
    public IList<SimpleLabel> LabelsList { get; set; }
    protected List<(int exLength, float exWidth, float width)> ColumnWidths { get; }

    public float PaddingTop => 0f;
    public float PaddingLeft => 0f;
    public float PaddingBottom => 0f;
    public float PaddingRight => 0f;

    public float VerticalHeight => 25 + Settings.SplitHeight;

    public float MinimumWidth { get; set; }

    public float HorizontalWidth => 10f; /* not available in horizontal mode */

    public float MinimumHeight { get; set; }

    public IDictionary<string, Action> ContextMenuControls => null;
    public LabelsComponent(SplitsSettings settings, IEnumerable<ColumnData> columns, List<(int exLength, float exWidth, float width)> columnWidths)
    {
        Settings = settings;
        MinimumHeight = 31;

        Cache = new GraphicsCache();
        LabelsList = [];
        ColumnsList = columns;
        ColumnWidths = columnWidths;
    }

    private void DrawGeneral(Graphics g, LiveSplitState state, float width, float height, LayoutMode mode)
    {
        if (Settings.BackgroundGradient == ExtendedGradientType.Alternating)
        {
            g.FillRectangle(new SolidBrush(
                Settings.BackgroundColor
                ), 0, 0, width, height);
        }

        foreach (SimpleLabel label in LabelsList)
        {
            label.ShadowColor = state.LayoutSettings.ShadowsColor;
            label.OutlineColor = state.LayoutSettings.TextOutlineColor;
            label.Y = 0;
            label.Height = height;
        }

        MinimumWidth = 10f;

        if (ColumnsList.Count() == LabelsList.Count)
        {
            while (ColumnWidths.Count < LabelsList.Count)
            {
                ColumnWidths.Add((0, 0f, 0f));
            }

            float curX = width - 7;
            foreach (SimpleLabel label in LabelsList.Reverse())
            {
                float labelWidth = ColumnWidths[LabelsList.IndexOf(label)].width;

                curX -= labelWidth + 5;
                label.Width = labelWidth;
                label.X = curX + 5;

                label.Font = state.LayoutSettings.TextFont;
                label.HasShadow = state.LayoutSettings.DropShadows;
                label.Draw(g);
            }
        }
    }

    public void DrawVertical(Graphics g, LiveSplitState state, float width, Region clipRegion)
    {
        DrawGeneral(g, state, width, VerticalHeight, LayoutMode.Vertical);
    }

    public void DrawHorizontal(Graphics g, LiveSplitState state, float height, Region clipRegion)
    {
        DrawGeneral(g, state, HorizontalWidth, height, LayoutMode.Horizontal);
    }

    public string ComponentName => "Labels";

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
        RecreateLabels();

        foreach (SimpleLabel label in LabelsList)
        {
            ColumnData column = ColumnsList.ElementAt(LabelsList.IndexOf(label));
            if (string.IsNullOrEmpty(column.Name))
            {
                label.Text = CompositeComparisons.GetShortComparisonName(column.Comparison == "Current Comparison" ? state.CurrentComparison : column.Comparison);
            }
            else
            {
                label.Text = column.Name;
            }

            label.ForeColor = Settings.LabelsColor;
        }
    }

    protected void RecreateLabels()
    {
        if (ColumnsList != null && LabelsList.Count != ColumnsList.Count())
        {
            LabelsList.Clear();
            foreach (ColumnData column in ColumnsList)
            {
                LabelsList.Add(new SimpleLabel()
                {
                    HorizontalAlignment = StringAlignment.Far,
                    VerticalAlignment = StringAlignment.Center
                });
            }
        }
    }

    public void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode)
    {
        UpdateAll(state);

        Cache.Restart();
        Cache["ColumnsCount"] = ColumnsList.Count();
        for (int index = 0; index < LabelsList.Count; index++)
        {
            SimpleLabel label = LabelsList[index];
            Cache["Columns" + index + "Text"] = label.Text;
            if (index < ColumnWidths.Count)
            {
                Cache["Columns" + index + "Width"] = ColumnWidths[index].width;
            }
        }

        if (invalidator != null && (Cache.HasChanged || FrameCount > 1))
        {
            invalidator.Invalidate(0, 0, width, height);
        }
    }

    public void Dispose()
    {
    }
}
