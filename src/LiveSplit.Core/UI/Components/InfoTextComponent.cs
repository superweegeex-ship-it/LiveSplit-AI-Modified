using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

using LiveSplit.Model;

namespace LiveSplit.UI.Components;

public class InfoTextComponent : IComponent
{
    public string InformationName { get => NameLabel.Text; set => NameLabel.Text = value; }
    public string InformationValue { get => ValueLabel.Text; set => ValueLabel.Text = value; }

    public GraphicsCache Cache { get; set; }

    public ICollection<string> AlternateNameText { get => NameLabel.AlternateText; set => NameLabel.AlternateText = value; }

    public SimpleLabel NameLabel { get; protected set; }
    public SimpleLabel ValueLabel { get; protected set; }

    public string LongestString { get; set; }
    protected SimpleLabel NameMeasureLabel { get; set; }

    public float PaddingTop { get; set; }
    public float PaddingLeft => 7f;
    public float PaddingBottom { get; set; }
    public float PaddingRight => 7f;

    public bool DisplayTwoRows { get; set; }

    /// <summary>Extra space reserved on the left for overlays such as a game icon (pixels).</summary>
    public float ContentInsetLeft { get; set; }

    /// <summary>Extra space reserved on the right for overlays such as a game icon (pixels).</summary>
    public float ContentInsetRight { get; set; }

    public float VerticalHeight { get; set; }

    public float MinimumWidth => 20;

    public float HorizontalWidth
        => Math.Max(NameMeasureLabel.ActualWidth, ValueLabel.ActualWidth) + 10 + ContentInsetLeft + ContentInsetRight;

    public float MinimumHeight { get; set; }

    public InfoTextComponent(string informationName, string informationValue)
    {
        Cache = new GraphicsCache();
        NameLabel = new SimpleLabel()
        {
            HorizontalAlignment = StringAlignment.Near,
            Text = informationName
        };
        ValueLabel = new SimpleLabel()
        {
            HorizontalAlignment = StringAlignment.Far,
            Text = informationValue
        };
        NameMeasureLabel = new SimpleLabel();
        MinimumHeight = 25;
        VerticalHeight = 31;
        LongestString = "";
    }

    public virtual void PrepareDraw(LiveSplitState state, LayoutMode mode)
    {
        NameMeasureLabel.Font = state.LayoutSettings.TextFont;
        ValueLabel.Font = state.LayoutSettings.TextFont;
        NameLabel.Font = state.LayoutSettings.TextFont;
        if (mode == LayoutMode.Vertical)
        {
            NameLabel.VerticalAlignment = StringAlignment.Center;
            ValueLabel.VerticalAlignment = StringAlignment.Center;
        }
        else
        {
            NameLabel.VerticalAlignment = StringAlignment.Near;
            ValueLabel.VerticalAlignment = StringAlignment.Far;
        }

        state.LayoutSettings.ApplyTextShadowTo(NameLabel);
        state.LayoutSettings.ApplyTextShadowTo(ValueLabel);
    }

    /// <summary>
    /// Positions labels for vertical layout. Call <see cref="DrawVerticalLabels"/> afterward to paint.
    /// </summary>
    public void ComputeVerticalLayout(Graphics g, LiveSplitState state, float width)
    {
        if (DisplayTwoRows)
        {
            VerticalHeight = 0.9f * (g.MeasureString("A", ValueLabel.Font).Height + g.MeasureString("A", NameLabel.Font).Height);
            PaddingTop = PaddingBottom = 0;
            LayoutTwoRows(g, state, width, VerticalHeight);
            PrepareDraw(state, LayoutMode.Horizontal);
        }
        else
        {
            VerticalHeight = 31;
            NameLabel.OutlineColor = state.LayoutSettings.TextOutlineColor;
            ValueLabel.OutlineColor = state.LayoutSettings.TextOutlineColor;

            float textHeight = 0.75f * Math.Max(g.MeasureString("A", ValueLabel.Font).Height, g.MeasureString("A", NameLabel.Font).Height);
            PaddingTop = Math.Max(0, (VerticalHeight - textHeight) / 2f);
            PaddingBottom = PaddingTop;

            NameMeasureLabel.Text = LongestString;
            NameMeasureLabel.SetActualWidth(g);
            ValueLabel.SetActualWidth(g);

            float leftPad = 5f + ContentInsetLeft;
            float rightPad = 5f + ContentInsetRight;
            NameLabel.Width = width - ValueLabel.ActualWidth - leftPad - rightPad;
            NameLabel.Height = VerticalHeight;
            NameLabel.X = leftPad;
            NameLabel.Y = 0;

            ValueLabel.Width = ValueLabel.IsMonospaced ? width - leftPad - rightPad - 2 : width - leftPad - rightPad;
            ValueLabel.Height = VerticalHeight;
            ValueLabel.Y = 0;
            ValueLabel.X = leftPad;

            PrepareDraw(state, LayoutMode.Vertical);
        }
    }

    public void DrawVerticalLabels(Graphics g)
    {
        NameLabel.Draw(g);
        ValueLabel.Draw(g);
    }

    public void DrawVertical(Graphics g, LiveSplitState state, float width, Region clipRegion)
    {
        ComputeVerticalLayout(g, state, width);
        DrawVerticalLabels(g);
    }

    public void ComputeHorizontalLayout(Graphics g, LiveSplitState state, float height)
    {
        LayoutTwoRows(g, state, HorizontalWidth, height);
        PrepareDraw(state, LayoutMode.Horizontal);
    }

    public void DrawHorizontalLabels(Graphics g)
    {
        NameLabel.Draw(g);
        ValueLabel.Draw(g);
    }

    public void DrawHorizontal(Graphics g, LiveSplitState state, float height, Region clipRegion)
    {
        ComputeHorizontalLayout(g, state, height);
        DrawHorizontalLabels(g);
    }

    protected void LayoutTwoRows(Graphics g, LiveSplitState state, float width, float height)
    {
        NameLabel.OutlineColor = state.LayoutSettings.TextOutlineColor;
        ValueLabel.OutlineColor = state.LayoutSettings.TextOutlineColor;

        if (InformationName != null && LongestString != null && InformationName.Length > LongestString.Length)
        {
            LongestString = InformationName;
            NameMeasureLabel.Text = LongestString;
        }

        NameMeasureLabel.Text = LongestString;
        NameMeasureLabel.Font = state.LayoutSettings.TextFont;
        NameMeasureLabel.SetActualWidth(g);

        MinimumHeight = 0.85f * (g.MeasureString("A", ValueLabel.Font).Height + g.MeasureString("A", NameLabel.Font).Height);
        float leftPad = 5f + ContentInsetLeft;
        float rightPad = 5f + ContentInsetRight;
        NameLabel.Width = width - leftPad - rightPad;
        NameLabel.Height = height;
        NameLabel.X = leftPad;
        NameLabel.Y = 0;

        ValueLabel.Width = ValueLabel.IsMonospaced ? width - leftPad - rightPad - 2 : width - leftPad - rightPad;
        ValueLabel.Height = height;
        ValueLabel.Y = 0;
        ValueLabel.X = leftPad;
    }

    protected void DrawTwoRows(Graphics g, LiveSplitState state, float width, float height)
    {
        LayoutTwoRows(g, state, width, height);
        PrepareDraw(state, LayoutMode.Horizontal);
        DrawHorizontalLabels(g);
    }

    public string ComponentName => throw new NotSupportedException();

    public Control GetSettingsControl(LayoutMode mode)
    {
        throw new NotImplementedException();
    }

    public void SetSettings(System.Xml.XmlNode settings)
    {
        throw new NotImplementedException();
    }

    public System.Xml.XmlNode GetSettings(System.Xml.XmlDocument document)
    {
        throw new NotImplementedException();
    }

    public string UpdateName => throw new NotSupportedException();

    public string XMLURL => throw new NotSupportedException();

    public string UpdateURL => throw new NotSupportedException();

    public Version Version => throw new NotSupportedException();

    public IDictionary<string, Action> ContextMenuControls => null;

    public void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode)
    {
        Cache.Restart();
        Cache["NameText"] = InformationName;
        Cache["ValueText"] = InformationValue;
        Cache["NameColor"] = NameLabel.ForeColor.ToArgb();
        Cache["ValueColor"] = ValueLabel.ForeColor.ToArgb();
        Cache["DisplayTwoRows"] = DisplayTwoRows;
        Cache["ContentInsetLeft"] = ContentInsetLeft;
        Cache["ContentInsetRight"] = ContentInsetRight;

        if (invalidator != null && Cache.HasChanged)
        {
            invalidator.Invalidate(0, 0, width, height);
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
