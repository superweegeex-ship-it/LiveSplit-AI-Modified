using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Xml;

using LiveSplit.Model;
using LiveSplit.Options;
using LiveSplit.Options.SettingsFactories;
using LiveSplit.UI;
using LiveSplit.UI.Components;
using LiveSplit.UI.LayoutFactories;
using LiveSplit.UI.LayoutSavers;

using Xunit;

namespace LiveSplit.Tests.UI;

public class LayoutSerializationFontOverridesMust
{
    [Fact]
    public void RoundtripFontOverrides_WhenSavedAndLoaded()
    {
        var layout = new Layout { Settings = new StandardLayoutSettingsFactory().Create() };
        var layoutComponent = new LayoutComponent("test.dll", new StubComponent())
        {
            FontOverrides = new FontOverrides
            {
                OverrideTextFont = true,
                TextFont = new Font("Arial", 14f)
            }
        };
        layout.LayoutComponents.Add(layoutComponent);

        var saver = new XMLLayoutSaver();
        using var stream = new MemoryStream();
        saver.Save(layout, stream);
        stream.Position = 0;

        var doc = new XmlDocument();
        doc.Load(stream);

        XmlNode fontOverridesNode = doc.SelectSingleNode("//FontOverrides");
        Assert.NotNull(fontOverridesNode);

        XmlNode overrideTextFontNode = doc.SelectSingleNode("//FontOverrides/OverrideTextFont");
        Assert.NotNull(overrideTextFontNode);
        Assert.Equal("True", overrideTextFontNode.InnerText);

        XmlNode textFontNode = doc.SelectSingleNode("//FontOverrides/TextFont");
        Assert.NotNull(textFontNode);
        Assert.False(string.IsNullOrEmpty(textFontNode.InnerText));
    }

    [Fact]
    public void LoadOldLayoutWithoutFontOverrides_DefaultsToNoOverride()
    {
        var layoutComponent = new LayoutComponent("test.dll", new StubComponent());

        Assert.NotNull(layoutComponent.FontOverrides);
        Assert.False(layoutComponent.FontOverrides.HasOverrides);
        Assert.False(layoutComponent.FontOverrides.OverrideTimerFont);
        Assert.False(layoutComponent.FontOverrides.OverrideTimesFont);
        Assert.False(layoutComponent.FontOverrides.OverrideTextFont);
        Assert.Null(layoutComponent.FontOverrides.TimerFont);
        Assert.Null(layoutComponent.FontOverrides.TimesFont);
        Assert.Null(layoutComponent.FontOverrides.TextFont);
    }

    [Fact]
    public void IncludeFontOverridesInHash_WhenOverridePresent()
    {
        var layout1 = new Layout { Settings = new StandardLayoutSettingsFactory().Create() };
        layout1.LayoutComponents.Add(new LayoutComponent("test.dll", new StubComponent()));

        var layout2 = new Layout { Settings = new StandardLayoutSettingsFactory().Create() };
        var lcWithOverride = new LayoutComponent("test.dll", new StubComponent())
        {
            FontOverrides = new FontOverrides
            {
                OverrideTextFont = true,
                TextFont = new Font("Arial", 14f)
            }
        };
        layout2.LayoutComponents.Add(lcWithOverride);

        var saver = new XMLLayoutSaver();
        int hash1 = saver.CreateLayoutNode(null, null, layout1);
        int hash2 = saver.CreateLayoutNode(null, null, layout2);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void OmitFontOverridesFromXml_WhenNoOverridesSet()
    {
        var layout = new Layout { Settings = new StandardLayoutSettingsFactory().Create() };
        layout.LayoutComponents.Add(new LayoutComponent("test.dll", new StubComponent()));

        var saver = new XMLLayoutSaver();
        using var stream = new MemoryStream();
        saver.Save(layout, stream);
        stream.Position = 0;

        var doc = new XmlDocument();
        doc.Load(stream);

        XmlNode fontOverridesNode = doc.SelectSingleNode("//FontOverrides");
        Assert.Null(fontOverridesNode);
    }

    [Fact]
    public void RoundtripFinalSplitVideoVolumeAsTargetPercent()
    {
        var layout = new Layout { Settings = new StandardLayoutSettingsFactory().Create() };
        layout.Settings.VideoVolumePercentWhenRunCompletes = 35f;

        using var stream = new MemoryStream();
        new XMLLayoutSaver().Save(layout, stream);
        stream.Position = 0;

        var loaded = new XMLLayoutFactory(stream).Create(null);

        Assert.Equal(35f, loaded.Settings.VideoVolumePercentWhenRunCompletes);
    }

    [Fact]
    public void RoundtripBackgroundVideoSpeedPercent()
    {
        var layout = new Layout { Settings = new StandardLayoutSettingsFactory().Create() };
        layout.Settings.VideoSpeedPercent = 175f;

        using var stream = new MemoryStream();
        new XMLLayoutSaver().Save(layout, stream);
        stream.Position = 0;

        var loaded = new XMLLayoutFactory(stream).Create(null);

        Assert.Equal(175f, loaded.Settings.VideoSpeedPercent);
    }

    [Fact]
    public void RoundtripNegativeBackgroundZoom()
    {
        var layout = new Layout { Settings = new StandardLayoutSettingsFactory().Create() };
        layout.Settings.VideoZoomExtra = -0.5f;
        layout.Settings.ImageZoomExtra = -0.25f;

        using var stream = new MemoryStream();
        new XMLLayoutSaver().Save(layout, stream);
        stream.Position = 0;

        var loaded = new XMLLayoutFactory(stream).Create(null);

        Assert.Equal(-0.5f, loaded.Settings.VideoZoomExtra);
        Assert.Equal(-0.25f, loaded.Settings.ImageZoomExtra);
    }

    [Fact]
    public void RoundtripTransparentBackgroundForCapture()
    {
        var layout = new Layout { Settings = new StandardLayoutSettingsFactory().Create() };
        layout.Settings.TransparentBackgroundForCapture = true;

        using var stream = new MemoryStream();
        new XMLLayoutSaver().Save(layout, stream);
        stream.Position = 0;

        var loaded = new XMLLayoutFactory(stream).Create(null);

        Assert.True(loaded.Settings.TransparentBackgroundForCapture);
    }

    [Fact]
    public void MigrateLegacyFinalSplitVideoVolumeReductionToTargetPercent()
    {
        var layout = new Layout { Settings = new StandardLayoutSettingsFactory().Create() };
        using var stream = new MemoryStream();
        new XMLLayoutSaver().Save(layout, stream);
        stream.Position = 0;

        var doc = new XmlDocument();
        doc.Load(stream);
        var settingsNode = (XmlElement)doc.SelectSingleNode("//Settings");
        settingsNode.RemoveChild(settingsNode["VideoVolumePercentWhenRunCompletes"]);

        XmlElement legacyReduction = doc.CreateElement("VideoVolumeReductionPercentWhenRunCompletes");
        legacyReduction.InnerText = "25";
        settingsNode.AppendChild(legacyReduction);

        using var legacyStream = new MemoryStream();
        doc.Save(legacyStream);
        legacyStream.Position = 0;

        var loaded = new XMLLayoutFactory(legacyStream).Create(null);

        Assert.Equal(75f, loaded.Settings.VideoVolumePercentWhenRunCompletes);
    }

    [Fact]
    public void MigrateLegacyAnimatedImageBackgroundToStaticImage()
    {
        var layout = new Layout { Settings = new StandardLayoutSettingsFactory().Create() };
        using var stream = new MemoryStream();
        new XMLLayoutSaver().Save(layout, stream);
        stream.Position = 0;

        var doc = new XmlDocument();
        doc.Load(stream);
        var backgroundType = (XmlElement)doc.SelectSingleNode("//Settings/BackgroundType");
        backgroundType.InnerText = "AnimatedImage";

        using var legacyStream = new MemoryStream();
        doc.Save(legacyStream);
        legacyStream.Position = 0;

        var loaded = new XMLLayoutFactory(legacyStream).Create(null);

        Assert.Equal(BackgroundType.Image, loaded.Settings.BackgroundType);
    }

    /// <summary>
    /// Minimal IComponent stub for serialization tests.
    /// Only GetSettings is needed by XMLLayoutSaver.
    /// </summary>
    private sealed class StubComponent : IComponent
    {
        public string ComponentName => "Stub";

        public float HorizontalWidth => 0;
        public float MinimumHeight => 0;
        public float VerticalHeight => 0;
        public float MinimumWidth => 0;
        public float PaddingTop => 0;
        public float PaddingBottom => 0;
        public float PaddingLeft => 0;
        public float PaddingRight => 0;

        public IDictionary<string, Action> ContextMenuControls => null;

        public void DrawHorizontal(Graphics g, LiveSplitState state, float height, Region clipRegion) { }
        public void DrawVertical(Graphics g, LiveSplitState state, float width, Region clipRegion) { }
        public Control GetSettingsControl(LayoutMode mode) { return null; }

        public XmlNode GetSettings(XmlDocument document)
        {
            return document.CreateElement("Settings");
        }

        public void SetSettings(XmlNode settings) { }
        public void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode) { }
        public void Dispose() { }
    }
}
