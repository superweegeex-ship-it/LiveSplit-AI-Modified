using System;
using System.Drawing;

namespace LiveSplit.Options;

public class LayoutSettings : ICloneable
{
    public Color TextColor { get; set; }
    public Color BackgroundColor { get; set; }
    public Color BackgroundColor2 { get; set; }
    public Color ThinSeparatorsColor { get; set; }
    public Color SeparatorsColor { get; set; }
    public float ThinSeparatorThickness { get; set; }
    public float SeparatorThickness { get; set; }
    public Color PersonalBestColor { get; set; }
    public Color AheadGainingTimeColor { get; set; }
    public Color AheadLosingTimeColor { get; set; }
    public Color BehindGainingTimeColor { get; set; }
    public Color BehindLosingTimeColor { get; set; }
    public Color BestSegmentColor { get; set; }
    public Color NotRunningColor { get; set; }
    public Color PausedColor { get; set; }
    public Color TextOutlineColor { get; set; }
    public Color ShadowsColor { get; set; }

    public BackgroundType BackgroundType { get; set; }

    public Image BackgroundImage { get; set; }
    public float ImageOpacity { get; set; }
    public float VideoOpacity { get; set; }
    public float ImageBlur { get; set; }
    public float VideoBlurScale { get; set; }
    public BackgroundVideoBlurType VideoBlurType { get; set; }
    public float VideoBlurDegrees { get; set; }
    public string BackgroundVideoPath { get; set; }
    public string BackgroundVideoSource { get; set; }
    public BackgroundVideoInputType BackgroundVideoInputType { get; set; }
    public BackgroundVideoBackend BackgroundVideoBackend { get; set; }
    public bool UseHardwareVideoDecoding { get; set; }
    public bool LoopVideo { get; set; }
    public bool PlayVideoAudio { get; set; }
    public float VideoAudioVolume { get; set; }
    /// <summary>
    /// Prefer a single in-window compositor path for better OBS Window Capture reliability.
    /// This may increase CPU usage versus the layered overlay window path.
    /// </summary>
    public bool ObsWindowCaptureCompatibilityMode { get; set; }
    /// <summary>Horizontal pan for video background (-1..1, mpv video-pan-x).</summary>
    public float VideoPanX { get; set; }
    /// <summary>Vertical pan for video background (-1..1, mpv video-pan-y).</summary>
    public float VideoPanY { get; set; }
    /// <summary>Extra zoom for video background (0..1 maps to mpv video-zoom).</summary>
    public float VideoZoomExtra { get; set; }
    /// <summary>When true, background video stays paused at the start until the run timer starts, then seeks to <see cref="VideoStartOffsetSeconds"/>.</summary>
    public bool VideoStartWithTimer { get; set; }
    /// <summary>
    /// When <see cref="VideoStartWithTimer"/> is true: after the timer has been started once, resetting the timer does not seek the video back to the start.
    /// The first time (before any start), the video still waits at the beginning until the timer starts.
    /// </summary>
    public bool VideoKeepPlaybackAcrossTimerResets { get; set; }
    /// <summary>
    /// When <see cref="VideoStartWithTimer"/> is true: if true, pause background video when the run completes (final split).
    /// </summary>
    public bool VideoPauseWhenRunCompletes { get; set; }
    /// <summary>
    /// 0..100: after the final split, multiply layout <see cref="VideoAudioVolume"/> by <c>(1 - value/100)</c> for playback. 0 = no reduction.
    /// </summary>
    public float VideoVolumeReductionPercentWhenRunCompletes { get; set; }
    /// <summary>Seconds into the video file to seek when the timer starts (used with <see cref="VideoStartWithTimer"/>).</summary>
    public float VideoStartOffsetSeconds { get; set; }

    public Font TimerFont { get; set; }
    public Font TimesFont { get; set; }
    public Font TextFont { get; set; }

    public bool ShowBestSegments { get; set; }
    public bool AlwaysOnTop { get; set; }
    public bool AntiAliasing { get; set; }
    public bool DropShadows { get; set; }
    public bool UseRainbowColor { get; set; }

    public float Opacity { get; set; }
    public bool MousePassThroughWhileRunning { get; set; }
    public bool AllowResizing { get; set; }
    public bool AllowMoving { get; set; }

    public object Clone()
    {
        var settings = new LayoutSettings();
        settings.Assign(this);
        return settings;
    }

    public void Assign(LayoutSettings settings)
    {
        TextColor = settings.TextColor;
        BackgroundColor = settings.BackgroundColor;
        BackgroundColor2 = settings.BackgroundColor2;
        ThinSeparatorsColor = settings.ThinSeparatorsColor;
        SeparatorsColor = settings.SeparatorsColor;
        ThinSeparatorThickness = settings.ThinSeparatorThickness;
        SeparatorThickness = settings.SeparatorThickness;
        PersonalBestColor = settings.PersonalBestColor;
        AheadGainingTimeColor = settings.AheadGainingTimeColor;
        AheadLosingTimeColor = settings.AheadLosingTimeColor;
        BehindGainingTimeColor = settings.BehindGainingTimeColor;
        BehindLosingTimeColor = settings.BehindLosingTimeColor;
        BestSegmentColor = settings.BestSegmentColor;
        UseRainbowColor = settings.UseRainbowColor;
        NotRunningColor = settings.NotRunningColor;
        PausedColor = settings.PausedColor;
        TextOutlineColor = settings.TextOutlineColor;
        ShadowsColor = settings.ShadowsColor;
        TimerFont = settings.TimerFont.Clone() as Font;
        TimesFont = settings.TimesFont.Clone() as Font;
        TextFont = settings.TextFont.Clone() as Font;
        ShowBestSegments = settings.ShowBestSegments;
        AlwaysOnTop = settings.AlwaysOnTop;
        AntiAliasing = settings.AntiAliasing;
        DropShadows = settings.DropShadows;
        Opacity = settings.Opacity;
        MousePassThroughWhileRunning = settings.MousePassThroughWhileRunning;
        BackgroundType = settings.BackgroundType;
        BackgroundImage = settings.BackgroundImage;
        ImageOpacity = settings.ImageOpacity;
        VideoOpacity = settings.VideoOpacity;
        ImageBlur = settings.ImageBlur;
        VideoBlurScale = settings.VideoBlurScale;
        VideoBlurType = settings.VideoBlurType;
        VideoBlurDegrees = settings.VideoBlurDegrees;
        BackgroundVideoPath = settings.BackgroundVideoPath;
        BackgroundVideoSource = settings.BackgroundVideoSource;
        BackgroundVideoInputType = settings.BackgroundVideoInputType;
        BackgroundVideoBackend = settings.BackgroundVideoBackend;
        UseHardwareVideoDecoding = settings.UseHardwareVideoDecoding;
        LoopVideo = settings.LoopVideo;
        PlayVideoAudio = settings.PlayVideoAudio;
        VideoAudioVolume = settings.VideoAudioVolume;
        ObsWindowCaptureCompatibilityMode = settings.ObsWindowCaptureCompatibilityMode;
        VideoPanX = settings.VideoPanX;
        VideoPanY = settings.VideoPanY;
        VideoZoomExtra = settings.VideoZoomExtra;
        VideoStartWithTimer = settings.VideoStartWithTimer;
        VideoKeepPlaybackAcrossTimerResets = settings.VideoKeepPlaybackAcrossTimerResets;
        VideoPauseWhenRunCompletes = settings.VideoPauseWhenRunCompletes;
        VideoVolumeReductionPercentWhenRunCompletes = settings.VideoVolumeReductionPercentWhenRunCompletes;
        VideoStartOffsetSeconds = settings.VideoStartOffsetSeconds;
        AllowResizing = settings.AllowResizing;
        AllowMoving = settings.AllowMoving;
    }
}
