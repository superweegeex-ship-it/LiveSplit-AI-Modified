using System;
using System.Drawing;
using System.Windows.Forms;

using LiveSplit.Localization;
using LiveSplit.Options;
using LiveSplit.UI;

namespace LiveSplit.View;

/// <summary>How aggressively to sync the running video player when layout settings change mid-dialog.</summary>
public enum BackgroundVideoLiveApplyScope
{
    /// <summary>Full <c>UpdateBackgroundVideoControl</c> (file, backend, placement, etc.).</summary>
    FullLayoutVideo = 0,

    /// <summary>Only push loop-file (and drift-sync timer eligibility) — no load, no timer-phase storm.</summary>
    MpvLoopOnly = 1,

    /// <summary>Timer-start sync flags + timer phase handler only.</summary>
    TimerStartSyncOnly = 2,

    /// <summary>Push mute + volume to mpv only (e.g. audio volume slider while dialog open).</summary>
    MpvVolumeOnly = 3,

    /// <summary>Push visual-only video effects such as opacity/dimming and blur without reloading media.</summary>
    VideoVisualEffectsOnly = 4,
}

public sealed class BackgroundVideoLiveApplyEventArgs : EventArgs
{
    public BackgroundVideoLiveApplyScope Scope { get; }

    public BackgroundVideoLiveApplyEventArgs(BackgroundVideoLiveApplyScope scope)
    {
        Scope = scope;
    }
}

public partial class LayoutSettingsControl : UserControl
{
    private static string T(string source) => UiLocalizer.Translate(source, LanguageResolver.ResolveCurrentCultureLanguage());

    /// <summary>
    /// Raised when a background/video option changes and the main timer window should sync immediately.
    /// Use <see cref="BackgroundVideoLiveApplyEventArgs.Scope"/> to avoid full mpv refresh for loop / timer sync.
    /// </summary>
    public event EventHandler<BackgroundVideoLiveApplyEventArgs> LiveApplyRequested;

    private bool suppressVideoPanZoomEvents;
    private bool suppressVideoTimerSyncUiEvents;
    private bool updatingBackgroundSettingsUi;
    private bool liveApplyHooksReady;
    private Label lblThinSeparatorThickness;
    private NumericUpDown numThinSeparatorThickness;
    private Label lblSeparatorThickness;
    private NumericUpDown numSeparatorThickness;
    private TableLayoutPanel separatorThicknessPanel;
    private GroupBox grpSeparatorAppearance;
    private Label lblSeparatorFillMode;
    private ComboBox cmbSeparatorFillMode;
    private Label lblSeparatorGradientEnd;
    private Button btnSeparatorGradientEndColor;
    private CheckBox chkSeparatorRgbWave;
    private Label lblSeparatorWaveAxis;
    private RadioButton rdoSeparatorWaveHorizontal;
    private RadioButton rdoSeparatorWaveVertical;
    private Label lblSeparatorWaveSpeed;
    private TrackBar trkSeparatorWaveSpeed;
    private bool suppressSeparatorWaveAxisEvents;
    private bool suppressSeparatorFillModeComboEvents;
    private GroupBox grpVideoBlur;
    private TableLayoutPanel tableVideoBlur;
    private Label lblVideoBlurType;
    private ComboBox cmbVideoBlurType;
    private Label lblVideoBlurScale;
    private TrackBar trkVideoBlurScale;
    private Label lblVideoBlurDegrees;
    private NumericUpDown numVideoBlurDegrees;
    private readonly ToolTip backgroundVideoToolTip = new ToolTip();

    public LayoutSettingsControl()
    {
        InitializeComponent();
        ConfigureAutoSizing();
        EnsureVideoBlurControls();
    }

    public Options.LayoutSettings Settings { get; set; }
    public new ILayout Layout { get; set; }

    private Image originalBackgroundImage { get; set; }

    public string TimerFont => SettingsHelper.FormatFont(Settings.TimerFont);
    public string MainFont => SettingsHelper.FormatFont(Settings.TimesFont);
    public string SplitNamesFont => SettingsHelper.FormatFont(Settings.TextFont);

    public float Opacity { get => Settings.Opacity * 100f; set => Settings.Opacity = value / 100f; }
    public float ImageOpacity { get => Settings.ImageOpacity * 100f; set => Settings.ImageOpacity = value / 100f; }
    public float VideoOpacity { get => Settings.VideoOpacity * 100f; set => Settings.VideoOpacity = value / 100f; }
    public float ImageBlur { get => Settings.ImageBlur * 100f; set => Settings.ImageBlur = value / 100f; }
    public float VideoBlurScale { get => Settings.VideoBlurScale * 100f; set => Settings.VideoBlurScale = value / 100f; }
    public float VideoAudioVolume { get => Settings.VideoAudioVolume * 100f; set => Settings.VideoAudioVolume = value / 100f; }

    public decimal VideoStartOffsetDecimal
    {
        get => (decimal)Settings.VideoStartOffsetSeconds;
        set => Settings.VideoStartOffsetSeconds = (float)value;
    }

    public decimal VideoVolumeReductionPercentDecimal
    {
        get => Math.Max(0m, Math.Min(100m, (decimal)Settings.VideoVolumeReductionPercentWhenRunCompletes));
        set => Settings.VideoVolumeReductionPercentWhenRunCompletes = (float)Math.Max(0m, Math.Min(100m, value));
    }

    public decimal VideoBlurDegreesDecimal
    {
        get => Math.Max(0m, Math.Min(360m, (decimal)Settings.VideoBlurDegrees));
        set => Settings.VideoBlurDegrees = (float)Math.Max(0m, Math.Min(360m, value));
    }

    public decimal ThinSeparatorThicknessDecimal
    {
        get => (decimal)Math.Max(0.1f, Settings.ThinSeparatorThickness);
        set => Settings.ThinSeparatorThickness = (float)value;
    }

    public decimal SeparatorThicknessDecimal
    {
        get => (decimal)Math.Max(0.1f, Settings.SeparatorThickness);
        set => Settings.SeparatorThickness = (float)value;
    }

    public LayoutSettingsControl(Options.LayoutSettings settings, ILayout layout)
    {
        InitializeComponent();
        ConfigureAutoSizing();
        Settings = settings;
        Layout = layout;
        chkBestSegments.DataBindings.Add("Checked", Settings, "ShowBestSegments", false, DataSourceUpdateMode.OnPropertyChanged);
        chkAlwaysOnTop.DataBindings.Add("Checked", Settings, "AlwaysOnTop", false, DataSourceUpdateMode.OnPropertyChanged);
        chkAntiAliasing.DataBindings.Add("Checked", Settings, "AntiAliasing", false, DataSourceUpdateMode.OnPropertyChanged);
        chkDropShadows.DataBindings.Add("Checked", Settings, "DropShadows", false, DataSourceUpdateMode.OnPropertyChanged);
        chkRainbow.DataBindings.Add("Checked", Settings, "UseRainbowColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnTextColor.DataBindings.Add("BackColor", Settings, "TextColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnBackground.DataBindings.Add("BackColor", Settings, "BackgroundColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnBackground2.DataBindings.Add("BackColor", Settings, "BackgroundColor2", false, DataSourceUpdateMode.OnPropertyChanged);
        btnThinSep.DataBindings.Add("BackColor", Settings, "ThinSeparatorsColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnSeparators.DataBindings.Add("BackColor", Settings, "SeparatorsColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnPB.DataBindings.Add("BackColor", Settings, "PersonalBestColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnGlod.DataBindings.Add("BackColor", Settings, "BestSegmentColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnAheadGaining.DataBindings.Add("BackColor", Settings, "AheadGainingTimeColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnAheadLosing.DataBindings.Add("BackColor", Settings, "AheadLosingTimeColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnBehindGaining.DataBindings.Add("BackColor", Settings, "BehindGainingTimeColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnBehindLosing.DataBindings.Add("BackColor", Settings, "BehindLosingTimeColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnNotRunning.DataBindings.Add("BackColor", Settings, "NotRunningColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnPausedColor.DataBindings.Add("BackColor", Settings, "PausedColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnTextOutlineColor.DataBindings.Add("BackColor", Settings, "TextOutlineColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnShadowsColor.DataBindings.Add("BackColor", Settings, "ShadowsColor", false, DataSourceUpdateMode.OnPropertyChanged);
        lblTimer.DataBindings.Add("Text", this, "TimerFont", false, DataSourceUpdateMode.OnPropertyChanged);
        lblText.DataBindings.Add("Text", this, "SplitNamesFont", false, DataSourceUpdateMode.OnPropertyChanged);
        lblTimes.DataBindings.Add("Text", this, "MainFont", false, DataSourceUpdateMode.OnPropertyChanged);
        trkOpacity.DataBindings.Add("Value", this, "Opacity", false, DataSourceUpdateMode.OnPropertyChanged);
        chkMousePassThroughWhileRunning.DataBindings.Add("Checked", Settings, "MousePassThroughWhileRunning", false, DataSourceUpdateMode.OnPropertyChanged);
        chkAllowResizing.DataBindings.Add("Checked", Settings, "AllowResizing", false, DataSourceUpdateMode.OnPropertyChanged);
        chkAllowMoving.DataBindings.Add("Checked", Settings, "AllowMoving", false, DataSourceUpdateMode.OnPropertyChanged);
        chkUseHardwareVideoDecoding.DataBindings.Add("Checked", Settings, "UseHardwareVideoDecoding", false, DataSourceUpdateMode.OnPropertyChanged);
        chkLoopVideo.DataBindings.Add("Checked", Settings, "LoopVideo", false, DataSourceUpdateMode.OnPropertyChanged);
        chkPlayVideoAudio.DataBindings.Add("Checked", Settings, "PlayVideoAudio", false, DataSourceUpdateMode.OnPropertyChanged);
        chkVideoStartWithTimer.DataBindings.Add("Checked", Settings, "VideoStartWithTimer", false, DataSourceUpdateMode.OnPropertyChanged);
        chkVideoKeepPlaybackAcrossTimerResets.DataBindings.Add("Checked", Settings, "VideoKeepPlaybackAcrossTimerResets", false, DataSourceUpdateMode.OnPropertyChanged);
        chkVideoPauseWhenRunCompletes.DataBindings.Add("Checked", Settings, "VideoPauseWhenRunCompletes", false, DataSourceUpdateMode.OnPropertyChanged);
        numVideoStartOffsetSeconds.DataBindings.Add("Value", this, nameof(VideoStartOffsetDecimal), true, DataSourceUpdateMode.OnPropertyChanged);
        numVideoVolumeReductionAfterRunCompletes.DataBindings.Add("Value", this, nameof(VideoVolumeReductionPercentDecimal), true, DataSourceUpdateMode.OnPropertyChanged);
        chkVideoStartWithTimer.CheckedChanged += VideoTimerSyncOptions_Changed;
        chkVideoKeepPlaybackAcrossTimerResets.CheckedChanged += VideoTimerSyncOptions_Changed;
        chkVideoPauseWhenRunCompletes.CheckedChanged += VideoTimerSyncOptions_Changed;
        numVideoStartOffsetSeconds.ValueChanged += VideoStartOffsetNud_LiveApply;
        numVideoVolumeReductionAfterRunCompletes.ValueChanged += VideoVolumeReductionAfterRun_LiveApply;
        chkLoopVideo.CheckedChanged += VideoLoopOption_LiveApply;
        chkPlayVideoAudio.CheckedChanged += VideoOrBackgroundLiveOption_Changed;
        chkUseHardwareVideoDecoding.CheckedChanged += VideoOrBackgroundLiveOption_Changed;
        trkImageOpacity.ValueChanged += VideoOpacityTrack_LiveApply;
        trkBlur.Scroll += TrkBlur_VideoVolumeLiveApply;
        trkBlur.ValueChanged += TrkBlur_VideoVolumeLiveApply;
        EnsureSeparatorThicknessControls();
        EnsureSeparatorAppearanceControls();
        EnsureSeparatorOutlineControls();
        EnsureVideoBlurControls();
        trkVideoBlurScale.DataBindings.Add("Value", this, nameof(VideoBlurScale), false, DataSourceUpdateMode.OnPropertyChanged);
        numVideoBlurDegrees.DataBindings.Add("Value", this, nameof(VideoBlurDegreesDecimal), true, DataSourceUpdateMode.OnPropertyChanged);
        cmbVideoBlurType.SelectedIndexChanged += VideoBlurControls_LiveApply;
        trkVideoBlurScale.ValueChanged += VideoBlurControls_LiveApply;
        numVideoBlurDegrees.ValueChanged += VideoBlurControls_LiveApply;
        numThinSeparatorThickness.DataBindings.Add("Value", this, nameof(ThinSeparatorThicknessDecimal), true, DataSourceUpdateMode.OnPropertyChanged);
        numSeparatorThickness.DataBindings.Add("Value", this, nameof(SeparatorThicknessDecimal), true, DataSourceUpdateMode.OnPropertyChanged);
        trkImageOpacity.DataBindings.Add("Value", this, "ImageOpacity", false, DataSourceUpdateMode.OnPropertyChanged);
        trkVideoPanX.ValueChanged += VideoPanZoomTrackbars_ValueChanged;
        trkVideoPanY.ValueChanged += VideoPanZoomTrackbars_ValueChanged;
        trkVideoZoom.ValueChanged += VideoPanZoomTrackbars_ValueChanged;

        cmbBackgroundType.SelectedItem = GetBackgroundTypeString(Settings.BackgroundType);
        originalBackgroundImage = Settings.BackgroundImage;
        cmbGradientType_SelectedIndexChanged(null, EventArgs.Empty);
        EnsureLayoutShadowControls();
        liveApplyHooksReady = true;
    }

    private void ConfigureAutoSizing()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(7);
        tableLayoutPanel5.AutoSize = true;
        tableLayoutPanel5.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        tableLayoutPanel5.Dock = DockStyle.Top;
    }

    private void NotifyLiveApplyRequested(BackgroundVideoLiveApplyScope scope = BackgroundVideoLiveApplyScope.FullLayoutVideo)
    {
        if (!liveApplyHooksReady)
        {
            return;
        }

        LiveApplyRequested?.Invoke(this, new BackgroundVideoLiveApplyEventArgs(scope));
    }

    /// <summary>
    /// Queues work after the current message so WinForms can finish writing bound controls into
    /// <see cref="Settings"/>. Uses <see cref="Form"/> marshaling when this <see cref="UserControl"/>
    /// has no handle yet (BeginInvoke on self throws).
    /// </summary>
    private void DeferAfterDataBindingCommit(Action action)
    {
        if (IsDisposed)
        {
            return;
        }

        void Run()
        {
            if (IsDisposed)
            {
                return;
            }

            action();
        }

        Control root = FindForm();
        if (root == null || !root.IsHandleCreated)
        {
            root = this;
        }

        if (root.IsHandleCreated)
        {
            try
            {
                root.BeginInvoke(new Action(Run));
            }
            catch (InvalidOperationException)
            {
                Run();
            }

            return;
        }

        void OnHostHandleCreated(object sender, EventArgs e)
        {
            root.HandleCreated -= OnHostHandleCreated;
            if (root.IsDisposed)
            {
                return;
            }

            try
            {
                if (root.IsHandleCreated)
                {
                    root.BeginInvoke(new Action(Run));
                }
                else
                {
                    Run();
                }
            }
            catch (InvalidOperationException)
            {
                Run();
            }
        }

        root.HandleCreated += OnHostHandleCreated;
    }

    private void VideoLoopOption_LiveApply(object sender, EventArgs e)
    {
        if (updatingBackgroundSettingsUi || Settings == null || !liveApplyHooksReady)
        {
            return;
        }

        // CheckedChanged can run before WinForms writes the checkbox into Layout.Settings; defer so
        // TimerForm reads the committed LoopVideo value (fixes loop still active at EOF while dialog open).
        DeferAfterDataBindingCommit(() =>
        {
            if (IsDisposed || updatingBackgroundSettingsUi || Settings == null || !liveApplyHooksReady)
            {
                return;
            }

            NotifyLiveApplyRequested(BackgroundVideoLiveApplyScope.MpvLoopOnly);
        });
    }

    private void VideoVolumeReductionAfterRun_LiveApply(object sender, EventArgs e)
    {
        if (suppressVideoTimerSyncUiEvents || updatingBackgroundSettingsUi || Settings == null || !liveApplyHooksReady)
        {
            return;
        }

        if (cmbBackgroundType.SelectedItem?.ToString() != "Video")
        {
            return;
        }

        DeferAfterDataBindingCommit(() =>
        {
            if (IsDisposed || suppressVideoTimerSyncUiEvents || updatingBackgroundSettingsUi || Settings == null || !liveApplyHooksReady)
            {
                return;
            }

            if (cmbBackgroundType.SelectedItem?.ToString() != "Video")
            {
                return;
            }

            NotifyLiveApplyRequested(BackgroundVideoLiveApplyScope.TimerStartSyncOnly);
        });
    }

    private void VideoStartOffsetNud_LiveApply(object sender, EventArgs e)
    {
        if (suppressVideoTimerSyncUiEvents || updatingBackgroundSettingsUi || Settings == null || !liveApplyHooksReady)
        {
            return;
        }

        if (cmbBackgroundType.SelectedItem?.ToString() != "Video")
        {
            return;
        }

        DeferAfterDataBindingCommit(() =>
        {
            if (IsDisposed || suppressVideoTimerSyncUiEvents || updatingBackgroundSettingsUi || Settings == null || !liveApplyHooksReady)
            {
                return;
            }

            if (cmbBackgroundType.SelectedItem?.ToString() != "Video")
            {
                return;
            }

            NotifyLiveApplyRequested(BackgroundVideoLiveApplyScope.TimerStartSyncOnly);
        });
    }

    private void VideoOrBackgroundLiveOption_Changed(object sender, EventArgs e)
    {
        if (updatingBackgroundSettingsUi || Settings == null || !liveApplyHooksReady)
        {
            return;
        }

        // Defer full apply so CheckBox → Settings binding has committed (same handle issue as loop).
        DeferAfterDataBindingCommit(() =>
        {
            if (IsDisposed || updatingBackgroundSettingsUi || Settings == null || !liveApplyHooksReady)
            {
                return;
            }

            NotifyLiveApplyRequested();
        });
    }

    private void VideoOpacityTrack_LiveApply(object sender, EventArgs e)
    {
        if (updatingBackgroundSettingsUi || Settings == null || !liveApplyHooksReady)
        {
            return;
        }

        if (cmbBackgroundType.SelectedItem?.ToString() == "Video")
        {
            try
            {
                foreach (Binding b in trkImageOpacity.DataBindings)
                {
                    b.WriteValue();
                }
            }
            catch
            {
            }

            Settings.VideoOpacity = Math.Max(0f, Math.Min(1f, trkImageOpacity.Value / 100f));
            NotifyLiveApplyRequested(BackgroundVideoLiveApplyScope.VideoVisualEffectsOnly);
        }
    }

    private void TrkBlur_VideoVolumeLiveApply(object sender, EventArgs e)
    {
        if (updatingBackgroundSettingsUi || Settings == null || !liveApplyHooksReady)
        {
            return;
        }

        if (cmbBackgroundType.SelectedItem?.ToString() != "Video")
        {
            return;
        }

        // TrackBar (int) → wrapper property → Settings often does not commit to the datasource on
        // every tick; WinForms may defer writes until validation or dialog OK. Flush the binding and
        // set Settings explicitly so TimerForm reads the real slider value while dragging.
        try
        {
            foreach (Binding b in trkBlur.DataBindings)
            {
                b.WriteValue();
            }
        }
        catch
        {
            // Binding may be mid-refresh when switching background type; fall through to direct write.
        }

        Settings.VideoAudioVolume = Math.Max(0f, Math.Min(1f, trkBlur.Value / 100f));
        NotifyLiveApplyRequested(BackgroundVideoLiveApplyScope.MpvVolumeOnly);
    }

    private void VideoBlurControls_LiveApply(object sender, EventArgs e)
    {
        if (updatingBackgroundSettingsUi || Settings == null || !liveApplyHooksReady)
        {
            return;
        }

        if (cmbBackgroundType.SelectedItem?.ToString() != "Video")
        {
            return;
        }

        Settings.VideoBlurScale = Math.Max(0f, Math.Min(1f, trkVideoBlurScale.Value / 100f));
        Settings.VideoBlurType = cmbVideoBlurType.SelectedIndex switch
        {
            1 => BackgroundVideoBlurType.Directional,
            2 => BackgroundVideoBlurType.Box,
            3 => BackgroundVideoBlurType.Rotational,
            _ => BackgroundVideoBlurType.Gaussian,
        };
        Settings.VideoBlurDegrees = (float)Math.Max(0m, Math.Min(360m, numVideoBlurDegrees.Value));
        UpdateVideoBlurDegreeControlsEnabled();
        NotifyLiveApplyRequested(BackgroundVideoLiveApplyScope.VideoVisualEffectsOnly);
    }

    private void UpdateLayoutOpacityControlsEnabled()
    {
        label13.Enabled = true;
        trkOpacity.Enabled = true;
        label13.Visible = true;
        trkOpacity.Visible = true;
    }

    private void EnsureSeparatorThicknessControls()
    {
        if (numThinSeparatorThickness != null)
        {
            return;
        }

        tableLayoutPanel5.SuspendLayout();
        try
        {
            tableLayoutPanel5.RowCount += 1;
            tableLayoutPanel5.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            lblThinSeparatorThickness = new Label
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = true,
                Text = T("Thin Separator Thickness:")
            };

            numThinSeparatorThickness = new NumericUpDown
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                DecimalPlaces = 1,
                Increment = 0.1M,
                Minimum = 0.1M,
                Maximum = 20M,
                Margin = new Padding(7, 3, 3, 3)
            };

            lblSeparatorThickness = new Label
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = true,
                Text = T("Separator Thickness:")
            };

            numSeparatorThickness = new NumericUpDown
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                DecimalPlaces = 1,
                Increment = 0.1M,
                Minimum = 0.1M,
                Maximum = 20M,
                Margin = new Padding(7, 3, 3, 3)
            };

            separatorThicknessPanel = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                // Top (not Fill): in an AutoSize table row, Dock Fill often collapses nested rows to zero height.
                Dock = DockStyle.Top,
                ColumnCount = 4,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            separatorThicknessPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));
            separatorThicknessPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));
            separatorThicknessPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));
            separatorThicknessPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));
            separatorThicknessPanel.Controls.Add(lblThinSeparatorThickness, 0, 0);
            separatorThicknessPanel.Controls.Add(numThinSeparatorThickness, 1, 0);
            separatorThicknessPanel.Controls.Add(lblSeparatorThickness, 2, 0);
            separatorThicknessPanel.Controls.Add(numSeparatorThickness, 3, 0);

            int row = tableLayoutPanel5.RowCount - 1;
            tableLayoutPanel5.Controls.Add(separatorThicknessPanel, 0, row);
            tableLayoutPanel5.SetColumnSpan(separatorThicknessPanel, 3);
        }
        finally
        {
            tableLayoutPanel5.ResumeLayout();
        }
    }

    private void EnsureSeparatorAppearanceControls()
    {
        if (grpSeparatorAppearance != null || separatorThicknessPanel == null)
        {
            return;
        }

        separatorThicknessPanel.SuspendLayout();
        try
        {
            separatorThicknessPanel.RowCount += 1;
            separatorThicknessPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            lblSeparatorFillMode = new Label
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = true,
                Text = T("Separator fill:")
            };

            cmbSeparatorFillMode = new ComboBox
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FormattingEnabled = false
            };

            foreach (string name in Enum.GetNames(typeof(CurrentSplitOutlineFillMode)))
            {
                cmbSeparatorFillMode.Items.Add(name);
            }

            lblSeparatorGradientEnd = new Label
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = true,
                Text = T("2nd color:")
            };

            btnSeparatorGradientEndColor = new Button
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                FlatStyle = FlatStyle.Popup,
                UseVisualStyleBackColor = false
            };

            chkSeparatorRgbWave = new CheckBox
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = true,
                Text = T("RGB wave (overrides two-color gradient)")
            };

            lblSeparatorWaveAxis = new Label
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = true,
                Text = T("Wave / gradient axis:")
            };

            rdoSeparatorWaveHorizontal = new RadioButton
            {
                Anchor = AnchorStyles.Left,
                AutoSize = true,
                Text = T("Horizontal")
            };

            rdoSeparatorWaveVertical = new RadioButton
            {
                Anchor = AnchorStyles.Left,
                AutoSize = true,
                Text = T("Vertical")
            };

            lblSeparatorWaveSpeed = new Label
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = true,
                Text = T("Wave speed:")
            };

            trkSeparatorWaveSpeed = new TrackBar
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Minimum = 0,
                Maximum = CurrentSplitOutlinePaint.OutlineWaveSpeedSliderMaximum,
                TickFrequency = 5
            };

            grpSeparatorAppearance = new GroupBox
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                Padding = new Padding(8),
                Text = T("Separator gradient & wave")
            };

            var inner = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                ColumnCount = 2,
                Padding = new Padding(0)
            };
            inner.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160F));
            inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            inner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            inner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            inner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            inner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            inner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            inner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            inner.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            inner.Controls.Add(lblSeparatorFillMode, 0, 0);
            inner.Controls.Add(cmbSeparatorFillMode, 1, 0);
            inner.Controls.Add(lblSeparatorGradientEnd, 0, 1);
            inner.Controls.Add(btnSeparatorGradientEndColor, 1, 1);
            inner.Controls.Add(chkSeparatorRgbWave, 0, 2);
            inner.SetColumnSpan(chkSeparatorRgbWave, 2);
            inner.Controls.Add(lblSeparatorWaveAxis, 0, 3);
            var axisPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0)
            };
            axisPanel.Controls.Add(rdoSeparatorWaveHorizontal);
            axisPanel.Controls.Add(rdoSeparatorWaveVertical);
            inner.Controls.Add(axisPanel, 1, 3);
            inner.Controls.Add(lblSeparatorWaveSpeed, 0, 4);
            inner.Controls.Add(trkSeparatorWaveSpeed, 1, 4);

            grpSeparatorAppearance.Controls.Add(inner);
            separatorThicknessPanel.Controls.Add(grpSeparatorAppearance, 0, 1);
            separatorThicknessPanel.SetColumnSpan(grpSeparatorAppearance, 4);

            if (Settings.SeparatorWaveSpeed > CurrentSplitOutlinePaint.OutlineWaveSpeedSliderMaximum)
            {
                Settings.SeparatorWaveSpeed = CurrentSplitOutlinePaint.OutlineWaveSpeedSliderMaximum;
            }

            btnSeparatorGradientEndColor.Click += ColorButtonClick;
            btnSeparatorGradientEndColor.DataBindings.Add("BackColor", Settings, nameof(LiveSplit.Options.LayoutSettings.SeparatorGradientEndColor), false, DataSourceUpdateMode.OnPropertyChanged);
            chkSeparatorRgbWave.DataBindings.Add("Checked", Settings, nameof(LiveSplit.Options.LayoutSettings.SeparatorRgbWave), false, DataSourceUpdateMode.OnPropertyChanged);
            trkSeparatorWaveSpeed.DataBindings.Add("Value", Settings, nameof(LiveSplit.Options.LayoutSettings.SeparatorWaveSpeed), false, DataSourceUpdateMode.OnPropertyChanged);

            cmbSeparatorFillMode.SelectedIndexChanged += SeparatorFillModeCombo_SelectedIndexChanged;
            chkSeparatorRgbWave.CheckedChanged += SeparatorAppearance_LiveApply;
            btnSeparatorGradientEndColor.BackColorChanged += SeparatorAppearance_LiveApply;
            trkSeparatorWaveSpeed.Scroll += SeparatorAppearance_LiveApply;
            trkSeparatorWaveSpeed.ValueChanged += SeparatorAppearance_LiveApply;
            rdoSeparatorWaveHorizontal.CheckedChanged += SeparatorWaveAxis_CheckedChanged;
            rdoSeparatorWaveVertical.CheckedChanged += SeparatorWaveAxis_CheckedChanged;

            SyncSeparatorFillModeComboFromSettings();
            SyncSeparatorWaveAxisRadiosFromSettings();
            UpdateSeparatorAppearanceOptionStates();
        }
        finally
        {
            separatorThicknessPanel.ResumeLayout();
        }
    }

    private void SeparatorFillModeCombo_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (suppressSeparatorFillModeComboEvents || cmbSeparatorFillMode?.SelectedItem is not string name)
        {
            return;
        }

        try
        {
            Settings.SeparatorFillMode = (CurrentSplitOutlineFillMode)Enum.Parse(typeof(CurrentSplitOutlineFillMode), name, true);
        }
        catch (ArgumentException)
        {
            Settings.SeparatorFillMode = CurrentSplitOutlineFillMode.Solid;
        }

        SyncSeparatorFillModeComboFromSettings();
        UpdateSeparatorAppearanceOptionStates();
        NotifyLiveApplyRequested();
    }

    private void SyncSeparatorFillModeComboFromSettings()
    {
        if (cmbSeparatorFillMode == null)
        {
            return;
        }

        string canonical = Settings.SeparatorFillMode.ToString();
        int idx = cmbSeparatorFillMode.FindStringExact(canonical);
        if (idx < 0)
        {
            idx = 0;
        }

        if (cmbSeparatorFillMode.SelectedIndex == idx)
        {
            return;
        }

        suppressSeparatorFillModeComboEvents = true;
        try
        {
            cmbSeparatorFillMode.SelectedIndex = idx;
        }
        finally
        {
            suppressSeparatorFillModeComboEvents = false;
        }
    }

    private void SeparatorWaveAxis_CheckedChanged(object sender, EventArgs e)
    {
        if (suppressSeparatorWaveAxisEvents)
        {
            return;
        }

        if (!rdoSeparatorWaveHorizontal.Checked && !rdoSeparatorWaveVertical.Checked)
        {
            return;
        }

        Settings.SeparatorWaveAxis = rdoSeparatorWaveVertical.Checked
            ? CurrentSplitOutlineWaveAxis.Vertical
            : CurrentSplitOutlineWaveAxis.Horizontal;
        NotifyLiveApplyRequested();
    }

    private void SyncSeparatorWaveAxisRadiosFromSettings()
    {
        if (rdoSeparatorWaveHorizontal == null || rdoSeparatorWaveVertical == null)
        {
            return;
        }

        suppressSeparatorWaveAxisEvents = true;
        try
        {
            if (Settings.SeparatorWaveAxis == CurrentSplitOutlineWaveAxis.Vertical)
            {
                rdoSeparatorWaveVertical.Checked = true;
            }
            else
            {
                rdoSeparatorWaveHorizontal.Checked = true;
            }
        }
        finally
        {
            suppressSeparatorWaveAxisEvents = false;
        }
    }

    private void SeparatorAppearance_LiveApply(object sender, EventArgs e)
    {
        UpdateSeparatorAppearanceOptionStates();
        NotifyLiveApplyRequested();
    }

    private void UpdateSeparatorAppearanceOptionStates()
    {
        if (lblSeparatorGradientEnd == null)
        {
            return;
        }

        bool gradient = Settings.SeparatorFillMode == CurrentSplitOutlineFillMode.Gradient;
        bool waveControlsOn = Settings.SeparatorRgbWave || gradient;
        // Match splits current-split outline: keep 2nd color editable in gradient mode (RGB wave overrides at draw time).
        lblSeparatorGradientEnd.Enabled = btnSeparatorGradientEndColor.Enabled = gradient;
        lblSeparatorWaveAxis.Enabled = rdoSeparatorWaveHorizontal.Enabled = rdoSeparatorWaveVertical.Enabled = waveControlsOn;
        lblSeparatorWaveSpeed.Enabled = trkSeparatorWaveSpeed.Enabled = waveControlsOn;
        UpdateSeparatorOutlineOptionStates();
    }

    private void EnsureVideoBlurControls()
    {
        if (grpVideoBlur != null)
        {
            return;
        }

        lblVideoBlurType = new Label
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            AutoSize = true,
            Text = T("Blur type:")
        };
        cmbVideoBlurType = new ComboBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        cmbVideoBlurType.Items.AddRange(new object[]
        {
            T("Gaussian"),
            T("Directional"),
            T("Box"),
            T("Rotational")
        });

        lblVideoBlurScale = new Label
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            AutoSize = true,
            Text = T("Blur scale:")
        };
        trkVideoBlurScale = new TrackBar
        {
            Dock = DockStyle.Fill,
            Maximum = 100,
            TickStyle = TickStyle.None
        };

        lblVideoBlurDegrees = new Label
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            AutoSize = true,
            Text = T("Degrees:")
        };
        numVideoBlurDegrees = new NumericUpDown
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            DecimalPlaces = 1,
            Increment = 1M,
            Minimum = 0M,
            Maximum = 360M,
            Margin = new Padding(3, 3, 3, 3)
        };

        tableVideoBlur = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            RowCount = 3
        };
        tableVideoBlur.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 91F));
        tableVideoBlur.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tableVideoBlur.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tableVideoBlur.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tableVideoBlur.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tableVideoBlur.Controls.Add(lblVideoBlurType, 0, 0);
        tableVideoBlur.Controls.Add(cmbVideoBlurType, 1, 0);
        tableVideoBlur.Controls.Add(lblVideoBlurScale, 0, 1);
        tableVideoBlur.Controls.Add(trkVideoBlurScale, 1, 1);
        tableVideoBlur.Controls.Add(lblVideoBlurDegrees, 0, 2);
        tableVideoBlur.Controls.Add(numVideoBlurDegrees, 1, 2);

        grpVideoBlur = new GroupBox
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Text = T("Video blur"),
            Visible = false
        };
        grpVideoBlur.Controls.Add(tableVideoBlur);

        tableLayoutPanel5.SuspendLayout();
        tableLayoutPanel3.SuspendLayout();
        try
        {
            int row = tableLayoutPanel3.RowCount;
            tableLayoutPanel3.RowCount = row + 1;
            tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tableLayoutPanel3.Controls.Add(grpVideoBlur, 0, row);
            tableLayoutPanel3.SetColumnSpan(grpVideoBlur, 6);
        }
        finally
        {
            tableLayoutPanel3.ResumeLayout();
            tableLayoutPanel5.ResumeLayout();
        }
    }

    private string GetBackgroundTypeString(BackgroundType type)
    {
        return type switch
        {
            BackgroundType.HorizontalGradient => "Horizontal Gradient",
            BackgroundType.VerticalGradient => "Vertical Gradient",
            BackgroundType.AnimatedImage => "Animated Image",
            BackgroundType.Video => "Video",
            BackgroundType.Image => "Image",
            _ => "Solid Color",
        };
    }

    private void cmbGradientType_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (updatingBackgroundSettingsUi)
        {
            return;
        }

        updatingBackgroundSettingsUi = true;
        try
        {
        BackgroundType previousBackgroundType = Settings.BackgroundType;
        string selectedItem = cmbBackgroundType.SelectedItem.ToString();
        bool imageBackground = selectedItem is "Image" or "Animated Image";
        bool videoBackground = selectedItem == "Video";
        trkImageOpacity.DataBindings.Clear();
        trkImageOpacity.DataBindings.Add(
            "Value",
            this,
            videoBackground ? "VideoOpacity" : "ImageOpacity",
            false,
            DataSourceUpdateMode.OnPropertyChanged);
        btnBackground.Visible = selectedItem != "Solid Color" && !imageBackground && !videoBackground;
        btnBackground2.DataBindings.Clear();
        lblImageOpacity.Enabled = trkImageOpacity.Enabled = imageBackground || videoBackground;
        lblImageOpacity.Text = videoBackground ? T("Video Opacity:") : T("Image Opacity:");
        trkBlur.DataBindings.Clear();
        chkUseHardwareVideoDecoding.Enabled = videoBackground;
        chkLoopVideo.Enabled = videoBackground;
        chkPlayVideoAudio.Enabled = videoBackground;
        grpVideoTimerSync.Visible = videoBackground;
        grpVideoTimerSync.Enabled = videoBackground;
        grpWhenRunCompletes.Visible = videoBackground;
        grpWhenRunCompletes.Enabled = videoBackground;
        if (videoBackground)
        {
            grpVideoTimerSync.Text = T("Video with run timer");
            grpWhenRunCompletes.Text = T("When run completes");
            grpVideoBlur.Text = T("Video blur");
            chkVideoStartWithTimer.Text = T("Start background video when timer starts");
            chkVideoKeepPlaybackAcrossTimerResets.Text = T("Keep video playing across timer resets (no restart at file start)");
            chkVideoPauseWhenRunCompletes.Text = T("Pause video when run completes");
            lblVideoVolumeReductionAfterRunCompletes.Text = T("Lower volume after final split (%):");
            lblVideoStartOffset.Text = T("Video start offset (seconds):");
            lblVideoBlurType.Text = T("Blur type:");
            lblVideoBlurScale.Text = T("Blur scale:");
            lblVideoBlurDegrees.Text = T("Degrees:");
            SyncVideoBlurControlsFromSettings();
        }

        grpVideoBlur.Visible = videoBackground;
        grpVideoBlur.Enabled = videoBackground;

        bool showBackgroundPanZoom = videoBackground || imageBackground;
        lblVideoPanX.Visible = lblVideoPanY.Visible = lblVideoZoom.Visible =
            trkVideoPanX.Visible = trkVideoPanY.Visible = trkVideoZoom.Visible = showBackgroundPanZoom;
        lblVideoPanX.Enabled = lblVideoPanY.Enabled = lblVideoZoom.Enabled =
            trkVideoPanX.Enabled = trkVideoPanY.Enabled = trkVideoZoom.Enabled = showBackgroundPanZoom;
        if (showBackgroundPanZoom)
        {
            if (videoBackground)
            {
                lblVideoPanX.Text = T("Video Pan X:");
                lblVideoPanY.Text = T("Video Pan Y:");
                lblVideoZoom.Text = T("Video Zoom:");
            }
            else
            {
                lblVideoPanX.Text = T("Image pan X:");
                lblVideoPanY.Text = T("Image pan Y:");
                lblVideoZoom.Text = T("Image zoom:");
            }

            SyncVideoPanZoomTrackbarsFromSettings();
        }

        UpdateLayoutOpacityControlsEnabled();

        bool supportsBlur = selectedItem == "Image";
        bool supportsVideoVolume = videoBackground;
        lblBlur.Enabled = trkBlur.Enabled = supportsBlur || supportsVideoVolume;
        lblBlur.Text = supportsVideoVolume ? T("Audio Volume:") : T("Image Blur:");
        if (supportsVideoVolume)
        {
            trkBlur.DataBindings.Add("Value", this, "VideoAudioVolume", false, DataSourceUpdateMode.OnPropertyChanged);
        }
        else
        {
            trkBlur.DataBindings.Add("Value", this, "ImageBlur", false, DataSourceUpdateMode.OnPropertyChanged);
        }
        if (imageBackground)
        {
            btnBackground2.BackgroundImage = Settings.BackgroundImage;
            btnBackground2.BackColor = Color.Transparent;
            lblBackground.Text = T("Image:");
        }
        else if (videoBackground)
        {
            btnBackground2.BackgroundImage = null;
            btnBackground2.BackColor = SystemColors.ControlDark;
            lblBackground.Text = T("Video:");
        }
        else
        {
            btnBackground2.BackgroundImage = null;
            btnBackground2.DataBindings.Add("BackColor", Settings, btnBackground.Visible ? "BackgroundColor2" : "BackgroundColor", false, DataSourceUpdateMode.OnPropertyChanged);
            lblBackground.Text = T("Color:");
        }

        Settings.BackgroundType = (BackgroundType)Enum.Parse(typeof(BackgroundType), selectedItem.Replace(" ", ""));
        bool backgroundTypeChanged = previousBackgroundType != Settings.BackgroundType;
        if (videoBackground && backgroundTypeChanged)
        {
            suppressVideoTimerSyncUiEvents = true;
            chkVideoStartWithTimer.Checked = Settings.VideoStartWithTimer;
            ClampAndSetVideoStartOffsetNud();
            suppressVideoTimerSyncUiEvents = false;
        }

        UpdateVideoTimerOffsetControlsEnabled(videoBackground);
        NotifyLiveApplyRequested();
        }
        finally
        {
            updatingBackgroundSettingsUi = false;
        }
    }

    private void ClampAndSetVideoStartOffsetNud()
    {
        decimal v = (decimal)Settings.VideoStartOffsetSeconds;
        v = Math.Max(numVideoStartOffsetSeconds.Minimum, Math.Min(numVideoStartOffsetSeconds.Maximum, v));
        numVideoStartOffsetSeconds.Value = v;
    }

    private void SyncVideoPanZoomTrackbarsFromSettings()
    {
        if (Settings == null)
        {
            return;
        }

        string sel = cmbBackgroundType.SelectedItem?.ToString();
        bool useVideo = sel == "Video";

        suppressVideoPanZoomEvents = true;
        try
        {
            if (useVideo)
            {
                trkVideoPanX.Value = Math.Max(trkVideoPanX.Minimum, Math.Min(trkVideoPanX.Maximum, (int)Math.Round(Settings.VideoPanX * 100.0)));
                trkVideoPanY.Value = Math.Max(trkVideoPanY.Minimum, Math.Min(trkVideoPanY.Maximum, (int)Math.Round(Settings.VideoPanY * 100.0)));
                trkVideoZoom.Value = Math.Max(trkVideoZoom.Minimum, Math.Min(trkVideoZoom.Maximum, (int)Math.Round(Settings.VideoZoomExtra * 100.0)));
            }
            else
            {
                trkVideoPanX.Value = Math.Max(trkVideoPanX.Minimum, Math.Min(trkVideoPanX.Maximum, (int)Math.Round(Settings.ImagePanX * 100.0)));
                trkVideoPanY.Value = Math.Max(trkVideoPanY.Minimum, Math.Min(trkVideoPanY.Maximum, (int)Math.Round(Settings.ImagePanY * 100.0)));
                trkVideoZoom.Value = Math.Max(trkVideoZoom.Minimum, Math.Min(trkVideoZoom.Maximum, (int)Math.Round(Settings.ImageZoomExtra * 100.0)));
            }
        }
        finally
        {
            suppressVideoPanZoomEvents = false;
        }
    }

    private void SyncVideoBlurControlsFromSettings()
    {
        if (Settings == null || cmbVideoBlurType == null || trkVideoBlurScale == null)
        {
            return;
        }

        int value = Math.Max(trkVideoBlurScale.Minimum, Math.Min(trkVideoBlurScale.Maximum, (int)Math.Round(Settings.VideoBlurScale * 100.0)));
        trkVideoBlurScale.Value = value;
        cmbVideoBlurType.SelectedIndex = Settings.VideoBlurType switch
        {
            BackgroundVideoBlurType.Directional => 1,
            BackgroundVideoBlurType.Box => 2,
            BackgroundVideoBlurType.Rotational => 3,
            _ => 0,
        };
        numVideoBlurDegrees.Value = Math.Max(numVideoBlurDegrees.Minimum, Math.Min(numVideoBlurDegrees.Maximum, (decimal)Settings.VideoBlurDegrees));
        UpdateVideoBlurDegreeControlsEnabled();
    }

    private void UpdateVideoBlurDegreeControlsEnabled()
    {
        bool usesDegrees = cmbVideoBlurType?.SelectedIndex is 1 or 3;
        if (lblVideoBlurDegrees != null)
        {
            lblVideoBlurDegrees.Enabled = usesDegrees;
        }

        if (numVideoBlurDegrees != null)
        {
            numVideoBlurDegrees.Enabled = usesDegrees;
        }
    }

    private void VideoPanZoomTrackbars_ValueChanged(object sender, EventArgs e)
    {
        if (suppressVideoPanZoomEvents || Settings == null)
        {
            return;
        }

        string sel = cmbBackgroundType.SelectedItem?.ToString();
        if (sel == "Video")
        {
            Settings.VideoPanX = trkVideoPanX.Value / 100f;
            Settings.VideoPanY = trkVideoPanY.Value / 100f;
            Settings.VideoZoomExtra = trkVideoZoom.Value / 100f;
        }
        else if (sel is "Image" or "Animated Image")
        {
            Settings.ImagePanX = trkVideoPanX.Value / 100f;
            Settings.ImagePanY = trkVideoPanY.Value / 100f;
            Settings.ImageZoomExtra = trkVideoZoom.Value / 100f;
        }
        else
        {
            return;
        }

        NotifyLiveApplyRequested();
    }

    private void ColorButtonClick(object sender, EventArgs e)
    {
        SettingsHelper.ColorButtonClick((Button)sender, this);
    }

    private void BackgroundColorButtonClick(object sender, EventArgs e)
    {
        string selectedItem = cmbBackgroundType.SelectedItem.ToString();
        if (selectedItem is "Image" or "Animated Image")
        {
            var dialog = new OpenFileDialog
            {
                Filter = T("Image Files|*.BMP;*.JPG;*.GIF;*.JPEG;*.PNG|All files (*.*)|*.*"),
                Title = T("Set Background Image...")
            };
            DialogResult result = dialog.ShowDialog();
            if (result == DialogResult.OK)
            {
                try
                {
                    var image = Image.FromFile(dialog.FileName);
                    if (Settings.BackgroundImage != null && Settings.BackgroundImage != originalBackgroundImage)
                    {
                        Settings.BackgroundImage.Dispose();
                    }

                    Settings.BackgroundImage = ((Button)sender).BackgroundImage = image;
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                    MessageBox.Show(T("Could not load image!"), T("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
        else if (selectedItem == "Video")
        {
            var dialog = new OpenFileDialog
            {
                Filter = T("Video Files|*.BMP;*.JPG;*.GIF;*.JPEG;*.PNG;*.MP4;*.MKV;*.WEBM;*.AVI;*.MOV;*.M4V;*.WMV|All files (*.*)|*.*"),
                Title = T("Set Background Video...")
            };
            DialogResult result = dialog.ShowDialog();
            if (result == DialogResult.OK)
            {
                Settings.BackgroundVideoInputType = BackgroundVideoInputType.File;
                Settings.BackgroundVideoPath = dialog.FileName;
                Settings.BackgroundVideoSource = dialog.FileName;
                ((Button)sender).BackgroundImage = null;
                ((Button)sender).Text = T("Loaded");
                NotifyLiveApplyRequested();
            }
        }
        else
        {
            SettingsHelper.ColorButtonClick((Button)sender, this);
        }
    }

    private void btnTimer_Click(object sender, EventArgs e)
    {
        // Scale down font in dialog so that size is closer to other font settings, and to allow more granular control over size
        var timerFont = new Font(Settings.TimerFont.FontFamily.Name, Settings.TimerFont.Size / 50f * 18f, Settings.TimerFont.Style, GraphicsUnit.Pixel);
        CustomFontDialog.FontDialog dialog = SettingsHelper.GetFontDialog(timerFont, 7, 20);
        dialog.FontChanged += (s, ev) => updateTimerFont(((CustomFontDialog.FontChangedEventArgs)ev).NewFont);
        dialog.ShowDialog(this);
        lblTimer.Text = TimerFont;
    }

    private void updateTimerFont(Font timerFont)
    {
        // Scale font back up to the size the Timer component expects
        Settings.TimerFont = new Font(timerFont.FontFamily.Name, timerFont.Size / 18f * 50f, timerFont.Style, GraphicsUnit.Pixel);
    }

    private void btnTimes_Click(object sender, EventArgs e)
    {
        CustomFontDialog.FontDialog dialog = SettingsHelper.GetFontDialog(Settings.TimesFont, 11, 26);
        dialog.FontChanged += (s, ev) => Settings.TimesFont = ((CustomFontDialog.FontChangedEventArgs)ev).NewFont;
        dialog.ShowDialog(this);
        lblTimes.Text = MainFont;
    }

    private void btnTextFont_Click(object sender, EventArgs e)
    {
        CustomFontDialog.FontDialog dialog = SettingsHelper.GetFontDialog(Settings.TextFont, 11, 26);
        dialog.FontChanged += (s, ev) => Settings.TextFont = ((CustomFontDialog.FontChangedEventArgs)ev).NewFont;
        dialog.ShowDialog(this);
        lblText.Text = SplitNamesFont;
    }

    private void chkRainbow_CheckedChanged(object sender, EventArgs e)
    {
        label9.Enabled = btnGlod.Enabled = !chkRainbow.Checked;
    }

    private void chkAntiAliasing_CheckedChanged(object sender, EventArgs e)
    {
        lblOutlines.Enabled = btnTextOutlineColor.Enabled = chkAntiAliasing.Checked;
    }

    private void LayoutSettingsControl_Load(object sender, EventArgs e)
    {
        chkRainbow_CheckedChanged(null, null);
        chkAntiAliasing_CheckedChanged(null, null);
    }

    private void VideoTimerSyncOptions_Changed(object sender, EventArgs e)
    {
        if (suppressVideoTimerSyncUiEvents || updatingBackgroundSettingsUi || Settings == null || !liveApplyHooksReady)
        {
            return;
        }

        bool videoBackground = cmbBackgroundType.SelectedItem?.ToString() == "Video";
        UpdateVideoTimerOffsetControlsEnabled(videoBackground);

        DeferAfterDataBindingCommit(() =>
        {
            if (IsDisposed || suppressVideoTimerSyncUiEvents || updatingBackgroundSettingsUi || Settings == null || !liveApplyHooksReady)
            {
                return;
            }

            if (cmbBackgroundType.SelectedItem?.ToString() != "Video")
            {
                return;
            }

            UpdateVideoTimerOffsetControlsEnabled(true);
            NotifyLiveApplyRequested(BackgroundVideoLiveApplyScope.TimerStartSyncOnly);
        });
    }

    private void UpdateVideoTimerOffsetControlsEnabled(bool videoBackground)
    {
        bool timerSync = videoBackground && chkVideoStartWithTimer.Checked;
        bool showOffset = timerSync;
        lblVideoStartOffset.Visible = numVideoStartOffsetSeconds.Visible = videoBackground;
        lblVideoStartOffset.Enabled = numVideoStartOffsetSeconds.Enabled = showOffset;
        chkVideoPauseWhenRunCompletes.Enabled = timerSync;
        lblVideoVolumeReductionAfterRunCompletes.Enabled = timerSync;
        numVideoVolumeReductionAfterRunCompletes.Enabled = timerSync;
    }
}
