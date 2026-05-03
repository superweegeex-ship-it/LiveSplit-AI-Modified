using System;
using System.Drawing;
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
}
