using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

using LiveSplit.Options;
using LiveSplit.UI;
using LiveSplit.UI.Components;

namespace LiveSplit.UI.LayoutSavers;

public class XMLLayoutSaver : ILayoutSaver
{
    private static int ToElement(XmlDocument document, XmlElement element, LayoutSettings settings)
    {
        return SettingsHelper.CreateSetting(document, element, "TextColor", settings.TextColor) ^
        SettingsHelper.CreateSetting(document, element, "BackgroundColor", settings.BackgroundColor) ^
        SettingsHelper.CreateSetting(document, element, "BackgroundColor2", settings.BackgroundColor2) ^
        SettingsHelper.CreateSetting(document, element, "ThinSeparatorsColor", settings.ThinSeparatorsColor) ^
        SettingsHelper.CreateSetting(document, element, "SeparatorsColor", settings.SeparatorsColor) ^
        SettingsHelper.CreateSetting(document, element, "ThinSeparatorThickness", settings.ThinSeparatorThickness) ^
        SettingsHelper.CreateSetting(document, element, "SeparatorThickness", settings.SeparatorThickness) ^
        SettingsHelper.CreateSetting(document, element, "SeparatorFillMode", settings.SeparatorFillMode) ^
        SettingsHelper.CreateSetting(document, element, "SeparatorGradientEndColor", settings.SeparatorGradientEndColor) ^
        SettingsHelper.CreateSetting(document, element, "SeparatorRgbWave", settings.SeparatorRgbWave) ^
        SettingsHelper.CreateSetting(document, element, "SeparatorWaveAxis", settings.SeparatorWaveAxis) ^
        SettingsHelper.CreateSetting(document, element, "SeparatorWaveSpeed", settings.SeparatorWaveSpeed) ^
        SettingsHelper.CreateSetting(document, element, "SeparatorOutlineEnabled", settings.SeparatorOutlineEnabled) ^
        SettingsHelper.CreateSetting(document, element, "SeparatorOutlineThickness", settings.SeparatorOutlineThickness) ^
        SettingsHelper.CreateSetting(document, element, "SeparatorOutlineColor", settings.SeparatorOutlineColor) ^
        SettingsHelper.CreateSetting(document, element, "SeparatorOutlineTransparency", settings.SeparatorOutlineTransparency) ^
        SettingsHelper.CreateSetting(document, element, "SeparatorOutlineFillMode", settings.SeparatorOutlineFillMode) ^
        SettingsHelper.CreateSetting(document, element, "SeparatorOutlineGradientEndColor", settings.SeparatorOutlineGradientEndColor) ^
        SettingsHelper.CreateSetting(document, element, "SeparatorOutlineRgbWave", settings.SeparatorOutlineRgbWave) ^
        SettingsHelper.CreateSetting(document, element, "SeparatorOutlineWaveAxis", settings.SeparatorOutlineWaveAxis) ^
        SettingsHelper.CreateSetting(document, element, "SeparatorOutlineWaveSpeed", settings.SeparatorOutlineWaveSpeed) ^
        SettingsHelper.CreateSetting(document, element, "SeparatorOutlineInterpolation", settings.SeparatorOutlineInterpolation) ^
        SettingsHelper.CreateSetting(document, element, "PersonalBestColor", settings.PersonalBestColor) ^
        SettingsHelper.CreateSetting(document, element, "AheadGainingTimeColor", settings.AheadGainingTimeColor) ^
        SettingsHelper.CreateSetting(document, element, "AheadLosingTimeColor", settings.AheadLosingTimeColor) ^
        SettingsHelper.CreateSetting(document, element, "BehindGainingTimeColor", settings.BehindGainingTimeColor) ^
        SettingsHelper.CreateSetting(document, element, "BehindLosingTimeColor", settings.BehindLosingTimeColor) ^
        SettingsHelper.CreateSetting(document, element, "BestSegmentColor", settings.BestSegmentColor) ^
        SettingsHelper.CreateSetting(document, element, "UseRainbowColor", settings.UseRainbowColor) ^
        SettingsHelper.CreateSetting(document, element, "NotRunningColor", settings.NotRunningColor) ^
        SettingsHelper.CreateSetting(document, element, "PausedColor", settings.PausedColor) ^
        SettingsHelper.CreateSetting(document, element, "TextOutlineColor", settings.TextOutlineColor) ^
        SettingsHelper.CreateSetting(document, element, "ShadowsColor", settings.ShadowsColor) ^
        SettingsHelper.CreateSetting(document, element, "TimesFont", settings.TimesFont) ^
        SettingsHelper.CreateSetting(document, element, "TimerFont", settings.TimerFont) ^
        SettingsHelper.CreateSetting(document, element, "TextFont", settings.TextFont) ^
        SettingsHelper.CreateSetting(document, element, "AlwaysOnTop", settings.AlwaysOnTop) ^
        SettingsHelper.CreateSetting(document, element, "ShowBestSegments", settings.ShowBestSegments) ^
        SettingsHelper.CreateSetting(document, element, "AntiAliasing", settings.AntiAliasing) ^
        SettingsHelper.CreateSetting(document, element, "DropShadows", settings.DropShadows) ^
        SettingsHelper.CreateSetting(document, element, "BackgroundType", settings.BackgroundType) ^
        SettingsHelper.CreateSetting(document, element, "BackgroundImage", settings.BackgroundImage) ^
        SettingsHelper.CreateSetting(document, element, "BackgroundVideoPath", settings.BackgroundVideoPath) ^
        SettingsHelper.CreateSetting(document, element, "BackgroundVideoSource", settings.BackgroundVideoSource) ^
        SettingsHelper.CreateSetting(document, element, "BackgroundVideoInputType", settings.BackgroundVideoInputType) ^
        SettingsHelper.CreateSetting(document, element, "BackgroundVideoBackend", settings.BackgroundVideoBackend) ^
        SettingsHelper.CreateSetting(document, element, "UseHardwareVideoDecoding", settings.UseHardwareVideoDecoding) ^
        SettingsHelper.CreateSetting(document, element, "LoopVideo", settings.LoopVideo) ^
        SettingsHelper.CreateSetting(document, element, "PlayVideoAudio", settings.PlayVideoAudio) ^
        SettingsHelper.CreateSetting(document, element, "VideoAudioVolume", settings.VideoAudioVolume) ^
        SettingsHelper.CreateSetting(document, element, "ObsWindowCaptureCompatibilityMode", settings.ObsWindowCaptureCompatibilityMode) ^
        SettingsHelper.CreateSetting(document, element, "VideoPanX", settings.VideoPanX) ^
        SettingsHelper.CreateSetting(document, element, "VideoPanY", settings.VideoPanY) ^
        SettingsHelper.CreateSetting(document, element, "VideoZoomExtra", settings.VideoZoomExtra) ^
        SettingsHelper.CreateSetting(document, element, "VideoStartWithTimer", settings.VideoStartWithTimer) ^
        SettingsHelper.CreateSetting(document, element, "VideoKeepPlaybackAcrossTimerResets", settings.VideoKeepPlaybackAcrossTimerResets) ^
        SettingsHelper.CreateSetting(document, element, "VideoPauseWhenRunCompletes", settings.VideoPauseWhenRunCompletes) ^
        SettingsHelper.CreateSetting(document, element, "VideoVolumeReductionPercentWhenRunCompletes", settings.VideoVolumeReductionPercentWhenRunCompletes) ^
        SettingsHelper.CreateSetting(document, element, "VideoStartOffsetSeconds", settings.VideoStartOffsetSeconds) ^
        SettingsHelper.CreateSetting(document, element, "ImageOpacity", settings.ImageOpacity) ^
        SettingsHelper.CreateSetting(document, element, "VideoOpacity", settings.VideoOpacity) ^
        SettingsHelper.CreateSetting(document, element, "ImageBlur", settings.ImageBlur) ^
        SettingsHelper.CreateSetting(document, element, "VideoBlurScale", settings.VideoBlurScale) ^
        SettingsHelper.CreateSetting(document, element, "VideoBlurType", settings.VideoBlurType) ^
        SettingsHelper.CreateSetting(document, element, "VideoBlurDegrees", settings.VideoBlurDegrees) ^
        SettingsHelper.CreateSetting(document, element, "Opacity", settings.Opacity) ^
        SettingsHelper.CreateSetting(document, element, "MousePassThroughWhileRunning", settings.MousePassThroughWhileRunning) ^
        SettingsHelper.CreateSetting(document, element, "AllowResizing", settings.AllowResizing) ^
        SettingsHelper.CreateSetting(document, element, "AllowMoving", settings.AllowMoving);
    }

    public int CreateLayoutNode(XmlDocument document, XmlElement parent, ILayout layout)
    {
        XmlElement element = null, components = null;
        if (document != null)
        {
            element = document.CreateElement("Settings");
            components = document.CreateElement("Components");
        }

        int hashCode = SettingsHelper.CreateSetting(document, parent, "Mode", layout.Mode)
            ^ SettingsHelper.CreateSetting(document, parent, "X", layout.X)
            ^ SettingsHelper.CreateSetting(document, parent, "Y", layout.Y)
            ^ SettingsHelper.CreateSetting(document, parent, "VerticalWidth", layout.VerticalWidth)
            ^ (SettingsHelper.CreateSetting(document, parent, "VerticalHeight", layout.VerticalHeight) * 1000)
            ^ SettingsHelper.CreateSetting(document, parent, "HorizontalWidth", layout.HorizontalWidth)
            ^ (SettingsHelper.CreateSetting(document, parent, "HorizontalHeight", layout.HorizontalHeight) * 1000)
            ^ ToElement(document, element, layout.Settings);

        if (document != null)
        {
            parent.AppendChild(element);
            parent.AppendChild(components);
        }

        var layoutComponents = new List<ILayoutComponent>(layout.LayoutComponents);
        int count = 1;

        foreach (ILayoutComponent component in layoutComponents)
        {
            try
            {
                if (document != null)
                {
                    XmlElement componentElement = document.CreateElement("Component");
                    components.AppendChild(componentElement);
                    SettingsHelper.CreateSetting(document, componentElement, "Path", component.Path);
                    XmlElement settings = document.CreateElement("Settings");

                    settings.InnerXml = component.Component.GetSettings(document).InnerXml;

                    componentElement.AppendChild(settings);

                    if (component is LayoutComponent layoutComponent
                        && layoutComponent.FontOverrides.HasOverrides)
                    {
                        XmlElement fontOverridesElement = document.CreateElement("FontOverrides");
                        SettingsHelper.CreateSetting(document, fontOverridesElement, "OverrideTimerFont", layoutComponent.FontOverrides.OverrideTimerFont);
                        if (layoutComponent.FontOverrides.OverrideTimerFont && layoutComponent.FontOverrides.TimerFont != null)
                        {
                            SettingsHelper.CreateSetting(document, fontOverridesElement, "TimerFont", layoutComponent.FontOverrides.TimerFont);
                        }

                        SettingsHelper.CreateSetting(document, fontOverridesElement, "OverrideTimesFont", layoutComponent.FontOverrides.OverrideTimesFont);
                        if (layoutComponent.FontOverrides.OverrideTimesFont && layoutComponent.FontOverrides.TimesFont != null)
                        {
                            SettingsHelper.CreateSetting(document, fontOverridesElement, "TimesFont", layoutComponent.FontOverrides.TimesFont);
                        }

                        SettingsHelper.CreateSetting(document, fontOverridesElement, "OverrideTextFont", layoutComponent.FontOverrides.OverrideTextFont);
                        if (layoutComponent.FontOverrides.OverrideTextFont && layoutComponent.FontOverrides.TextFont != null)
                        {
                            SettingsHelper.CreateSetting(document, fontOverridesElement, "TextFont", layoutComponent.FontOverrides.TextFont);
                        }

                        componentElement.AppendChild(fontOverridesElement);
                    }
                }
                else
                {
                    Type type = component.Component.GetType();
                    if (type.GetMethod("GetSettingsHashCode") != null)
                    {
                        hashCode ^= ((dynamic)component.Component).GetSettingsHashCode() ^ (component.GetHashCode() * count);
                    }
                    else
                    {
                        hashCode ^= component.Component.GetSettings(new XmlDocument()).InnerXml.GetHashCode() ^ (component.GetHashCode() * count);
                    }

                    if (component is LayoutComponent layoutComponentForHash)
                    {
                        int fontHash = (layoutComponentForHash.FontOverrides.OverrideTimerFont ? 1 : 0)
                            ^ (layoutComponentForHash.FontOverrides.OverrideTimesFont ? 2 : 0)
                            ^ (layoutComponentForHash.FontOverrides.OverrideTextFont ? 4 : 0);
                        if (layoutComponentForHash.FontOverrides.TimerFont != null)
                        {
                            fontHash ^= SettingsHelper.CreateSetting(null, null, "TimerFont", layoutComponentForHash.FontOverrides.TimerFont);
                        }

                        if (layoutComponentForHash.FontOverrides.TimesFont != null)
                        {
                            fontHash ^= SettingsHelper.CreateSetting(null, null, "TimesFont", layoutComponentForHash.FontOverrides.TimesFont);
                        }

                        if (layoutComponentForHash.FontOverrides.TextFont != null)
                        {
                            fontHash ^= SettingsHelper.CreateSetting(null, null, "TextFont", layoutComponentForHash.FontOverrides.TextFont);
                        }

                        hashCode ^= fontHash ^ (count * 31337);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e);
            }

            count++;
        }

        return hashCode;
    }

    public void Save(ILayout layout, Stream stream)
    {
        var document = new XmlDocument();

        XmlNode docNode = document.CreateXmlDeclaration("1.0", "UTF-8", null);
        document.AppendChild(docNode);
        XmlElement parent = document.CreateElement("Layout");
        parent.Attributes.Append(SettingsHelper.ToAttribute(document, "version", "1.6.1"));
        CreateLayoutNode(document, parent, layout);
        document.AppendChild(parent);

        document.Save(stream);
    }
}

