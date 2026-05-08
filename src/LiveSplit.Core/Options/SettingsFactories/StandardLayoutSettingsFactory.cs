using System.Drawing;

using LiveSplit.UI;

using static System.Drawing.Color;

namespace LiveSplit.Options.SettingsFactories;

public class StandardLayoutSettingsFactory : ILayoutSettingsFactory
{
    public LayoutSettings Create()
    {
        return new LayoutSettings()
        {
            TextColor = FromArgb(255, 255, 255),
            BackgroundColor = FromArgb(0, 0, 0, 0),
            BackgroundColor2 = FromArgb(0, 0, 0, 0),
            ThinSeparatorsColor = FromArgb(9, 255, 255, 255),
            SeparatorsColor = FromArgb(38, 255, 255, 255),
            ThinSeparatorThickness = 1f,
            SeparatorThickness = 2f,
            PersonalBestColor = FromArgb(22, 166, 255),
            AheadGainingTimeColor = FromArgb(41, 204, 84),
            AheadLosingTimeColor = FromArgb(112, 204, 137),
            BehindGainingTimeColor = FromArgb(204, 120, 112),
            BehindLosingTimeColor = FromArgb(204, 55, 41),
            BestSegmentColor = FromArgb(216, 175, 31),
            NotRunningColor = FromArgb(122, 122, 122),
            PausedColor = FromArgb(122, 122, 122),
            TextOutlineColor = FromArgb(0, 0, 0, 0),
            ShadowsColor = FromArgb(128, 0, 0, 0),
            IconShadowOffset = 0f,
            IconShadowTransparency = 100f,
            IconShadowBlur = 28f,
            TextShadowOffset = 2f,
            TextShadowTransparency = 100f,
            TextShadowBlur = 26f,
            TimerFont = new Font("Century Gothic", 43.75f, FontStyle.Bold, GraphicsUnit.Pixel),
            TimesFont = new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel),
            TextFont = new Font("Segoe UI", 16, FontStyle.Regular, GraphicsUnit.Pixel),
            ShowBestSegments = true,
            UseRainbowColor = false,
            AlwaysOnTop = true,
            AntiAliasing = true,
            DropShadows = true,
            BackgroundType = BackgroundType.SolidColor,
            BackgroundImage = null,
            ImageOpacity = 1f,
            VideoOpacity = 1f,
            ImageBlur = 0f,
            VideoBlurScale = 0f,
            VideoBlurType = BackgroundVideoBlurType.Gaussian,
            VideoBlurDegrees = 0f,
            BackgroundVideoPath = "",
            BackgroundVideoSource = "",
            BackgroundVideoInputType = BackgroundVideoInputType.File,
            UseHardwareVideoDecoding = false,
            LoopVideo = false,
            PlayVideoAudio = false,
            VideoAudioVolume = 1f,
            VideoPanX = 0f,
            VideoPanY = 0f,
            VideoZoomExtra = 0f,
            ImagePanX = 0f,
            ImagePanY = 0f,
            ImageZoomExtra = 0f,
            VideoStartWithTimer = false,
            VideoKeepPlaybackAcrossTimerResets = true,
            VideoPauseWhenRunCompletes = false,
            VideoVolumePercentWhenRunCompletes = 100f,
            VideoStartOffsetSeconds = 0f,
            Opacity = 1,
            MousePassThroughWhileRunning = false,
            TransparentBackgroundForCapture = false,
            AllowResizing = true,
            AllowMoving = true,
            SeparatorFillMode = CurrentSplitOutlineFillMode.Solid,
            SeparatorGradientEndColor = FromArgb(255, 80, 200),
            SeparatorRgbWave = false,
            SeparatorWaveAxis = CurrentSplitOutlineWaveAxis.Horizontal,
            SeparatorWaveSpeed = 0,
            SeparatorOutlineEnabled = false,
            SeparatorOutlineThickness = 2f,
            SeparatorOutlineColor = FromArgb(255, 255, 255),
            SeparatorOutlineTransparency = 0f,
            SeparatorOutlineFillMode = CurrentSplitOutlineFillMode.Solid,
            SeparatorOutlineGradientEndColor = FromArgb(255, 80, 200),
            SeparatorOutlineRgbWave = false,
            SeparatorOutlineWaveAxis = CurrentSplitOutlineWaveAxis.Horizontal,
            SeparatorOutlineWaveSpeed = 0,
            SeparatorOutlineInterpolation = CurrentSplitImageInterpolationFilter.Nearest
        };
    }
}
