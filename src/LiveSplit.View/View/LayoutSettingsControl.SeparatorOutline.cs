using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

using LiveSplit.Options;
using LiveSplit.UI;

namespace LiveSplit.View;

public partial class LayoutSettingsControl
{
    private GroupBox grpSeparatorOutline;
    private CheckBox chkSeparatorOutlineEnabled;
    private Label lblSeparatorOutlineThickness;
    private NumericUpDown numSeparatorOutlineThickness;
    private Label lblSeparatorOutlineColor;
    private Button btnSeparatorOutlineColor;
    private Label lblSeparatorOutlineTransparency;
    private NumericUpDown numSeparatorOutlineTransparency;
    private Label lblSeparatorOutlineFillMode;
    private ComboBox cmbSeparatorOutlineFillMode;
    private Label lblSeparatorOutlineGradientEnd;
    private Button btnSeparatorOutlineGradientEndColor;
    private CheckBox chkSeparatorOutlineRgbWave;
    private Label lblSeparatorOutlineWaveAxis;
    private RadioButton rdoSeparatorOutlineWaveHorizontal;
    private RadioButton rdoSeparatorOutlineWaveVertical;
    private Label lblSeparatorOutlineWaveSpeed;
    private TrackBar trkSeparatorOutlineWaveSpeed;
    private Label lblSeparatorOutlineInterpolation;
    private ComboBox cmbSeparatorOutlineInterpolation;
    private bool suppressSeparatorOutlineFillModeComboEvents;
    private bool suppressSeparatorOutlineWaveAxisEvents;

    private GroupBox grpLayoutShadows;
    private GroupBox grpLayoutIconShadows;
    private GroupBox grpLayoutTextShadows;
    private TrackBar trkLayoutIconShadowOffset;
    private TrackBar trkLayoutIconShadowTransparency;
    private TrackBar trkLayoutIconShadowBlur;
    private TrackBar trkLayoutTextShadowOffset;
    private TrackBar trkLayoutTextShadowTransparency;
    private TrackBar trkLayoutTextShadowBlur;
    private Label lblLayoutIconShadowOffsetValue;
    private Label lblLayoutIconShadowTransparencyValue;
    private Label lblLayoutIconShadowBlurValue;
    private Label lblLayoutTextShadowOffsetValue;
    private Label lblLayoutTextShadowTransparencyValue;
    private Label lblLayoutTextShadowBlurValue;

    public decimal SeparatorOutlineThicknessDecimal
    {
        get => (decimal)Math.Max(0.1f, Settings.SeparatorOutlineThickness);
        set => Settings.SeparatorOutlineThickness = (float)value;
    }

    public decimal SeparatorOutlineTransparencyDecimal
    {
        get => (decimal)Math.Max(0m, Math.Min(100m, (decimal)Settings.SeparatorOutlineTransparency));
        set => Settings.SeparatorOutlineTransparency = (float)Math.Max(0m, Math.Min(100m, value));
    }

    /// <summary>TrackBar value: icon offset in tenths of a pixel (-100..100 => -10.0..10.0 px).</summary>
    public int IconShadowOffsetTenths
    {
        get => (int)Math.Round(Math.Min(100, Math.Max(-100, Settings.IconShadowOffset * 10f)));
        set => Settings.IconShadowOffset = Math.Min(10f, Math.Max(-10f, value / 10f));
    }

    public int IconShadowTransparencyTrack
    {
        get => (int)Math.Round(Math.Min(100f, Math.Max(0f, Settings.IconShadowTransparency)));
        set => Settings.IconShadowTransparency = Math.Min(100f, Math.Max(0f, value));
    }

    public int IconShadowBlurTrack
    {
        get => (int)Math.Round(Math.Min(100f, Math.Max(0f, Settings.IconShadowBlur)));
        set => Settings.IconShadowBlur = Math.Min(100f, Math.Max(0f, value));
    }

    /// <summary>TrackBar value: text shadow SE offset in tenths of a pixel (0..160 => 0.0..16.0 px).</summary>
    public int TextShadowOffsetTenths
    {
        get => (int)Math.Round(Math.Min(160, Math.Max(0, Settings.TextShadowOffset * 10f)));
        set => Settings.TextShadowOffset = Math.Min(16f, Math.Max(0f, value / 10f));
    }

    public int TextShadowTransparencyTrack
    {
        get => (int)Math.Round(Math.Min(100f, Math.Max(0f, Settings.TextShadowTransparency)));
        set => Settings.TextShadowTransparency = Math.Min(100f, Math.Max(0f, value));
    }

    public int TextShadowBlurTrack
    {
        get => (int)Math.Round(Math.Min(100f, Math.Max(0f, Settings.TextShadowBlur)));
        set => Settings.TextShadowBlur = Math.Min(100f, Math.Max(0f, value));
    }

    private void EnsureSeparatorOutlineControls()
    {
        if (grpSeparatorOutline != null || separatorThicknessPanel == null)
        {
            return;
        }

        separatorThicknessPanel.SuspendLayout();
        try
        {
            separatorThicknessPanel.RowCount += 1;
            separatorThicknessPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            chkSeparatorOutlineEnabled = new CheckBox
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = true,
                Text = T("Outline around separator")
            };

            lblSeparatorOutlineThickness = new Label
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = true,
                Text = T("Outline thickness (px):")
            };

            numSeparatorOutlineThickness = new NumericUpDown
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                DecimalPlaces = 1,
                Increment = 0.1M,
                Minimum = 0.1M,
                Maximum = 20M,
                Margin = new Padding(7, 3, 3, 3)
            };

            lblSeparatorOutlineColor = new Label
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = true,
                Text = T("Outline color:")
            };

            btnSeparatorOutlineColor = new Button
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                FlatStyle = FlatStyle.Popup,
                UseVisualStyleBackColor = false
            };

            lblSeparatorOutlineTransparency = new Label
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = true,
                Text = T("Outline transparency (0–100):")
            };

            numSeparatorOutlineTransparency = new NumericUpDown
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                DecimalPlaces = 0,
                Increment = 1M,
                Minimum = 0M,
                Maximum = 100M,
                Margin = new Padding(7, 3, 3, 3)
            };

            lblSeparatorOutlineFillMode = new Label
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = true,
                Text = T("Outline fill:")
            };

            cmbSeparatorOutlineFillMode = new ComboBox
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                DropDownStyle = ComboBoxStyle.DropDownList
            };

            foreach (string name in Enum.GetNames(typeof(CurrentSplitOutlineFillMode)))
            {
                cmbSeparatorOutlineFillMode.Items.Add(name);
            }

            lblSeparatorOutlineGradientEnd = new Label
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = true,
                Text = T("Outline 2nd color:")
            };

            btnSeparatorOutlineGradientEndColor = new Button
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                FlatStyle = FlatStyle.Popup,
                UseVisualStyleBackColor = false
            };

            chkSeparatorOutlineRgbWave = new CheckBox
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = true,
                Text = T("Outline RGB wave")
            };

            lblSeparatorOutlineWaveAxis = new Label
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = true,
                Text = T("Outline wave / gradient axis:")
            };

            rdoSeparatorOutlineWaveHorizontal = new RadioButton
            {
                Anchor = AnchorStyles.Left,
                AutoSize = true,
                Text = T("Horizontal")
            };

            rdoSeparatorOutlineWaveVertical = new RadioButton
            {
                Anchor = AnchorStyles.Left,
                AutoSize = true,
                Text = T("Vertical")
            };

            lblSeparatorOutlineWaveSpeed = new Label
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = true,
                Text = T("Outline wave speed:")
            };

            trkSeparatorOutlineWaveSpeed = new TrackBar
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Minimum = 0,
                Maximum = CurrentSplitOutlinePaint.OutlineWaveSpeedSliderMaximum,
                TickFrequency = 5
            };

            lblSeparatorOutlineInterpolation = new Label
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = true,
                Text = T("Outline edge quality:")
            };

            cmbSeparatorOutlineInterpolation = new ComboBox
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                DropDownStyle = ComboBoxStyle.DropDownList
            };

            foreach (string name in Enum.GetNames(typeof(CurrentSplitImageInterpolationFilter)))
            {
                cmbSeparatorOutlineInterpolation.Items.Add(name);
            }

            grpSeparatorOutline = new GroupBox
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                Padding = new Padding(8),
                Text = T("Separator outline (border)")
            };

            var outlineInner = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                ColumnCount = 2,
                Padding = new Padding(0)
            };
            outlineInner.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180F));
            outlineInner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 10; i++)
            {
                outlineInner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            outlineInner.Controls.Add(chkSeparatorOutlineEnabled, 0, 0);
            outlineInner.SetColumnSpan(chkSeparatorOutlineEnabled, 2);
            outlineInner.Controls.Add(lblSeparatorOutlineThickness, 0, 1);
            outlineInner.Controls.Add(numSeparatorOutlineThickness, 1, 1);
            outlineInner.Controls.Add(lblSeparatorOutlineColor, 0, 2);
            outlineInner.Controls.Add(btnSeparatorOutlineColor, 1, 2);
            outlineInner.Controls.Add(lblSeparatorOutlineTransparency, 0, 3);
            outlineInner.Controls.Add(numSeparatorOutlineTransparency, 1, 3);
            outlineInner.Controls.Add(lblSeparatorOutlineFillMode, 0, 4);
            outlineInner.Controls.Add(cmbSeparatorOutlineFillMode, 1, 4);
            outlineInner.Controls.Add(lblSeparatorOutlineGradientEnd, 0, 5);
            outlineInner.Controls.Add(btnSeparatorOutlineGradientEndColor, 1, 5);
            outlineInner.Controls.Add(chkSeparatorOutlineRgbWave, 0, 6);
            outlineInner.SetColumnSpan(chkSeparatorOutlineRgbWave, 2);
            outlineInner.Controls.Add(lblSeparatorOutlineWaveAxis, 0, 7);
            var outlineAxisPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0)
            };
            outlineAxisPanel.Controls.Add(rdoSeparatorOutlineWaveHorizontal);
            outlineAxisPanel.Controls.Add(rdoSeparatorOutlineWaveVertical);
            outlineInner.Controls.Add(outlineAxisPanel, 1, 7);
            outlineInner.Controls.Add(lblSeparatorOutlineWaveSpeed, 0, 8);
            outlineInner.Controls.Add(trkSeparatorOutlineWaveSpeed, 1, 8);
            outlineInner.Controls.Add(lblSeparatorOutlineInterpolation, 0, 9);
            outlineInner.Controls.Add(cmbSeparatorOutlineInterpolation, 1, 9);

            grpSeparatorOutline.Controls.Add(outlineInner);
            separatorThicknessPanel.Controls.Add(grpSeparatorOutline, 0, 2);
            separatorThicknessPanel.SetColumnSpan(grpSeparatorOutline, 4);

            if (Settings.SeparatorOutlineWaveSpeed > CurrentSplitOutlinePaint.OutlineWaveSpeedSliderMaximum)
            {
                Settings.SeparatorOutlineWaveSpeed = CurrentSplitOutlinePaint.OutlineWaveSpeedSliderMaximum;
            }

            chkSeparatorOutlineEnabled.DataBindings.Add("Checked", Settings, nameof(LiveSplit.Options.LayoutSettings.SeparatorOutlineEnabled), false, DataSourceUpdateMode.OnPropertyChanged);
            numSeparatorOutlineThickness.DataBindings.Add("Value", this, nameof(SeparatorOutlineThicknessDecimal), true, DataSourceUpdateMode.OnPropertyChanged);
            btnSeparatorOutlineColor.Click += ColorButtonClick;
            btnSeparatorOutlineColor.DataBindings.Add("BackColor", Settings, nameof(LiveSplit.Options.LayoutSettings.SeparatorOutlineColor), false, DataSourceUpdateMode.OnPropertyChanged);
            numSeparatorOutlineTransparency.DataBindings.Add("Value", this, nameof(SeparatorOutlineTransparencyDecimal), true, DataSourceUpdateMode.OnPropertyChanged);
            btnSeparatorOutlineGradientEndColor.Click += ColorButtonClick;
            btnSeparatorOutlineGradientEndColor.DataBindings.Add("BackColor", Settings, nameof(LiveSplit.Options.LayoutSettings.SeparatorOutlineGradientEndColor), false, DataSourceUpdateMode.OnPropertyChanged);
            chkSeparatorOutlineRgbWave.DataBindings.Add("Checked", Settings, nameof(LiveSplit.Options.LayoutSettings.SeparatorOutlineRgbWave), false, DataSourceUpdateMode.OnPropertyChanged);
            trkSeparatorOutlineWaveSpeed.DataBindings.Add("Value", Settings, nameof(LiveSplit.Options.LayoutSettings.SeparatorOutlineWaveSpeed), false, DataSourceUpdateMode.OnPropertyChanged);

            cmbSeparatorOutlineFillMode.SelectedIndexChanged += SeparatorOutlineFillModeCombo_SelectedIndexChanged;
            chkSeparatorOutlineRgbWave.CheckedChanged += SeparatorOutline_LiveApply;
            btnSeparatorOutlineGradientEndColor.BackColorChanged += SeparatorOutline_LiveApply;
            trkSeparatorOutlineWaveSpeed.Scroll += SeparatorOutline_LiveApply;
            trkSeparatorOutlineWaveSpeed.ValueChanged += SeparatorOutline_LiveApply;
            rdoSeparatorOutlineWaveHorizontal.CheckedChanged += SeparatorOutlineWaveAxis_CheckedChanged;
            rdoSeparatorOutlineWaveVertical.CheckedChanged += SeparatorOutlineWaveAxis_CheckedChanged;
            cmbSeparatorOutlineInterpolation.SelectedIndexChanged += SeparatorOutlineInterpolation_SelectedIndexChanged;
            chkSeparatorOutlineEnabled.CheckedChanged += (_, _) =>
            {
                UpdateSeparatorOutlineOptionStates();
                NotifyLiveApplyRequested();
            };
            numSeparatorOutlineThickness.ValueChanged += (_, _) => NotifyLiveApplyRequested();
            numSeparatorOutlineTransparency.ValueChanged += (_, _) => NotifyLiveApplyRequested();
            btnSeparatorOutlineColor.BackColorChanged += (_, _) => NotifyLiveApplyRequested();

            SyncSeparatorOutlineFillModeComboFromSettings();
            SyncSeparatorOutlineWaveAxisRadiosFromSettings();
            SyncSeparatorOutlineInterpolationComboFromSettings();
            UpdateSeparatorOutlineOptionStates();
        }
        finally
        {
            separatorThicknessPanel.ResumeLayout();
        }
    }

    private void SeparatorOutline_LiveApply(object sender, EventArgs e)
    {
        UpdateSeparatorOutlineOptionStates();
        NotifyLiveApplyRequested();
    }

    private void SeparatorOutlineInterpolation_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (cmbSeparatorOutlineInterpolation?.SelectedItem is not string name)
        {
            return;
        }

        try
        {
            Settings.SeparatorOutlineInterpolation = (CurrentSplitImageInterpolationFilter)Enum.Parse(
                typeof(CurrentSplitImageInterpolationFilter),
                name,
                true);
        }
        catch (ArgumentException)
        {
            Settings.SeparatorOutlineInterpolation = CurrentSplitImageInterpolationFilter.Nearest;
        }

        SyncSeparatorOutlineInterpolationComboFromSettings();
        NotifyLiveApplyRequested();
    }

    private void SyncSeparatorOutlineInterpolationComboFromSettings()
    {
        if (cmbSeparatorOutlineInterpolation == null)
        {
            return;
        }

        string canonical = Settings.SeparatorOutlineInterpolation.ToString();
        int idx = cmbSeparatorOutlineInterpolation.FindStringExact(canonical);
        if (idx < 0)
        {
            idx = 0;
        }

        if (cmbSeparatorOutlineInterpolation.SelectedIndex == idx)
        {
            return;
        }

        cmbSeparatorOutlineInterpolation.SelectedIndex = idx;
    }

    private void SeparatorOutlineFillModeCombo_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (suppressSeparatorOutlineFillModeComboEvents || cmbSeparatorOutlineFillMode?.SelectedItem is not string name)
        {
            return;
        }

        try
        {
            Settings.SeparatorOutlineFillMode = (CurrentSplitOutlineFillMode)Enum.Parse(typeof(CurrentSplitOutlineFillMode), name, true);
        }
        catch (ArgumentException)
        {
            Settings.SeparatorOutlineFillMode = CurrentSplitOutlineFillMode.Solid;
        }

        SyncSeparatorOutlineFillModeComboFromSettings();
        UpdateSeparatorOutlineOptionStates();
        NotifyLiveApplyRequested();
    }

    private void SyncSeparatorOutlineFillModeComboFromSettings()
    {
        if (cmbSeparatorOutlineFillMode == null)
        {
            return;
        }

        string canonical = Settings.SeparatorOutlineFillMode.ToString();
        int idx = cmbSeparatorOutlineFillMode.FindStringExact(canonical);
        if (idx < 0)
        {
            idx = 0;
        }

        if (cmbSeparatorOutlineFillMode.SelectedIndex == idx)
        {
            return;
        }

        suppressSeparatorOutlineFillModeComboEvents = true;
        try
        {
            cmbSeparatorOutlineFillMode.SelectedIndex = idx;
        }
        finally
        {
            suppressSeparatorOutlineFillModeComboEvents = false;
        }
    }

    private void SeparatorOutlineWaveAxis_CheckedChanged(object sender, EventArgs e)
    {
        if (suppressSeparatorOutlineWaveAxisEvents)
        {
            return;
        }

        if (!rdoSeparatorOutlineWaveHorizontal.Checked && !rdoSeparatorOutlineWaveVertical.Checked)
        {
            return;
        }

        Settings.SeparatorOutlineWaveAxis = rdoSeparatorOutlineWaveVertical.Checked
            ? CurrentSplitOutlineWaveAxis.Vertical
            : CurrentSplitOutlineWaveAxis.Horizontal;

        SyncSeparatorOutlineWaveAxisRadiosFromSettings();
        NotifyLiveApplyRequested();
    }

    private void SyncSeparatorOutlineWaveAxisRadiosFromSettings()
    {
        if (rdoSeparatorOutlineWaveHorizontal == null || rdoSeparatorOutlineWaveVertical == null)
        {
            return;
        }

        suppressSeparatorOutlineWaveAxisEvents = true;
        try
        {
            if (Settings.SeparatorOutlineWaveAxis == CurrentSplitOutlineWaveAxis.Vertical)
            {
                rdoSeparatorOutlineWaveVertical.Checked = true;
            }
            else
            {
                rdoSeparatorOutlineWaveHorizontal.Checked = true;
            }
        }
        finally
        {
            suppressSeparatorOutlineWaveAxisEvents = false;
        }
    }

    private void UpdateSeparatorOutlineOptionStates()
    {
        if (lblSeparatorOutlineThickness == null)
        {
            return;
        }

        // Keep thickness / colors / fill / interpolation editable even when the outline is off
        // (same workflow as tuning options before enabling). Disabled popup buttons pick up
        // WinFormsTheme's dark disabled border (red), which looked broken.
        lblSeparatorOutlineThickness.Enabled = numSeparatorOutlineThickness.Enabled = true;
        lblSeparatorOutlineColor.Enabled = btnSeparatorOutlineColor.Enabled = true;
        lblSeparatorOutlineTransparency.Enabled = numSeparatorOutlineTransparency.Enabled = true;
        lblSeparatorOutlineFillMode.Enabled = cmbSeparatorOutlineFillMode.Enabled = true;
        lblSeparatorOutlineInterpolation.Enabled = cmbSeparatorOutlineInterpolation.Enabled = true;

        bool gradient = Settings.SeparatorOutlineFillMode == CurrentSplitOutlineFillMode.Gradient;
        lblSeparatorOutlineGradientEnd.Enabled = btnSeparatorOutlineGradientEndColor.Enabled = gradient;

        bool waveControlsOn = Settings.SeparatorOutlineRgbWave || gradient;
        lblSeparatorOutlineWaveAxis.Enabled = rdoSeparatorOutlineWaveHorizontal.Enabled = rdoSeparatorOutlineWaveVertical.Enabled = waveControlsOn;
        lblSeparatorOutlineWaveSpeed.Enabled = trkSeparatorOutlineWaveSpeed.Enabled = waveControlsOn;

        RefreshSeparatorOutlineThemedControls();
    }

    private void RefreshSeparatorOutlineThemedControls()
    {
        if (grpSeparatorOutline == null)
        {
            return;
        }

        WinFormsTheme.Apply(grpSeparatorOutline);
    }

    private void EnsureLayoutShadowControls()
    {
        if (grpLayoutShadows != null || tableLayoutPanel5 == null)
        {
            return;
        }

        tableLayoutPanel5.SuspendLayout();
        try
        {
            int row = tableLayoutPanel5.RowCount;
            tableLayoutPanel5.RowCount = row + 1;
            tableLayoutPanel5.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            grpLayoutShadows = new GroupBox
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                Margin = new Padding(3, 6, 3, 3),
                Padding = new Padding(6, 4, 10, 8),
                Text = T("Shadows")
            };

            var outer = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 2,
                Dock = DockStyle.Top,
                Margin = new Padding(0)
            };
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            grpLayoutIconShadows = CreateShadowSubgroup(
                T("Icons"),
                T("Offset (+SE / -NW, px)"),
                out trkLayoutIconShadowOffset,
                out trkLayoutIconShadowTransparency,
                out trkLayoutIconShadowBlur,
                out lblLayoutIconShadowOffsetValue,
                out lblLayoutIconShadowTransparencyValue,
                out lblLayoutIconShadowBlurValue,
                -100,
                100,
                10);

            grpLayoutTextShadows = CreateShadowSubgroup(
                T("Text, numbers, and fonts"),
                T("Offset south-east (px)"),
                out trkLayoutTextShadowOffset,
                out trkLayoutTextShadowTransparency,
                out trkLayoutTextShadowBlur,
                out lblLayoutTextShadowOffsetValue,
                out lblLayoutTextShadowTransparencyValue,
                out lblLayoutTextShadowBlurValue,
                0,
                160,
                10);

            outer.Controls.Add(grpLayoutIconShadows, 0, 0);
            outer.Controls.Add(grpLayoutTextShadows, 0, 1);
            grpLayoutShadows.Controls.Add(outer);

            tableLayoutPanel5.Controls.Add(grpLayoutShadows, 0, row);
            tableLayoutPanel5.SetColumnSpan(grpLayoutShadows, 3);

            trkLayoutIconShadowOffset.DataBindings.Add("Value", this, nameof(IconShadowOffsetTenths), false, DataSourceUpdateMode.OnPropertyChanged);
            trkLayoutIconShadowTransparency.DataBindings.Add("Value", this, nameof(IconShadowTransparencyTrack), false, DataSourceUpdateMode.OnPropertyChanged);
            trkLayoutIconShadowBlur.DataBindings.Add("Value", this, nameof(IconShadowBlurTrack), false, DataSourceUpdateMode.OnPropertyChanged);
            trkLayoutTextShadowOffset.DataBindings.Add("Value", this, nameof(TextShadowOffsetTenths), false, DataSourceUpdateMode.OnPropertyChanged);
            trkLayoutTextShadowTransparency.DataBindings.Add("Value", this, nameof(TextShadowTransparencyTrack), false, DataSourceUpdateMode.OnPropertyChanged);
            trkLayoutTextShadowBlur.DataBindings.Add("Value", this, nameof(TextShadowBlurTrack), false, DataSourceUpdateMode.OnPropertyChanged);

            WireShadowTenthsValueLabel(trkLayoutIconShadowOffset, lblLayoutIconShadowOffsetValue);
            WireShadowValueLabel(trkLayoutIconShadowTransparency, lblLayoutIconShadowTransparencyValue);
            WireShadowValueLabel(trkLayoutIconShadowBlur, lblLayoutIconShadowBlurValue);
            WireShadowTenthsValueLabel(trkLayoutTextShadowOffset, lblLayoutTextShadowOffsetValue);
            WireShadowValueLabel(trkLayoutTextShadowTransparency, lblLayoutTextShadowTransparencyValue);
            WireShadowValueLabel(trkLayoutTextShadowBlur, lblLayoutTextShadowBlurValue);

            WinFormsTheme.Apply(grpLayoutShadows);
        }
        finally
        {
            tableLayoutPanel5.ResumeLayout(false);
            tableLayoutPanel5.PerformLayout();
        }
    }

    private static void WireShadowValueLabel(TrackBar trk, Label val)
    {
        void Update(object _, EventArgs __) => val.Text = trk.Value.ToString();
        trk.ValueChanged += Update;
        Update(null, EventArgs.Empty);
    }

    private static void WireShadowTenthsValueLabel(TrackBar trk, Label val)
    {
        void Update(object _, EventArgs __) =>
            val.Text = (trk.Value / 10.0).ToString("0.0", CultureInfo.CurrentCulture);
        trk.ValueChanged += Update;
        Update(null, EventArgs.Empty);
    }

    private GroupBox CreateShadowSubgroup(
        string title,
        string offsetRowCaption,
        out TrackBar trkOffset,
        out TrackBar trkTransparency,
        out TrackBar trkBlur,
        out Label lblOffsetVal,
        out Label lblTransparencyVal,
        out Label lblBlurVal,
        int offsetMin,
        int offsetMax,
        int offsetTickFrequency)
    {
        var grp = new GroupBox
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 6),
            Padding = new Padding(6, 4, 10, 6),
            Text = title
        };

        var tlp = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            Dock = DockStyle.Top,
            Margin = new Padding(0)
        };
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        // Wide enough for "-10.0" / "100" value readouts (was 36f and clipped the label text).
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54f));

        static Label Caption(string text) => new Label
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            Margin = new Padding(0, 6, 6, 6),
            Text = text
        };

        static Label ValueCell() => new Label
        {
            Anchor = AnchorStyles.Right,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleRight,
            Margin = new Padding(3, 8, 8, 6),
            MinimumSize = new Size(52, 0),
            Width = 52
        };

        static TrackBar MakeTrack(int min, int max, int tickFreq) => new TrackBar
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            AutoSize = false,
            Margin = new Padding(0, 2, 0, 2),
            Minimum = min,
            Maximum = max,
            TickFrequency = tickFreq,
            TickStyle = TickStyle.None,
            Height = 42
        };

        int r = 0;
        tlp.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tlp.Controls.Add(Caption(offsetRowCaption), 0, r);
        trkOffset = MakeTrack(offsetMin, offsetMax, offsetTickFrequency);
        tlp.Controls.Add(trkOffset, 1, r);
        lblOffsetVal = ValueCell();
        tlp.Controls.Add(lblOffsetVal, 2, r);
        r++;

        tlp.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tlp.Controls.Add(Caption(T("Transparency (0-100)")), 0, r);
        trkTransparency = MakeTrack(0, 100, 10);
        tlp.Controls.Add(trkTransparency, 1, r);
        lblTransparencyVal = ValueCell();
        tlp.Controls.Add(lblTransparencyVal, 2, r);
        r++;

        tlp.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tlp.Controls.Add(Caption(T("Blur (0-100)")), 0, r);
        trkBlur = MakeTrack(0, 100, 10);
        tlp.Controls.Add(trkBlur, 1, r);
        lblBlurVal = ValueCell();
        tlp.Controls.Add(lblBlurVal, 2, r);

        grp.Controls.Add(tlp);
        WinFormsTheme.Apply(grp);
        return grp;
    }
}
