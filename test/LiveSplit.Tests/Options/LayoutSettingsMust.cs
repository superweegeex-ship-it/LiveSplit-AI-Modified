using System.Collections.Generic;
using System.Drawing;
using System.IO;

using LiveSplit.Options;
using LiveSplit.Options.SettingsFactories;
using LiveSplit.Options.SettingsSavers;
using LiveSplit.UI;
using LiveSplit.UI.Components;

using Xunit;

namespace LiveSplit.Tests.Options;

public class LayoutSettingsMust
{
    [Fact]
    public void PersistDialogPanelResizingOption()
    {
        ComponentManager.RaceProviderFactories ??= new Dictionary<string, IRaceProviderFactory>();
        ISettings settings = new StandardSettingsFactory().Create();
        settings.AllowDialogPanelResizing = true;

        using var stream = new MemoryStream();
        new XMLSettingsSaver().Save(settings, stream);

        stream.Position = 0;
        ISettings loaded = new XMLSettingsFactory(stream).Create();

        Assert.True(loaded.AllowDialogPanelResizing);
    }

    [Fact]
    public void RememberValuesCorrectly()
    {
        LayoutSettings sut = CreateSubjectUnderTest();
        ValidateSubject(sut);
    }

    private static LayoutSettings CreateSubjectUnderTest()
    {
        return new LayoutSettings
        {
            TextColor = Color.Yellow,
            AheadGainingTimeColor = Color.Red,
            AheadLosingTimeColor = Color.AliceBlue,
            AlwaysOnTop = true,
            AntiAliasing = true,
            BackgroundColor = Color.Red,
            BackgroundColor2 = Color.Transparent,
            BackgroundImage = new Bitmap(10, 10),
            BackgroundType = BackgroundType.HorizontalGradient,
            BehindGainingTimeColor = Color.Magenta,
            BehindLosingTimeColor = Color.IndianRed,
            BestSegmentColor = Color.AntiqueWhite,
            DropShadows = true,
            ImageBlur = 4.5F,
            ImageOpacity = 1,
            VideoBlurScale = 0.35F,
            VideoBlurType = BackgroundVideoBlurType.Directional,
            VideoBlurDegrees = 45F,
            VideoSpeedPercent = 175F,
            VideoVolumePercentWhenRunCompletes = 33F,
            MousePassThroughWhileRunning = true,
            TransparentBackgroundForCapture = true,
            AllowResizing = true,
            AllowMoving = true,
            NotRunningColor = Color.Gray,
            Opacity = 9.1F,
            PausedColor = Color.HotPink,
            PersonalBestColor = Color.Aqua,
            SeparatorsColor = Color.White,
            SeparatorThickness = 3.5f,
            SeparatorFillMode = CurrentSplitOutlineFillMode.Gradient,
            SeparatorGradientEndColor = Color.Teal,
            SeparatorRgbWave = true,
            SeparatorWaveAxis = CurrentSplitOutlineWaveAxis.Vertical,
            SeparatorWaveSpeed = 7,
            SeparatorOutlineEnabled = true,
            SeparatorOutlineThickness = 2.5f,
            SeparatorOutlineColor = Color.Navy,
            SeparatorOutlineTransparency = 12f,
            SeparatorOutlineFillMode = CurrentSplitOutlineFillMode.Gradient,
            SeparatorOutlineGradientEndColor = Color.Lime,
            SeparatorOutlineRgbWave = false,
            SeparatorOutlineWaveAxis = CurrentSplitOutlineWaveAxis.Horizontal,
            SeparatorOutlineWaveSpeed = 3,
            SeparatorOutlineInterpolation = CurrentSplitImageInterpolationFilter.Bilinear,
            ShadowsColor = Color.Brown,
            IconShadowOffset = 6f,
            IconShadowTransparency = 80f,
            IconShadowBlur = 35f,
            TextShadowOffset = 5f,
            TextShadowTransparency = 90f,
            TextShadowBlur = 40f,
            ShowBestSegments = true,
            TextFont = new Font("Arial", 8.0F),
            TextOutlineColor = Color.CadetBlue,
            ThinSeparatorsColor = Color.Chartreuse,
            ThinSeparatorThickness = 1.5f,
            TimerFont = new Font("Arial", 9.0F),
            TimesFont = new Font("Arial", 10.0F)
        };
    }

    private static void ValidateSubject(LayoutSettings sut)
    {
        Assert.Equal(Color.Yellow, sut.TextColor);
        Assert.Equal(Color.Red, sut.AheadGainingTimeColor);
        Assert.Equal(Color.AliceBlue, sut.AheadLosingTimeColor);
        Assert.True(sut.AlwaysOnTop);
        Assert.True(sut.AntiAliasing);
        Assert.Equal(Color.Red, sut.BackgroundColor);
        Assert.Equal(Color.Transparent, sut.BackgroundColor2);
        Assert.NotNull(sut.BackgroundImage);
        Assert.Equal(10, sut.BackgroundImage.Width);
        Assert.Equal(10, sut.BackgroundImage.Height);
        Assert.Equal(BackgroundType.HorizontalGradient, sut.BackgroundType);
        Assert.Equal(Color.Magenta, sut.BehindGainingTimeColor);
        Assert.Equal(Color.IndianRed, sut.BehindLosingTimeColor);
        Assert.Equal(Color.AntiqueWhite, sut.BestSegmentColor);
        Assert.True(sut.DropShadows);
        Assert.Equal(4.5F, sut.ImageBlur);
        Assert.Equal(1, sut.ImageOpacity);
        Assert.Equal(0.35F, sut.VideoBlurScale);
        Assert.Equal(BackgroundVideoBlurType.Directional, sut.VideoBlurType);
        Assert.Equal(45F, sut.VideoBlurDegrees);
        Assert.Equal(175F, sut.VideoSpeedPercent);
        Assert.Equal(33F, sut.VideoVolumePercentWhenRunCompletes);
        Assert.True(sut.MousePassThroughWhileRunning);
        Assert.True(sut.TransparentBackgroundForCapture);
        Assert.True(sut.AllowResizing);
        Assert.True(sut.AllowMoving);
        Assert.Equal(Color.Gray, sut.NotRunningColor);
        Assert.Equal(9.1F, sut.Opacity);
        Assert.Equal(Color.HotPink, sut.PausedColor);
        Assert.Equal(Color.Aqua, sut.PersonalBestColor);
        Assert.Equal(Color.White, sut.SeparatorsColor);
        Assert.Equal(3.5f, sut.SeparatorThickness);
        Assert.Equal(CurrentSplitOutlineFillMode.Gradient, sut.SeparatorFillMode);
        Assert.Equal(Color.Teal, sut.SeparatorGradientEndColor);
        Assert.True(sut.SeparatorRgbWave);
        Assert.Equal(CurrentSplitOutlineWaveAxis.Vertical, sut.SeparatorWaveAxis);
        Assert.Equal(7, sut.SeparatorWaveSpeed);
        Assert.True(sut.SeparatorOutlineEnabled);
        Assert.Equal(2.5f, sut.SeparatorOutlineThickness);
        Assert.Equal(Color.Navy, sut.SeparatorOutlineColor);
        Assert.Equal(12f, sut.SeparatorOutlineTransparency);
        Assert.Equal(CurrentSplitOutlineFillMode.Gradient, sut.SeparatorOutlineFillMode);
        Assert.Equal(Color.Lime, sut.SeparatorOutlineGradientEndColor);
        Assert.False(sut.SeparatorOutlineRgbWave);
        Assert.Equal(CurrentSplitOutlineWaveAxis.Horizontal, sut.SeparatorOutlineWaveAxis);
        Assert.Equal(3, sut.SeparatorOutlineWaveSpeed);
        Assert.Equal(CurrentSplitImageInterpolationFilter.Bilinear, sut.SeparatorOutlineInterpolation);
        Assert.Equal(Color.Brown, sut.ShadowsColor);
        Assert.Equal(6f, sut.IconShadowOffset);
        Assert.Equal(80f, sut.IconShadowTransparency);
        Assert.Equal(35f, sut.IconShadowBlur);
        Assert.Equal(5f, sut.TextShadowOffset);
        Assert.Equal(90f, sut.TextShadowTransparency);
        Assert.Equal(40f, sut.TextShadowBlur);
        Assert.True(sut.ShowBestSegments);
        Assert.NotNull(sut.TextFont);
        Assert.Equal("Arial", sut.TextFont.Name);
        Assert.Equal(Color.CadetBlue, sut.TextOutlineColor);
        Assert.Equal(Color.Chartreuse, sut.ThinSeparatorsColor);
        Assert.Equal(1.5f, sut.ThinSeparatorThickness);
        Assert.Equal("Arial", sut.TimerFont.Name);
        Assert.Equal("Arial", sut.TimesFont.Name);
    }

    [Fact]
    public void CloneLayoutSettingsCorrectly()
    {
        LayoutSettings original = CreateSubjectUnderTest();
        var sut = (LayoutSettings)original.Clone();
        ValidateSubject(sut);
    }

    [Fact]
    public void AssignLayoutSettingsCorrectly()
    {
        LayoutSettings original = CreateSubjectUnderTest();
        var sut = new LayoutSettings();
        sut.Assign(original);
        ValidateSubject(sut);
    }
}
