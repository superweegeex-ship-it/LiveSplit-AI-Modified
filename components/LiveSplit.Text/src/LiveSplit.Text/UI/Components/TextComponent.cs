using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

using LiveSplit.Model;
using LiveSplit.TimeFormatters;

namespace LiveSplit.UI.Components;

[GlobalFontConsumer(GlobalFont.TextFont)]
public class TextComponent : IComponent
{
    protected TextTextComponent InternalComponent { get; set; }
    public TextComponentSettings Settings { get; set; }
    private readonly GraphicsCache iconCache = new();
    private Image shadow;
    private Image shadowSource;
    private int shadowKey;
    private Image animatedIcon;
    private static readonly EventHandler AnimationFrame = (_, _) => { };

    private RectangleF IconBounds(LiveSplitState state, float width, float height)
    {
        Image icon = Settings.ResolveIcon(state.Run.GameIcon);
        if (!Settings.DisplayGameIcon || icon == null || width <= 4) return RectangleF.Empty;
        float scale = Math.Max(1f, height - 4) * Settings.IconSizePercent / 100f / Math.Max(icon.Width, icon.Height);
        float w = icon.Width * scale, h = icon.Height * scale;
        if (w > width - 4) { h *= (width - 4) / w; w = width - 4; }
        float x = Settings.IconOnRight ? width - 7 - w : 7;
        x = Math.Max(2, Math.Min(width - 2 - w, x + Settings.IconHorizontalOffset));
        return new RectangleF(x, (height - h) / 2, w, h);
    }

    private void DrawContent(Graphics g, LiveSplitState state, float width, float height, LayoutMode mode)
    {
        PrepareDraw(state, mode);
        InternalComponent.ContentInsetLeft = InternalComponent.ContentInsetRight = 0;
        if (mode == LayoutMode.Vertical) InternalComponent.ComputeVerticalLayout(g, state, width);
        else InternalComponent.ComputeHorizontalLayout(g, state, height);
        if (mode == LayoutMode.Vertical) height = VerticalHeight;
        else width = HorizontalWidth;
        RectangleF bounds = IconBounds(state, width, height);
        if (!bounds.IsEmpty)
        {
            if (Settings.IconOnRight) InternalComponent.ContentInsetRight = Math.Max(0, width - bounds.Left - 2);
            else InternalComponent.ContentInsetLeft = Math.Max(0, bounds.Right - 2);
            if (mode == LayoutMode.Vertical) InternalComponent.ComputeVerticalLayout(g, state, width);
            else InternalComponent.ComputeHorizontalLayout(g, state, height);
            if (mode == LayoutMode.Horizontal)
            {
                width = HorizontalWidth;
                bounds = IconBounds(state, width, height);
            }
        }
        float left = bounds.IsEmpty || Settings.IconOnRight ? 5 : bounds.Right + 3;
        float right = bounds.IsEmpty || !Settings.IconOnRight ? width - 5 : bounds.Left - 3;
        foreach (SimpleLabel label in new[] { InternalComponent.NameLabel, InternalComponent.ValueLabel })
        {
            label.X += Settings.TextHorizontalOffset;
            float end = Math.Min(right, label.X + label.Width);
            label.X = Math.Max(left, label.X);
            label.Width = Math.Max(0, end - label.X);
        }
        InternalComponent.DrawVerticalLabels(g);
        if (!bounds.IsEmpty) DrawIcon(g, state, bounds);
    }

    private void DrawIcon(Graphics g, LiveSplitState state, RectangleF bounds)
    {
        Image icon = Settings.ResolveIcon(state.Run.GameIcon);
        if (animatedIcon != icon)
        {
            if (animatedIcon != null) ImageAnimator.StopAnimate(animatedIcon, AnimationFrame);
            animatedIcon = icon;
            ImageAnimator.Animate(icon, AnimationFrame);
        }
        var layout = state.LayoutSettings;
        if (layout.DropShadows)
        {
            int key = layout.ShadowsColor.GetHashCode() ^ layout.IconShadowOffset.GetHashCode()
                ^ layout.IconShadowTransparency.GetHashCode() ^ layout.IconShadowBlur.GetHashCode();
            if (shadowSource != icon || shadowKey != key || shadow == null)
            {
                shadow?.Dispose();
                shadow = IconShadow.Generate(icon, layout.ShadowsColor, layout.IconShadowOffset, layout.IconShadowTransparency, layout.IconShadowBlur);
                shadowSource = icon; shadowKey = key;
            }
            if (shadow != null)
            {
                ImageAnimator.UpdateFrames(shadow);
                g.DrawImage(shadow, bounds.X - bounds.Width / 8 - .7f, bounds.Y - bounds.Height / 8 - .7f, bounds.Width * 1.25f, bounds.Height * 1.25f);
            }
        }
        ImageAnimator.UpdateFrames(icon);
        g.DrawImage(icon, bounds);
    }

    public float PaddingTop => InternalComponent.PaddingTop;
    public float PaddingLeft => InternalComponent.PaddingLeft;
    public float PaddingBottom => InternalComponent.PaddingBottom;
    public float PaddingRight => InternalComponent.PaddingRight;

    public IDictionary<string, Action> ContextMenuControls => null;

    public TextComponent(LiveSplitState state)
    {
        Settings = new TextComponentSettings()
        {
            CurrentState = state
        };
        InternalComponent = new TextTextComponent(Settings);
    }

    private void PrepareDraw(LiveSplitState state, LayoutMode mode)
    {
        InternalComponent.DisplayTwoRows = Settings.Display2Rows;

        InternalComponent.NameLabel.HasShadow
            = InternalComponent.ValueLabel.HasShadow
            = state.LayoutSettings.DropShadows;

        if (string.IsNullOrEmpty(Settings.Text1) || string.IsNullOrEmpty(Settings.Text2))
        {
            InternalComponent.NameLabel.HorizontalAlignment = StringAlignment.Center;
            InternalComponent.ValueLabel.HorizontalAlignment = StringAlignment.Center;
            InternalComponent.NameLabel.VerticalAlignment = StringAlignment.Center;
            InternalComponent.ValueLabel.VerticalAlignment = StringAlignment.Center;
        }
        else
        {
            InternalComponent.NameLabel.HorizontalAlignment = StringAlignment.Near;
            InternalComponent.ValueLabel.HorizontalAlignment = StringAlignment.Far;
            InternalComponent.NameLabel.VerticalAlignment =
                mode == LayoutMode.Horizontal || Settings.Display2Rows ? StringAlignment.Near : StringAlignment.Center;
            InternalComponent.ValueLabel.VerticalAlignment =
                mode == LayoutMode.Horizontal || Settings.Display2Rows ? StringAlignment.Far : StringAlignment.Center;
        }

        InternalComponent.NameLabel.ForeColor = Settings.OverrideTextColor ? Settings.TextColor : state.LayoutSettings.TextColor;
        InternalComponent.ValueLabel.ForeColor = Settings.OverrideTimeColor ? Settings.TimeColor : state.LayoutSettings.TextColor;
    }

    private void DrawBackground(Graphics g, LiveSplitState state, float width, float height)
    {
        if (Settings.BackgroundColor.A > 0
            || (Settings.BackgroundGradient != GradientType.Plain
            && Settings.BackgroundColor2.A > 0))
        {
            var gradientBrush = new LinearGradientBrush(
                        new PointF(0, 0),
                        Settings.BackgroundGradient == GradientType.Horizontal
                        ? new PointF(width, 0)
                        : new PointF(0, height),
                        Settings.BackgroundColor,
                        Settings.BackgroundGradient == GradientType.Plain
                        ? Settings.BackgroundColor
                        : Settings.BackgroundColor2);
            g.FillRectangle(gradientBrush, 0, 0, width, height);
        }
    }

    public void DrawVertical(Graphics g, LiveSplitState state, float width, Region clipRegion)
    {
        DrawBackground(g, state, width, VerticalHeight);
        DrawContent(g, state, width, VerticalHeight, LayoutMode.Vertical);
    }

    public void DrawHorizontal(Graphics g, LiveSplitState state, float height, Region clipRegion)
    {
        DrawBackground(g, state, HorizontalWidth, height);
        DrawContent(g, state, HorizontalWidth, height, LayoutMode.Horizontal);
    }

    public float VerticalHeight => InternalComponent.VerticalHeight;

    public float MinimumWidth => InternalComponent.MinimumWidth;

    public float HorizontalWidth => InternalComponent.HorizontalWidth;

    public float MinimumHeight => InternalComponent.MinimumHeight;

    public string ComponentName => string.Join(" ", Settings.Text1, Settings.Text2);

    public Control GetSettingsControl(LayoutMode mode)
    {
        Settings.Mode = mode;
        return Settings;
    }

    public void SetSettings(System.Xml.XmlNode settings)
    {
        Settings.SetSettings(settings);
    }

    public void MigrateFontOverrides(Options.FontOverrides overrides)
    {
        if (Settings.OverrideFont1 && Settings.Font1 != null)
        {
            overrides.OverrideTextFont = true;
            overrides.TextFont = (Font)Settings.Font1.Clone();
            Settings.OverrideFont1 = false;
        }
    }

    public System.Xml.XmlNode GetSettings(System.Xml.XmlDocument document)
    {
        return Settings.GetSettings(document);
    }

    public void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode)
    {
        string text2Value = Settings.CustomVariable
            ? (state.Run.Metadata.CustomVariableValue(Settings.Text2) ?? TimeFormatConstants.DASH)
            : Settings.Text2;
        InternalComponent.InformationName = Settings.Text1;
        InternalComponent.InformationValue = text2Value;
        InternalComponent.LongestString = Settings.Text1.Length > text2Value.Length
            ? Settings.Text1
            : text2Value;

        InternalComponent.Update(invalidator, state, width, height, mode);
        iconCache.Restart();
        iconCache["Settings"] = Settings.GetSettingsHashCode();
        iconCache["Icon"] = Settings.ResolveIcon(state.Run.GameIcon);
        if (iconCache.HasChanged || (Settings.DisplayGameIcon && ImageAnimator.CanAnimate(Settings.ResolveIcon(state.Run.GameIcon))))
            invalidator?.Invalidate(0, 0, width, height);
    }

    public void Dispose()
    {
        shadow?.Dispose();
        if (animatedIcon != null) ImageAnimator.StopAnimate(animatedIcon, AnimationFrame);
        Settings.Dispose();
    }

    public int GetSettingsHashCode()
    {
        return Settings.GetSettingsHashCode();
    }
}
