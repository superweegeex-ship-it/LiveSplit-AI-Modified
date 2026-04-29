using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

using LiveSplit.Model;
using LiveSplit.UI;

namespace LiveSplit.UI.Components;

public class LineComponent : IComponent
{
    public float PaddingTop => 0f;
    public float PaddingLeft => 0f;
    public float PaddingBottom => 0f;
    public float PaddingRight => 0f;

    public float VerticalHeight { get; set; }
    public float HorizontalWidth { get; set; }
    public Color LineColor { get; set; }

    public LineComponent(int size, Color lineColor)
    {
        VerticalHeight = size;
        HorizontalWidth = size;
        LineColor = lineColor;
    }

    public void DrawVertical(Graphics g, LiveSplitState state, float width, Region clipRegion)
    {
        float h = Math.Max(VerticalHeight, 0.0001f);
        using var brush = new SolidBrush(LineColor);
        g.FillRectangle(brush, 0f, 0f, width, h);
    }

    public string ComponentName => throw new NotSupportedException();

    public float MinimumWidth => 0f;

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

    public float MinimumHeight => throw new NotImplementedException();

    public void DrawHorizontal(Graphics g, LiveSplitState state, float height, Region clipRegion)
    {
        float w = Math.Max(HorizontalWidth, 0.0001f);
        using var brush = new SolidBrush(LineColor);
        g.FillRectangle(brush, 0f, 0f, w, height);
    }

    public void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode)
    {
        invalidator.Invalidate(0, 0, width, height);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
