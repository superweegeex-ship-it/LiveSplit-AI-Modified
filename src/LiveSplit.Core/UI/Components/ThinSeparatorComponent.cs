using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

using LiveSplit.Model;
using LiveSplit.Options;

namespace LiveSplit.UI.Components;

public class ThinSeparatorComponent : IComponent
{
    private float separatorThickness = 1f;

    public float PaddingTop => 0f;
    public float PaddingLeft => 0f;
    public float PaddingBottom => 0f;
    public float PaddingRight => 0f;

    public bool LockToBottom { get; set; }

    public GraphicsCache Cache { get; set; }

    protected LineComponent Line { get; set; }

    public float VerticalHeight => separatorThickness;

    public float MinimumWidth => 0f;

    public float HorizontalWidth => separatorThickness;

    public float MinimumHeight => 0f;

    public ThinSeparatorComponent()
    {
        Line = new LineComponent(1, Color.White);
        Cache = new GraphicsCache();
    }

    public void DrawVertical(Graphics g, LiveSplitState state, float width, Region clipRegion)
    {
        Region oldClip = g.Clip;
        System.Drawing.Drawing2D.Matrix oldMatrix = g.Transform;
        System.Drawing.Drawing2D.SmoothingMode oldMode = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
        g.Clip = new Region();
        Color primary = state.LayoutSettings.ThinSeparatorsColor;
        float scale = g.Transform.Elements.First();
        float targetThickness = Math.Max(0.1f, state.LayoutSettings.ThinSeparatorThickness);
        float newHeight = Math.Max((int)((targetThickness * scale) + 0.5f), 1) / scale;
        Line.VerticalHeight = newHeight;
        if (LockToBottom)
        {
            g.TranslateTransform(0, targetThickness - newHeight);
        }

        SeparatorDrawing.FillSeparatorRect(g, state.LayoutSettings, 0f, 0f, width, newHeight, primary);
        SeparatorDrawing.DrawSeparatorOutline(g, state.LayoutSettings, 0f, 0f, width, newHeight);
        g.Clip = oldClip;
        g.Transform = oldMatrix;
        g.SmoothingMode = oldMode;
    }

    public void DrawHorizontal(Graphics g, LiveSplitState state, float height, Region clipRegion)
    {
        Region oldClip = g.Clip;
        System.Drawing.Drawing2D.Matrix oldMatrix = g.Transform;
        System.Drawing.Drawing2D.SmoothingMode oldMode = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
        g.Clip = new Region();
        Color primary = state.LayoutSettings.ThinSeparatorsColor;
        float scale = g.Transform.Elements.First();
        float targetThickness = Math.Max(0.1f, state.LayoutSettings.ThinSeparatorThickness);
        float newWidth = Math.Max((int)((targetThickness * scale) + 0.5f), 1) / scale;
        if (LockToBottom)
        {
            g.TranslateTransform(targetThickness - newWidth, 0);
        }

        Line.HorizontalWidth = newWidth;
        SeparatorDrawing.FillSeparatorRect(g, state.LayoutSettings, 0f, 0f, newWidth, height, primary);
        SeparatorDrawing.DrawSeparatorOutline(g, state.LayoutSettings, 0f, 0f, newWidth, height);
        g.Clip = oldClip;
        g.Transform = oldMatrix;
        g.SmoothingMode = oldMode;
    }

    public string ComponentName
        => "Thin Separator";

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

    public IDictionary<string, Action> ContextMenuControls
        => null;

    public void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode)
    {
        Cache.Restart();
        Cache["LockToBottom"] = LockToBottom;
        separatorThickness = Math.Max(0.1f, state.LayoutSettings.ThinSeparatorThickness);
        Cache["SeparatorThickness"] = separatorThickness;
        var ls = state.LayoutSettings;
        Cache["SeparatorFillMode"] = ls.SeparatorFillMode;
        Cache["SeparatorGradientEnd"] = ls.SeparatorGradientEndColor.ToArgb();
        Cache["SeparatorRgbWave"] = ls.SeparatorRgbWave;
        Cache["SeparatorWaveAxis"] = ls.SeparatorWaveAxis;
        Cache["SeparatorWaveSpeed"] = ls.SeparatorWaveSpeed;
        Cache["SeparatorOutlineEnabled"] = ls.SeparatorOutlineEnabled;
        Cache["SeparatorOutlineThickness"] = ls.SeparatorOutlineThickness;
        Cache["SeparatorOutlineColor"] = ls.SeparatorOutlineColor.ToArgb();
        Cache["SeparatorOutlineTransparency"] = ls.SeparatorOutlineTransparency;
        Cache["SeparatorOutlineFillMode"] = ls.SeparatorOutlineFillMode;
        Cache["SeparatorOutlineGradientEnd"] = ls.SeparatorOutlineGradientEndColor.ToArgb();
        Cache["SeparatorOutlineRgbWave"] = ls.SeparatorOutlineRgbWave;
        Cache["SeparatorOutlineWaveAxis"] = ls.SeparatorOutlineWaveAxis;
        Cache["SeparatorOutlineWaveSpeed"] = ls.SeparatorOutlineWaveSpeed;
        Cache["SeparatorOutlineInterpolation"] = ls.SeparatorOutlineInterpolation;

        bool separatorAnim = SeparatorDrawing.SeparatorAppearanceIsAnimated(ls)
            || SeparatorDrawing.SeparatorOutlineIsAnimated(ls);
        if (invalidator != null && (Cache.HasChanged || separatorAnim))
        {
            invalidator.Invalidate(0, 0, width, height);
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
