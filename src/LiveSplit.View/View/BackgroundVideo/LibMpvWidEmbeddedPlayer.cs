using System;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

using LiveSplit.Model;
using LiveSplit.Options;

namespace LiveSplit.View.BackgroundVideo;

/// <summary>
/// libmpv embedded into a Win32 child region via <c>wid</c> (no render API / readback).
/// </summary>
internal sealed class LibMpvWidEmbeddedPlayer : IBackgroundVideoPlayer
{
    /// <summary>
    /// Hardware decoder fallback chain for wid-embed (zero-copy preferred). First entry is tried first;
    /// each subsequent entry is tried only if the previous one fails to initialize/work.
    /// d3d11va is best on Windows 8+ with D3D11 swap-chain presentation.
    /// </summary>
    private static readonly string[] HwdecFallbackChain = new[]
    {
        "d3d11va",
        "d3d11va-copy",
        "dxva2",
        "dxva2-copy",
        "auto-safe",
        "auto-copy",
        "auto",
    };
    /// <summary>
    /// libavfilter effects need CPU-readable frames; direct d3d11va keeps frames on the GPU, so switch
    /// filtered playback to copy-back hardware decoding instead of disabling hardware decode entirely.
    /// </summary>
    private const string HwdecCopyBackForFilters = "auto-copy";
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x80000;
    private const uint LWA_ALPHA = 0x2;
    /// <summary>mpv_event.event_id — see libmpv client.h (stable across releases we target).</summary>
    private const int MpvEventNone = 0;
    private const int MpvEventShutdown = 1;
    private const int MpvEventStartFile = 6;
    private const int MpvEventEndFile = 7;
    private const int MpvEventFileLoaded = 8;
    private const int MpvEventVideoReconfig = 17;
    private const int MpvFormatString = 1;
    private const int MpvFormatDouble = 5;
    private static readonly bool EnableEmbeddedMpvStatsOverlayByDefault = false;
    /// <summary>When |mpv time-pos minus expected| exceeds this, apply a corrective seek (run uses wall-clock).</summary>
    private const double DriftCorrectAboveSeconds = 0.22;
    /// <summary>Beyond this gap, seek absolutely (keyframes) instead of a tight relative seek.</summary>
    private const double DriftAbsoluteSeekAboveSeconds = 1.25;
    private const int StallProbeIntervalMs = 1000;
    private const double StallProbeMinAdvanceSeconds = 0.012;
    private const int StallConsecutiveSamplesBeforeRecover = 4;
    private const int MaxMpvEventsPerUiTick = 24;
    private const int ZOrderMaintenanceIntervalMs = 250;

    private readonly Form hostForm;
    private readonly VideoHostPanel videoHost;
    private readonly VideoHostPanel mpvSurface;
    private readonly Panel layoutOverlay;
    private IntPtr mpvHandle;
    private string activeVideoProfileName = "unknown";
    /// <summary>Drains <c>mpv_wait_event</c> on the UI thread — libmpv is not thread-safe with a background pump.</summary>
    private System.Windows.Forms.Timer mpvUiPumpTimer;
    private volatile bool disposed;
    private float renderOpacity = 1f;
    private float lastAppliedRenderOpacity = float.NaN;
    private float lastAppliedBrightness = float.NaN;
    private float videoBlurScale;
    private float lastAppliedVideoBlurScale = float.NaN;
    private float videoBlurDegrees;
    private float lastAppliedVideoBlurDegrees = float.NaN;
    private BackgroundVideoBlurType videoBlurType = BackgroundVideoBlurType.Gaussian;
    private BackgroundVideoBlurType lastAppliedVideoBlurType = BackgroundVideoBlurType.Gaussian;
    private string lastAppliedVideoFilter = null;
    private string loadedSource;
    private bool appliedMpvRuntimeOptions;
    private bool lastAppliedLoopVideo;
    private bool lastAppliedPlayAudio;
    private int lastAppliedVolume = -1;
    private bool lastAppliedHardwareDecoding;
    private string lastAppliedHwdecOption = null;
    private float lastAppliedVideoPanX = float.NaN;
    private float lastAppliedVideoPanY = float.NaN;
    private float lastAppliedVideoZoom = float.NaN;
    private bool waitingForTimerStartBeforePlayback;
    private double lastTimerSyncSeekPositionSeconds = double.NaN;
    private bool forceTimerSyncResync;
    private double videoStartOffsetSeconds;
    private readonly System.Diagnostics.Stopwatch stallProbeClock = System.Diagnostics.Stopwatch.StartNew();
    private long lastStallProbeTicks;
    private double lastStallProbeTimePos = double.NaN;
    private int consecutiveStalledSamples;
    private string lastAppliedScalerName = string.Empty;
    /// <summary>Vertical resolutions that get pixel-perfect (nearest-neighbor) scaling. Classic NES/SNES/N64/etc.</summary>
    private static readonly int[] RetroVerticalResolutions = new[] { 224, 240, 448, 480 };
    private readonly System.Diagnostics.Stopwatch playbackFpsClock = System.Diagnostics.Stopwatch.StartNew();
    private long lastPlaybackFpsSampleMs;
    private long lastDecoderFrameCount = -1;
    private long lastFrameDropCount;
    private double measuredPlaybackFps;
    private double measuredFrameDropPerSec;
    /// <summary>True after mpv emits MpvEventEndFile; suppresses drift correction / stall recovery.</summary>
    private volatile bool atEndOfFile;
    private DateTime lastDebugTimePosReadUtc = DateTime.MinValue;
    private double cachedDebugTimePosSeconds = double.NaN;
    private const int DebugTimePosRefreshIntervalMs = 100;
    private int lastZOrderMaintenanceTicks;

    public LibMpvWidEmbeddedPlayer(
        Form hostForm,
        VideoHostPanel videoHost,
        VideoHostPanel mpvSurface,
        Panel layoutOverlay)
    {
        this.hostForm = hostForm;
        this.videoHost = videoHost;
        this.mpvSurface = mpvSurface;
        this.layoutOverlay = layoutOverlay;
    }

    public bool IsInitialized { get; private set; }
    public bool IsLoaded { get; private set; }
    public string LastError { get; private set; }
    public bool UsesEmbeddedNativeCompositor => true;

    public bool UseHardwareDecoding { get; set; }
    public bool LoopVideo { get; set; }
    public bool PlayAudio { get; set; }
    public float VideoPanX { get; set; }
    public float VideoPanY { get; set; }
    public float VideoZoomExtra { get; set; }
    public float VideoBlurScale
    {
        get => videoBlurScale;
        set => videoBlurScale = Math.Max(0f, Math.Min(1f, value));
    }

    public BackgroundVideoBlurType VideoBlurType
    {
        get => videoBlurType;
        set => videoBlurType = value;
    }

    public float VideoBlurDegrees
    {
        get => videoBlurDegrees;
        set => videoBlurDegrees = Math.Max(0f, Math.Min(360f, value));
    }

    public bool StartVideoWithTimer { get; set; }

    private float audioVolume = 1f;
    public float AudioVolume
    {
        get => audioVolume;
        set => audioVolume = Math.Max(0f, Math.Min(1f, value));
    }

    public double VideoStartOffsetSeconds
    {
        get => videoStartOffsetSeconds;
        set => videoStartOffsetSeconds = Math.Max(0, Math.Min(86400, value));
    }

    public int MaxPresentFps { get; set; } = 30;

    public bool TryInitialize()
    {
        if (IsInitialized)
        {
            return true;
        }

        if (mpvHandle != IntPtr.Zero)
        {
            try
            {
                mpv_terminate_destroy(mpvHandle);
            }
            catch
            {
            }

            mpvHandle = IntPtr.Zero;
        }

        if (!EnsureLibMpvAvailable(out string libMpvError))
        {
            LastError = libMpvError;
            return false;
        }

        try
        {
            if (!videoHost.IsHandleCreated)
            {
                _ = videoHost.Handle;
            }

            if (!mpvSurface.IsHandleCreated)
            {
                _ = mpvSurface.Handle;
            }

            if (!TryInitializeWithFallbackProfiles())
            {
                return false;
            }

            appliedMpvRuntimeOptions = false;
            lastAppliedLoopVideo = !LoopVideo;
            lastAppliedPlayAudio = !PlayAudio;
            lastAppliedVolume = -1;
            lastAppliedHardwareDecoding = !UseHardwareDecoding;
            lastAppliedVideoPanX = float.NaN;
            lastAppliedVideoPanY = float.NaN;
            lastAppliedVideoZoom = float.NaN;

            StartMpvUiPumpTimer();
            IsInitialized = true;
            LastError = null;
            return true;
        }
        catch (DllNotFoundException)
        {
            LastError = "libmpv initialization failed: libmpv-2.dll was not found.";
            mpvHandle = IntPtr.Zero;
            return false;
        }
        catch (Exception ex)
        {
            LastError = "libmpv initialization failed: " + ex.Message;
            if (mpvHandle != IntPtr.Zero)
            {
                mpv_terminate_destroy(mpvHandle);
                mpvHandle = IntPtr.Zero;
            }

            return false;
        }
    }

    private bool TryInitializeWithFallbackProfiles()
    {
        (string Name, string Vo, string GpuContext, string GpuApi)[] profiles =
        [
            ("gpu-next-d3d11", "gpu-next", "d3d11", "d3d11"),
            ("gpu-d3d11", "gpu", "d3d11", "d3d11"),
            ("gpu-angle-d3d11", "gpu", "angle", "d3d11"),
            ("direct3d-safe", "direct3d", null, null),
        ];

        string lastError = "unknown error";
        foreach ((string name, string vo, string gpuContext, string gpuApi) in profiles)
        {
            mpvHandle = mpv_create();
            if (mpvHandle == IntPtr.Zero)
            {
                lastError = "mpv_create returned null.";
                continue;
            }

            try
            {
                _ = mpv_set_option_string(mpvHandle, "wid", mpvSurface.Handle.ToInt64().ToString(CultureInfo.InvariantCulture));
                _ = mpv_set_option_string(mpvHandle, "keep-open", "yes");
                _ = mpv_set_option_string(mpvHandle, "terminal", "no");
                _ = mpv_set_option_string(mpvHandle, "keepaspect", "yes");
                _ = mpv_set_option_string(mpvHandle, "panscan", "1.0");
                _ = mpv_set_option_string(mpvHandle, "vo", vo);
                if (!string.IsNullOrWhiteSpace(gpuContext))
                {
                    _ = mpv_set_option_string(mpvHandle, "gpu-context", gpuContext);
                }

                if (!string.IsNullOrWhiteSpace(gpuApi))
                {
                    _ = mpv_set_option_string(mpvHandle, "gpu-api", gpuApi);
                }

                _ = mpv_set_option_string(mpvHandle, "hwdec", GetEffectiveHwdecOption());
                _ = mpv_set_option_string(mpvHandle, "profile", "fast");
                _ = mpv_set_option_string(mpvHandle, "scale", "bilinear");
                _ = mpv_set_option_string(mpvHandle, "demuxer-readahead-secs", "2");

                int initResult = mpv_initialize(mpvHandle);
                if (initResult >= 0)
                {
                    activeVideoProfileName = name;
                    return true;
                }

                lastError = $"profile '{name}' failed with mpv_initialize={initResult}.";
            }
            catch (Exception ex)
            {
                lastError = $"profile '{name}' failed: {ex.Message}";
            }

            try
            {
                mpv_terminate_destroy(mpvHandle);
            }
            catch
            {
            }

            mpvHandle = IntPtr.Zero;
        }

        LastError = "libmpv initialization failed for all embedded profiles. Last: " + lastError;
        return false;
    }

    private void StartMpvUiPumpTimer()
    {
        if (mpvUiPumpTimer != null)
        {
            return;
        }

        void start()
        {
            mpvUiPumpTimer = new System.Windows.Forms.Timer { Interval = 16 };
            mpvUiPumpTimer.Tick += MpvUiPumpTimerOnTick;
            mpvUiPumpTimer.Start();
        }

        if (hostForm.InvokeRequired)
        {
            hostForm.BeginInvoke((MethodInvoker)start);
        }
        else
        {
            start();
        }
    }

    private void StopMpvUiPumpTimer()
    {
        try
        {
            mpvUiPumpTimer?.Stop();
            mpvUiPumpTimer?.Dispose();
            mpvUiPumpTimer = null;
        }
        catch
        {
        }
    }

    private void MpvUiPumpTimerOnTick(object sender, EventArgs e)
    {
        if (disposed || mpvHandle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            int processedEvents = 0;
            while (processedEvents < MaxMpvEventsPerUiTick)
            {
                IntPtr evPtr = mpv_wait_event(mpvHandle, 0);
                if (evPtr == IntPtr.Zero)
                {
                    break;
                }

                int eventId = Marshal.ReadInt32(evPtr);
                if (eventId == MpvEventNone)
                {
                    break;
                }

                if (eventId == MpvEventShutdown)
                {
                    IsInitialized = false;
                    IsLoaded = false;
                    LastError = "libmpv reported shutdown event for embedded backend.";
                    break;
                }

                if (eventId == MpvEventStartFile || eventId == MpvEventFileLoaded || eventId == MpvEventVideoReconfig)
                {
                    BringLayoutAboveNativeVideoChildren();
                    atEndOfFile = false;
                }

                if (eventId == MpvEventFileLoaded || eventId == MpvEventVideoReconfig)
                {
                    ApplyScalerForCurrentInputResolution();
                }

                if (eventId == MpvEventEndFile)
                {
                    // mpv has reached EOF (or was stopped). Stop trying to seek/unpause from our
                    // drift / stall paths; loop=inf will restart automatically, otherwise we let
                    // mpv stay at end (keep-open=yes was set during init).
                    atEndOfFile = true;
                }

                processedEvents++;
            }
        }
        catch
        {
        }

        if (IsLoaded)
        {
            int nowTicks = Environment.TickCount;
            if (unchecked(nowTicks - lastZOrderMaintenanceTicks) >= ZOrderMaintenanceIntervalMs)
            {
                lastZOrderMaintenanceTicks = nowTicks;
                BringLayoutAboveNativeVideoChildren();
            }
        }
    }

    public bool Load(string source)
    {
        if (!IsInitialized || string.IsNullOrWhiteSpace(source) || mpvHandle == IntPtr.Zero)
        {
            return false;
        }

        loadedSource = source;
        ResetDebugTimePosCache();
        consecutiveStalledSamples = 0;
        lastStallProbeTimePos = double.NaN;
        lastStallProbeTicks = stallProbeClock.ElapsedMilliseconds;
        lastAppliedScalerName = string.Empty;
        lastDecoderFrameCount = -1;
        lastFrameDropCount = 0;
        measuredPlaybackFps = 0.0;
        measuredFrameDropPerSec = 0.0;
        atEndOfFile = false;
        forceTimerSyncResync = false;
        ApplyMpvRuntimeOptions();
        // No "stop" before loadfile: mpv's loadfile replace handles VO continuity. A separate
        // stop here was leaving the VO in a half-attached state, so file/option changes did not
        // visibly take effect until something else (the close dialog) shook the window state.
        int mpvResult = MpvCommand("loadfile", source, "replace");
        IsLoaded = mpvResult >= 0;
        if (!IsLoaded)
        {
            LastError = "libmpv failed to load media source.";
            return false;
        }

        _ = MpvCommand("set", "vid", "auto");
        _ = MpvCommand("seek", "0", "absolute+keyframes");
        if (StartVideoWithTimer)
        {
            waitingForTimerStartBeforePlayback = true;
            lastTimerSyncSeekPositionSeconds = double.NaN;
            _ = MpvCommand("set", "pause", "yes");
        }
        else
        {
            // Always start unpaused on load (audio + video). The legacy A/V "startup hold" was causing
            // playback to stay frozen when the host's WM_PAINT loop never advanced the release counter.
            waitingForTimerStartBeforePlayback = false;
            lastTimerSyncSeekPositionSeconds = double.NaN;
            _ = MpvCommand("set", "pause", "no");
        }

        LastError = null;
        ResetMpvVisualEffectsCache();
        lastAppliedVideoPanX = float.NaN;
        lastAppliedVideoPanY = float.NaN;
        lastAppliedVideoZoom = float.NaN;
        if (EnableEmbeddedMpvStatsOverlayByDefault)
        {
            _ = MpvCommand("script-binding", "stats/display-stats-toggle");
        }
        SyncPresentationWithHost(true, MaxPresentFps);
        return true;
    }

    public void Stop()
    {
        if (mpvHandle == IntPtr.Zero)
        {
            return;
        }

        _ = MpvCommand("stop");
        IsLoaded = false;
        ResetDebugTimePosCache();
        waitingForTimerStartBeforePlayback = false;
        lastTimerSyncSeekPositionSeconds = double.NaN;
        forceTimerSyncResync = false;
        consecutiveStalledSamples = 0;
        lastStallProbeTimePos = double.NaN;
        atEndOfFile = false;
        ResetMpvVisualEffectsCache();
        SyncPresentationWithHost(false, MaxPresentFps);
    }

    public void RunTimerSyncWaitAtBeginning()
    {
        if (!IsInitialized || !IsLoaded || !StartVideoWithTimer || mpvHandle == IntPtr.Zero)
        {
            return;
        }

        waitingForTimerStartBeforePlayback = true;
        lastTimerSyncSeekPositionSeconds = double.NaN;
        _ = MpvCommand("seek", "0", "absolute+keyframes");
        _ = MpvCommand("set", "pause", "yes");
    }

    public void RunTimerSyncNotRunningKeepPlayback()
    {
        if (!IsInitialized || !IsLoaded || !StartVideoWithTimer || mpvHandle == IntPtr.Zero)
        {
            return;
        }

        waitingForTimerStartBeforePlayback = false;
        _ = MpvCommand("set", "pause", "no");
    }

    public void InvalidateTimerSyncSeekTarget()
    {
        forceTimerSyncResync = true;
    }

    public void RunTimerSyncEnsurePlayingForRunningPhase()
    {
        if (!IsLoaded || !StartVideoWithTimer || mpvHandle == IntPtr.Zero)
        {
            return;
        }

        // Only seek to layout offset when (a) the run just left the waiting-for-start state,
        // (b) the user changed the configured offset, or (c) an explicit resync was requested.
        // Treating "last seek unknown" (NaN) as "must seek to offset" caused large jumps when
        // toggling timer-start sync or leaving free-play mode while the timer was already running.
        bool layoutOffsetChanged = !double.IsNaN(lastTimerSyncSeekPositionSeconds)
            && Math.Abs(lastTimerSyncSeekPositionSeconds - VideoStartOffsetSeconds) > 0.02;

        if (waitingForTimerStartBeforePlayback)
        {
            var inv = CultureInfo.InvariantCulture;
            _ = MpvCommand("seek", VideoStartOffsetSeconds.ToString(inv), "absolute");
            _ = MpvCommand("set", "pause", "no");
            waitingForTimerStartBeforePlayback = false;
            lastTimerSyncSeekPositionSeconds = VideoStartOffsetSeconds;
            forceTimerSyncResync = false;
            return;
        }

        if (forceTimerSyncResync || layoutOffsetChanged)
        {
            forceTimerSyncResync = false;
            var inv = CultureInfo.InvariantCulture;
            _ = MpvCommand("seek", VideoStartOffsetSeconds.ToString(inv), "absolute");
            _ = MpvCommand("set", "pause", "no");
            lastTimerSyncSeekPositionSeconds = VideoStartOffsetSeconds;
            return;
        }

        _ = MpvCommand("set", "pause", "no");
    }

    public void RunTimerSyncPausePlayback()
    {
        if (!IsInitialized || !IsLoaded || !StartVideoWithTimer || mpvHandle == IntPtr.Zero)
        {
            return;
        }

        _ = MpvCommand("set", "pause", "yes");
    }

    public void RunTimerSyncUnpauseAfterRunCompletes()
    {
        if (!IsInitialized || !IsLoaded || !StartVideoWithTimer || mpvHandle == IntPtr.Zero)
        {
            return;
        }

        _ = MpvCommand("set", "pause", "no");
    }

    public void TickPlaybackRunTimerDriftCorrection(TimerPhase runPhase, TimeSpan runElapsedWallClock)
    {
        if (!IsInitialized || !IsLoaded || !StartVideoWithTimer || mpvHandle == IntPtr.Zero || LoopVideo || atEndOfFile)
        {
            return;
        }

        if (runPhase != TimerPhase.Running || runElapsedWallClock < TimeSpan.Zero)
        {
            return;
        }

        // Treat eof-reached as "do not seek/unpause" — keeps us from spamming seek commands past
        // the end of file, which on some codecs hard-crashes mpv.
        if (TryGetMpvStringProperty("eof-reached", out string eofText)
            && (string.Equals(eofText.Trim(), "yes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(eofText.Trim(), "true", StringComparison.OrdinalIgnoreCase)))
        {
            atEndOfFile = true;
            return;
        }

        if (TryGetMpvStringProperty("pause", out string pauseText) && pauseText.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!TryGetPlaybackTimePosSeconds(out double timePos))
        {
            return;
        }

        double expected = VideoStartOffsetSeconds + runElapsedWallClock.TotalSeconds;
        ProbeForPlaybackStallAndRecover(runPhase, timePos, expected);
        double delta = expected - timePos;
        if (Math.Abs(delta) < DriftCorrectAboveSeconds)
        {
            return;
        }

        var inv = CultureInfo.InvariantCulture;
        if (Math.Abs(delta) >= DriftAbsoluteSeekAboveSeconds)
        {
            _ = MpvCommand("seek", expected.ToString(inv), "absolute+keyframes");
        }
        else
        {
            _ = MpvCommand("seek", delta.ToString(inv), "relative", "exact");
        }
    }

    private void ProbeForPlaybackStallAndRecover(TimerPhase runPhase, double timePos, double expected)
    {
        if (runPhase != TimerPhase.Running)
        {
            consecutiveStalledSamples = 0;
            lastStallProbeTimePos = timePos;
            lastStallProbeTicks = stallProbeClock.ElapsedMilliseconds;
            return;
        }

        long nowMs = stallProbeClock.ElapsedMilliseconds;
        if (nowMs - lastStallProbeTicks < StallProbeIntervalMs)
        {
            return;
        }

        if (!double.IsNaN(lastStallProbeTimePos))
        {
            double advance = timePos - lastStallProbeTimePos;
            if (advance < StallProbeMinAdvanceSeconds)
            {
                consecutiveStalledSamples++;
            }
            else
            {
                consecutiveStalledSamples = 0;
            }
        }

        lastStallProbeTimePos = timePos;
        lastStallProbeTicks = nowMs;

        if (consecutiveStalledSamples < StallConsecutiveSamplesBeforeRecover)
        {
            return;
        }

        // Soft-recover by seeking to expected timeline position and forcing playback resumed.
        var inv = CultureInfo.InvariantCulture;
        _ = MpvCommand("seek", expected.ToString(inv), "absolute+exact");
        _ = MpvCommand("set", "pause", "no");
        consecutiveStalledSamples = 0;
        lastStallProbeTimePos = double.NaN;
    }

    public void ExitTimerStartSyncToNormalAutoplay()
    {
        waitingForTimerStartBeforePlayback = false;
        lastTimerSyncSeekPositionSeconds = double.NaN;
        if (!IsLoaded || mpvHandle == IntPtr.Zero)
        {
            return;
        }

        _ = MpvCommand("set", "pause", "no");
    }

    public void Render(Graphics graphics, int width, int height)
    {
        _ = graphics;
        _ = width;
        _ = height;
    }

    public void SetOpacity(float opacity)
    {
        renderOpacity = Math.Max(0f, Math.Min(1f, opacity));
        ApplyMpvVisualEffects();
    }

    public void UpdatePlacement()
    {
        BringLayoutAboveNativeVideoChildren();
    }

    public void NotifyHostClientSize(int w, int h)
    {
        _ = w;
        _ = h;
    }

    public void SyncPresentationWithHost(bool want, int fps)
    {
        _ = fps;
        _ = want;
        // Embedded mpv presents directly to its surface; no host-driven pacing required.
    }

    public void PingRuntimeOptionsToMpv()
    {
        ApplyMpvRuntimeOptions();
    }

    public void PushLoopFileOptionToMpvNow()
    {
        if (mpvHandle == IntPtr.Zero)
        {
            return;
        }

        _ = MpvCommand("set", "loop-file", LoopVideo ? "yes" : "no");
        lastAppliedLoopVideo = LoopVideo;
    }

    public void PushAudioVolumeToMpvNow()
    {
        if (mpvHandle == IntPtr.Zero)
        {
            return;
        }

        int volume = Math.Max(0, Math.Min(100, (int)Math.Round(AudioVolume * 100f)));
        _ = MpvCommand("set", "mute", PlayAudio ? "no" : "yes");
        _ = MpvCommand("set", "volume", volume.ToString(CultureInfo.InvariantCulture));
        lastAppliedVolume = volume;
        lastAppliedPlayAudio = PlayAudio;
    }

    private void ResetDebugTimePosCache()
    {
        lastDebugTimePosReadUtc = DateTime.MinValue;
        cachedDebugTimePosSeconds = double.NaN;
    }

    private string GetOutputResolutionString()
    {
        try
        {
            if (mpvSurface == null || mpvSurface.IsDisposed || !mpvSurface.IsHandleCreated)
            {
                return "?x?";
            }

            Size s = mpvSurface.ClientSize;
            if (s.Width < 1 || s.Height < 1)
            {
                return "?x?";
            }

            return string.Format(CultureInfo.InvariantCulture, "{0}x{1}", s.Width, s.Height);
        }
        catch
        {
            return "?x?";
        }
    }

    /// <summary>Formats <c>time-pos</c> for the debug overlay; mpv is queried at most every 100ms.</summary>
    private string FormatDebugCurrentDuration()
    {
        DateTime nowUtc = DateTime.UtcNow;
        if (double.IsNaN(cachedDebugTimePosSeconds)
            || (nowUtc - lastDebugTimePosReadUtc).TotalMilliseconds >= DebugTimePosRefreshIntervalMs)
        {
            lastDebugTimePosReadUtc = nowUtc;
            if (TryGetMpvDoubleProperty("time-pos", out double pos)
                && !double.IsNaN(pos)
                && !double.IsInfinity(pos)
                && pos >= 0.0)
            {
                cachedDebugTimePosSeconds = pos;
            }
            else
            {
                cachedDebugTimePosSeconds = double.NaN;
            }
        }

        return FormatVideoDuration(cachedDebugTimePosSeconds);
    }

    public string GetDebugOverlayText()
    {
        string hwdecOption = TryGetMpvStringProperty("hwdec", out string hOpt) ? hOpt : "?";
        string hwdecCurrent = TryGetMpvStringProperty("hwdec-current", out string hCur) ? hCur : "?";
        string videoFps = TryGetMpvStringProperty("estimated-vf-fps", out string fps) ? fps : "?";
        string vCodec = TryGetMpvStringProperty("video-codec-name", out string vc) ? vc : "?";
        string vBitrate = TryGetMpvStringProperty("video-bitrate", out string vb) ? vb : "?";
        string aCodec = TryGetMpvStringProperty("audio-codec-name", out string ac) ? ac : "?";
        string vWidth = TryGetMpvStringProperty("width", out string vw) ? vw : "?";
        string vHeight = TryGetMpvStringProperty("height", out string vh) ? vh : "?";
        string videoSync = TryGetMpvStringProperty("video-sync", out string vs) ? vs : "?";
        string framedrop = TryGetMpvStringProperty("framedrop", out string fd) ? fd : "?";
        string fileSize = TryGetMpvStringProperty("file-size", out string fsz) ? fsz : "?";
        string container = TryGetMpvStringProperty("file-format", out string ff) ? ff : "?";
        string displayFps = TryGetMpvStringProperty("display-fps", out string dfps) ? dfps : "?";
        string currentDuration = FormatDebugCurrentDuration();
        string scalerActive = string.IsNullOrEmpty(lastAppliedScalerName) ? (TryGetMpvStringProperty("scale", out string sc) ? sc : "?") : lastAppliedScalerName;

        SamplePlaybackFpsIfDue();

        return
            "Video Debug" + Environment.NewLine +
            "Backend: mpv (window embed / wid)" + Environment.NewLine +
            $"VO profile: {activeVideoProfileName}" + Environment.NewLine +
            $"hwdec (option): {hwdecOption}" + Environment.NewLine +
            $"hwdec-current: {hwdecCurrent}" + Environment.NewLine +
            $"video-codec: {vCodec}" + Environment.NewLine +
            $"audio-codec: {aCodec}" + Environment.NewLine +
            $"input resolution: {vWidth}x{vHeight}" + Environment.NewLine +
            $"output resolution: {GetOutputResolutionString()}" + Environment.NewLine +
            $"input-fps (estimated-vf-fps): {videoFps}" + Environment.NewLine +
            $"playback-fps (decoder rate): {measuredPlaybackFps:0.0}" + Environment.NewLine +
            $"frame-drops/sec: {measuredFrameDropPerSec:0.0}" + Environment.NewLine +
            $"display-fps (monitor): {displayFps}" + Environment.NewLine +
            $"scaler: {scalerActive}" + Environment.NewLine +
            $"current duration: {currentDuration}" + Environment.NewLine +
            $"video-bitrate: {vBitrate}" + Environment.NewLine +
            $"video-sync: {videoSync}" + Environment.NewLine +
            $"framedrop: {framedrop}" + Environment.NewLine +
            $"container: {container}" + Environment.NewLine +
            $"file-size: {fileSize}" + Environment.NewLine +
            $"Source: {(string.IsNullOrEmpty(loadedSource) ? "—" : loadedSource)}";
    }

    /// <summary>
    /// Tries hardware decoders in priority order. After applying, reads <c>hwdec-current</c>;
    /// if it's <c>no</c> (mpv refused or fell back to software) tries the next option in the chain.
    /// </summary>
    private void TryApplyHwdecWithFallback()
    {
        if (mpvHandle == IntPtr.Zero)
        {
            return;
        }

        if (!UseHardwareDecoding)
        {
            _ = MpvCommand("set", "hwdec", "no");
            return;
        }

        foreach (string candidate in HwdecFallbackChain)
        {
            _ = MpvCommand("set", "hwdec", candidate);
            // Give mpv a moment to evaluate, then check what the renderer actually picked.
            if (TryGetMpvStringProperty("hwdec-current", out string current)
                && !string.IsNullOrWhiteSpace(current)
                && !string.Equals(current.Trim(), "no", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(current.Trim(), "(unset)", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
    }

    /// <summary>
    /// Force nearest-neighbor scaling when input height matches retro game vertical resolutions
    /// (NES/SNES/Genesis/N64/etc.). For every other input, restore default bilinear scaling.
    /// </summary>
    private void ApplyScalerForCurrentInputResolution()
    {
        if (mpvHandle == IntPtr.Zero)
        {
            return;
        }

        if (!TryGetMpvIntProperty("height", out long heightLong))
        {
            return;
        }

        int height = (int)heightLong;
        bool isRetro = false;
        foreach (int retroHeight in RetroVerticalResolutions)
        {
            if (height == retroHeight)
            {
                isRetro = true;
                break;
            }
        }

        string desiredScaler = isRetro ? "nearest" : "bilinear";
        if (string.Equals(desiredScaler, lastAppliedScalerName, StringComparison.Ordinal))
        {
            return;
        }

        _ = MpvCommand("set", "scale", desiredScaler);
        _ = MpvCommand("set", "dscale", desiredScaler);
        _ = MpvCommand("set", "cscale", desiredScaler);
        lastAppliedScalerName = desiredScaler;
    }

    private bool TryGetMpvIntProperty(string propertyName, out long value)
    {
        value = 0;
        if (mpvHandle == IntPtr.Zero)
        {
            return false;
        }

        long propValue = 0;
        const int MpvFormatInt64 = 4;
        int result = mpv_get_property_int64(mpvHandle, propertyName, MpvFormatInt64, ref propValue);
        if (result >= 0)
        {
            value = propValue;
            return true;
        }

        // Fallback: some mpv builds expose certain numeric properties only via string.
        if (TryGetMpvStringProperty(propertyName, out string s)
            && long.TryParse(
                s.Trim(),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out long parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    /// <summary>Updates per-second playback FPS / frame-drop measurements from mpv decoder counters.</summary>
    private void SamplePlaybackFpsIfDue()
    {
        if (mpvHandle == IntPtr.Zero)
        {
            return;
        }

        long nowMs = playbackFpsClock.ElapsedMilliseconds;
        if (nowMs - lastPlaybackFpsSampleMs < 500)
        {
            return;
        }

        // mpv property names vary by build; try the most likely first, fall back through the rest.
        long decoderCount = 0;
        bool gotDecoder = TryGetMpvIntProperty("decoder-frame-count", out decoderCount)
            || TryGetMpvIntProperty("vo-passes/last/frames", out decoderCount)
            || TryGetMpvIntProperty("frames-decoded", out decoderCount);
        if (!gotDecoder)
        {
            // Last resort: derive an approximate playback fps from time-pos (real-time progression × input fps).
            if (TryGetMpvDoubleProperty("estimated-vf-fps", out double inputFps) && inputFps > 0.05)
            {
                measuredPlaybackFps = inputFps;
            }

            measuredFrameDropPerSec = 0.0;
            lastPlaybackFpsSampleMs = nowMs;
            return;
        }

        long dropCount = 0;
        _ = TryGetMpvIntProperty("frame-drop-count", out dropCount)
            || TryGetMpvIntProperty("vo-drop-frame-count", out dropCount);

        if (lastDecoderFrameCount >= 0 && nowMs > lastPlaybackFpsSampleMs)
        {
            double dt = (nowMs - lastPlaybackFpsSampleMs) / 1000.0;
            if (dt > 0.0)
            {
                measuredPlaybackFps = Math.Max(0.0, (decoderCount - lastDecoderFrameCount) / dt);
                measuredFrameDropPerSec = Math.Max(0.0, (dropCount - lastFrameDropCount) / dt);
            }
        }

        lastDecoderFrameCount = decoderCount;
        lastFrameDropCount = dropCount;
        lastPlaybackFpsSampleMs = nowMs;
    }

    private bool TryGetMpvDoubleProperty(string propertyName, out double value)
    {
        value = 0.0;
        if (mpvHandle == IntPtr.Zero)
        {
            return false;
        }

        double propValue = 0.0;
        const int MpvFormatDouble = 5;
        int result = mpv_get_property(mpvHandle, propertyName, MpvFormatDouble, ref propValue);
        if (result >= 0 && !double.IsNaN(propValue) && !double.IsInfinity(propValue))
        {
            value = propValue;
            return true;
        }

        if (TryGetMpvStringProperty(propertyName, out string s)
            && double.TryParse(
                s.Trim(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    public void NotifyCompositorActivated()
    {
        BringLayoutAboveNativeVideoChildren();
        ApplyMpvVisualEffects();
    }

    public void NotifyCompositorDeactivated()
    {
    }

    private void BringLayoutAboveNativeVideoChildren()
    {
        try
        {
            if (!videoHost.IsHandleCreated || !layoutOverlay.IsHandleCreated || !mpvSurface.IsHandleCreated)
            {
                return;
            }

            IntPtr overlayHwnd = layoutOverlay.Handle;
            IntPtr mpvSurfaceHwnd = mpvSurface.Handle;

            // Push mpv VO HWNDs (created as children of mpvSurface) to the bottom of mpvSurface's z-order.
            _ = EnumChildWindows(
                mpvSurfaceHwnd,
                (hwnd, lParam) =>
                {
                    _ = NativeSetWindowPos(
                        hwnd,
                        HWND_BOTTOM,
                        0,
                        0,
                        0,
                        0,
                        (uint)(SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE));
                    return true;
                },
                IntPtr.Zero);

            // Force overlay to top of compositor host z-order, mpv surface beneath it.
            _ = NativeSetWindowPos(
                mpvSurfaceHwnd,
                HWND_BOTTOM,
                0,
                0,
                0,
                0,
                (uint)(SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE));
            _ = NativeSetWindowPos(
                overlayHwnd,
                HWND_TOP,
                0,
                0,
                0,
                0,
                (uint)(SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE));
        }
        catch
        {
        }
    }

    private void TryApplyLayeredOpacityToVideoSubtree()
    {
        try
        {
            if (!mpvSurface.IsHandleCreated)
            {
                return;
            }

            byte alpha = (byte)Math.Round(Math.Max(0f, Math.Min(1f, renderOpacity)) * 255f);
            IntPtr mpvSurfaceHwnd = mpvSurface.Handle;
            if (alpha >= 255)
            {
                _ = EnumChildWindows(
                    mpvSurfaceHwnd,
                    (hwnd, lParam) =>
                    {
                        uint ex = NativeGetWindowLong(hwnd, GWL_EXSTYLE);
                        if ((ex & (uint)WS_EX_LAYERED) != 0)
                        {
                            _ = NativeSetWindowLong(hwnd, GWL_EXSTYLE, ex & ~(uint)WS_EX_LAYERED);
                        }

                        return true;
                    },
                    IntPtr.Zero);
                return;
            }

            _ = EnumChildWindows(
                mpvSurfaceHwnd,
                (hwnd, lParam) =>
                {
                    uint ex = NativeGetWindowLong(hwnd, GWL_EXSTYLE);
                    if ((ex & (uint)WS_EX_LAYERED) == 0)
                    {
                        _ = NativeSetWindowLong(hwnd, GWL_EXSTYLE, ex | (uint)WS_EX_LAYERED);
                    }

                    _ = SetLayeredWindowAttributes(hwnd, 0, alpha, LWA_ALPHA);
                    return true;
                },
                IntPtr.Zero);
        }
        catch
        {
        }
    }

    private void ApplyMpvRuntimeOptions()
    {
        if (mpvHandle == IntPtr.Zero)
        {
            return;
        }

        int volume = Math.Max(0, Math.Min(100, (int)Math.Round(AudioVolume * 100f)));
        float panX = Math.Max(-1f, Math.Min(1f, VideoPanX));
        float panY = Math.Max(-1f, Math.Min(1f, VideoPanY));
        float zoomMpv = Math.Max(0f, Math.Min(1f, VideoZoomExtra)) * 2f;

        ApplyMpvVisualEffects();

        // Cache so we only send mpv `set` commands when something actually changed; otherwise this
        // method would flood mpv (called every refresh tick) and freeze the UI thread.
        bool needsApply = !appliedMpvRuntimeOptions
            || lastAppliedLoopVideo != LoopVideo
            || lastAppliedPlayAudio != PlayAudio
            || lastAppliedVolume != volume
            || lastAppliedHardwareDecoding != UseHardwareDecoding
            || float.IsNaN(lastAppliedVideoPanX) || Math.Abs(lastAppliedVideoPanX - panX) > 0.0005f
            || float.IsNaN(lastAppliedVideoPanY) || Math.Abs(lastAppliedVideoPanY - panY) > 0.0005f
            || float.IsNaN(lastAppliedVideoZoom) || Math.Abs(lastAppliedVideoZoom - zoomMpv) > 0.0005f;

        if (!needsApply)
        {
            return;
        }

        _ = MpvCommand("set", "mute", PlayAudio ? "no" : "yes");
        _ = MpvCommand("set", "volume", volume.ToString(CultureInfo.InvariantCulture));
        _ = MpvCommand("set", "video-sync", "audio");
        if (PlayAudio)
        {
            _ = MpvCommand("set", "audio-buffer", "0.2");
        }

        // "yes"/"no" is accepted on more libmpv builds than "inf" for infinite file looping.
        _ = MpvCommand("set", "loop-file", LoopVideo ? "yes" : "no");

        string hwdecOption = GetEffectiveHwdecOption();
        if (lastAppliedHardwareDecoding != UseHardwareDecoding
            || !string.Equals(lastAppliedHwdecOption, hwdecOption, StringComparison.Ordinal)
            || !appliedMpvRuntimeOptions)
        {
            _ = MpvCommand("set", "hwdec", hwdecOption);
        }

        var inv = CultureInfo.InvariantCulture;
        _ = MpvCommand("set", "video-pan-x", panX.ToString(inv));
        _ = MpvCommand("set", "video-pan-y", panY.ToString(inv));
        _ = MpvCommand("set", "video-zoom", zoomMpv.ToString(inv));

        appliedMpvRuntimeOptions = true;
        lastAppliedLoopVideo = LoopVideo;
        lastAppliedPlayAudio = PlayAudio;
        lastAppliedVolume = volume;
        lastAppliedHardwareDecoding = UseHardwareDecoding;
        lastAppliedHwdecOption = hwdecOption;
        lastAppliedVideoPanX = panX;
        lastAppliedVideoPanY = panY;
        lastAppliedVideoZoom = zoomMpv;
    }

    private void ApplyMpvVisualEffects()
    {
        if (mpvHandle == IntPtr.Zero)
        {
            return;
        }

        float opacity = Math.Max(0f, Math.Min(1f, renderOpacity));
        // Use mpv brightness for dimming when possible. This avoids adding a lavfi color-channel
        // filter for opacity-only changes, which otherwise forces slower filter paths.
        float brightness = (opacity - 1f) * 100f;
        float blurScale = Math.Max(0f, Math.Min(1f, VideoBlurScale));
        float blurDegrees = Math.Max(0f, Math.Min(360f, VideoBlurDegrees));
        string videoFilter = BuildVideoFilterChain(blurScale, VideoBlurType, blurDegrees);
        string hwdecOption = GetEffectiveHwdecOption();
        if (lastAppliedHardwareDecoding != UseHardwareDecoding
            || !string.Equals(lastAppliedHwdecOption, hwdecOption, StringComparison.Ordinal))
        {
            _ = MpvCommand("set", "hwdec", hwdecOption);
            lastAppliedHardwareDecoding = UseHardwareDecoding;
            lastAppliedHwdecOption = hwdecOption;
        }

        if (!float.IsNaN(lastAppliedRenderOpacity)
            && Math.Abs(lastAppliedRenderOpacity - opacity) <= 0.0005f
            && !float.IsNaN(lastAppliedBrightness)
            && Math.Abs(lastAppliedBrightness - brightness) <= 0.05f
            && !float.IsNaN(lastAppliedVideoBlurScale)
            && Math.Abs(lastAppliedVideoBlurScale - blurScale) <= 0.0005f
            && !float.IsNaN(lastAppliedVideoBlurDegrees)
            && Math.Abs(lastAppliedVideoBlurDegrees - blurDegrees) <= 0.0005f
            && lastAppliedVideoBlurType == VideoBlurType
            && string.Equals(lastAppliedVideoFilter, videoFilter, StringComparison.Ordinal))
        {
            return;
        }

        _ = MpvCommand("set", "brightness", brightness.ToString("0.###", CultureInfo.InvariantCulture));
        _ = MpvCommand("set", "vf", videoFilter);

        lastAppliedRenderOpacity = opacity;
        lastAppliedBrightness = brightness;
        lastAppliedVideoBlurScale = blurScale;
        lastAppliedVideoBlurDegrees = blurDegrees;
        lastAppliedVideoBlurType = VideoBlurType;
        lastAppliedVideoFilter = videoFilter;
    }

    private bool HasActiveVisualEffects()
    {
        // Keep hwdec copy-back requirement tied to real blur filters only.
        // Opacity dimming is handled through mpv brightness, which is cheaper than lavfi.
        return VideoBlurScale > 0.0005f;
    }

    private string GetEffectiveHwdecOption()
    {
        if (!UseHardwareDecoding)
        {
            return "no";
        }

        return HasActiveVisualEffects() ? HwdecCopyBackForFilters : HwdecFallbackChain[0];
    }

    private void ResetMpvVisualEffectsCache()
    {
        lastAppliedRenderOpacity = float.NaN;
        lastAppliedBrightness = float.NaN;
        lastAppliedVideoBlurScale = float.NaN;
        lastAppliedVideoBlurDegrees = float.NaN;
        lastAppliedVideoBlurType = VideoBlurType;
        lastAppliedVideoFilter = null;
        lastAppliedHwdecOption = null;
    }

    private static string BuildVideoFilterChain(float blurScale, BackgroundVideoBlurType blurType, float blurDegrees)
    {
        blurScale = Math.Max(0f, Math.Min(1f, blurScale));
        if (blurScale <= 0.0005f)
        {
            return string.Empty;
        }

        var lavfi = new System.Text.StringBuilder();
        AppendBlurFilter(lavfi, blurScale, blurType, blurDegrees);
        if (blurScale > 0.0005f)
        {
            AppendLavfiSeparator(lavfi);
            lavfi.Append("format=rgba");
        }

        return "lavfi=[" + lavfi + "]";
    }

    private static void AppendBlurFilter(System.Text.StringBuilder lavfi, float blurScale, BackgroundVideoBlurType blurType, float blurDegrees)
    {
        if (blurScale <= 0.0005f)
        {
            return;
        }

        AppendLavfiSeparator(lavfi);
        switch (blurType)
        {
            case BackgroundVideoBlurType.Directional:
                float directionalRadius = Math.Max(0.1f, blurScale * 20f);
                lavfi.Append("format=gbrp,dblur=angle=")
                    .Append(blurDegrees.ToString("0.###", CultureInfo.InvariantCulture))
                    .Append(":radius=")
                    .Append(directionalRadius.ToString("0.###", CultureInfo.InvariantCulture));
                break;
            case BackgroundVideoBlurType.Box:
                float boxRadius = Math.Max(1f, blurScale * 20f);
                string boxRadiusText = boxRadius.ToString("0.###", CultureInfo.InvariantCulture);
                lavfi.Append("format=gbrp,boxblur=lr=")
                    .Append(boxRadiusText)
                    .Append(":lp=1:cr=")
                    .Append(boxRadiusText)
                    .Append(":cp=1");
                break;
            case BackgroundVideoBlurType.Rotational:
                lavfi.Append(BuildRotationalBlurFilter(blurScale, blurDegrees));
                break;
            case BackgroundVideoBlurType.Gaussian:
            default:
                float sigma = Math.Max(0.1f, blurScale * 10f);
                lavfi.Append("format=gbrp,gblur=sigma=")
                    .Append(sigma.ToString("0.###", CultureInfo.InvariantCulture));
                break;
        }
    }

    private static void AppendLavfiSeparator(System.Text.StringBuilder lavfi)
    {
        if (lavfi.Length > 0)
        {
            lavfi.Append(',');
        }
    }

    private static string BuildRotationalBlurFilter(float blurScale, float blurDegrees)
    {
        float spreadDegrees = Math.Max(0.1f, blurScale * Math.Max(1f, blurDegrees));
        double spreadRadians = spreadDegrees * Math.PI / 180.0;
        string r1 = spreadRadians.ToString("0.######", CultureInfo.InvariantCulture);
        string r2 = (spreadRadians * 0.5).ToString("0.######", CultureInfo.InvariantCulture);
        return "format=rgba,split=5[o][a][b][c][d];"
            + "[a]rotate=" + r1 + ":ow=iw:oh=ih:c=black@0[a1];"
            + "[b]rotate=-" + r1 + ":ow=iw:oh=ih:c=black@0[b1];"
            + "[c]rotate=" + r2 + ":ow=iw:oh=ih:c=black@0[c1];"
            + "[d]rotate=-" + r2 + ":ow=iw:oh=ih:c=black@0[d1];"
            + "[o][a1]blend=all_mode=average[t1];"
            + "[t1][b1]blend=all_mode=average[t2];"
            + "[t2][c1]blend=all_mode=average[t3];"
            + "[t3][d1]blend=all_mode=average";
    }

    private static string FormatVideoDuration(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0.0)
        {
            return "?";
        }

        TimeSpan duration = TimeSpan.FromSeconds(seconds);
        int fractional = (int)((duration.Ticks % TimeSpan.TicksPerSecond) / 1000);
        if (duration.TotalHours >= 1.0)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}.{3:0000}", (int)duration.TotalHours, duration.Minutes, duration.Seconds, fractional);
        }

        if (duration.TotalMinutes >= 1.0)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}.{2:0000}", (int)duration.TotalMinutes, duration.Seconds, fractional);
        }

        return seconds.ToString("0.0000", CultureInfo.InvariantCulture);
    }

    private int MpvCommand(params string[] args)
    {
        if (mpvHandle == IntPtr.Zero)
        {
            return -1;
        }

        IntPtr argvPtr = IntPtr.Zero;
        IntPtr[] allocatedStrings = new IntPtr[args.Length + 1];
        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                allocatedStrings[i] = Marshal.StringToHGlobalAnsi(args[i]);
            }

            allocatedStrings[args.Length] = IntPtr.Zero;
            int size = IntPtr.Size * allocatedStrings.Length;
            argvPtr = Marshal.AllocHGlobal(size);
            for (int i = 0; i < allocatedStrings.Length; i++)
            {
                Marshal.WriteIntPtr(argvPtr, i * IntPtr.Size, allocatedStrings[i]);
            }

            return mpv_command(mpvHandle, argvPtr);
        }
        catch
        {
            return -1;
        }
        finally
        {
            if (argvPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(argvPtr);
            }

            for (int i = 0; i < allocatedStrings.Length; i++)
            {
                if (allocatedStrings[i] != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(allocatedStrings[i]);
                }
            }
        }
    }

    private bool TryGetPlaybackTimePosSeconds(out double seconds)
    {
        seconds = 0.0;
        if (mpvHandle == IntPtr.Zero)
        {
            return false;
        }

        double propValue = 0.0;
        int result = mpv_get_property(mpvHandle, "time-pos", MpvFormatDouble, ref propValue);
        if (result < 0 || double.IsNaN(propValue) || double.IsInfinity(propValue) || propValue < 0.0)
        {
            return false;
        }

        seconds = propValue;
        return true;
    }

    private bool TryGetMpvStringProperty(string propertyName, out string value)
    {
        value = string.Empty;
        if (mpvHandle == IntPtr.Zero)
        {
            return false;
        }

        IntPtr strPtr = IntPtr.Zero;
        try
        {
            int result = mpv_get_property_string(mpvHandle, propertyName, MpvFormatString, ref strPtr);
            if (result < 0 || strPtr == IntPtr.Zero)
            {
                return false;
            }

            value = PtrToStringUtf8(strPtr);
            return true;
        }
        finally
        {
            if (strPtr != IntPtr.Zero)
            {
                mpv_free(strPtr);
            }
        }
    }

    private static string PtrToStringUtf8(IntPtr nativeUtf8)
    {
        if (nativeUtf8 == IntPtr.Zero)
        {
            return string.Empty;
        }

        int byteLen = 0;
        while (Marshal.ReadByte(nativeUtf8, byteLen) != 0)
        {
            byteLen++;
            if (byteLen > 4096)
            {
                break;
            }
        }

        if (byteLen == 0)
        {
            return string.Empty;
        }

        byte[] buffer = new byte[byteLen];
        Marshal.Copy(nativeUtf8, buffer, 0, byteLen);
        return Encoding.UTF8.GetString(buffer);
    }

    private static bool EnsureLibMpvAvailable(out string error)
    {
        IntPtr module = LoadLibrary("libmpv-2.dll");
        if (module == IntPtr.Zero)
        {
            string localPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libmpv-2.dll");
            error = "libmpv initialization failed: could not load libmpv-2.dll." + Environment.NewLine +
                "Expected in app directory or PATH." + Environment.NewLine +
                "Checked app path: " + localPath;
            return false;
        }

        _ = FreeLibrary(module);
        error = null;
        return true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        IsLoaded = false;
        IsInitialized = false;
        loadedSource = null;

        StopMpvUiPumpTimer();

        try
        {
            if (mpvHandle != IntPtr.Zero)
            {
                mpv_terminate_destroy(mpvHandle);
                mpvHandle = IntPtr.Zero;
            }
        }
        catch
        {
        }

        LastError = null;
    }

    private const int SWP_NOMOVE = 0x0002;
    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_TOP = new(0);
    private static readonly IntPtr HWND_BOTTOM = new(1);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowPos")]
    private static extern bool NativeSetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern uint NativeGetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern uint NativeSetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mpv_create();

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_initialize(IntPtr ctx);

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_set_option_string(IntPtr ctx, string name, string value);

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_command(IntPtr ctx, IntPtr args);

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_get_property(IntPtr ctx, string name, int format, ref double data);

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_get_property")]
    private static extern int mpv_get_property_int64(IntPtr ctx, string name, int format, ref long data);

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_get_property")]
    private static extern int mpv_get_property_string(IntPtr ctx, string name, int format, ref IntPtr data);

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void mpv_free(IntPtr data);

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void mpv_terminate_destroy(IntPtr ctx);

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mpv_wait_event(IntPtr ctx, double timeout);
}
