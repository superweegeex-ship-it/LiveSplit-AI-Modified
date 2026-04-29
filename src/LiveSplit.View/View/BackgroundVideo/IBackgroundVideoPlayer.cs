using System;
using System.Drawing;

using LiveSplit.Model;
using LiveSplit.Options;

namespace LiveSplit.View.BackgroundVideo;

/// <summary>
/// Plays layout background video (mpv readback or mpv wid embed) with shared timer sync hooks.
/// </summary>
internal interface IBackgroundVideoPlayer : IDisposable
{
    bool IsInitialized { get; }

    bool IsLoaded { get; }

    string LastError { get; }

    /// <summary>
    /// When true, the timer uses a native host under a transparent overlay panel so splits paint above the video.
    /// </summary>
    bool UsesEmbeddedNativeCompositor { get; }

    bool UseHardwareDecoding { get; set; }

    bool LoopVideo { get; set; }

    bool PlayAudio { get; set; }

    float AudioVolume { get; set; }

    float VideoPanX { get; set; }

    float VideoPanY { get; set; }

    float VideoZoomExtra { get; set; }

    float VideoBlurScale { get; set; }

    BackgroundVideoBlurType VideoBlurType { get; set; }

    float VideoBlurDegrees { get; set; }

    bool StartVideoWithTimer { get; set; }

    double VideoStartOffsetSeconds { get; set; }

    int MaxPresentFps { get; set; }

    bool TryInitialize();

    bool Load(string source);

    void Stop();

    void SetOpacity(float opacity);

    void UpdatePlacement();

    void NotifyHostClientSize(int w, int h);

    void SyncPresentationWithHost(bool want, int fps);

    void PingRuntimeOptionsToMpv();

    /// <summary>
    /// Sends <c>set loop-file</c> immediately (bypasses runtime-option caching). Call after <see cref="LoopVideo"/> is updated.
    /// </summary>
    void PushLoopFileOptionToMpvNow();

    /// <summary>
    /// Sends <c>set mute</c> and <c>set volume</c> immediately (bypasses runtime-option caching). Call after <see cref="AudioVolume"/> / <see cref="PlayAudio"/> are updated.
    /// </summary>
    void PushAudioVolumeToMpvNow();

    void InvalidateTimerSyncSeekTarget();

    void RunTimerSyncWaitAtBeginning();

    /// <summary>
    /// Timer is not running after a reset while keeping continuous playback: do not seek to the start, resume if appropriate.
    /// </summary>
    void RunTimerSyncNotRunningKeepPlayback();

    void RunTimerSyncEnsurePlayingForRunningPhase();

    void RunTimerSyncPausePlayback();

    /// <summary>
    /// When the run completes and the user does not want the video to pause, keep playback running (no seek).
    /// </summary>
    void RunTimerSyncUnpauseAfterRunCompletes();

    void ExitTimerStartSyncToNormalAutoplay();

    /// <summary>
    /// Periodically compares mpv playback time to the run's wall-clock elapsed time (when timer-start sync is on)
    /// and applies a corrective seek if drift exceeds a threshold. Works for readback and wid backends.
    /// </summary>
    void TickPlaybackRunTimerDriftCorrection(TimerPhase runPhase, TimeSpan runElapsedWallClock);

    void Render(Graphics g, int width, int height);

    string GetDebugOverlayText();

    void NotifyCompositorActivated();

    void NotifyCompositorDeactivated();
}
