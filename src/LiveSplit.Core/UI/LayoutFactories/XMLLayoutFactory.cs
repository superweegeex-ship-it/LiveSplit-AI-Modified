using System;
using System.Drawing;
using System.IO;
using System.Xml;

using LiveSplit.Model;
using LiveSplit.Options;
using LiveSplit.UI;
using LiveSplit.UI.Components;

namespace LiveSplit.UI.LayoutFactories;

public class XMLLayoutFactory : ILayoutFactory
{
    public Stream Stream { get; set; }

    public XMLLayoutFactory(Stream stream)
    {
        Stream = stream;
    }

    /// <summary>Reads <c>IconShadowOffset</c> / <c>IconShadowBlur</c> (and text equivalents), or migrates legacy radius/sharpness keys.</summary>
    private static void ApplyShadowXmlMigration(XmlElement e, LayoutSettings s)
    {
        if (e["IconShadowOffset"] != null)
        {
            s.IconShadowOffset = SettingsHelper.ParseFloat(e["IconShadowOffset"], s.IconShadowOffset);
        }
        else if (e["IconShadowRadius"] != null)
        {
            float legacy = SettingsHelper.ParseFloat(e["IconShadowRadius"], 4f) * 0.45f;
            s.IconShadowOffset = Math.Min(10f, Math.Max(-10f, legacy - 2f));
        }

        if (e["IconShadowBlur"] != null)
        {
            s.IconShadowBlur = SettingsHelper.ParseFloat(e["IconShadowBlur"], s.IconShadowBlur);
        }
        else if (e["IconShadowSharpness"] != null)
        {
            s.IconShadowBlur = Math.Min(100f, Math.Max(0f, 100f - SettingsHelper.ParseFloat(e["IconShadowSharpness"], 0f)));
        }

        if (e["TextShadowOffset"] != null)
        {
            s.TextShadowOffset = SettingsHelper.ParseFloat(e["TextShadowOffset"], s.TextShadowOffset);
        }
        else if (e["TextShadowRadius"] != null)
        {
            s.TextShadowOffset = Math.Min(16f, Math.Max(0f, SettingsHelper.ParseFloat(e["TextShadowRadius"], 2f)));
        }

        if (e["TextShadowBlur"] != null)
        {
            s.TextShadowBlur = SettingsHelper.ParseFloat(e["TextShadowBlur"], s.TextShadowBlur);
        }
        else if (e["TextShadowSharpness"] != null)
        {
            s.TextShadowBlur = Math.Min(100f, Math.Max(0f, 100f - SettingsHelper.ParseFloat(e["TextShadowSharpness"], 100f)));
        }
    }

    private static float ClampPercent(float value)
    {
        return Math.Min(100f, Math.Max(0f, value));
    }

    private static BackgroundType ParseBackgroundType(XmlElement element, BackgroundType defaultType = BackgroundType.SolidColor)
    {
        if (element == null)
        {
            return defaultType;
        }

        return string.Equals(element.InnerText, "AnimatedImage", StringComparison.OrdinalIgnoreCase)
            ? BackgroundType.Image
            : SettingsHelper.ParseEnum(element, defaultType);
    }

    private static float ParseVideoVolumePercentWhenRunCompletes(XmlElement element)
    {
        if (element["VideoVolumePercentWhenRunCompletes"] != null)
        {
            return ClampPercent(SettingsHelper.ParseFloat(element["VideoVolumePercentWhenRunCompletes"], 100f));
        }

        if (element["VideoVolumeReductionPercentWhenRunCompletes"] != null)
        {
            float oldReductionPercent = ClampPercent(SettingsHelper.ParseFloat(element["VideoVolumeReductionPercentWhenRunCompletes"], 0f));
            return 100f - oldReductionPercent;
        }

        return 100f;
    }

    private static LayoutSettings ParseSettings(XmlElement element, Version version)
    {
        var settings = new LayoutSettings
        {
            TextColor = SettingsHelper.ParseColor(element["TextColor"]),
            BackgroundColor = SettingsHelper.ParseColor(element["BackgroundColor"]),
            ThinSeparatorsColor = SettingsHelper.ParseColor(element["ThinSeparatorsColor"]),
            SeparatorsColor = SettingsHelper.ParseColor(element["SeparatorsColor"]),
            ThinSeparatorThickness = SettingsHelper.ParseFloat(element["ThinSeparatorThickness"], 1f),
            SeparatorThickness = SettingsHelper.ParseFloat(element["SeparatorThickness"], 2f),
            PersonalBestColor = SettingsHelper.ParseColor(element["PersonalBestColor"]),
            AheadGainingTimeColor = SettingsHelper.ParseColor(element["AheadGainingTimeColor"]),
            AheadLosingTimeColor = SettingsHelper.ParseColor(element["AheadLosingTimeColor"]),
            BehindGainingTimeColor = SettingsHelper.ParseColor(element["BehindGainingTimeColor"]),
            BehindLosingTimeColor = SettingsHelper.ParseColor(element["BehindLosingTimeColor"]),
            BestSegmentColor = SettingsHelper.ParseColor(element["BestSegmentColor"]),
            UseRainbowColor = SettingsHelper.ParseBool(element["UseRainbowColor"], false),
            NotRunningColor = SettingsHelper.ParseColor(element["NotRunningColor"]),
            PausedColor = SettingsHelper.ParseColor(element["PausedColor"], Color.FromArgb(122, 122, 122)),
            AntiAliasing = SettingsHelper.ParseBool(element["AntiAliasing"], true),
            DropShadows = SettingsHelper.ParseBool(element["DropShadows"], true),
            Opacity = SettingsHelper.ParseFloat(element["Opacity"], 1),
            MousePassThroughWhileRunning = SettingsHelper.ParseBool(element["MousePassThroughWhileRunning"]),
            TransparentBackgroundForCapture = SettingsHelper.ParseBool(element["TransparentBackgroundForCapture"], false),
            AllowResizing = SettingsHelper.ParseBool(element["AllowResizing"], true),
            AllowMoving = SettingsHelper.ParseBool(element["AllowMoving"], true),
            TextOutlineColor = SettingsHelper.ParseColor(element["TextOutlineColor"], Color.FromArgb(0, 0, 0, 0)),
            ShadowsColor = SettingsHelper.ParseColor(element["ShadowsColor"], Color.FromArgb(128, 0, 0, 0)),
            IconShadowOffset = 0f,
            IconShadowTransparency = SettingsHelper.ParseFloat(element["IconShadowTransparency"], 100f),
            IconShadowBlur = 28f,
            TextShadowOffset = 2f,
            TextShadowTransparency = SettingsHelper.ParseFloat(element["TextShadowTransparency"], 100f),
            TextShadowBlur = 26f,
            ShowBestSegments = SettingsHelper.ParseBool(element["ShowBestSegments"]),
            AlwaysOnTop = SettingsHelper.ParseBool(element["AlwaysOnTop"]),
            TimerFont = SettingsHelper.GetFontFromElement(element["TimerFont"]),
            ImageOpacity = SettingsHelper.ParseFloat(element["ImageOpacity"], 1f),
            VideoOpacity = SettingsHelper.ParseFloat(element["VideoOpacity"], 1f),
            ImageBlur = SettingsHelper.ParseFloat(element["ImageBlur"], 0f),
            VideoBlurScale = SettingsHelper.ParseFloat(element["VideoBlurScale"], 0f),
            VideoBlurType = SettingsHelper.ParseEnum(element["VideoBlurType"], BackgroundVideoBlurType.Gaussian),
            VideoBlurDegrees = SettingsHelper.ParseFloat(element["VideoBlurDegrees"], 0f),
            BackgroundVideoPath = SettingsHelper.ParseString(element["BackgroundVideoPath"]),
            BackgroundVideoSource = SettingsHelper.ParseString(element["BackgroundVideoSource"]),
            BackgroundVideoInputType = SettingsHelper.ParseEnum(element["BackgroundVideoInputType"], BackgroundVideoInputType.File),
            UseHardwareVideoDecoding = SettingsHelper.ParseBool(element["UseHardwareVideoDecoding"], false),
            LoopVideo = SettingsHelper.ParseBool(element["LoopVideo"], false),
            PlayVideoAudio = SettingsHelper.ParseBool(element["PlayVideoAudio"], false),
            VideoAudioVolume = SettingsHelper.ParseFloat(element["VideoAudioVolume"], 1f),
            VideoPanX = SettingsHelper.ParseFloat(element["VideoPanX"], 0f),
            VideoPanY = SettingsHelper.ParseFloat(element["VideoPanY"], 0f),
            VideoZoomExtra = SettingsHelper.ParseFloat(element["VideoZoomExtra"], 0f),
            VideoSpeedPercent = Math.Max(10f, Math.Min(400f, SettingsHelper.ParseFloat(element["VideoSpeedPercent"], 100f))),
            ImagePanX = SettingsHelper.ParseFloat(element["ImagePanX"], 0f),
            ImagePanY = SettingsHelper.ParseFloat(element["ImagePanY"], 0f),
            ImageZoomExtra = SettingsHelper.ParseFloat(element["ImageZoomExtra"], 0f),
            VideoStartWithTimer = SettingsHelper.ParseBool(element["VideoStartWithTimer"], false),
            VideoKeepPlaybackAcrossTimerResets = SettingsHelper.ParseBool(element["VideoKeepPlaybackAcrossTimerResets"], true),
            VideoPauseWhenRunCompletes = SettingsHelper.ParseBool(element["VideoPauseWhenRunCompletes"], false),
            VideoVolumePercentWhenRunCompletes = ParseVideoVolumePercentWhenRunCompletes(element),
            VideoStartOffsetSeconds = SettingsHelper.ParseFloat(element["VideoStartOffsetSeconds"], 0f)
        };

        ApplyShadowXmlMigration(element, settings);

        if (element["SeparatorFillMode"] != null)
        {
            settings.SeparatorFillMode = SettingsHelper.ParseEnum(
                element["SeparatorFillMode"],
                CurrentSplitOutlineFillMode.Solid);
        }

        if (element["SeparatorGradientEndColor"] != null)
        {
            settings.SeparatorGradientEndColor = SettingsHelper.ParseColor(element["SeparatorGradientEndColor"]);
        }

        if (element["SeparatorRgbWave"] != null)
        {
            settings.SeparatorRgbWave = SettingsHelper.ParseBool(element["SeparatorRgbWave"], false);
        }

        if (element["SeparatorWaveAxis"] != null)
        {
            settings.SeparatorWaveAxis = SettingsHelper.ParseEnum(
                element["SeparatorWaveAxis"],
                CurrentSplitOutlineWaveAxis.Horizontal);
        }

        if (element["SeparatorWaveSpeed"] != null)
        {
            int parsed = SettingsHelper.ParseInt(element["SeparatorWaveSpeed"], 0);
            settings.SeparatorWaveSpeed = Math.Min(CurrentSplitOutlinePaint.OutlineWaveSpeedSliderMaximum, Math.Max(0, parsed));
        }

        if (element["SeparatorOutlineEnabled"] != null)
        {
            settings.SeparatorOutlineEnabled = SettingsHelper.ParseBool(element["SeparatorOutlineEnabled"], false);
        }

        if (element["SeparatorOutlineThickness"] != null)
        {
            settings.SeparatorOutlineThickness = SettingsHelper.ParseFloat(element["SeparatorOutlineThickness"], 2f);
        }

        if (element["SeparatorOutlineColor"] != null)
        {
            settings.SeparatorOutlineColor = SettingsHelper.ParseColor(element["SeparatorOutlineColor"]);
        }

        if (element["SeparatorOutlineTransparency"] != null)
        {
            settings.SeparatorOutlineTransparency = SettingsHelper.ParseFloat(element["SeparatorOutlineTransparency"], 0f);
        }

        if (element["SeparatorOutlineFillMode"] != null)
        {
            settings.SeparatorOutlineFillMode = SettingsHelper.ParseEnum(
                element["SeparatorOutlineFillMode"],
                CurrentSplitOutlineFillMode.Solid);
        }

        if (element["SeparatorOutlineGradientEndColor"] != null)
        {
            settings.SeparatorOutlineGradientEndColor = SettingsHelper.ParseColor(element["SeparatorOutlineGradientEndColor"]);
        }

        if (element["SeparatorOutlineRgbWave"] != null)
        {
            settings.SeparatorOutlineRgbWave = SettingsHelper.ParseBool(element["SeparatorOutlineRgbWave"], false);
        }

        if (element["SeparatorOutlineWaveAxis"] != null)
        {
            settings.SeparatorOutlineWaveAxis = SettingsHelper.ParseEnum(
                element["SeparatorOutlineWaveAxis"],
                CurrentSplitOutlineWaveAxis.Horizontal);
        }

        if (element["SeparatorOutlineWaveSpeed"] != null)
        {
            int parsedOutline = SettingsHelper.ParseInt(element["SeparatorOutlineWaveSpeed"], 0);
            settings.SeparatorOutlineWaveSpeed = Math.Min(CurrentSplitOutlinePaint.OutlineWaveSpeedSliderMaximum, Math.Max(0, parsedOutline));
        }

        if (element["SeparatorOutlineInterpolation"] != null)
        {
            settings.SeparatorOutlineInterpolation = SettingsHelper.ParseEnum(
                element["SeparatorOutlineInterpolation"],
                CurrentSplitImageInterpolationFilter.Nearest);
        }

        if (version >= new Version(1, 3))
        {
            settings.BackgroundColor2 = SettingsHelper.ParseColor(element["BackgroundColor2"]);
            settings.TimesFont = SettingsHelper.GetFontFromElement(element["TimesFont"]);
            settings.TextFont = SettingsHelper.GetFontFromElement(element["TextFont"]);
        }
        else
        {
            if (settings.BackgroundColor == Color.Black)
            {
                settings.BackgroundColor = settings.BackgroundColor2 = Color.Transparent;
            }
            else
            {
                settings.BackgroundColor2 = settings.BackgroundColor;
            }

            settings.TimesFont = SettingsHelper.GetFontFromElement(element["MainFont"]);
            settings.TextFont = SettingsHelper.GetFontFromElement(element["SplitNamesFont"]);
        }

        if (version >= new Version(1, 6, 1))
        {
            settings.BackgroundType = ParseBackgroundType(element["BackgroundType"]);
        }
        else
        {
            XmlElement gradientType = element["BackgroundGradient"];
            if (gradientType == null || gradientType.InnerText == "Plain")
            {
                settings.BackgroundType = BackgroundType.SolidColor;
            }
            else if (gradientType.InnerText == "Vertical")
            {
                settings.BackgroundType = BackgroundType.VerticalGradient;
            }
            else
            {
                settings.BackgroundType = BackgroundType.HorizontalGradient;
            }
        }

        settings.BackgroundImage = SettingsHelper.GetImageFromElement(element["BackgroundImage"]);

        return settings;
    }

    public ILayout Create(LiveSplitState state)
    {
        var document = new XmlDocument();
        document.Load(Stream);
        var layout = new Layout();
        XmlElement parent = document["Layout"];
        Version version = SettingsHelper.ParseAttributeVersion(parent);

        layout.X = SettingsHelper.ParseInt(parent["X"]);
        layout.Y = SettingsHelper.ParseInt(parent["Y"]);
        layout.VerticalWidth = SettingsHelper.ParseInt(parent["VerticalWidth"]);
        layout.VerticalHeight = SettingsHelper.ParseInt(parent["VerticalHeight"]);
        layout.HorizontalWidth = SettingsHelper.ParseInt(parent["HorizontalWidth"]);
        layout.HorizontalHeight = SettingsHelper.ParseInt(parent["HorizontalHeight"]);
        layout.Mode = SettingsHelper.ParseEnum<LayoutMode>(parent["Mode"]);
        layout.Settings = ParseSettings(parent["Settings"], version);

        XmlElement components = parent["Components"];
        foreach (object componentNode in components.GetElementsByTagName("Component"))
        {
            var componentElement = componentNode as XmlElement;
            XmlElement path = componentElement["Path"];
            XmlElement settings = componentElement["Settings"];
            ILayoutComponent layoutComponent = ComponentManager.LoadLayoutComponent(path.InnerText, state);
            if (layoutComponent != null)
            {
                try
                {
                    layoutComponent.Component.SetSettings(settings);

                    XmlElement fontOverridesElement = componentElement["FontOverrides"];
                    if (fontOverridesElement != null && layoutComponent is LayoutComponent lc)
                    {
                        lc.FontOverrides.OverrideTimerFont = SettingsHelper.ParseBool(fontOverridesElement["OverrideTimerFont"]);
                        if (lc.FontOverrides.OverrideTimerFont)
                        {
                            lc.FontOverrides.TimerFont = SettingsHelper.GetFontFromElement(fontOverridesElement["TimerFont"]);
                        }

                        lc.FontOverrides.OverrideTimesFont = SettingsHelper.ParseBool(fontOverridesElement["OverrideTimesFont"]);
                        if (lc.FontOverrides.OverrideTimesFont)
                        {
                            lc.FontOverrides.TimesFont = SettingsHelper.GetFontFromElement(fontOverridesElement["TimesFont"]);
                        }

                        lc.FontOverrides.OverrideTextFont = SettingsHelper.ParseBool(fontOverridesElement["OverrideTextFont"]);
                        if (lc.FontOverrides.OverrideTextFont)
                        {
                            lc.FontOverrides.TextFont = SettingsHelper.GetFontFromElement(fontOverridesElement["TextFont"]);
                        }
                    }
                    else if (layoutComponent is LayoutComponent lcLegacy)
                    {
                        // Migrate legacy per-component font overrides via reflection.
                        // Components that had their own font override settings can provide
                        // a MigrateFontOverrides(FontOverrides) method to populate the new
                        // unified FontOverrides on the LayoutComponent wrapper.
                        layoutComponent.Component.GetType()
                            .GetMethod("MigrateFontOverrides", [typeof(FontOverrides)])
                            ?.Invoke(layoutComponent.Component, [lcLegacy.FontOverrides]);
                    }
                }
                catch (Exception e)
                {
                    Log.Error(e);
                }

                layout.LayoutComponents.Add(layoutComponent);
            }
            else
            {
                throw new Exception(path.InnerText + " could not be found");
            }
        }

        return layout;
    }
}
