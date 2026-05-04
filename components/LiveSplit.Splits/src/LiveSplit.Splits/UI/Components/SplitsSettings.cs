using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml;

using LiveSplit.Localization;
using LiveSplit.Model;
using LiveSplit.TimeFormatters;
using LiveSplit.UI;

namespace LiveSplit.UI.Components;

public partial class SplitsSettings : UserControl
{
    private static readonly EventHandler CurrentSplitBackgroundOnFrameChanged = static (_, _) => { };

    /// <summary>Avoids re-entrancy when syncing the outline fill combo from <see cref="CurrentSplitOutlineFillModeString"/>.</summary>
    private bool _inOutlineFillModeComboSync;
    private bool _parentScrollRefreshPending;

    private static string T(string source) => UiLocalizer.Translate(source, LanguageResolver.ResolveCurrentCultureLanguage());

    private int _VisualSplitCount { get; set; }
    public int VisualSplitCount
    {
        get => _VisualSplitCount;
        set
        {
            _VisualSplitCount = value;
            int max = Math.Max(0, _VisualSplitCount - (AlwaysShowLastSplit ? 2 : 1));
            if (dmnUpcomingSegments.Value > max)
            {
                dmnUpcomingSegments.Value = max;
            }

            dmnUpcomingSegments.Maximum = max;
        }
    }
    public Color CurrentSplitTopColor { get; set; }
    public Color CurrentSplitBottomColor { get; set; }
    public int SplitPreviewCount { get; set; }
    public float SplitWidth { get; set; }
    public float SplitHeight { get; set; }
    public float ScaledSplitHeight { get => SplitHeight * 10f; set => SplitHeight = value / 10f; }
    public float IconSize { get; set; }

    public bool Display2Rows { get; set; }

    public Color BackgroundColor { get; set; }
    public Color BackgroundColor2 { get; set; }

    public ExtendedGradientType BackgroundGradient { get; set; }
    public string GradientString
    {
        get => BackgroundGradient.ToString();
        set => BackgroundGradient = (ExtendedGradientType)Enum.Parse(typeof(ExtendedGradientType), value);
    }

    public LiveSplitState CurrentState { get; set; }

    public bool DisplayIcons { get; set; }
    public bool IconShadows { get; set; }
    public bool ShowThinSeparators { get; set; }
    public bool AlwaysShowLastSplit { get; set; }
    public bool ShowBlankSplits { get; set; }
    public bool LockLastSplit { get; set; }
    public bool SeparatorLastSplit { get; set; }

    public bool DropDecimals { get; set; }
    public TimeAccuracy DeltasAccuracy { get; set; }

    public bool OverrideDeltasColor { get; set; }
    public Color DeltasColor { get; set; }

    public bool ShowColumnLabels { get; set; }
    public Color LabelsColor { get; set; }

    public bool AutomaticAbbreviations { get; set; }
    public Color BeforeNamesColor { get; set; }
    public Color CurrentNamesColor { get; set; }
    public Color AfterNamesColor { get; set; }
    public bool OverrideTextColor { get; set; }
    public Color BeforeTimesColor { get; set; }
    public Color CurrentTimesColor { get; set; }
    public Color AfterTimesColor { get; set; }
    public bool OverrideTimesColor { get; set; }

    public TimeAccuracy SplitTimesAccuracy { get; set; }
    public GradientType CurrentSplitGradient { get; set; }
    public string SplitGradientString
    {
        get => CurrentSplitGradient.ToString();
        set => CurrentSplitGradient = (GradientType)Enum.Parse(typeof(GradientType), value);
    }

    public string CurrentSplitBackgroundImagePath { get; set; }

    public bool CurrentSplitOutlineEnabled { get; set; }

    public decimal CurrentSplitOutlineThickness { get; set; }

    public Color CurrentSplitOutlineColor { get; set; }

    /// <summary>0 = fully opaque outline, 100 = fully transparent (invisible).</summary>
    public decimal CurrentSplitOutlineTransparency { get; set; }

    public CurrentSplitImageInterpolationFilter CurrentSplitImageInterpolation { get; set; }

    public string CurrentSplitImageInterpolationString
    {
        get => CurrentSplitImageInterpolation.ToString();
        set => CurrentSplitImageInterpolation = (CurrentSplitImageInterpolationFilter)Enum.Parse(typeof(CurrentSplitImageInterpolationFilter), value);
    }

    /// <summary>Resampling / edge quality for the current-split outline stroke (independent of the split-image filter).</summary>
    public CurrentSplitImageInterpolationFilter CurrentSplitOutlineInterpolation { get; set; }

    public string CurrentSplitOutlineInterpolationString
    {
        get => CurrentSplitOutlineInterpolation.ToString();
        set
        {
            try
            {
                CurrentSplitOutlineInterpolation = (CurrentSplitImageInterpolationFilter)Enum.Parse(
                    typeof(CurrentSplitImageInterpolationFilter),
                    value,
                    true);
            }
            catch (ArgumentException)
            {
                CurrentSplitOutlineInterpolation = CurrentSplitImageInterpolationFilter.Nearest;
            }
        }
    }

    public CurrentSplitOutlineFillMode CurrentSplitOutlineFillMode { get; set; }

    public string CurrentSplitOutlineFillModeString
    {
        get => CurrentSplitOutlineFillMode.ToString();
        set
        {
            if (string.Equals(value, "GradientHorizontal", StringComparison.OrdinalIgnoreCase))
            {
                CurrentSplitOutlineFillMode = CurrentSplitOutlineFillMode.Gradient;
                CurrentSplitOutlineWaveAxis = CurrentSplitOutlineWaveAxis.Horizontal;
            }
            else if (string.Equals(value, "GradientVertical", StringComparison.OrdinalIgnoreCase))
            {
                CurrentSplitOutlineFillMode = CurrentSplitOutlineFillMode.Gradient;
                CurrentSplitOutlineWaveAxis = CurrentSplitOutlineWaveAxis.Vertical;
            }
            else
            {
                try
                {
                    CurrentSplitOutlineFillMode = (CurrentSplitOutlineFillMode)Enum.Parse(typeof(CurrentSplitOutlineFillMode), value, true);
                }
                catch (ArgumentException)
                {
                    CurrentSplitOutlineFillMode = CurrentSplitOutlineFillMode.Solid;
                }
            }
        }
    }

    public Color CurrentSplitOutlineGradientEndColor { get; set; }

    public bool CurrentSplitOutlineRgbWave { get; set; }

    public CurrentSplitOutlineWaveAxis CurrentSplitOutlineWaveAxis { get; set; }

    /// <summary>0 = frozen rainbow; higher values scroll the RGB wave faster.</summary>
    public int CurrentSplitOutlineWaveSpeed { get; set; }

    /// <summary>When true, the current-split row fill uses a scrolling spectrum instead of top/bottom colors (non-image modes only).</summary>
    public bool CurrentSplitGradientFillRgbWave { get; set; }

    /// <summary>Axis along which the row fill pattern scrolls (independent of Plain / Vertical / Horizontal gradient direction).</summary>
    public CurrentSplitOutlineWaveAxis CurrentSplitGradientFillWaveAxis { get; set; }

    /// <summary>0 = static fill; higher values scroll the two-color or RGB fill faster.</summary>
    public int CurrentSplitGradientFillWaveSpeed { get; set; }

    private Image _currentSplitBackgroundImage;
    private string _currentSplitBackgroundImagePathLoaded;

    internal void ReleaseCurrentSplitBackgroundImageResources()
    {
        if (_currentSplitBackgroundImage != null)
        {
            ImageAnimator.StopAnimate(_currentSplitBackgroundImage, CurrentSplitBackgroundOnFrameChanged);
            _currentSplitBackgroundImage.Dispose();
            _currentSplitBackgroundImage = null;
        }

        _currentSplitBackgroundImagePathLoaded = null;
    }

    internal Image GetCurrentSplitBackgroundImageForRendering()
    {
        if (CurrentSplitGradient != GradientType.Image)
        {
            return null;
        }

        string path = CurrentSplitBackgroundImagePath ?? string.Empty;
        if (_currentSplitBackgroundImagePathLoaded == path && _currentSplitBackgroundImage != null)
        {
            return _currentSplitBackgroundImage;
        }

        ReleaseCurrentSplitBackgroundImageResources();
        _currentSplitBackgroundImagePathLoaded = path;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            _currentSplitBackgroundImage = Image.FromFile(path);
            ImageAnimator.Animate(_currentSplitBackgroundImage, CurrentSplitBackgroundOnFrameChanged);
            return _currentSplitBackgroundImage;
        }
        catch
        {
            ReleaseCurrentSplitBackgroundImageResources();
            return null;
        }
    }

    public event EventHandler SplitLayoutChanged;

    public LayoutMode Mode { get; set; }

    public IList<ColumnSettings> ColumnsList { get; set; }
    public Size StartingSize { get; set; }
    public Size StartingTableLayoutSize { get; set; }
    public Size StartingGroupColumnsSize { get; set; }
    public int StartingColumnSettingHeight { get; set; }

    public SplitsSettings(LiveSplitState state)
    {
        InitializeComponent();
        trkCurrentSplitOutlineWaveSpeed.Maximum = CurrentSplitOutlinePaint.OutlineWaveSpeedSliderMaximum;
        trkCurrentSplitOutlineWaveSpeed.TickFrequency = 5;
        trkCurrentSplitFillWaveSpeed.Maximum = CurrentSplitOutlinePaint.OutlineWaveSpeedSliderMaximum;
        trkCurrentSplitFillWaveSpeed.TickFrequency = 5;

        CurrentState = state;

        StartingSize = Size;
        StartingTableLayoutSize = tableColumns.Size;
        StartingGroupColumnsSize = groupColumns.Size;

        VisualSplitCount = 8;
        SplitPreviewCount = 1;
        DisplayIcons = true;
        IconShadows = true;
        ShowThinSeparators = true;
        AlwaysShowLastSplit = true;
        ShowBlankSplits = true;
        LockLastSplit = true;
        SeparatorLastSplit = true;
        SplitTimesAccuracy = TimeAccuracy.Seconds;
        CurrentSplitTopColor = Color.FromArgb(51, 115, 244);
        CurrentSplitBottomColor = Color.FromArgb(21, 53, 116);
        SplitWidth = 20;
        SplitHeight = 3.6f;
        IconSize = 24f;
        AutomaticAbbreviations = false;
        BeforeNamesColor = Color.FromArgb(255, 255, 255);
        CurrentNamesColor = Color.FromArgb(255, 255, 255);
        AfterNamesColor = Color.FromArgb(255, 255, 255);
        OverrideTextColor = false;
        BeforeTimesColor = Color.FromArgb(255, 255, 255);
        CurrentTimesColor = Color.FromArgb(255, 255, 255);
        AfterTimesColor = Color.FromArgb(255, 255, 255);
        OverrideTimesColor = false;
        CurrentSplitGradient = GradientType.Vertical;
        CurrentSplitBackgroundImagePath = string.Empty;
        CurrentSplitOutlineEnabled = false;
        CurrentSplitOutlineThickness = 2;
        CurrentSplitOutlineColor = Color.White;
        CurrentSplitOutlineTransparency = 0;
        CurrentSplitImageInterpolation = CurrentSplitImageInterpolationFilter.Bilinear;
        CurrentSplitOutlineInterpolation = CurrentSplitImageInterpolationFilter.Nearest;
        CurrentSplitOutlineFillMode = CurrentSplitOutlineFillMode.Solid;
        CurrentSplitOutlineGradientEndColor = Color.FromArgb(255, 80, 200);
        CurrentSplitOutlineRgbWave = false;
        CurrentSplitOutlineWaveAxis = CurrentSplitOutlineWaveAxis.Horizontal;
        CurrentSplitOutlineWaveSpeed = 25;
        CurrentSplitGradientFillRgbWave = false;
        CurrentSplitGradientFillWaveAxis = CurrentSplitOutlineWaveAxis.Horizontal;
        CurrentSplitGradientFillWaveSpeed = 0;
        nudCurrentSplitOutlineThickness.DecimalPlaces = 1;
        nudCurrentSplitOutlineThickness.Increment = 0.1m;
        nudCurrentSplitOutlineThickness.Minimum = 0.1m;
        nudCurrentSplitOutlineThickness.Maximum = 20m;
        btnCurrentSplitBackgroundBrowse.Click += btnCurrentSplitBackgroundBrowse_Click;
        Disposed += (_, _) => ReleaseCurrentSplitBackgroundImageResources();
        BackgroundColor = Color.Transparent;
        BackgroundColor2 = Color.FromArgb(1, 255, 255, 255);
        BackgroundGradient = ExtendedGradientType.Alternating;
        DropDecimals = true;
        DeltasAccuracy = TimeAccuracy.Tenths;
        OverrideDeltasColor = false;
        DeltasColor = Color.FromArgb(255, 255, 255);
        Display2Rows = false;
        ShowColumnLabels = false;
        LabelsColor = Color.FromArgb(255, 255, 255);

        dmnTotalSegments.DataBindings.Add("Value", this, "VisualSplitCount", false, DataSourceUpdateMode.OnPropertyChanged);
        dmnUpcomingSegments.DataBindings.Add("Value", this, "SplitPreviewCount", false, DataSourceUpdateMode.OnPropertyChanged);
        btnTopColor.DataBindings.Add("BackColor", this, "CurrentSplitTopColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnBottomColor.DataBindings.Add("BackColor", this, "CurrentSplitBottomColor", false, DataSourceUpdateMode.OnPropertyChanged);
        chkAutomaticAbbreviations.DataBindings.Add("Checked", this, "AutomaticAbbreviations", false, DataSourceUpdateMode.OnPropertyChanged);
        btnBeforeNamesColor.DataBindings.Add("BackColor", this, "BeforeNamesColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnCurrentNamesColor.DataBindings.Add("BackColor", this, "CurrentNamesColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnAfterNamesColor.DataBindings.Add("BackColor", this, "AfterNamesColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnBeforeTimesColor.DataBindings.Add("BackColor", this, "BeforeTimesColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnCurrentTimesColor.DataBindings.Add("BackColor", this, "CurrentTimesColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnAfterTimesColor.DataBindings.Add("BackColor", this, "AfterTimesColor", false, DataSourceUpdateMode.OnPropertyChanged);
        chkDisplayIcons.DataBindings.Add("Checked", this, "DisplayIcons", false, DataSourceUpdateMode.OnPropertyChanged);
        chkIconShadows.DataBindings.Add("Checked", this, "IconShadows", false, DataSourceUpdateMode.OnPropertyChanged);
        chkThinSeparators.DataBindings.Add("Checked", this, "ShowThinSeparators", false, DataSourceUpdateMode.OnPropertyChanged);
        chkLastSplit.DataBindings.Add("Checked", this, "AlwaysShowLastSplit", false, DataSourceUpdateMode.OnPropertyChanged);
        chkOverrideTextColor.DataBindings.Add("Checked", this, "OverrideTextColor", false, DataSourceUpdateMode.OnPropertyChanged);
        chkOverrideTimesColor.DataBindings.Add("Checked", this, "OverrideTimesColor", false, DataSourceUpdateMode.OnPropertyChanged);
        chkShowBlankSplits.DataBindings.Add("Checked", this, "ShowBlankSplits", false, DataSourceUpdateMode.OnPropertyChanged);
        chkLockLastSplit.DataBindings.Add("Checked", this, "LockLastSplit", false, DataSourceUpdateMode.OnPropertyChanged);
        chkSeparatorLastSplit.DataBindings.Add("Checked", this, "SeparatorLastSplit", false, DataSourceUpdateMode.OnPropertyChanged);
        chkDropDecimals.DataBindings.Add("Checked", this, "DropDecimals", false, DataSourceUpdateMode.OnPropertyChanged);
        chkOverrideDeltaColor.DataBindings.Add("Checked", this, "OverrideDeltasColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnDeltaColor.DataBindings.Add("BackColor", this, "DeltasColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnLabelColor.DataBindings.Add("BackColor", this, "LabelsColor", false, DataSourceUpdateMode.OnPropertyChanged);
        trkIconSize.DataBindings.Add("Value", this, "IconSize", false, DataSourceUpdateMode.OnPropertyChanged);
        EnsureCurrentSplitGradientComboItems();
        EnsureCurrentSplitImageInterpolationComboItems();
        cmbSplitGradient.DataBindings.Add("SelectedItem", this, "SplitGradientString", false, DataSourceUpdateMode.OnPropertyChanged);
        cmbGradientType.DataBindings.Add("SelectedItem", this, "GradientString", false, DataSourceUpdateMode.OnPropertyChanged);
        btnColor1.DataBindings.Add("BackColor", this, "BackgroundColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnColor2.DataBindings.Add("BackColor", this, "BackgroundColor2", false, DataSourceUpdateMode.OnPropertyChanged);
        chkCurrentSplitOutline.DataBindings.Add("Checked", this, "CurrentSplitOutlineEnabled", false, DataSourceUpdateMode.OnPropertyChanged);
        nudCurrentSplitOutlineThickness.DataBindings.Add("Value", this, "CurrentSplitOutlineThickness", true, DataSourceUpdateMode.OnPropertyChanged);
        nudCurrentSplitOutlineTransparency.DataBindings.Add("Value", this, "CurrentSplitOutlineTransparency", true, DataSourceUpdateMode.OnPropertyChanged);
        btnCurrentSplitOutlineColor.DataBindings.Add("BackColor", this, "CurrentSplitOutlineColor", false, DataSourceUpdateMode.OnPropertyChanged);
        cmbCurrentSplitImageFilter.DataBindings.Add("SelectedItem", this, "CurrentSplitImageInterpolationString", false, DataSourceUpdateMode.OnPropertyChanged);
        EnsureCurrentSplitOutlineInterpolationComboItems();
        cmbCurrentSplitOutlineInterpolation.DataBindings.Add("SelectedItem", this, "CurrentSplitOutlineInterpolationString", false, DataSourceUpdateMode.OnPropertyChanged);
        EnsureCurrentSplitOutlineFillModeComboItems();
        // Do not data-bind SelectedItem for outline fill mode: WinForms throws FormatException when the
        // persisted string is not exactly an Items entry (legacy layouts, whitespace, etc.). Sync in code instead.
        btnCurrentSplitOutlineGradientEndColor.DataBindings.Add("BackColor", this, "CurrentSplitOutlineGradientEndColor", false, DataSourceUpdateMode.OnPropertyChanged);
        chkCurrentSplitOutlineRgbWave.DataBindings.Add("Checked", this, "CurrentSplitOutlineRgbWave", false, DataSourceUpdateMode.OnPropertyChanged);
        trkCurrentSplitOutlineWaveSpeed.DataBindings.Add("Value", this, "CurrentSplitOutlineWaveSpeed", false, DataSourceUpdateMode.OnPropertyChanged);
        chkCurrentSplitFillRgbWave.DataBindings.Add("Checked", this, "CurrentSplitGradientFillRgbWave", false, DataSourceUpdateMode.OnPropertyChanged);
        trkCurrentSplitFillWaveSpeed.DataBindings.Add("Value", this, "CurrentSplitGradientFillWaveSpeed", false, DataSourceUpdateMode.OnPropertyChanged);
        chkCurrentSplitOutline.CheckedChanged += (_, _) => UpdateCurrentSplitOutlineControlsEnabled();
        UpdateCurrentSplitOutlineControlsEnabled();
        UpdateCurrentSplitImageAndOutlineOptionStates();

        ColumnsList = [];
        ColumnsList.Add(new ColumnSettings(CurrentState, "+/-", ColumnsList) { Data = new ColumnData("+/-", ColumnType.Delta, "Current Comparison", "Current Timing Method") });
        ColumnsList.Add(new ColumnSettings(CurrentState, T("Time"), ColumnsList) { Data = new ColumnData(T("Time"), ColumnType.SplitTime, "Current Comparison", "Current Timing Method") });

        StartingColumnSettingHeight = ColumnsList[0].Height;
        ResetColumns();
    }

    private void EnsureCurrentSplitImageInterpolationComboItems()
    {
        foreach (string name in Enum.GetNames(typeof(CurrentSplitImageInterpolationFilter)))
        {
            if (!cmbCurrentSplitImageFilter.Items.Contains(name))
            {
                cmbCurrentSplitImageFilter.Items.Add(name);
            }
        }
    }

    private void EnsureCurrentSplitOutlineFillModeComboItems()
    {
        foreach (string name in Enum.GetNames(typeof(CurrentSplitOutlineFillMode)))
        {
            if (!cmbCurrentSplitOutlineColorMode.Items.Contains(name))
            {
                cmbCurrentSplitOutlineColorMode.Items.Add(name);
            }
        }
    }

    private void EnsureCurrentSplitOutlineInterpolationComboItems()
    {
        foreach (string name in Enum.GetNames(typeof(CurrentSplitImageInterpolationFilter)))
        {
            if (!cmbCurrentSplitOutlineInterpolation.Items.Contains(name))
            {
                cmbCurrentSplitOutlineInterpolation.Items.Add(name);
            }
        }
    }

    private void cmbCurrentSplitOutlineInterpolation_SelectedIndexChanged(object sender, EventArgs e)
    {
        SplitLayoutChanged?.Invoke(this, null);
    }

    private void cmbCurrentSplitOutlineColorMode_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (_inOutlineFillModeComboSync || cmbCurrentSplitOutlineColorMode.SelectedIndex < 0)
        {
            return;
        }

        if (cmbCurrentSplitOutlineColorMode.SelectedItem is not string rawName)
        {
            return;
        }

        string name = rawName.Trim();
        try
        {
            _inOutlineFillModeComboSync = true;
            CurrentSplitOutlineFillModeString = name;
            SyncCurrentSplitOutlineFillModeCombo();
        }
        finally
        {
            _inOutlineFillModeComboSync = false;
        }

        UpdateCurrentSplitOutlineAppearanceOptionStates();
        SplitLayoutChanged?.Invoke(this, null);
    }

    private void SyncCurrentSplitOutlineFillModeCombo()
    {
        if (cmbCurrentSplitOutlineColorMode == null || cmbCurrentSplitOutlineColorMode.IsDisposed)
        {
            return;
        }

        EnsureCurrentSplitOutlineFillModeComboItems();
        string canonical = CurrentSplitOutlineFillMode.ToString();
        if (!cmbCurrentSplitOutlineColorMode.Items.Contains(canonical))
        {
            CurrentSplitOutlineFillMode = CurrentSplitOutlineFillMode.Solid;
            canonical = CurrentSplitOutlineFillMode.ToString();
        }

        SelectComboItem(cmbCurrentSplitOutlineColorMode, canonical);
    }

    private void chkCurrentSplitOutlineRgbWave_CheckedChanged(object sender, EventArgs e)
    {
        UpdateCurrentSplitOutlineAppearanceOptionStates();
        SyncOutlineWaveAxisRadios();
        SplitLayoutChanged?.Invoke(this, null);
    }

    private void rdoCurrentSplitOutlineWaveAxis_CheckedChanged(object sender, EventArgs e)
    {
        if (!rdoCurrentSplitOutlineWaveHorizontal.Checked && !rdoCurrentSplitOutlineWaveVertical.Checked)
        {
            return;
        }

        CurrentSplitOutlineWaveAxis = rdoCurrentSplitOutlineWaveVertical.Checked
            ? CurrentSplitOutlineWaveAxis.Vertical
            : CurrentSplitOutlineWaveAxis.Horizontal;
        SplitLayoutChanged?.Invoke(this, null);
    }

    private void trkCurrentSplitOutlineWaveSpeed_Scroll(object sender, EventArgs e)
    {
        SplitLayoutChanged?.Invoke(this, null);
    }

    private void chkCurrentSplitFillRgbWave_CheckedChanged(object sender, EventArgs e)
    {
        RefreshCurrentSplitFillGradientThemedControls();
        SplitLayoutChanged?.Invoke(this, null);
    }

    private void rdoCurrentSplitFillWaveAxis_CheckedChanged(object sender, EventArgs e)
    {
        if (!rdoCurrentSplitFillWaveHorizontal.Checked && !rdoCurrentSplitFillWaveVertical.Checked)
        {
            return;
        }

        CurrentSplitGradientFillWaveAxis = rdoCurrentSplitFillWaveVertical.Checked
            ? CurrentSplitOutlineWaveAxis.Vertical
            : CurrentSplitOutlineWaveAxis.Horizontal;
        SplitLayoutChanged?.Invoke(this, null);
    }

    private void trkCurrentSplitFillWaveSpeed_Scroll(object sender, EventArgs e)
    {
        SplitLayoutChanged?.Invoke(this, null);
    }

    private void SyncFillWaveAxisRadios()
    {
        if (CurrentSplitGradientFillWaveAxis == CurrentSplitOutlineWaveAxis.Vertical)
        {
            rdoCurrentSplitFillWaveVertical.Checked = true;
        }
        else
        {
            rdoCurrentSplitFillWaveHorizontal.Checked = true;
        }
    }

    private void SyncOutlineWaveAxisRadios()
    {
        if (CurrentSplitOutlineWaveAxis == CurrentSplitOutlineWaveAxis.Vertical)
        {
            rdoCurrentSplitOutlineWaveVertical.Checked = true;
        }
        else
        {
            rdoCurrentSplitOutlineWaveHorizontal.Checked = true;
        }
    }

    private static void EnsureGradientComboHasAllModes(ComboBox combo)
    {
        foreach (string name in Enum.GetNames(typeof(GradientType)))
        {
            if (!combo.Items.Contains(name))
            {
                combo.Items.Add(name);
            }
        }
    }

    private void EnsureCurrentSplitGradientComboItems()
    {
        EnsureGradientComboHasAllModes(cmbSplitGradient);
    }

    private void chkColumnLabels_CheckedChanged(object sender, EventArgs e)
    {
        btnLabelColor.Enabled = lblLabelsColor.Enabled = chkColumnLabels.Checked;
    }

    private void chkDisplayIcons_CheckedChanged(object sender, EventArgs e)
    {
        trkIconSize.Enabled = label5.Enabled = chkIconShadows.Enabled = chkDisplayIcons.Checked;
    }

    private void chkOverrideTimesColor_CheckedChanged(object sender, EventArgs e)
    {
        label6.Enabled = label9.Enabled = label7.Enabled = btnBeforeTimesColor.Enabled
            = btnCurrentTimesColor.Enabled = btnAfterTimesColor.Enabled = chkOverrideTimesColor.Checked;
    }

    private void chkOverrideDeltaColor_CheckedChanged(object sender, EventArgs e)
    {
        label8.Enabled = btnDeltaColor.Enabled = chkOverrideDeltaColor.Checked;
    }

    private void chkOverrideTextColor_CheckedChanged(object sender, EventArgs e)
    {
        label3.Enabled = label10.Enabled = label13.Enabled = btnBeforeNamesColor.Enabled
        = btnCurrentNamesColor.Enabled = btnAfterNamesColor.Enabled = chkOverrideTextColor.Checked;
    }

    private void rdoDeltaHundredths_CheckedChanged(object sender, EventArgs e)
    {
        UpdateDeltaAccuracy();
    }

    private void rdoDeltaTenths_CheckedChanged(object sender, EventArgs e)
    {
        UpdateDeltaAccuracy();
    }

    private void rdoDeltaSeconds_CheckedChanged(object sender, EventArgs e)
    {
        UpdateDeltaAccuracy();
    }

    private void chkSeparatorLastSplit_CheckedChanged(object sender, EventArgs e)
    {
        SeparatorLastSplit = chkSeparatorLastSplit.Checked;
        SplitLayoutChanged(this, null);
    }

    private void cmbGradientType_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (cmbGradientType.SelectedItem == null)
        {
            return;
        }

        btnColor1.Visible = cmbGradientType.SelectedItem.ToString() != "Plain";
        btnColor2.DataBindings.Clear();
        btnColor2.DataBindings.Add("BackColor", this, btnColor1.Visible ? "BackgroundColor2" : "BackgroundColor", false, DataSourceUpdateMode.OnPropertyChanged);
        GradientString = cmbGradientType.SelectedItem.ToString();
    }

    private void cmbSplitGradient_SelectedIndexChanged(object sender, EventArgs e)
    {
        string selected = cmbSplitGradient.SelectedItem?.ToString() ?? GradientType.Vertical.ToString();
        bool isImage = selected == nameof(GradientType.Image);
        bool showTopColor = !isImage;
        bool showBottomColor = selected != nameof(GradientType.Plain) && !isImage;

        btnTopColor.Visible = showTopColor;
        btnBottomColor.Visible = showBottomColor;
        btnCurrentSplitBackgroundBrowse.Visible = isImage;

        btnBottomColor.DataBindings.Clear();
        if (showBottomColor)
        {
            btnBottomColor.DataBindings.Add("BackColor", this, "CurrentSplitBottomColor", false, DataSourceUpdateMode.OnPropertyChanged);
        }
        else if (!isImage)
        {
            btnBottomColor.DataBindings.Add("BackColor", this, "CurrentSplitTopColor", false, DataSourceUpdateMode.OnPropertyChanged);
        }

        SplitGradientString = selected;

        if (CurrentSplitGradient != GradientType.Image)
        {
            ReleaseCurrentSplitBackgroundImageResources();
        }

        UpdateCurrentSplitImageAndOutlineOptionStates();
    }

    private void btnCurrentSplitBackgroundBrowse_Click(object sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|GIF|*.gif|All files|*.*",
            CheckFileExists = true
        };
        if (!string.IsNullOrWhiteSpace(CurrentSplitBackgroundImagePath))
        {
            dlg.FileName = CurrentSplitBackgroundImagePath;
        }

        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        ReleaseCurrentSplitBackgroundImageResources();
        CurrentSplitBackgroundImagePath = dlg.FileName;
        SelectComboItem(cmbSplitGradient, nameof(GradientType.Image));
        SplitGradientString = nameof(GradientType.Image);
        SplitLayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void chkLockLastSplit_CheckedChanged(object sender, EventArgs e)
    {
        LockLastSplit = chkLockLastSplit.Checked;
        SplitLayoutChanged(this, null);
    }

    private void chkShowBlankSplits_CheckedChanged(object sender, EventArgs e)
    {
        ShowBlankSplits = chkLockLastSplit.Enabled = chkShowBlankSplits.Checked;
        SplitLayoutChanged(this, null);
    }

    private void rdoHundredths_CheckedChanged(object sender, EventArgs e)
    {
        UpdateAccuracy();
    }

    private void rdoTenths_CheckedChanged(object sender, EventArgs e)
    {
        UpdateAccuracy();
    }

    private void rdoSeconds_CheckedChanged(object sender, EventArgs e)
    {
        UpdateAccuracy();
    }

    private void UpdateAccuracy()
    {
        if (rdoSeconds.Checked)
        {
            SplitTimesAccuracy = TimeAccuracy.Seconds;
        }
        else if (rdoTenths.Checked)
        {
            SplitTimesAccuracy = TimeAccuracy.Tenths;
        }
        else if (rdoHundredths.Checked)
        {
            SplitTimesAccuracy = TimeAccuracy.Hundredths;
        }
        else
        {
            SplitTimesAccuracy = TimeAccuracy.Milliseconds;
        }
    }

    private void UpdateDeltaAccuracy()
    {
        if (rdoDeltaSeconds.Checked)
        {
            DeltasAccuracy = TimeAccuracy.Seconds;
        }
        else if (rdoDeltaTenths.Checked)
        {
            DeltasAccuracy = TimeAccuracy.Tenths;
        }
        else if (rdoDeltaHundredths.Checked)
        {
            DeltasAccuracy = TimeAccuracy.Hundredths;
        }
        else
        {
            DeltasAccuracy = TimeAccuracy.Milliseconds;
        }
    }

    private void chkLastSplit_CheckedChanged(object sender, EventArgs e)
    {
        AlwaysShowLastSplit = chkLastSplit.Checked;
        VisualSplitCount = VisualSplitCount;
        SplitLayoutChanged(this, null);
    }

    private void chkThinSeparators_CheckedChanged(object sender, EventArgs e)
    {
        ShowThinSeparators = chkThinSeparators.Checked;
        SplitLayoutChanged(this, null);
    }

    private void UpdateCurrentSplitOutlineControlsEnabled()
    {
        bool on = chkCurrentSplitOutline.Checked;
        lblCurrentSplitOutlineThickness.Enabled = nudCurrentSplitOutlineThickness.Enabled = lblCurrentSplitOutlineColor.Enabled =
            btnCurrentSplitOutlineColor.Enabled = lblCurrentSplitOutlineTransparency.Enabled = nudCurrentSplitOutlineTransparency.Enabled = on;
        lblCurrentSplitOutlineColorMode.Enabled = cmbCurrentSplitOutlineColorMode.Enabled = on;
        UpdateCurrentSplitOutlineAppearanceOptionStates();
        UpdateCurrentSplitImageAndOutlineOptionStates();
    }

    private CurrentSplitOutlineFillMode GetCurrentSplitOutlineFillModeFromUi()
    {
        if (cmbCurrentSplitOutlineColorMode?.SelectedItem is string name
            && Enum.TryParse(name, out CurrentSplitOutlineFillMode mode))
        {
            return mode;
        }

        return CurrentSplitOutlineFillMode;
    }

    private bool IsCurrentSplitOutlineGradientFillFromUi()
    {
        CurrentSplitOutlineFillMode mode = GetCurrentSplitOutlineFillModeFromUi();
        return mode == CurrentSplitOutlineFillMode.Gradient;
    }

    private void RefreshCurrentSplitOutlineThemedControls()
    {
        foreach (Control c in new Control[]
                 {
                     lblCurrentSplitOutlineGradientEndColor, lblCurrentSplitOutlineWaveSpeed, lblCurrentSplitOutlineColorMode,
                     lblCurrentSplitOutlineInterpolation, trkCurrentSplitOutlineWaveSpeed,
                     rdoCurrentSplitOutlineWaveHorizontal, rdoCurrentSplitOutlineWaveVertical
                 })
        {
            if (c != null)
            {
                WinFormsTheme.Apply(c);
            }
        }
    }

    private void RefreshCurrentSplitFillGradientThemedControls()
    {
        foreach (Control c in new Control[]
                 {
                     chkCurrentSplitFillRgbWave, lblCurrentSplitFillWaveMotion, lblCurrentSplitFillWaveSpeed,
                     trkCurrentSplitFillWaveSpeed, rdoCurrentSplitFillWaveHorizontal, rdoCurrentSplitFillWaveVertical
                 })
        {
            if (c != null)
            {
                WinFormsTheme.Apply(c);
            }
        }
    }

    private void UpdateCurrentSplitOutlineAppearanceOptionStates()
    {
        bool on = chkCurrentSplitOutline.Checked;
        // Use the checkbox state here: CheckedChanged can run before the data binding pushes
        // the new value onto CurrentSplitOutlineRgbWave, which inverted wave vs. speed enablement.
        bool gradientFillUi = on && IsCurrentSplitOutlineGradientFillFromUi();
        lblCurrentSplitOutlineGradientEndColor.Enabled = btnCurrentSplitOutlineGradientEndColor.Enabled = gradientFillUi;

        bool wave = on && (chkCurrentSplitOutlineRgbWave.Checked || IsCurrentSplitOutlineGradientFillFromUi());
        lblCurrentSplitOutlineWaveSpeed.Enabled = trkCurrentSplitOutlineWaveSpeed.Enabled = wave;
        bool axisMeaningful = on && (chkCurrentSplitOutlineRgbWave.Checked || IsCurrentSplitOutlineGradientFillFromUi());
        rdoCurrentSplitOutlineWaveHorizontal.Enabled = rdoCurrentSplitOutlineWaveVertical.Enabled = axisMeaningful;

        lblCurrentSplitOutlineInterpolation.Enabled = cmbCurrentSplitOutlineInterpolation.Enabled = on;
        RefreshCurrentSplitOutlineThemedControls();
    }

    private void UpdateCurrentSplitImageAndOutlineOptionStates()
    {
        bool imageMode = CurrentSplitGradient == GradientType.Image;
        lblCurrentSplitImageFilter.Enabled = cmbCurrentSplitImageFilter.Enabled = imageMode;
        if (grpCurrentSplitFillGradientMotion != null && !grpCurrentSplitFillGradientMotion.IsDisposed)
        {
            grpCurrentSplitFillGradientMotion.Enabled = !imageMode;
        }

        RefreshCurrentSplitFillGradientThemedControls();
    }

    private void SplitsSettings_Load(object sender, EventArgs e)
    {
        EnsureCurrentSplitGradientComboItems();
        ReadBindings(this);
        SelectComboItem(cmbGradientType, GradientString);
        SelectComboItem(cmbSplitGradient, SplitGradientString);
        SelectComboItem(cmbCurrentSplitImageFilter, CurrentSplitImageInterpolationString);
        EnsureCurrentSplitOutlineInterpolationComboItems();
        SelectComboItem(cmbCurrentSplitOutlineInterpolation, CurrentSplitOutlineInterpolationString);
        SyncCurrentSplitOutlineFillModeCombo();
        SyncOutlineWaveAxisRadios();
        SyncFillWaveAxisRadios();

        ResetColumns();

        chkOverrideDeltaColor_CheckedChanged(null, null);
        chkOverrideTextColor_CheckedChanged(null, null);
        chkOverrideTimesColor_CheckedChanged(null, null);
        chkColumnLabels_CheckedChanged(null, null);
        chkDisplayIcons_CheckedChanged(null, null);
        chkLockLastSplit.Enabled = chkShowBlankSplits.Checked;

        rdoSeconds.Checked = SplitTimesAccuracy == TimeAccuracy.Seconds;
        rdoTenths.Checked = SplitTimesAccuracy == TimeAccuracy.Tenths;
        rdoHundredths.Checked = SplitTimesAccuracy == TimeAccuracy.Hundredths;
        rdoMilliseconds.Checked = SplitTimesAccuracy == TimeAccuracy.Milliseconds;

        rdoDeltaSeconds.Checked = DeltasAccuracy == TimeAccuracy.Seconds;
        rdoDeltaTenths.Checked = DeltasAccuracy == TimeAccuracy.Tenths;
        rdoDeltaHundredths.Checked = DeltasAccuracy == TimeAccuracy.Hundredths;
        rdoDeltaMilliseconds.Checked = DeltasAccuracy == TimeAccuracy.Milliseconds;

        cmbSplitGradient_SelectedIndexChanged(null, null);
        UpdateCurrentSplitOutlineControlsEnabled();
        UpdateCurrentSplitImageAndOutlineOptionStates();

        if (Mode == LayoutMode.Horizontal)
        {
            trkSize.DataBindings.Clear();
            trkSize.Minimum = 5;
            trkSize.Maximum = 120;
            SplitWidth = Math.Min(Math.Max(trkSize.Minimum, SplitWidth), trkSize.Maximum);
            trkSize.DataBindings.Add("Value", this, "SplitWidth", false, DataSourceUpdateMode.OnPropertyChanged);
            lblSplitSize.Text = T("Split Width:");
            chkDisplayRows.Enabled = false;
            chkDisplayRows.DataBindings.Clear();
            chkDisplayRows.Checked = true;
            chkColumnLabels.DataBindings.Clear();
            chkColumnLabels.Enabled = chkColumnLabels.Checked = false;
        }
        else
        {
            trkSize.DataBindings.Clear();
            trkSize.Minimum = 0;
            trkSize.Maximum = 250;
            ScaledSplitHeight = Math.Min(Math.Max(trkSize.Minimum, ScaledSplitHeight), trkSize.Maximum);
            trkSize.DataBindings.Add("Value", this, "ScaledSplitHeight", false, DataSourceUpdateMode.OnPropertyChanged);
            lblSplitSize.Text = T("Split Height:");
            chkDisplayRows.Enabled = true;
            chkDisplayRows.DataBindings.Clear();
            chkDisplayRows.DataBindings.Add("Checked", this, "Display2Rows", false, DataSourceUpdateMode.OnPropertyChanged);
            chkColumnLabels.DataBindings.Clear();
            chkColumnLabels.Enabled = true;
            chkColumnLabels.DataBindings.Add("Checked", this, "ShowColumnLabels", false, DataSourceUpdateMode.OnPropertyChanged);
        }

        WinFormsTheme.Apply(this);
    }

    private static void SelectComboItem(ComboBox comboBox, string value)
    {
        if (comboBox.Items.Contains(value))
        {
            comboBox.SelectedItem = value;
        }
        else if (comboBox.Items.Count > 0 && comboBox.SelectedItem == null)
        {
            comboBox.SelectedIndex = 0;
        }
    }

    private static void ReadBindings(Control control)
    {
        foreach (Binding binding in control.DataBindings)
        {
            binding.ReadValue();
        }

        foreach (Control child in control.Controls)
        {
            ReadBindings(child);
        }
    }

    public void SetSettings(XmlNode node)
    {
        var element = (XmlElement)node;
        Version version = SettingsHelper.ParseVersion(element["Version"]);

        ReleaseCurrentSplitBackgroundImageResources();

        CurrentSplitTopColor = SettingsHelper.ParseColor(element["CurrentSplitTopColor"], Color.FromArgb(51, 115, 244));
        CurrentSplitBottomColor = SettingsHelper.ParseColor(element["CurrentSplitBottomColor"], Color.FromArgb(21, 53, 116));
        VisualSplitCount = SettingsHelper.ParseInt(element["VisualSplitCount"]);
        SplitPreviewCount = SettingsHelper.ParseInt(element["SplitPreviewCount"]);
        DisplayIcons = SettingsHelper.ParseBool(element["DisplayIcons"]);
        ShowThinSeparators = SettingsHelper.ParseBool(element["ShowThinSeparators"]);
        AlwaysShowLastSplit = SettingsHelper.ParseBool(element["AlwaysShowLastSplit"]);
        SplitWidth = SettingsHelper.ParseFloat(element["SplitWidth"]);
        AutomaticAbbreviations = SettingsHelper.ParseBool(element["AutomaticAbbreviations"], false);
        ShowColumnLabels = SettingsHelper.ParseBool(element["ShowColumnLabels"], false);
        LabelsColor = SettingsHelper.ParseColor(element["LabelsColor"], Color.FromArgb(255, 255, 255));
        OverrideTimesColor = SettingsHelper.ParseBool(element["OverrideTimesColor"], false);
        BeforeTimesColor = SettingsHelper.ParseColor(element["BeforeTimesColor"], Color.FromArgb(255, 255, 255));
        CurrentTimesColor = SettingsHelper.ParseColor(element["CurrentTimesColor"], Color.FromArgb(255, 255, 255));
        AfterTimesColor = SettingsHelper.ParseColor(element["AfterTimesColor"], Color.FromArgb(255, 255, 255));
        SplitHeight = SettingsHelper.ParseFloat(element["SplitHeight"], 6);
        SplitGradientString = SettingsHelper.ParseString(element["CurrentSplitGradient"], GradientType.Vertical.ToString());
        CurrentSplitBackgroundImagePath = SettingsHelper.ParseString(element["CurrentSplitBackgroundImagePath"], string.Empty);
        CurrentSplitOutlineEnabled = SettingsHelper.ParseBool(element["CurrentSplitOutlineEnabled"], false);
        CurrentSplitOutlineThickness = (decimal)SettingsHelper.ParseFloat(element["CurrentSplitOutlineThickness"], 2f);
        CurrentSplitOutlineColor = SettingsHelper.ParseColor(element["CurrentSplitOutlineColor"], Color.White);
        CurrentSplitOutlineTransparency = (decimal)SettingsHelper.ParseFloat(element["CurrentSplitOutlineTransparency"], 0f);
        if (element["CurrentSplitImageInterpolation"] != null)
        {
            CurrentSplitImageInterpolationString = SettingsHelper.ParseString(
                element["CurrentSplitImageInterpolation"],
                CurrentSplitImageInterpolationFilter.Bilinear.ToString());
        }
        else
        {
            bool legacyBilinear = SettingsHelper.ParseBool(element["CurrentSplitBackgroundBilinear"], false);
            CurrentSplitImageInterpolation = legacyBilinear
                ? CurrentSplitImageInterpolationFilter.Bilinear
                : CurrentSplitImageInterpolationFilter.Nearest;
        }

        if (element["CurrentSplitOutlineInterpolation"] != null)
        {
            CurrentSplitOutlineInterpolationString = SettingsHelper.ParseString(
                element["CurrentSplitOutlineInterpolation"],
                CurrentSplitImageInterpolationFilter.Nearest.ToString());
        }
        else
        {
            CurrentSplitOutlineInterpolation = SettingsHelper.ParseBool(element["CurrentSplitOutlineAntiAlias"], false)
                ? CurrentSplitImageInterpolationFilter.Bilinear
                : CurrentSplitImageInterpolationFilter.Nearest;
        }

        CurrentSplitOutlineFillModeString = SettingsHelper.ParseString(
            element["CurrentSplitOutlineFillMode"],
            CurrentSplitOutlineFillMode.Solid.ToString());
        CurrentSplitOutlineGradientEndColor = SettingsHelper.ParseColor(
            element["CurrentSplitOutlineGradientEndColor"],
            Color.FromArgb(255, 80, 200));
        CurrentSplitOutlineRgbWave = SettingsHelper.ParseBool(element["CurrentSplitOutlineRgbWave"], false);
        CurrentSplitOutlineWaveAxis = SettingsHelper.ParseEnum(
            element["CurrentSplitOutlineWaveAxis"],
            CurrentSplitOutlineWaveAxis.Horizontal);
        int parsedOutlineWaveSpeed = SettingsHelper.ParseInt(element["CurrentSplitOutlineWaveSpeed"], 25);
        CurrentSplitOutlineWaveSpeed = Math.Min(
            CurrentSplitOutlinePaint.OutlineWaveSpeedSliderMaximum,
            Math.Max(0, parsedOutlineWaveSpeed));
        CurrentSplitGradientFillRgbWave = SettingsHelper.ParseBool(element["CurrentSplitGradientFillRgbWave"], false);
        CurrentSplitGradientFillWaveAxis = SettingsHelper.ParseEnum(
            element["CurrentSplitGradientFillWaveAxis"],
            CurrentSplitOutlineWaveAxis.Horizontal);
        int parsedFillWaveSpeed = SettingsHelper.ParseInt(element["CurrentSplitGradientFillWaveSpeed"], 0);
        CurrentSplitGradientFillWaveSpeed = Math.Min(
            CurrentSplitOutlinePaint.OutlineWaveSpeedSliderMaximum,
            Math.Max(0, parsedFillWaveSpeed));
        BackgroundColor = SettingsHelper.ParseColor(element["BackgroundColor"], Color.Transparent);
        BackgroundColor2 = SettingsHelper.ParseColor(element["BackgroundColor2"], Color.Transparent);
        GradientString = SettingsHelper.ParseString(element["BackgroundGradient"], ExtendedGradientType.Plain.ToString());
        SeparatorLastSplit = SettingsHelper.ParseBool(element["SeparatorLastSplit"], true);
        DropDecimals = SettingsHelper.ParseBool(element["DropDecimals"], true);
        DeltasAccuracy = SettingsHelper.ParseEnum(element["DeltasAccuracy"], TimeAccuracy.Tenths);
        OverrideDeltasColor = SettingsHelper.ParseBool(element["OverrideDeltasColor"], false);
        DeltasColor = SettingsHelper.ParseColor(element["DeltasColor"], Color.FromArgb(255, 255, 255));
        Display2Rows = SettingsHelper.ParseBool(element["Display2Rows"], false);
        SplitTimesAccuracy = SettingsHelper.ParseEnum(element["SplitTimesAccuracy"], TimeAccuracy.Seconds);
        ShowBlankSplits = SettingsHelper.ParseBool(element["ShowBlankSplits"], true);
        LockLastSplit = SettingsHelper.ParseBool(element["LockLastSplit"], false);
        IconSize = SettingsHelper.ParseFloat(element["IconSize"], 24f);
        IconShadows = SettingsHelper.ParseBool(element["IconShadows"], true);

        if (version >= new Version(1, 5))
        {
            XmlElement columnsElement = element["Columns"];
            ColumnsList.Clear();
            foreach (object child in columnsElement.ChildNodes)
            {
                var columnData = ColumnData.FromXml((XmlNode)child);
                ColumnsList.Add(new ColumnSettings(CurrentState, columnData.Name, ColumnsList) { Data = columnData });
            }
        }
        else
        {
            ColumnsList.Clear();
            string comparison = SettingsHelper.ParseString(element["Comparison"]);
            if (SettingsHelper.ParseBool(element["ShowSplitTimes"]))
            {
                ColumnsList.Add(new ColumnSettings(CurrentState, "+/-", ColumnsList) { Data = new ColumnData("+/-", ColumnType.Delta, comparison, "Current Timing Method") });
                ColumnsList.Add(new ColumnSettings(CurrentState, "Time", ColumnsList) { Data = new ColumnData("Time", ColumnType.SplitTime, comparison, "Current Timing Method") });
            }
            else
            {
                ColumnsList.Add(new ColumnSettings(CurrentState, "+/-", ColumnsList) { Data = new ColumnData("+/-", ColumnType.DeltaorSplitTime, comparison, "Current Timing Method") });
            }
        }

        if (version >= new Version(1, 3))
        {
            BeforeNamesColor = SettingsHelper.ParseColor(element["BeforeNamesColor"]);
            CurrentNamesColor = SettingsHelper.ParseColor(element["CurrentNamesColor"]);
            AfterNamesColor = SettingsHelper.ParseColor(element["AfterNamesColor"]);
            OverrideTextColor = SettingsHelper.ParseBool(element["OverrideTextColor"]);
        }
        else
        {
            if (version >= new Version(1, 2))
            {
                BeforeNamesColor = CurrentNamesColor = AfterNamesColor = SettingsHelper.ParseColor(element["SplitNamesColor"]);
            }
            else
            {
                BeforeNamesColor = Color.FromArgb(255, 255, 255);
                CurrentNamesColor = Color.FromArgb(255, 255, 255);
                AfterNamesColor = Color.FromArgb(255, 255, 255);
            }

            OverrideTextColor = !SettingsHelper.ParseBool(element["UseTextColor"], true);
        }

        SyncCurrentSplitOutlineFillModeCombo();
        ResetColumns();
    }

    public XmlNode GetSettings(XmlDocument document)
    {
        XmlElement parent = document.CreateElement("Settings");
        CreateSettingsNode(document, parent);
        return parent;
    }

    public int GetSettingsHashCode()
    {
        return CreateSettingsNode(null, null);
    }

    private int CreateSettingsNode(XmlDocument document, XmlElement parent)
    {
        int hashCode = SettingsHelper.CreateSetting(document, parent, "Version", "1.8") ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitTopColor", CurrentSplitTopColor) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitBottomColor", CurrentSplitBottomColor) ^
        SettingsHelper.CreateSetting(document, parent, "VisualSplitCount", VisualSplitCount) ^
        SettingsHelper.CreateSetting(document, parent, "SplitPreviewCount", SplitPreviewCount) ^
        SettingsHelper.CreateSetting(document, parent, "DisplayIcons", DisplayIcons) ^
        SettingsHelper.CreateSetting(document, parent, "ShowThinSeparators", ShowThinSeparators) ^
        SettingsHelper.CreateSetting(document, parent, "AlwaysShowLastSplit", AlwaysShowLastSplit) ^
        SettingsHelper.CreateSetting(document, parent, "SplitWidth", SplitWidth) ^
        SettingsHelper.CreateSetting(document, parent, "SplitTimesAccuracy", SplitTimesAccuracy) ^
        SettingsHelper.CreateSetting(document, parent, "AutomaticAbbreviations", AutomaticAbbreviations) ^
        SettingsHelper.CreateSetting(document, parent, "BeforeNamesColor", BeforeNamesColor) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentNamesColor", CurrentNamesColor) ^
        SettingsHelper.CreateSetting(document, parent, "AfterNamesColor", AfterNamesColor) ^
        SettingsHelper.CreateSetting(document, parent, "OverrideTextColor", OverrideTextColor) ^
        SettingsHelper.CreateSetting(document, parent, "BeforeTimesColor", BeforeTimesColor) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentTimesColor", CurrentTimesColor) ^
        SettingsHelper.CreateSetting(document, parent, "AfterTimesColor", AfterTimesColor) ^
        SettingsHelper.CreateSetting(document, parent, "OverrideTimesColor", OverrideTimesColor) ^
        SettingsHelper.CreateSetting(document, parent, "ShowBlankSplits", ShowBlankSplits) ^
        SettingsHelper.CreateSetting(document, parent, "LockLastSplit", LockLastSplit) ^
        SettingsHelper.CreateSetting(document, parent, "IconSize", IconSize) ^
        SettingsHelper.CreateSetting(document, parent, "IconShadows", IconShadows) ^
        SettingsHelper.CreateSetting(document, parent, "SplitHeight", SplitHeight) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitGradient", CurrentSplitGradient) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitBackgroundImagePath", CurrentSplitBackgroundImagePath ?? string.Empty) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitOutlineEnabled", CurrentSplitOutlineEnabled) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitOutlineThickness", (float)CurrentSplitOutlineThickness) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitOutlineColor", CurrentSplitOutlineColor) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitOutlineTransparency", (float)CurrentSplitOutlineTransparency) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitImageInterpolation", CurrentSplitImageInterpolation) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitOutlineInterpolation", CurrentSplitOutlineInterpolation) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitOutlineFillMode", CurrentSplitOutlineFillMode) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitOutlineGradientEndColor", CurrentSplitOutlineGradientEndColor) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitOutlineRgbWave", CurrentSplitOutlineRgbWave) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitOutlineWaveAxis", CurrentSplitOutlineWaveAxis) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitOutlineWaveSpeed", CurrentSplitOutlineWaveSpeed) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitGradientFillRgbWave", CurrentSplitGradientFillRgbWave) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitGradientFillWaveAxis", CurrentSplitGradientFillWaveAxis) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitGradientFillWaveSpeed", CurrentSplitGradientFillWaveSpeed) ^
        SettingsHelper.CreateSetting(document, parent, "BackgroundColor", BackgroundColor) ^
        SettingsHelper.CreateSetting(document, parent, "BackgroundColor2", BackgroundColor2) ^
        SettingsHelper.CreateSetting(document, parent, "BackgroundGradient", BackgroundGradient) ^
        SettingsHelper.CreateSetting(document, parent, "SeparatorLastSplit", SeparatorLastSplit) ^
        SettingsHelper.CreateSetting(document, parent, "DeltasAccuracy", DeltasAccuracy) ^
        SettingsHelper.CreateSetting(document, parent, "DropDecimals", DropDecimals) ^
        SettingsHelper.CreateSetting(document, parent, "OverrideDeltasColor", OverrideDeltasColor) ^
        SettingsHelper.CreateSetting(document, parent, "DeltasColor", DeltasColor) ^
        SettingsHelper.CreateSetting(document, parent, "Display2Rows", Display2Rows) ^
        SettingsHelper.CreateSetting(document, parent, "ShowColumnLabels", ShowColumnLabels) ^
        SettingsHelper.CreateSetting(document, parent, "LabelsColor", LabelsColor);

        XmlElement columnsElement = null;
        if (document != null)
        {
            columnsElement = document.CreateElement("Columns");
            parent.AppendChild(columnsElement);
        }

        int count = 1;
        foreach (ColumnData columnData in ColumnsList.Select(x => x.Data))
        {
            XmlElement settings = null;
            if (document != null)
            {
                settings = document.CreateElement("Settings");
                columnsElement.AppendChild(settings);
            }

            hashCode ^= columnData.CreateElement(document, settings) * count;
            count++;
        }

        return hashCode;
    }

    private void ColorButtonClick(object sender, EventArgs e)
    {
        SettingsHelper.ColorButtonClick((Button)sender, this);
    }

    private void ResetColumns()
    {
        ClearLayout();
        int index = 1;
        foreach (ColumnSettings column in ColumnsList)
        {
            UpdateLayoutForColumn();
            AddColumnToLayout(column, index);
            column.UpdateEnabledButtons();
            index++;
        }

        RefreshParentScrollExtent();
    }

    private void AddColumnToLayout(ColumnSettings column, int index)
    {
        tableColumns.Controls.Add(column, 0, index);
        tableColumns.SetColumnSpan(column, 4);
        WinFormsTheme.Apply(column);
        column.ColumnRemoved -= column_ColumnRemoved;
        column.MovedUp -= column_MovedUp;
        column.MovedDown -= column_MovedDown;
        column.ColumnRemoved += column_ColumnRemoved;
        column.MovedUp += column_MovedUp;
        column.MovedDown += column_MovedDown;
    }

    private void column_MovedDown(object sender, EventArgs e)
    {
        var column = (ColumnSettings)sender;
        int index = ColumnsList.IndexOf(column);
        ColumnsList.Remove(column);
        ColumnsList.Insert(index + 1, column);
        ResetColumns();
        column.SelectControl();
    }

    private void column_MovedUp(object sender, EventArgs e)
    {
        var column = (ColumnSettings)sender;
        int index = ColumnsList.IndexOf(column);
        ColumnsList.Remove(column);
        ColumnsList.Insert(index - 1, column);
        ResetColumns();
        column.SelectControl();
    }

    private void column_ColumnRemoved(object sender, EventArgs e)
    {
        var column = (ColumnSettings)sender;
        int index = ColumnsList.IndexOf(column);
        ColumnsList.Remove(column);
        ResetColumns();
        if (ColumnsList.Count > 0)
        {
            ColumnsList.Last().SelectControl();
        }
        else
        {
            chkColumnLabels.Select();
        }
    }

    private void ClearLayout()
    {
        tableColumns.RowCount = 1;
        tableColumns.RowStyles.Clear();
        tableColumns.RowStyles.Add(new RowStyle(SizeType.Absolute, StartingTableLayoutSize.Height));
        tableColumns.Size = StartingTableLayoutSize;
        foreach (ColumnSettings control in tableColumns.Controls.OfType<ColumnSettings>().ToList())
        {
            tableColumns.Controls.Remove(control);
        }

        groupColumns.Size = new Size(groupColumns.Size.Width, StartingGroupColumnsSize.Height);
        Size = StartingSize;
        SyncColumnsGroupLayout();
    }

    private void UpdateLayoutForColumn()
    {
        tableColumns.RowCount++;
        tableColumns.RowStyles.Add(new RowStyle(SizeType.Absolute, StartingColumnSettingHeight));
        tableColumns.Size = new Size(tableColumns.Size.Width, tableColumns.Size.Height + StartingColumnSettingHeight);
        SyncColumnsGroupLayout();
    }

    private void SyncColumnsGroupLayout()
    {
        int groupChromeHeight = Math.Max(0, StartingGroupColumnsSize.Height - StartingTableLayoutSize.Height);
        int desiredTableHeight = tableColumns.RowStyles
            .Cast<RowStyle>()
            .Where(rowStyle => rowStyle.SizeType == SizeType.Absolute)
            .Sum(rowStyle => (int)Math.Ceiling(rowStyle.Height));
        desiredTableHeight = Math.Max(StartingTableLayoutSize.Height, desiredTableHeight);
        int desiredGroupHeight = desiredTableHeight + groupChromeHeight;
        if (tableColumns.MinimumSize.Height != desiredTableHeight)
        {
            tableColumns.MinimumSize = new Size(tableColumns.MinimumSize.Width, desiredTableHeight);
        }

        if (groupColumns.MinimumSize.Height != desiredGroupHeight)
        {
            groupColumns.MinimumSize = new Size(groupColumns.MinimumSize.Width, desiredGroupHeight);
        }

        if (groupColumns.Height != desiredGroupHeight)
        {
            groupColumns.Height = desiredGroupHeight;
        }

        int row = tableLayoutPanel1.GetRow(groupColumns);
        if (row >= 0 && row < tableLayoutPanel1.RowStyles.Count)
        {
            RowStyle rowStyle = tableLayoutPanel1.RowStyles[row];
            rowStyle.SizeType = SizeType.Absolute;
            rowStyle.Height = desiredGroupHeight + groupColumns.Margin.Vertical;
        }

        int desiredHeight = Math.Max(
            StartingSize.Height,
            tableLayoutPanel1.Top + groupColumns.Top + desiredGroupHeight + groupColumns.Margin.Bottom + Padding.Bottom);
        if (Height != desiredHeight)
        {
            Height = desiredHeight;
        }

        if (MinimumSize.Height != desiredHeight)
        {
            MinimumSize = new Size(MinimumSize.Width, desiredHeight);
        }

        tableLayoutPanel1.PerformLayout();
        PerformLayout();
        for (Control parent = Parent; parent != null; parent = parent.Parent)
        {
            parent.PerformLayout();
        }

        RefreshParentScrollExtent();
    }

    private void RefreshParentScrollExtent()
    {
        if (_parentScrollRefreshPending || IsDisposed || !IsHandleCreated)
        {
            return;
        }

        _parentScrollRefreshPending = true;
        try
        {
            BeginInvoke(new Action(() =>
            {
                _parentScrollRefreshPending = false;
                if (IsDisposed)
                {
                    return;
                }

                UpdateParentPanelExtents();
            }));
        }
        catch (InvalidOperationException)
        {
            _parentScrollRefreshPending = false;
        }
    }

    private void UpdateParentPanelExtents()
    {
        for (Control ancestor = Parent; ancestor != null; ancestor = ancestor.Parent)
        {
            if (ancestor is not Panel panel)
            {
                continue;
            }

            int contentHeight = MeasureDescendantBottom(panel, panel) + panel.Padding.Bottom + 8;
            if (!panel.AutoScroll && panel.Dock != DockStyle.Fill && contentHeight > 0)
            {
                int panelHeight = Math.Max(panel.MinimumSize.Height, contentHeight);
                if (panel.Height != panelHeight)
                {
                    panel.Height = panelHeight;
                }
            }

            if (panel.AutoScroll)
            {
                int minHeight = Math.Max(panel.ClientSize.Height, contentHeight);
                var minSize = new Size(0, minHeight);
                if (!panel.AutoScrollMinSize.Equals(minSize))
                {
                    panel.AutoScrollMinSize = minSize;
                }

                panel.HorizontalScroll.Enabled = false;
                panel.HorizontalScroll.Visible = false;
                panel.HorizontalScroll.Maximum = 0;
            }
        }
    }

    private static int MeasureDescendantBottom(Panel origin, Control current)
    {
        int bottom = 0;
        int scrollY = Math.Abs(origin.AutoScrollPosition.Y);
        foreach (Control child in current.Controls)
        {
            if (!child.Visible)
            {
                continue;
            }

            Point childLocation = origin.PointToClient(child.Parent.PointToScreen(child.Location));
            bottom = Math.Max(bottom, childLocation.Y + scrollY + child.Height + child.Margin.Bottom);
            bottom = Math.Max(bottom, MeasureDescendantBottom(origin, child));
        }

        return bottom;
    }

    private void btnAddColumn_Click(object sender, EventArgs e)
    {
        UpdateLayoutForColumn();

        var columnControl = new ColumnSettings(CurrentState, "", ColumnsList);
        ColumnsList.Add(columnControl);
        AddColumnToLayout(columnControl, ColumnsList.Count);

        foreach (ColumnSettings column in ColumnsList)
        {
            column.UpdateEnabledButtons();
        }

        RefreshParentScrollExtent();
    }
}
