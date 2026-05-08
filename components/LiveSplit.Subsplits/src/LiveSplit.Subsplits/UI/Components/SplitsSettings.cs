using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml;

using LiveSplit.Localization;
using LiveSplit.Model;
using LiveSplit.Model.Comparisons;
using LiveSplit.TimeFormatters;
using LiveSplit.UI;

namespace LiveSplit.UI.Components;

public enum CurrentSplitImageLayoutMode
{
    Fill,
    FitWidth
}

public partial class SplitsSettings : UserControl
{
    private static readonly EventHandler CurrentSplitBackgroundOnFrameChanged = static (_, _) => { };
    private static readonly EventHandler HeaderBackgroundOnFrameChanged = static (_, _) => { };
    private const string CurrentSplitImageLayoutFillText = "Fill";
    private const string CurrentSplitImageLayoutFitWidthText = "Fit Width";

    private bool _inOutlineFillModeComboSync;
    private bool _parentScrollRefreshPending;
    private Label lblCurrentSplitImageLayout;
    private ComboBox cmbCurrentSplitImageLayout;
    private Label lblCurrentSplitImageBrightness;
    private NumericUpDown nudCurrentSplitImageBrightness;
    private Label lblCurrentSplitImagePanX;
    private TrackBar trkCurrentSplitImagePanX;
    private Label lblCurrentSplitImagePanXValue;
    private Label lblCurrentSplitImagePanY;
    private TrackBar trkCurrentSplitImagePanY;
    private Label lblCurrentSplitImagePanYValue;
    private Label lblCurrentSplitImageZoom;
    private TrackBar trkCurrentSplitImageZoom;
    private Label lblCurrentSplitImageZoomValue;
    private bool syncingCurrentSplitImagePlacementControls;
    private Label lblHeaderBackgroundImage;
    private Button btnHeaderBackgroundBrowse;
    private Label lblHeaderImageFilter;
    private ComboBox cmbHeaderImageFilter;
    private Label lblHeaderImageLayout;
    private ComboBox cmbHeaderImageLayout;
    private Label lblHeaderImageBrightness;
    private NumericUpDown nudHeaderImageBrightness;
    private Label lblHeaderImagePan;
    private FlowLayoutPanel flpHeaderImagePan;
    private Label lblHeaderImagePanX;
    private NumericUpDown nudHeaderImagePanX;
    private Label lblHeaderImagePanY;
    private NumericUpDown nudHeaderImagePanY;
    private Label lblHeaderImageZoom;
    private NumericUpDown nudHeaderImageZoom;

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
    static public ISegment HilightSplit { get; set; }
    static public ISegment SectionSplit { get; set; }

    public bool AutomaticAbbreviation { get; set; }
    public Color CurrentSplitTopColor { get; set; }
    public Color CurrentSplitBottomColor { get; set; }
    public int SplitPreviewCount { get; set; }
    public int MinimumMajorSplits { get; set; }
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

    public string HeaderComparison { get; set; }
    public string HeaderTimingMethod { get; set; }
    public LiveSplitState CurrentState { get; set; }

    public bool DisplayIcons { get; set; }
    public bool IconShadows { get; set; }
    public bool IndentBlankIcons { get; set; }
    public bool ShowThinSeparators { get; set; }
    public bool AlwaysShowLastSplit { get; set; }
    public bool LockLastSplit { get; set; }
    public bool SeparatorLastSplit { get; set; }

    public bool IndentSubsplits { get; set; }
    public bool HideSubsplits { get; set; }
    public bool ShowSubsplits { get; set; }
    public bool CurrentSectionOnly { get; set; }
    public bool OverrideSubsplitColor { get; set; }
    public Color SubsplitTopColor { get; set; }
    public Color SubsplitBottomColor { get; set; }
    public GradientType SubsplitGradient { get; set; }
    public string SubsplitGradientString
    {
        get => SubsplitGradient.ToString();
        set => SubsplitGradient = (GradientType)Enum.Parse(typeof(GradientType), value);
    }

    public bool ShowHeader { get; set; }
    public bool IndentSectionSplit { get; set; }
    public bool ShowIconSectionSplit { get; set; }
    public bool ShowSectionIcon { get; set; }
    public Color HeaderTopColor { get; set; }
    public Color HeaderBottomColor { get; set; }
    public GradientType HeaderGradient { get; set; }
    public string HeaderGradientString
    {
        get => HeaderGradient.ToString();
        set => HeaderGradient = (GradientType)Enum.Parse(typeof(GradientType), value);
    }

    public string HeaderBackgroundImagePath { get; set; }

    public CurrentSplitImageInterpolationFilter HeaderImageInterpolation { get; set; }

    public string HeaderImageInterpolationString
    {
        get => HeaderImageInterpolation.ToString();
        set => HeaderImageInterpolation = (CurrentSplitImageInterpolationFilter)Enum.Parse(typeof(CurrentSplitImageInterpolationFilter), value);
    }

    public CurrentSplitImageLayoutMode HeaderImageLayout { get; set; }

    public string HeaderImageLayoutString
    {
        get => HeaderImageLayout == CurrentSplitImageLayoutMode.FitWidth
            ? CurrentSplitImageLayoutFitWidthText
            : CurrentSplitImageLayoutFillText;
        set => HeaderImageLayout = ParseCurrentSplitImageLayout(value);
    }

    public decimal HeaderImagePanX { get; set; }

    public decimal HeaderImagePanY { get; set; }

    public decimal HeaderImageZoom { get; set; }

    public decimal HeaderImageBrightness { get; set; }

    public bool OverrideHeaderColor { get; set; }
    public Color HeaderTextColor { get; set; }
    public bool HeaderText { get; set; }
    public Color HeaderTimesColor { get; set; }
    public bool HeaderTimes { get; set; }
    public TimeAccuracy HeaderAccuracy { get; set; }
    public bool SectionTimer { get; set; }
    public Color SectionTimerColor { get; set; }
    public bool SectionTimerGradient { get; set; }
    public TimeAccuracy SectionTimerAccuracy { get; set; }

    public bool DropDecimals { get; set; }
    public TimeAccuracy DeltasAccuracy { get; set; }

    public bool OverrideDeltasColor { get; set; }
    public Color DeltasColor { get; set; }

    public bool ShowColumnLabels { get; set; }
    public Color LabelsColor { get; set; }

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

    public CurrentSplitImageLayoutMode CurrentSplitImageLayout { get; set; }

    public string CurrentSplitImageLayoutString
    {
        get => CurrentSplitImageLayout == CurrentSplitImageLayoutMode.FitWidth
            ? CurrentSplitImageLayoutFitWidthText
            : CurrentSplitImageLayoutFillText;
        set => CurrentSplitImageLayout = ParseCurrentSplitImageLayout(value);
    }

    public decimal CurrentSplitImagePanX { get; set; }

    public decimal CurrentSplitImagePanY { get; set; }

    public decimal CurrentSplitImageZoom { get; set; }

    public decimal CurrentSplitImageBrightness { get; set; }

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
        ReleaseHeaderBackgroundImageResources();
        _currentSplitBackgroundImagePathLoaded = path;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            _currentSplitBackgroundImage = Image.FromFile(path);
            if (ImageAnimator.CanAnimate(_currentSplitBackgroundImage))
            {
                ImageAnimator.Animate(_currentSplitBackgroundImage, CurrentSplitBackgroundOnFrameChanged);
            }
            return _currentSplitBackgroundImage;
        }
        catch
        {
            ReleaseCurrentSplitBackgroundImageResources();
            return null;
        }
    }

    private Image _headerBackgroundImage;
    private string _headerBackgroundImagePathLoaded;

    internal void ReleaseHeaderBackgroundImageResources()
    {
        if (_headerBackgroundImage != null)
        {
            ImageAnimator.StopAnimate(_headerBackgroundImage, HeaderBackgroundOnFrameChanged);
            _headerBackgroundImage.Dispose();
            _headerBackgroundImage = null;
        }

        _headerBackgroundImagePathLoaded = null;
    }

    internal Image GetHeaderBackgroundImageForRendering()
    {
        if (HeaderGradient != GradientType.Image)
        {
            return null;
        }

        string path = HeaderBackgroundImagePath ?? string.Empty;
        if (_headerBackgroundImagePathLoaded == path && _headerBackgroundImage != null)
        {
            return _headerBackgroundImage;
        }

        ReleaseHeaderBackgroundImageResources();
        _headerBackgroundImagePathLoaded = path;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            _headerBackgroundImage = Image.FromFile(path);
            if (ImageAnimator.CanAnimate(_headerBackgroundImage))
            {
                ImageAnimator.Animate(_headerBackgroundImage, HeaderBackgroundOnFrameChanged);
            }
            return _headerBackgroundImage;
        }
        catch
        {
            ReleaseHeaderBackgroundImageResources();
            return null;
        }
    }

    public event EventHandler SplitLayoutChanged;

    public LayoutMode Mode { get; set; }

    public IList<ColumnSettings> ColumnsList { get; set; }
    public Size StartingSize { get; set; }
    public Size StartingTableLayoutSize { get; set; }
    public Size StartingGroupColumnsSize { get; set; }
    private readonly int startingColumnSettingHeight;

    public SplitsSettings(LiveSplitState state)
    {
        InitializeComponent();
        EnsureCurrentSplitImageLayoutControls();
        EnsureHeaderImageControls();
        trkCurrentSplitOutlineWaveSpeed.Maximum = CurrentSplitOutlinePaint.OutlineWaveSpeedSliderMaximum;
        trkCurrentSplitOutlineWaveSpeed.TickFrequency = 5;

        CurrentState = state;

        StartingSize = Size;
        StartingTableLayoutSize = tableColumns.Size;
        StartingGroupColumnsSize = groupColumns.Size;

        AutomaticAbbreviation = false;
        VisualSplitCount = 8;
        SplitPreviewCount = 1;
        MinimumMajorSplits = 0;
        DisplayIcons = true;
        IconShadows = true;
        ShowThinSeparators = false;
        AlwaysShowLastSplit = true;
        LockLastSplit = true;
        SeparatorLastSplit = true;
        SplitTimesAccuracy = TimeAccuracy.Seconds;
        CurrentSplitTopColor = Color.FromArgb(51, 115, 244);
        CurrentSplitBottomColor = Color.FromArgb(21, 53, 116);
        SplitWidth = 20;
        SplitHeight = 3.6f;
        ScaledSplitHeight = 60;
        IconSize = 24f;
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
        CurrentSplitImageLayout = CurrentSplitImageLayoutMode.Fill;
        CurrentSplitImagePanX = 0;
        CurrentSplitImagePanY = 0;
        CurrentSplitImageZoom = 1;
        CurrentSplitImageBrightness = 100;
        CurrentSplitOutlineInterpolation = CurrentSplitImageInterpolationFilter.Nearest;
        CurrentSplitOutlineFillMode = CurrentSplitOutlineFillMode.Solid;
        CurrentSplitOutlineGradientEndColor = Color.FromArgb(255, 80, 200);
        CurrentSplitOutlineRgbWave = false;
        CurrentSplitOutlineWaveAxis = CurrentSplitOutlineWaveAxis.Horizontal;
        CurrentSplitOutlineWaveSpeed = 25;
        nudCurrentSplitOutlineThickness.DecimalPlaces = 1;
        nudCurrentSplitOutlineThickness.Increment = 0.1m;
        nudCurrentSplitOutlineThickness.Minimum = 0.1m;
        nudCurrentSplitOutlineThickness.Maximum = 20m;
        cmbSplitGradient.SelectedIndexChanged += cmbSplitGradient_SelectedIndexChanged;
        btnCurrentSplitBackgroundBrowse.Click += btnCurrentSplitBackgroundBrowse_Click;
        Disposed += (_, _) =>
        {
            ReleaseCurrentSplitBackgroundImageResources();
            ReleaseHeaderBackgroundImageResources();
        };
        BackgroundColor = Color.Transparent;
        BackgroundColor2 = Color.FromArgb(1, 255, 255, 255);
        BackgroundGradient = ExtendedGradientType.Alternating;
        DropDecimals = true;
        DeltasAccuracy = TimeAccuracy.Tenths;
        OverrideDeltasColor = false;
        DeltasColor = Color.FromArgb(255, 255, 255);
        HeaderComparison = "Current Comparison";
        HeaderTimingMethod = "Current Timing Method";
        Display2Rows = false;
        ShowColumnLabels = false;
        LabelsColor = Color.FromArgb(255, 255, 255);

        IndentBlankIcons = true;
        IndentSubsplits = true;
        HideSubsplits = false;
        ShowSubsplits = false;
        CurrentSectionOnly = false;
        OverrideSubsplitColor = false;
        SubsplitTopColor = Color.FromArgb(0x8D, 0x00, 0x00, 0x00);
        SubsplitBottomColor = Color.Transparent;
        SubsplitGradient = GradientType.Plain;
        ShowHeader = true;
        IndentSectionSplit = true;
        ShowIconSectionSplit = true;
        ShowSectionIcon = true;
        HeaderTopColor = Color.FromArgb(0x2B, 0xFF, 0xFF, 0xFF);
        HeaderBottomColor = Color.FromArgb(0xD8, 0x00, 0x00, 0x00);
        HeaderGradient = GradientType.Vertical;
        HeaderBackgroundImagePath = string.Empty;
        HeaderImageInterpolation = CurrentSplitImageInterpolationFilter.Bilinear;
        HeaderImageLayout = CurrentSplitImageLayoutMode.Fill;
        HeaderImagePanX = 0;
        HeaderImagePanY = 0;
        HeaderImageZoom = 1;
        HeaderImageBrightness = 100;
        OverrideHeaderColor = false;
        HeaderTextColor = Color.FromArgb(255, 255, 255);
        HeaderText = true;
        HeaderTimesColor = Color.FromArgb(255, 255, 255);
        HeaderTimes = true;
        HeaderAccuracy = TimeAccuracy.Tenths;
        SectionTimer = true;
        SectionTimerColor = Color.FromArgb(0x77, 0x77, 0x77);
        SectionTimerGradient = true;
        SectionTimerAccuracy = TimeAccuracy.Tenths;

        chkAutomaticAbbreviation.DataBindings.Add("Checked", this, "AutomaticAbbreviation", false, DataSourceUpdateMode.OnPropertyChanged);
        dmnTotalSegments.DataBindings.Add("Value", this, "VisualSplitCount", false, DataSourceUpdateMode.OnPropertyChanged);
        dmnUpcomingSegments.DataBindings.Add("Value", this, "SplitPreviewCount", false, DataSourceUpdateMode.OnPropertyChanged);
        dmnMinimumMajorSplits.DataBindings.Add("Value", this, "MinimumMajorSplits", false, DataSourceUpdateMode.OnPropertyChanged);
        btnTopColor.DataBindings.Add("BackColor", this, "CurrentSplitTopColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnBottomColor.DataBindings.Add("BackColor", this, "CurrentSplitBottomColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnBeforeNamesColor.DataBindings.Add("BackColor", this, "BeforeNamesColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnCurrentNamesColor.DataBindings.Add("BackColor", this, "CurrentNamesColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnAfterNamesColor.DataBindings.Add("BackColor", this, "AfterNamesColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnBeforeTimesColor.DataBindings.Add("BackColor", this, "BeforeTimesColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnCurrentTimesColor.DataBindings.Add("BackColor", this, "CurrentTimesColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnAfterTimesColor.DataBindings.Add("BackColor", this, "AfterTimesColor", false, DataSourceUpdateMode.OnPropertyChanged);
        chkDisplayIcons.DataBindings.Add("Checked", this, "DisplayIcons", false, DataSourceUpdateMode.OnPropertyChanged);
        chkIconShadows.DataBindings.Add("Checked", this, "IconShadows", false, DataSourceUpdateMode.OnPropertyChanged);
        chkIndentBlankIcons.DataBindings.Add("Checked", this, "IndentBlankIcons", false, DataSourceUpdateMode.OnPropertyChanged);
        chkThinSeparators.DataBindings.Add("Checked", this, "ShowThinSeparators", false, DataSourceUpdateMode.OnPropertyChanged);
        chkLastSplit.DataBindings.Add("Checked", this, "AlwaysShowLastSplit", false, DataSourceUpdateMode.OnPropertyChanged);
        chkOverrideTextColor.DataBindings.Add("Checked", this, "OverrideTextColor", false, DataSourceUpdateMode.OnPropertyChanged);
        chkOverrideTimesColor.DataBindings.Add("Checked", this, "OverrideTimesColor", false, DataSourceUpdateMode.OnPropertyChanged);
        chkLockLastSplit.DataBindings.Add("Checked", this, "LockLastSplit", false, DataSourceUpdateMode.OnPropertyChanged);
        chkSeparatorLastSplit.DataBindings.Add("Checked", this, "SeparatorLastSplit", false, DataSourceUpdateMode.OnPropertyChanged);
        chkDropDecimals.DataBindings.Add("Checked", this, "DropDecimals", false, DataSourceUpdateMode.OnPropertyChanged);
        chkOverrideDeltaColor.DataBindings.Add("Checked", this, "OverrideDeltasColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnDeltaColor.DataBindings.Add("BackColor", this, "DeltasColor", false, DataSourceUpdateMode.OnPropertyChanged);

        chkIndentSubsplits.DataBindings.Add("Checked", this, "IndentSubsplits", false, DataSourceUpdateMode.OnPropertyChanged);
        chkCurrentSectionOnly.DataBindings.Add("Checked", this, "CurrentSectionOnly", false, DataSourceUpdateMode.OnPropertyChanged);
        chkOverrideSubsplitColor.DataBindings.Add("Checked", this, "OverrideSubsplitColor", false, DataSourceUpdateMode.OnPropertyChanged);
        cmbSubsplitGradient.DataBindings.Add("SelectedItem", this, "SubsplitGradientString", false, DataSourceUpdateMode.OnPropertyChanged);
        btnSubsplitTopColor.DataBindings.Add("BackColor", this, "SubsplitTopColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnSubsplitBottomColor.DataBindings.Add("BackColor", this, "SubsplitBottomColor", false, DataSourceUpdateMode.OnPropertyChanged);

        chkShowHeader.DataBindings.Add("Checked", this, "ShowHeader", false, DataSourceUpdateMode.OnPropertyChanged);
        chkIndentSectionSplit.DataBindings.Add("Checked", this, "IndentSectionSplit", false, DataSourceUpdateMode.OnPropertyChanged);
        chkShowIconSectionSplit.DataBindings.Add("Checked", this, "ShowIconSectionSplit", false, DataSourceUpdateMode.OnPropertyChanged);
        chkShowSectionIcon.DataBindings.Add("Checked", this, "ShowSectionIcon", false, DataSourceUpdateMode.OnPropertyChanged);
        btnHeaderTopColor.DataBindings.Add("BackColor", this, "HeaderTopColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnHeaderBottomColor.DataBindings.Add("BackColor", this, "HeaderBottomColor", false, DataSourceUpdateMode.OnPropertyChanged);
        EnsureHeaderGradientComboItems();
        cmbHeaderGradient.DataBindings.Add("SelectedItem", this, "HeaderGradientString", false, DataSourceUpdateMode.OnPropertyChanged);
        EnsureHeaderImageInterpolationComboItems();
        EnsureCurrentSplitImageLayoutComboItems(cmbHeaderImageLayout);
        cmbHeaderImageFilter.DataBindings.Add("SelectedItem", this, "HeaderImageInterpolationString", false, DataSourceUpdateMode.OnPropertyChanged);
        cmbHeaderImageLayout.DataBindings.Add("SelectedItem", this, "HeaderImageLayoutString", false, DataSourceUpdateMode.OnPropertyChanged);
        nudHeaderImageBrightness.DataBindings.Add("Value", this, "HeaderImageBrightness", true, DataSourceUpdateMode.OnPropertyChanged);
        nudHeaderImagePanX.DataBindings.Add("Value", this, "HeaderImagePanX", true, DataSourceUpdateMode.OnPropertyChanged);
        nudHeaderImagePanY.DataBindings.Add("Value", this, "HeaderImagePanY", true, DataSourceUpdateMode.OnPropertyChanged);
        nudHeaderImageZoom.DataBindings.Add("Value", this, "HeaderImageZoom", true, DataSourceUpdateMode.OnPropertyChanged);
        chkOverrideHeaderColor.DataBindings.Add("Checked", this, "OverrideHeaderColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnHeaderTextColor.DataBindings.Add("BackColor", this, "HeaderTextColor", false, DataSourceUpdateMode.OnPropertyChanged);
        chkHeaderText.DataBindings.Add("Checked", this, "HeaderText", false, DataSourceUpdateMode.OnPropertyChanged);
        btnHeaderTimesColor.DataBindings.Add("BackColor", this, "HeaderTimesColor", false, DataSourceUpdateMode.OnPropertyChanged);
        chkHeaderTimes.DataBindings.Add("Checked", this, "HeaderTimes", false, DataSourceUpdateMode.OnPropertyChanged);
        chkSectionTimer.DataBindings.Add("Checked", this, "SectionTimer", false, DataSourceUpdateMode.OnPropertyChanged);
        btnSectionTimerColor.DataBindings.Add("BackColor", this, "SectionTimerColor", false, DataSourceUpdateMode.OnPropertyChanged);
        chkSectionTimerGradient.DataBindings.Add("Checked", this, "SectionTimerGradient", false, DataSourceUpdateMode.OnPropertyChanged);

        trkIconSize.DataBindings.Add("Value", this, "IconSize", false, DataSourceUpdateMode.OnPropertyChanged);
        EnsureCurrentSplitGradientComboItems();
        EnsureCurrentSplitImageInterpolationComboItems();
        cmbSplitGradient.DataBindings.Add("SelectedItem", this, "SplitGradientString", false, DataSourceUpdateMode.OnPropertyChanged);
        cmbHeaderComparison.DataBindings.Add("SelectedItem", this, "HeaderComparison", false, DataSourceUpdateMode.OnPropertyChanged);
        cmbHeaderTimingMethod.DataBindings.Add("SelectedItem", this, "HeaderTimingMethod", false, DataSourceUpdateMode.OnPropertyChanged);
        cmbGradientType.DataBindings.Add("SelectedItem", this, "GradientString", false, DataSourceUpdateMode.OnPropertyChanged);
        btnColor1.DataBindings.Add("BackColor", this, "BackgroundColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnColor2.DataBindings.Add("BackColor", this, "BackgroundColor2", false, DataSourceUpdateMode.OnPropertyChanged);
        chkCurrentSplitOutline.DataBindings.Add("Checked", this, "CurrentSplitOutlineEnabled", false, DataSourceUpdateMode.OnPropertyChanged);
        nudCurrentSplitOutlineThickness.DataBindings.Add("Value", this, "CurrentSplitOutlineThickness", true, DataSourceUpdateMode.OnPropertyChanged);
        nudCurrentSplitOutlineTransparency.DataBindings.Add("Value", this, "CurrentSplitOutlineTransparency", true, DataSourceUpdateMode.OnPropertyChanged);
        btnCurrentSplitOutlineColor.DataBindings.Add("BackColor", this, "CurrentSplitOutlineColor", false, DataSourceUpdateMode.OnPropertyChanged);
        cmbCurrentSplitImageFilter.DataBindings.Add("SelectedItem", this, "CurrentSplitImageInterpolationString", false, DataSourceUpdateMode.OnPropertyChanged);
        EnsureCurrentSplitImageLayoutComboItems(cmbCurrentSplitImageLayout);
        cmbCurrentSplitImageLayout.DataBindings.Add("SelectedItem", this, "CurrentSplitImageLayoutString", false, DataSourceUpdateMode.OnPropertyChanged);
        nudCurrentSplitImageBrightness.DataBindings.Add("Value", this, "CurrentSplitImageBrightness", true, DataSourceUpdateMode.OnPropertyChanged);
        SyncCurrentSplitImagePlacementControlsFromProperties();
        cmbCurrentSplitImageFilter.SelectedIndexChanged += CurrentSplitImageFilter_SelectedIndexChanged;
        EnsureCurrentSplitOutlineInterpolationComboItems();
        cmbCurrentSplitOutlineInterpolation.DataBindings.Add("SelectedItem", this, "CurrentSplitOutlineInterpolationString", false, DataSourceUpdateMode.OnPropertyChanged);
        EnsureCurrentSplitOutlineFillModeComboItems();
        btnCurrentSplitOutlineGradientEndColor.DataBindings.Add("BackColor", this, "CurrentSplitOutlineGradientEndColor", false, DataSourceUpdateMode.OnPropertyChanged);
        chkCurrentSplitOutlineRgbWave.DataBindings.Add("Checked", this, "CurrentSplitOutlineRgbWave", false, DataSourceUpdateMode.OnPropertyChanged);
        trkCurrentSplitOutlineWaveSpeed.DataBindings.Add("Value", this, "CurrentSplitOutlineWaveSpeed", false, DataSourceUpdateMode.OnPropertyChanged);
        chkCurrentSplitOutline.CheckedChanged += (_, _) => UpdateCurrentSplitOutlineControlsEnabled();
        UpdateCurrentSplitOutlineControlsEnabled();
        UpdateCurrentSplitImageAndOutlineOptionStates();

        btnLabelColor.DataBindings.Add("BackColor", this, "LabelsColor", false, DataSourceUpdateMode.OnPropertyChanged);

        ColumnsList = [];
        ColumnsList.Add(new ColumnSettings(CurrentState, "+/-", ColumnsList) { Data = new ColumnData("+/-", ColumnType.Delta, "Current Comparison", "Current Timing Method") });
        ColumnsList.Add(new ColumnSettings(CurrentState, "Time", ColumnsList) { Data = new ColumnData("Time", ColumnType.SplitTime, "Current Comparison", "Current Timing Method") });

        startingColumnSettingHeight = ColumnsList[0].Height;
        ResetColumns();
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

    private void EnsureHeaderGradientComboItems()
    {
        EnsureGradientComboHasAllModes(cmbHeaderGradient);
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

    private void EnsureHeaderImageInterpolationComboItems()
    {
        foreach (string name in Enum.GetNames(typeof(CurrentSplitImageInterpolationFilter)))
        {
            if (!cmbHeaderImageFilter.Items.Contains(name))
            {
                cmbHeaderImageFilter.Items.Add(name);
            }
        }
    }

    private static CurrentSplitImageLayoutMode ParseCurrentSplitImageLayout(string value)
    {
        string normalized = (value ?? string.Empty).Trim().Replace(" ", string.Empty).Replace("-", string.Empty);
        return string.Equals(normalized, nameof(CurrentSplitImageLayoutMode.FitWidth), StringComparison.OrdinalIgnoreCase)
            ? CurrentSplitImageLayoutMode.FitWidth
            : CurrentSplitImageLayoutMode.Fill;
    }

    private static decimal ClampDecimal(decimal value, decimal minimum, decimal maximum)
    {
        if (value < minimum)
        {
            return minimum;
        }

        return value > maximum ? maximum : value;
    }

    private void EnsureCurrentSplitImageLayoutControls()
    {
        if (cmbCurrentSplitImageLayout != null)
        {
            return;
        }

        tableLayoutPanelCurrentSplitOutline.SuspendLayout();
        try
        {
            lblCurrentSplitImageLayout = CreateImageOptionLabel("Image layout:");
            cmbCurrentSplitImageLayout = CreateImageOptionCombo();
            cmbCurrentSplitImageLayout.SelectedIndexChanged += (_, _) =>
            {
                CurrentSplitImageLayoutMode previous = CurrentSplitImageLayout;
                CurrentSplitImageLayoutString = cmbCurrentSplitImageLayout.SelectedItem?.ToString();
                UpdateCurrentSplitImageAndOutlineOptionStates();
                if (CurrentSplitImageLayout != previous)
                {
                    SplitLayoutChanged?.Invoke(this, null);
                }
            };
            AddCurrentSplitImageOptionRow(lblCurrentSplitImageLayout, cmbCurrentSplitImageLayout);

            lblCurrentSplitImageBrightness = CreateImageOptionLabel("Image brightness (%):");
            nudCurrentSplitImageBrightness = CreateImageOptionNumeric(0m, 100m, 100m, 0);
            nudCurrentSplitImageBrightness.ValueChanged += CurrentSplitImageAppearance_ValueChanged;
            AddCurrentSplitImageOptionRow(lblCurrentSplitImageBrightness, nudCurrentSplitImageBrightness);

            lblCurrentSplitImagePanX = CreateImageOptionLabel("Pan X:");
            trkCurrentSplitImagePanX = CreateImageOptionSlider(-5000, 5000, 0);
            trkCurrentSplitImagePanX.ValueChanged += CurrentSplitImagePlacement_ValueChanged;
            lblCurrentSplitImagePanXValue = CreateImageOptionValueLabel();
            AddCurrentSplitImageOptionRow(lblCurrentSplitImagePanX, CreateImageSliderPanel(trkCurrentSplitImagePanX, lblCurrentSplitImagePanXValue));

            lblCurrentSplitImagePanY = CreateImageOptionLabel("Pan Y:");
            trkCurrentSplitImagePanY = CreateImageOptionSlider(-5000, 5000, 0);
            trkCurrentSplitImagePanY.ValueChanged += CurrentSplitImagePlacement_ValueChanged;
            lblCurrentSplitImagePanYValue = CreateImageOptionValueLabel();
            AddCurrentSplitImageOptionRow(lblCurrentSplitImagePanY, CreateImageSliderPanel(trkCurrentSplitImagePanY, lblCurrentSplitImagePanYValue));

            lblCurrentSplitImageZoom = CreateImageOptionLabel("Image zoom:");
            trkCurrentSplitImageZoom = CreateImageOptionSlider(5, 1000, 100);
            trkCurrentSplitImageZoom.ValueChanged += CurrentSplitImagePlacement_ValueChanged;
            lblCurrentSplitImageZoomValue = CreateImageOptionValueLabel();
            AddCurrentSplitImageOptionRow(lblCurrentSplitImageZoom, CreateImageSliderPanel(trkCurrentSplitImageZoom, lblCurrentSplitImageZoomValue));
        }
        finally
        {
            tableLayoutPanelCurrentSplitOutline.ResumeLayout(false);
            tableLayoutPanelCurrentSplitOutline.PerformLayout();
        }

        GrowCurrentSplitImageSettingsArea(145);
    }

    private void EnsureHeaderImageControls()
    {
        if (cmbHeaderImageLayout != null)
        {
            return;
        }

        tableLayoutPanel13.SuspendLayout();
        try
        {
            lblHeaderBackgroundImage = CreateImageOptionLabel("Image:");
            btnHeaderBackgroundBrowse = new Button
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Text = "Browse...",
                UseVisualStyleBackColor = true
            };
            btnHeaderBackgroundBrowse.Click += btnHeaderBackgroundBrowse_Click;
            AddHeaderImageOptionRow(lblHeaderBackgroundImage, btnHeaderBackgroundBrowse);

            lblHeaderImageFilter = CreateImageOptionLabel("Image filter:");
            cmbHeaderImageFilter = CreateImageOptionCombo();
            cmbHeaderImageFilter.SelectedIndexChanged += HeaderImageFilter_SelectedIndexChanged;
            AddHeaderImageOptionRow(lblHeaderImageFilter, cmbHeaderImageFilter);

            lblHeaderImageLayout = CreateImageOptionLabel("Image layout:");
            cmbHeaderImageLayout = CreateImageOptionCombo();
            cmbHeaderImageLayout.SelectedIndexChanged += (_, _) =>
            {
                CurrentSplitImageLayoutMode previous = HeaderImageLayout;
                HeaderImageLayoutString = cmbHeaderImageLayout.SelectedItem?.ToString();
                UpdateHeaderImageOptionStates();
                if (HeaderImageLayout != previous)
                {
                    SplitLayoutChanged?.Invoke(this, null);
                }
            };
            AddHeaderImageOptionRow(lblHeaderImageLayout, cmbHeaderImageLayout);

            lblHeaderImageBrightness = CreateImageOptionLabel("Image brightness (%):");
            nudHeaderImageBrightness = CreateImageOptionNumeric(0m, 100m, 100m, 0);
            nudHeaderImageBrightness.ValueChanged += HeaderImageAppearance_ValueChanged;
            AddHeaderImageOptionRow(lblHeaderImageBrightness, nudHeaderImageBrightness);

            lblHeaderImagePan = CreateImageOptionLabel("Image pan (%):");
            flpHeaderImagePan = CreateImagePanPanel();
            lblHeaderImagePanX = CreateInlineImageOptionLabel("X");
            nudHeaderImagePanX = CreateImageOptionNumeric(-500m, 500m, 0m, 1);
            nudHeaderImagePanX.ValueChanged += HeaderImagePlacement_ValueChanged;
            lblHeaderImagePanY = CreateInlineImageOptionLabel("Y");
            nudHeaderImagePanY = CreateImageOptionNumeric(-500m, 500m, 0m, 1);
            nudHeaderImagePanY.ValueChanged += HeaderImagePlacement_ValueChanged;
            flpHeaderImagePan.Controls.Add(lblHeaderImagePanX);
            flpHeaderImagePan.Controls.Add(nudHeaderImagePanX);
            flpHeaderImagePan.Controls.Add(lblHeaderImagePanY);
            flpHeaderImagePan.Controls.Add(nudHeaderImagePanY);
            AddHeaderImageOptionRow(lblHeaderImagePan, flpHeaderImagePan);

            lblHeaderImageZoom = CreateImageOptionLabel("Image zoom:");
            nudHeaderImageZoom = CreateImageOptionNumeric(0.05m, 10m, 1m, 2);
            nudHeaderImageZoom.Increment = 0.05m;
            nudHeaderImageZoom.ValueChanged += HeaderImagePlacement_ValueChanged;
            AddHeaderImageOptionRow(lblHeaderImageZoom, nudHeaderImageZoom);
        }
        finally
        {
            tableLayoutPanel13.ResumeLayout(false);
            tableLayoutPanel13.PerformLayout();
        }

        GrowHeaderImageSettingsArea(174);
    }

    private static ComboBox CreateImageOptionCombo() =>
        new()
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FormattingEnabled = true
        };

    private static Label CreateImageOptionLabel(string text) =>
        new()
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            Text = text
        };

    private static Label CreateInlineImageOptionLabel(string text) =>
        new()
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            Margin = new Padding(0, 5, 3, 0),
            Text = text
        };

    private static FlowLayoutPanel CreateImagePanPanel() =>
        new()
        {
            Anchor = AnchorStyles.Left,
            AutoSize = false,
            FlowDirection = FlowDirection.LeftToRight,
            Height = 24,
            Margin = new Padding(3, 2, 3, 2),
            Width = 200,
            WrapContents = false
        };

    private static Label CreateImageOptionValueLabel() =>
        new()
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            AutoSize = false,
            Margin = new Padding(3, 5, 0, 0),
            TextAlign = ContentAlignment.MiddleRight,
            Width = 58
        };

    private static TrackBar CreateImageOptionSlider(int minimum, int maximum, int value) =>
        new()
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            AutoSize = false,
            Height = 24,
            LargeChange = 100,
            Margin = new Padding(0, 2, 3, 0),
            Minimum = minimum,
            Maximum = maximum,
            SmallChange = 10,
            TickStyle = TickStyle.None,
            Value = value
        };

    private static TableLayoutPanel CreateImageSliderPanel(TrackBar slider, Label valueLabel)
    {
        var panel = new TableLayoutPanel
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            ColumnCount = 2,
            Height = 26,
            Margin = new Padding(3, 1, 3, 1),
            RowCount = 1
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62F));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
        panel.Controls.Add(slider, 0, 0);
        panel.Controls.Add(valueLabel, 1, 0);
        return panel;
    }

    private static NumericUpDown CreateImageOptionNumeric(decimal minimum, decimal maximum, decimal value, int decimalPlaces) =>
        new()
        {
            DecimalPlaces = decimalPlaces,
            Increment = 1m,
            Minimum = minimum,
            Maximum = maximum,
            Value = value,
            Width = 70
        };

    private void AddCurrentSplitImageOptionRow(Control label, Control control)
    {
        int row = tableLayoutPanelCurrentSplitOutline.RowCount;
        tableLayoutPanelCurrentSplitOutline.RowCount = row + 1;
        tableLayoutPanelCurrentSplitOutline.RowStyles.Add(new RowStyle(SizeType.Absolute, 29F));
        tableLayoutPanelCurrentSplitOutline.Controls.Add(label, 0, row);
        tableLayoutPanelCurrentSplitOutline.Controls.Add(control, 1, row);
        tableLayoutPanelCurrentSplitOutline.SetColumnSpan(control, 5);
    }

    private void AddHeaderImageOptionRow(Control label, Control control)
    {
        int row = tableLayoutPanel13.RowCount;
        tableLayoutPanel13.RowCount = row + 1;
        tableLayoutPanel13.RowStyles.Add(new RowStyle(SizeType.Absolute, 29F));
        tableLayoutPanel13.Controls.Add(label, 0, row);
        tableLayoutPanel13.Controls.Add(control, 1, row);
        tableLayoutPanel13.SetColumnSpan(control, 3);
    }

    private void GrowCurrentSplitImageSettingsArea(int addedHeight)
    {
        tableLayoutPanelCurrentSplitOutline.Height += addedHeight;
        grpCurrentSplitOutline.Height += addedHeight;
        tableLayoutPanel1.Height += addedHeight;
        Height += addedHeight;

        int row = tableLayoutPanel1.GetRow(grpCurrentSplitOutline);
        if (row >= 0 && row < tableLayoutPanel1.RowStyles.Count)
        {
            RowStyle style = tableLayoutPanel1.RowStyles[row];
            if (style.SizeType == SizeType.Absolute)
            {
                style.Height += addedHeight;
            }
        }
    }

    private void GrowHeaderImageSettingsArea(int addedHeight)
    {
        tableLayoutPanel13.Height += addedHeight;
        groupBox12.Height += addedHeight;
        groupBox11.Height += addedHeight;
        tableLayoutPanel1.Height += addedHeight;
        Height += addedHeight;

        int row = tableLayoutPanel1.GetRow(groupBox11);
        if (row >= 0 && row < tableLayoutPanel1.RowStyles.Count)
        {
            RowStyle style = tableLayoutPanel1.RowStyles[row];
            if (style.SizeType == SizeType.Absolute)
            {
                style.Height += addedHeight;
            }
        }
    }

    private static void EnsureCurrentSplitImageLayoutComboItems(ComboBox combo)
    {
        foreach (string name in new[] { CurrentSplitImageLayoutFillText, CurrentSplitImageLayoutFitWidthText })
        {
            if (!combo.Items.Contains(name))
            {
                combo.Items.Add(name);
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

    private void CurrentSplitImageFilter_SelectedIndexChanged(object sender, EventArgs e)
    {
        string selected = cmbCurrentSplitImageFilter.SelectedItem?.ToString();
        if (!string.IsNullOrWhiteSpace(selected))
        {
            CurrentSplitImageInterpolationFilter previous = CurrentSplitImageInterpolation;
            CurrentSplitImageInterpolationString = selected;
            if (CurrentSplitImageInterpolation != previous)
            {
                SplitLayoutChanged?.Invoke(this, null);
            }
        }
    }

    private void CurrentSplitImagePlacement_ValueChanged(object sender, EventArgs e)
    {
        if (syncingCurrentSplitImagePlacementControls)
        {
            return;
        }

        CurrentSplitImagePanX = SliderValueToPanPercent(trkCurrentSplitImagePanX.Value);
        CurrentSplitImagePanY = SliderValueToPanPercent(trkCurrentSplitImagePanY.Value);
        CurrentSplitImageZoom = SliderValueToZoom(trkCurrentSplitImageZoom.Value);
        UpdateCurrentSplitImagePlacementValueLabels();
        SplitLayoutChanged?.Invoke(this, null);
    }

    private void CurrentSplitImageAppearance_ValueChanged(object sender, EventArgs e)
    {
        CurrentSplitImageBrightness = nudCurrentSplitImageBrightness.Value;
        SplitLayoutChanged?.Invoke(this, null);
    }

    private void HeaderImageFilter_SelectedIndexChanged(object sender, EventArgs e)
    {
        string selected = cmbHeaderImageFilter.SelectedItem?.ToString();
        if (!string.IsNullOrWhiteSpace(selected))
        {
            CurrentSplitImageInterpolationFilter previous = HeaderImageInterpolation;
            HeaderImageInterpolationString = selected;
            if (HeaderImageInterpolation != previous)
            {
                SplitLayoutChanged?.Invoke(this, null);
            }
        }
    }

    private void HeaderImagePlacement_ValueChanged(object sender, EventArgs e)
    {
        HeaderImagePanX = nudHeaderImagePanX.Value;
        HeaderImagePanY = nudHeaderImagePanY.Value;
        HeaderImageZoom = nudHeaderImageZoom.Value;
        SplitLayoutChanged?.Invoke(this, null);
    }

    private void HeaderImageAppearance_ValueChanged(object sender, EventArgs e)
    {
        HeaderImageBrightness = nudHeaderImageBrightness.Value;
        SplitLayoutChanged?.Invoke(this, null);
    }

    private void SyncImageControlValues()
    {
        if (trkCurrentSplitImagePanX != null && !trkCurrentSplitImagePanX.IsDisposed)
        {
            CurrentSplitImagePanX = SliderValueToPanPercent(trkCurrentSplitImagePanX.Value);
            CurrentSplitImagePanY = SliderValueToPanPercent(trkCurrentSplitImagePanY.Value);
            CurrentSplitImageZoom = SliderValueToZoom(trkCurrentSplitImageZoom.Value);
            CurrentSplitImageBrightness = nudCurrentSplitImageBrightness.Value;
            UpdateCurrentSplitImagePlacementValueLabels();
        }

        if (nudHeaderImagePanX != null && !nudHeaderImagePanX.IsDisposed)
        {
            HeaderImagePanX = nudHeaderImagePanX.Value;
            HeaderImagePanY = nudHeaderImagePanY.Value;
            HeaderImageZoom = nudHeaderImageZoom.Value;
            HeaderImageBrightness = nudHeaderImageBrightness.Value;
        }
    }

    private void SyncCurrentSplitImagePlacementControlsFromProperties()
    {
        if (trkCurrentSplitImagePanX == null || trkCurrentSplitImagePanX.IsDisposed)
        {
            return;
        }

        syncingCurrentSplitImagePlacementControls = true;
        try
        {
            trkCurrentSplitImagePanX.Value = PanPercentToSliderValue(CurrentSplitImagePanX);
            trkCurrentSplitImagePanY.Value = PanPercentToSliderValue(CurrentSplitImagePanY);
            trkCurrentSplitImageZoom.Value = ZoomToSliderValue(CurrentSplitImageZoom);
        }
        finally
        {
            syncingCurrentSplitImagePlacementControls = false;
        }

        UpdateCurrentSplitImagePlacementValueLabels();
    }

    private void UpdateCurrentSplitImagePlacementValueLabels()
    {
        if (lblCurrentSplitImagePanXValue == null)
        {
            return;
        }

        lblCurrentSplitImagePanXValue.Text = FormatSignedPercent(SliderValueToPanPercent(trkCurrentSplitImagePanX.Value));
        lblCurrentSplitImagePanYValue.Text = FormatSignedPercent(SliderValueToPanPercent(trkCurrentSplitImagePanY.Value));
        lblCurrentSplitImageZoomValue.Text = FormatZoom(SliderValueToZoom(trkCurrentSplitImageZoom.Value));
    }

    private static int PanPercentToSliderValue(decimal value)
    {
        decimal clamped = ClampDecimal(value, -500m, 500m);
        return Math.Min(5000, Math.Max(-5000, (int)Math.Round(clamped * 10m)));
    }

    private static decimal SliderValueToPanPercent(int value)
    {
        return value / 10m;
    }

    private static int ZoomToSliderValue(decimal value)
    {
        decimal clamped = ClampDecimal(value, 0.05m, 10m);
        return Math.Min(1000, Math.Max(5, (int)Math.Round(clamped * 100m)));
    }

    private static decimal SliderValueToZoom(int value)
    {
        return value / 100m;
    }

    private static string FormatSignedPercent(decimal value)
    {
        return value > 0m
            ? $"+{value:0.#}%"
            : $"{value:0.#}%";
    }

    private static string FormatZoom(decimal value)
    {
        return $"{value:0.##}x";
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

        if (cmbCurrentSplitOutlineColorMode.Items.Contains(canonical))
        {
            cmbCurrentSplitOutlineColorMode.SelectedItem = canonical;
        }
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

    private void chkColumnLabels_CheckedChanged(object sender, EventArgs e)
    {
        btnLabelColor.Enabled = lblLabelsColor.Enabled = chkColumnLabels.Checked;
    }

    private void chkDisplayIcons_CheckedChanged(object sender, EventArgs e)
    {
        trkIconSize.Enabled = label5.Enabled = chkIconShadows.Enabled = chkDisplayIcons.Checked;
    }

    private void chkIndentBlankIcons_CheckedChanged(object sender, EventArgs e)
    {
        SplitLayoutChanged(this, null);
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
        btnColor1.Visible = cmbGradientType.SelectedItem.ToString() != "Plain";
        btnColor2.DataBindings.Clear();
        btnColor2.DataBindings.Add("BackColor", this, btnColor1.Visible ? "BackgroundColor2" : "BackgroundColor", false, DataSourceUpdateMode.OnPropertyChanged);
        GradientString = cmbGradientType.SelectedItem.ToString();
    }

    private void cmbSplitGradient_SelectedIndexChanged(object sender, EventArgs e)
    {
        string selected = cmbSplitGradient.SelectedItem?.ToString() ?? GradientType.Vertical.ToString();
        bool isImage = selected == nameof(GradientType.Image);
        bool showTwoColors = selected != nameof(GradientType.Plain) && !isImage;

        btnTopColor.Visible = showTwoColors;
        btnBottomColor.Visible = showTwoColors;
        btnCurrentSplitBackgroundBrowse.Visible = isImage;

        btnBottomColor.DataBindings.Clear();
        if (showTwoColors)
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
        SplitLayoutChanged?.Invoke(this, null);
    }

    private void btnCurrentSplitBackgroundBrowse_Click(object sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|GIF|*.gif|All files|*.*",
        };

        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        ReleaseCurrentSplitBackgroundImageResources();
        CurrentSplitBackgroundImagePath = dlg.FileName;
        SplitLayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void chkLockLastSplit_CheckedChanged(object sender, EventArgs e)
    {
        LockLastSplit = chkLockLastSplit.Checked;
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

    private void UpdateSubsplitVisibility()
    {
        if (rdoShowSubsplits.Checked)
        {
            ShowSubsplits = true;
            HideSubsplits = false;
            CurrentSectionOnly = false;
            chkCurrentSectionOnly.Enabled = false;
            chkIndentSubsplits.Enabled = true;
            chkIndentSectionSplit.Enabled = false;
            lblMinimumMajorSplits.Enabled = false;
            dmnMinimumMajorSplits.Enabled = false;
        }
        else if (rdoHideSubsplits.Checked)
        {
            ShowSubsplits = false;
            HideSubsplits = true;
            CurrentSectionOnly = chkCurrentSectionOnly.Checked;
            chkCurrentSectionOnly.Enabled = true;
            chkIndentSubsplits.Enabled = false;
            chkIndentSectionSplit.Enabled = false;
            lblMinimumMajorSplits.Enabled = false;
            dmnMinimumMajorSplits.Enabled = false;
        }
        else
        {
            ShowSubsplits = false;
            HideSubsplits = false;
            CurrentSectionOnly = chkCurrentSectionOnly.Checked;
            chkCurrentSectionOnly.Enabled = true;
            chkIndentSubsplits.Enabled = true;
            chkIndentSectionSplit.Enabled = chkIndentSubsplits.Checked;
            lblMinimumMajorSplits.Enabled = true;
            dmnMinimumMajorSplits.Enabled = true;
        }
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

    private void UpdateCurrentSplitOutlineAppearanceOptionStates()
    {
        bool on = chkCurrentSplitOutline.Checked;
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
        lblCurrentSplitImageLayout.Enabled = cmbCurrentSplitImageLayout.Enabled = imageMode;
        lblCurrentSplitImageBrightness.Enabled = nudCurrentSplitImageBrightness.Enabled = imageMode;

        bool fitWidthMode = imageMode && CurrentSplitImageLayout == CurrentSplitImageLayoutMode.FitWidth;
        lblCurrentSplitImagePanX.Enabled = trkCurrentSplitImagePanX.Enabled = lblCurrentSplitImagePanXValue.Enabled = fitWidthMode;
        lblCurrentSplitImagePanY.Enabled = trkCurrentSplitImagePanY.Enabled = lblCurrentSplitImagePanYValue.Enabled = fitWidthMode;
        lblCurrentSplitImageZoom.Enabled = trkCurrentSplitImageZoom.Enabled = lblCurrentSplitImageZoomValue.Enabled = fitWidthMode;
    }

    private void UpdateHeaderImageOptionStates()
    {
        bool imageMode = HeaderGradient == GradientType.Image;
        lblHeaderBackgroundImage.Enabled = btnHeaderBackgroundBrowse.Enabled = imageMode;
        lblHeaderImageFilter.Enabled = cmbHeaderImageFilter.Enabled = imageMode;
        lblHeaderImageLayout.Enabled = cmbHeaderImageLayout.Enabled = imageMode;
        lblHeaderImageBrightness.Enabled = nudHeaderImageBrightness.Enabled = imageMode;

        bool fitWidthMode = imageMode && HeaderImageLayout == CurrentSplitImageLayoutMode.FitWidth;
        lblHeaderImagePan.Enabled = flpHeaderImagePan.Enabled = fitWidthMode;
        lblHeaderImageZoom.Enabled = nudHeaderImageZoom.Enabled = fitWidthMode;
        foreach (Control control in flpHeaderImagePan.Controls)
        {
            control.Enabled = fitWidthMode;
        }
    }

    private void SplitsSettings_Load(object sender, EventArgs e)
    {
        EnsureCurrentSplitGradientComboItems();
        EnsureCurrentSplitImageInterpolationComboItems();
        if (cmbCurrentSplitImageFilter.Items.Contains(CurrentSplitImageInterpolationString))
        {
            cmbCurrentSplitImageFilter.SelectedItem = CurrentSplitImageInterpolationString;
        }

        EnsureCurrentSplitImageLayoutComboItems(cmbCurrentSplitImageLayout);
        if (cmbCurrentSplitImageLayout.Items.Contains(CurrentSplitImageLayoutString))
        {
            cmbCurrentSplitImageLayout.SelectedItem = CurrentSplitImageLayoutString;
        }

        EnsureHeaderGradientComboItems();
        if (cmbHeaderGradient.Items.Contains(HeaderGradientString))
        {
            cmbHeaderGradient.SelectedItem = HeaderGradientString;
        }

        EnsureHeaderImageInterpolationComboItems();
        if (cmbHeaderImageFilter.Items.Contains(HeaderImageInterpolationString))
        {
            cmbHeaderImageFilter.SelectedItem = HeaderImageInterpolationString;
        }

        EnsureCurrentSplitImageLayoutComboItems(cmbHeaderImageLayout);
        if (cmbHeaderImageLayout.Items.Contains(HeaderImageLayoutString))
        {
            cmbHeaderImageLayout.SelectedItem = HeaderImageLayoutString;
        }

        EnsureCurrentSplitOutlineInterpolationComboItems();
        if (cmbCurrentSplitOutlineInterpolation.Items.Contains(CurrentSplitOutlineInterpolationString))
        {
            cmbCurrentSplitOutlineInterpolation.SelectedItem = CurrentSplitOutlineInterpolationString;
        }

        SyncCurrentSplitOutlineFillModeCombo();

        SyncOutlineWaveAxisRadios();
        SyncCurrentSplitImagePlacementControlsFromProperties();

        ResetColumns();

        chkOverrideDeltaColor_CheckedChanged(null, null);
        chkOverrideTextColor_CheckedChanged(null, null);
        chkOverrideTimesColor_CheckedChanged(null, null);
        chkOverrideSubsplitColor_CheckedChanged(null, null);
        chkShowHeader_CheckedChanged(null, null);
        chkOverrideHeaderColor_CheckedChanged(null, null);
        chkSectionTimer_CheckedChanged(null, null);
        chkDisplayIcons_CheckedChanged(null, null);
        chkColumnLabels_CheckedChanged(null, null);

        cmbHeaderComparison.Items.Clear();
        cmbHeaderComparison.Items.Add("Current Comparison");
        cmbHeaderComparison.Items.AddRange(CurrentState.Run.Comparisons.Where(x => x != NoneComparisonGenerator.ComparisonName).ToArray());
        if (!cmbHeaderComparison.Items.Contains(HeaderComparison))
        {
            cmbHeaderComparison.Items.Add(HeaderComparison);
        }

        rdoHideSubsplits.Checked = !ShowSubsplits && HideSubsplits;
        rdoShowSubsplits.Checked = ShowSubsplits && !HideSubsplits;
        rdoNormalSubsplits.Checked = !ShowSubsplits && !HideSubsplits;

        rdoSeconds.Checked = SplitTimesAccuracy == TimeAccuracy.Seconds;
        rdoTenths.Checked = SplitTimesAccuracy == TimeAccuracy.Tenths;
        rdoHundredths.Checked = SplitTimesAccuracy == TimeAccuracy.Hundredths;
        rdoMilliseconds.Checked = SplitTimesAccuracy == TimeAccuracy.Milliseconds;

        rdoDeltaSeconds.Checked = DeltasAccuracy == TimeAccuracy.Seconds;
        rdoDeltaTenths.Checked = DeltasAccuracy == TimeAccuracy.Tenths;
        rdoDeltaHundredths.Checked = DeltasAccuracy == TimeAccuracy.Hundredths;
        rdoDeltaMilliseconds.Checked = DeltasAccuracy == TimeAccuracy.Milliseconds;

        rdoHeaderAccuracySeconds.Checked = HeaderAccuracy == TimeAccuracy.Seconds;
        rdoHeaderAccuracyTenths.Checked = HeaderAccuracy == TimeAccuracy.Tenths;
        rdoHeaderAccuracyHundredths.Checked = HeaderAccuracy == TimeAccuracy.Hundredths;
        rdoHeaderAccuracyMilliseconds.Checked = HeaderAccuracy == TimeAccuracy.Milliseconds;

        rdoSectionTimerAccuracySeconds.Checked = SectionTimerAccuracy == TimeAccuracy.Seconds;
        rdoSectionTimerAccuracyTenths.Checked = SectionTimerAccuracy == TimeAccuracy.Tenths;
        rdoSectionTimerAccuracyHundredths.Checked = SectionTimerAccuracy == TimeAccuracy.Hundredths;
        rdoSectionTimerAccuracyMilliseconds.Checked = SectionTimerAccuracy == TimeAccuracy.Milliseconds;

        cmbSplitGradient_SelectedIndexChanged(null, null);
        cmbHeaderGradient_SelectedIndexChanged(null, null);
        UpdateCurrentSplitOutlineControlsEnabled();
        UpdateCurrentSplitImageAndOutlineOptionStates();
        UpdateHeaderImageOptionStates();

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
    }

    public void SetSettings(XmlNode node)
    {
        var element = (XmlElement)node;
        Version version = SettingsHelper.ParseVersion(element["Version"]);

        ReleaseCurrentSplitBackgroundImageResources();

        AutomaticAbbreviation = SettingsHelper.ParseBool(element["AutomaticAbbreviation"], false);
        CurrentSplitTopColor = SettingsHelper.ParseColor(element["CurrentSplitTopColor"], Color.FromArgb(51, 115, 244));
        CurrentSplitBottomColor = SettingsHelper.ParseColor(element["CurrentSplitBottomColor"], Color.FromArgb(21, 53, 116));
        VisualSplitCount = SettingsHelper.ParseInt(element["VisualSplitCount"], 8);
        SplitPreviewCount = SettingsHelper.ParseInt(element["SplitPreviewCount"], 1);
        MinimumMajorSplits = SettingsHelper.ParseInt(element["MinimumMajorSplits"], 0);
        DisplayIcons = SettingsHelper.ParseBool(element["DisplayIcons"], true);
        ShowThinSeparators = SettingsHelper.ParseBool(element["ShowThinSeparators"], false);
        AlwaysShowLastSplit = SettingsHelper.ParseBool(element["AlwaysShowLastSplit"], true);
        SplitWidth = SettingsHelper.ParseFloat(element["SplitWidth"], 20);
        IndentBlankIcons = SettingsHelper.ParseBool(element["IndentBlankIcons"], true);
        IndentSubsplits = SettingsHelper.ParseBool(element["IndentSubsplits"], true);
        HideSubsplits = SettingsHelper.ParseBool(element["HideSubsplits"], false);
        ShowSubsplits = SettingsHelper.ParseBool(element["ShowSubsplits"], false);
        CurrentSectionOnly = SettingsHelper.ParseBool(element["CurrentSectionOnly"], false);
        OverrideSubsplitColor = SettingsHelper.ParseBool(element["OverrideSubsplitColor"], false);
        SubsplitTopColor = SettingsHelper.ParseColor(element["SubsplitTopColor"], Color.FromArgb(0x8D, 0x00, 0x00, 0x00));
        SubsplitBottomColor = SettingsHelper.ParseColor(element["SubsplitBottomColor"], Color.Transparent);
        SubsplitGradientString = SettingsHelper.ParseString(element["SubsplitGradient"], GradientType.Plain.ToString());
        ShowHeader = SettingsHelper.ParseBool(element["ShowHeader"], true);
        IndentSectionSplit = SettingsHelper.ParseBool(element["IndentSectionSplit"], true);
        ShowIconSectionSplit = SettingsHelper.ParseBool(element["ShowIconSectionSplit"], true);
        ShowSectionIcon = SettingsHelper.ParseBool(element["ShowSectionIcon"], true);
        HeaderTopColor = SettingsHelper.ParseColor(element["HeaderTopColor"], Color.FromArgb(0x2B, 0xFF, 0xFF, 0xFF));
        HeaderBottomColor = SettingsHelper.ParseColor(element["HeaderBottomColor"], Color.FromArgb(0xD8, 0x00, 0x00, 0x00));
        HeaderGradientString = SettingsHelper.ParseString(element["HeaderGradient"], GradientType.Vertical.ToString());
        HeaderBackgroundImagePath = SettingsHelper.ParseString(element["HeaderBackgroundImagePath"], string.Empty);
        HeaderImageInterpolationString = SettingsHelper.ParseString(
            element["HeaderImageInterpolation"],
            CurrentSplitImageInterpolationFilter.Bilinear.ToString());
        HeaderImageLayoutString = SettingsHelper.ParseString(
            element["HeaderImageLayout"],
            CurrentSplitImageLayoutMode.Fill.ToString());
        HeaderImagePanX = ClampDecimal(
            (decimal)SettingsHelper.ParseFloat(element["HeaderImagePanX"], 0f),
            -500m,
            500m);
        HeaderImagePanY = ClampDecimal(
            (decimal)SettingsHelper.ParseFloat(element["HeaderImagePanY"], 0f),
            -500m,
            500m);
        HeaderImageZoom = ClampDecimal(
            (decimal)SettingsHelper.ParseFloat(element["HeaderImageZoom"], 1f),
            0.05m,
            10m);
        HeaderImageBrightness = ClampDecimal(
            (decimal)SettingsHelper.ParseFloat(element["HeaderImageBrightness"], 100f),
            0m,
            100m);
        OverrideHeaderColor = SettingsHelper.ParseBool(element["OverrideHeaderColor"], false);
        HeaderTextColor = SettingsHelper.ParseColor(element["HeaderTextColor"], Color.FromArgb(255, 255, 255));
        HeaderText = SettingsHelper.ParseBool(element["HeaderText"], true);
        HeaderTimesColor = SettingsHelper.ParseColor(element["HeaderTimesColor"], Color.FromArgb(255, 255, 255));
        HeaderTimes = SettingsHelper.ParseBool(element["HeaderTimes"], true);
        HeaderAccuracy = SettingsHelper.ParseEnum(element["HeaderAccuracy"], TimeAccuracy.Tenths);
        SectionTimer = SettingsHelper.ParseBool(element["SectionTimer"], true);
        SectionTimerColor = SettingsHelper.ParseColor(element["SectionTimerColor"], Color.FromArgb(0x77, 0x77, 0x77));
        SectionTimerGradient = SettingsHelper.ParseBool(element["SectionTimerGradient"], true);
        SectionTimerAccuracy = SettingsHelper.ParseEnum(element["SectionTimerAccuracy"], TimeAccuracy.Tenths);
        OverrideTimesColor = SettingsHelper.ParseBool(element["OverrideTimesColor"], false);
        BeforeTimesColor = SettingsHelper.ParseColor(element["BeforeTimesColor"], Color.FromArgb(255, 255, 255));
        CurrentTimesColor = SettingsHelper.ParseColor(element["CurrentTimesColor"], Color.FromArgb(255, 255, 255));
        AfterTimesColor = SettingsHelper.ParseColor(element["AfterTimesColor"], Color.FromArgb(255, 255, 255));
        SplitHeight = SettingsHelper.ParseFloat(element["SplitHeight"], 3.6f);
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

        CurrentSplitImageLayoutString = SettingsHelper.ParseString(
            element["CurrentSplitImageLayout"],
            CurrentSplitImageLayoutMode.Fill.ToString());
        CurrentSplitImagePanX = ClampDecimal(
            (decimal)SettingsHelper.ParseFloat(element["CurrentSplitImagePanX"], 0f),
            -500m,
            500m);
        CurrentSplitImagePanY = ClampDecimal(
            (decimal)SettingsHelper.ParseFloat(element["CurrentSplitImagePanY"], 0f),
            -500m,
            500m);
        CurrentSplitImageZoom = ClampDecimal(
            (decimal)SettingsHelper.ParseFloat(element["CurrentSplitImageZoom"], 1f),
            0.05m,
            10m);
        CurrentSplitImageBrightness = ClampDecimal(
            (decimal)SettingsHelper.ParseFloat(element["CurrentSplitImageBrightness"], 100f),
            0m,
            100m);

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
        BackgroundColor = SettingsHelper.ParseColor(element["BackgroundColor"], Color.Transparent);
        BackgroundColor2 = SettingsHelper.ParseColor(element["BackgroundColor2"], Color.FromArgb(1, 255, 255, 255));
        GradientString = SettingsHelper.ParseString(element["BackgroundGradient"], ExtendedGradientType.Alternating.ToString());
        SeparatorLastSplit = SettingsHelper.ParseBool(element["SeparatorLastSplit"], true);
        DropDecimals = SettingsHelper.ParseBool(element["DropDecimals"], true);
        DeltasAccuracy = SettingsHelper.ParseEnum(element["DeltasAccuracy"], TimeAccuracy.Tenths);
        OverrideDeltasColor = SettingsHelper.ParseBool(element["OverrideDeltasColor"], false);
        DeltasColor = SettingsHelper.ParseColor(element["DeltasColor"], Color.FromArgb(255, 255, 255));
        Display2Rows = SettingsHelper.ParseBool(element["Display2Rows"], false);
        SplitTimesAccuracy = SettingsHelper.ParseEnum(element["SplitTimesAccuracy"], TimeAccuracy.Seconds);
        LockLastSplit = SettingsHelper.ParseBool(element["LockLastSplit"], true);
        IconSize = SettingsHelper.ParseFloat(element["IconSize"], 24f);
        IconShadows = SettingsHelper.ParseBool(element["IconShadows"], true);
        ShowColumnLabels = SettingsHelper.ParseBool(element["ShowColumnLabels"], false);
        LabelsColor = SettingsHelper.ParseColor(element["LabelsColor"], Color.FromArgb(255, 255, 255));

        if (version >= new Version(1, 7))
        {
            HeaderComparison = SettingsHelper.ParseString(element["HeaderComparison"], "Current Comparison");
            HeaderTimingMethod = SettingsHelper.ParseString(element["HeaderTimingMethod"], "Current Timing Method");
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
            HeaderComparison = SettingsHelper.ParseString(element["Comparison"], "Current Comparison");
            HeaderTimingMethod = "Current Timing Method";
            ColumnsList.Clear();
            if (SettingsHelper.ParseBool(element["ShowSplitTimes"]))
            {
                ColumnsList.Add(new ColumnSettings(CurrentState, "+/-", ColumnsList) { Data = new ColumnData("+/-", ColumnType.Delta, HeaderComparison, "Current Timing Method") });
                ColumnsList.Add(new ColumnSettings(CurrentState, "Time", ColumnsList) { Data = new ColumnData("Time", ColumnType.SplitTime, HeaderComparison, "Current Timing Method") });
            }
            else
            {
                ColumnsList.Add(new ColumnSettings(CurrentState, "+/-", ColumnsList) { Data = new ColumnData("+/-", ColumnType.DeltaorSplitTime, HeaderComparison, "Current Timing Method") });
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
        SyncCurrentSplitImagePlacementControlsFromProperties();
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
        SyncImageControlValues();

        int hashCode = SettingsHelper.CreateSetting(document, parent, "Version", "1.8") ^
            SettingsHelper.CreateSetting(document, parent, "AutomaticAbbreviation", AutomaticAbbreviation) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitTopColor", CurrentSplitTopColor) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitBottomColor", CurrentSplitBottomColor) ^
        SettingsHelper.CreateSetting(document, parent, "VisualSplitCount", VisualSplitCount) ^
        SettingsHelper.CreateSetting(document, parent, "SplitPreviewCount", SplitPreviewCount) ^
        SettingsHelper.CreateSetting(document, parent, "MinimumMajorSplits", MinimumMajorSplits) ^
        SettingsHelper.CreateSetting(document, parent, "DisplayIcons", DisplayIcons) ^
        SettingsHelper.CreateSetting(document, parent, "ShowThinSeparators", ShowThinSeparators) ^
        SettingsHelper.CreateSetting(document, parent, "AlwaysShowLastSplit", AlwaysShowLastSplit) ^
        SettingsHelper.CreateSetting(document, parent, "SplitWidth", SplitWidth) ^
        SettingsHelper.CreateSetting(document, parent, "SplitTimesAccuracy", SplitTimesAccuracy) ^
        SettingsHelper.CreateSetting(document, parent, "BeforeNamesColor", BeforeNamesColor) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentNamesColor", CurrentNamesColor) ^
        SettingsHelper.CreateSetting(document, parent, "AfterNamesColor", AfterNamesColor) ^
        SettingsHelper.CreateSetting(document, parent, "OverrideTextColor", OverrideTextColor) ^
        SettingsHelper.CreateSetting(document, parent, "BeforeTimesColor", BeforeTimesColor) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentTimesColor", CurrentTimesColor) ^
        SettingsHelper.CreateSetting(document, parent, "AfterTimesColor", AfterTimesColor) ^
        SettingsHelper.CreateSetting(document, parent, "OverrideTimesColor", OverrideTimesColor) ^
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
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitImageLayout", CurrentSplitImageLayout) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitImagePanX", (float)CurrentSplitImagePanX) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitImagePanY", (float)CurrentSplitImagePanY) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitImageZoom", (float)CurrentSplitImageZoom) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitImageBrightness", (float)CurrentSplitImageBrightness) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitOutlineInterpolation", CurrentSplitOutlineInterpolation) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitOutlineFillMode", CurrentSplitOutlineFillMode) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitOutlineGradientEndColor", CurrentSplitOutlineGradientEndColor) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitOutlineRgbWave", CurrentSplitOutlineRgbWave) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitOutlineWaveAxis", CurrentSplitOutlineWaveAxis) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitOutlineWaveSpeed", CurrentSplitOutlineWaveSpeed) ^
        SettingsHelper.CreateSetting(document, parent, "BackgroundColor", BackgroundColor) ^
        SettingsHelper.CreateSetting(document, parent, "BackgroundColor2", BackgroundColor2) ^
        SettingsHelper.CreateSetting(document, parent, "BackgroundGradient", BackgroundGradient) ^
        SettingsHelper.CreateSetting(document, parent, "SeparatorLastSplit", SeparatorLastSplit) ^
        SettingsHelper.CreateSetting(document, parent, "DeltasAccuracy", DeltasAccuracy) ^
        SettingsHelper.CreateSetting(document, parent, "DropDecimals", DropDecimals) ^
        SettingsHelper.CreateSetting(document, parent, "OverrideDeltasColor", OverrideDeltasColor) ^
        SettingsHelper.CreateSetting(document, parent, "DeltasColor", DeltasColor) ^
        SettingsHelper.CreateSetting(document, parent, "HeaderComparison", HeaderComparison) ^
        SettingsHelper.CreateSetting(document, parent, "HeaderTimingMethod", HeaderTimingMethod) ^
        SettingsHelper.CreateSetting(document, parent, "Display2Rows", Display2Rows) ^
        SettingsHelper.CreateSetting(document, parent, "IndentBlankIcons", IndentBlankIcons) ^
        SettingsHelper.CreateSetting(document, parent, "IndentSubsplits", IndentSubsplits) ^
        SettingsHelper.CreateSetting(document, parent, "HideSubsplits", HideSubsplits) ^
        SettingsHelper.CreateSetting(document, parent, "ShowSubsplits", ShowSubsplits) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSectionOnly", CurrentSectionOnly) ^
        SettingsHelper.CreateSetting(document, parent, "OverrideSubsplitColor", OverrideSubsplitColor) ^
        SettingsHelper.CreateSetting(document, parent, "SubsplitGradient", SubsplitGradient) ^
        SettingsHelper.CreateSetting(document, parent, "ShowHeader", ShowHeader) ^
        SettingsHelper.CreateSetting(document, parent, "IndentSectionSplit", IndentSectionSplit) ^
        SettingsHelper.CreateSetting(document, parent, "ShowIconSectionSplit", ShowIconSectionSplit) ^
        SettingsHelper.CreateSetting(document, parent, "ShowSectionIcon", ShowSectionIcon) ^
        SettingsHelper.CreateSetting(document, parent, "HeaderGradient", HeaderGradient) ^
        SettingsHelper.CreateSetting(document, parent, "HeaderBackgroundImagePath", HeaderBackgroundImagePath ?? string.Empty) ^
        SettingsHelper.CreateSetting(document, parent, "HeaderImageInterpolation", HeaderImageInterpolation) ^
        SettingsHelper.CreateSetting(document, parent, "HeaderImageLayout", HeaderImageLayout) ^
        SettingsHelper.CreateSetting(document, parent, "HeaderImagePanX", (float)HeaderImagePanX) ^
        SettingsHelper.CreateSetting(document, parent, "HeaderImagePanY", (float)HeaderImagePanY) ^
        SettingsHelper.CreateSetting(document, parent, "HeaderImageZoom", (float)HeaderImageZoom) ^
        SettingsHelper.CreateSetting(document, parent, "HeaderImageBrightness", (float)HeaderImageBrightness) ^
        SettingsHelper.CreateSetting(document, parent, "OverrideHeaderColor", OverrideHeaderColor) ^
        SettingsHelper.CreateSetting(document, parent, "HeaderText", HeaderText) ^
        SettingsHelper.CreateSetting(document, parent, "HeaderTimes", HeaderTimes) ^
        SettingsHelper.CreateSetting(document, parent, "HeaderAccuracy", HeaderAccuracy) ^
        SettingsHelper.CreateSetting(document, parent, "SectionTimer", SectionTimer) ^
        SettingsHelper.CreateSetting(document, parent, "SectionTimerGradient", SectionTimerGradient) ^
        SettingsHelper.CreateSetting(document, parent, "SectionTimerAccuracy", SectionTimerAccuracy) ^
        SettingsHelper.CreateSetting(document, parent, "SubsplitTopColor", SubsplitTopColor) ^
        SettingsHelper.CreateSetting(document, parent, "SubsplitBottomColor", SubsplitBottomColor) ^
        SettingsHelper.CreateSetting(document, parent, "HeaderTopColor", HeaderTopColor) ^
        SettingsHelper.CreateSetting(document, parent, "HeaderBottomColor", HeaderBottomColor) ^
        SettingsHelper.CreateSetting(document, parent, "HeaderTextColor", HeaderTextColor) ^
        SettingsHelper.CreateSetting(document, parent, "HeaderTimesColor", HeaderTimesColor) ^
        SettingsHelper.CreateSetting(document, parent, "SectionTimerColor", SectionTimerColor) ^
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

    private void rdoHideSubsplits_CheckedChanged(object sender, EventArgs e)
    {
        UpdateSubsplitVisibility();
    }

    private void rdoNormalSubsplits_CheckedChanged(object sender, EventArgs e)
    {
        UpdateSubsplitVisibility();
    }

    private void rdoShowSubsplits_CheckedChanged(object sender, EventArgs e)
    {
        UpdateSubsplitVisibility();
    }

    private void chkOverrideSubsplitColor_CheckedChanged(object sender, EventArgs e)
    {
        label15.Enabled = btnSubsplitTopColor.Enabled = btnSubsplitBottomColor.Enabled = cmbSubsplitGradient.Enabled = chkOverrideSubsplitColor.Checked;
    }

    private void cmbSubsplitGradient_SelectedIndexChanged(object sender, EventArgs e)
    {
        btnSubsplitTopColor.Visible = cmbSubsplitGradient.SelectedItem.ToString() != "Plain";
        btnSubsplitBottomColor.DataBindings.Clear();
        btnSubsplitBottomColor.DataBindings.Add("BackColor", this, btnSubsplitTopColor.Visible ? "SubsplitBottomColor" : "SubsplitTopColor", false, DataSourceUpdateMode.OnPropertyChanged);
        SubsplitGradientString = cmbSubsplitGradient.SelectedItem.ToString();
    }

    private void chkShowHeader_CheckedChanged(object sender, EventArgs e)
    {
        chkShowSectionIcon.Enabled = groupBox12.Enabled = groupBox13.Enabled
            = lblComparison.Enabled = cmbHeaderComparison.Enabled = lblTimingMethod.Enabled = cmbHeaderTimingMethod.Enabled
            = chkShowHeader.Checked;
    }

    private void cmbHeaderGradient_SelectedIndexChanged(object sender, EventArgs e)
    {
        string selected = cmbHeaderGradient.SelectedItem?.ToString() ?? GradientType.Vertical.ToString();
        bool isImage = selected == nameof(GradientType.Image);
        bool showTwoColors = selected != nameof(GradientType.Plain) && !isImage;

        btnHeaderTopColor.Visible = showTwoColors;
        btnHeaderBottomColor.Visible = showTwoColors;
        btnHeaderBottomColor.DataBindings.Clear();
        if (showTwoColors)
        {
            btnHeaderBottomColor.DataBindings.Add("BackColor", this, "HeaderBottomColor", false, DataSourceUpdateMode.OnPropertyChanged);
        }
        else if (!isImage)
        {
            btnHeaderBottomColor.DataBindings.Add("BackColor", this, "HeaderTopColor", false, DataSourceUpdateMode.OnPropertyChanged);
        }

        HeaderGradientString = selected;

        if (HeaderGradient != GradientType.Image)
        {
            ReleaseHeaderBackgroundImageResources();
        }

        UpdateHeaderImageOptionStates();
        SplitLayoutChanged?.Invoke(this, null);
    }

    private void btnHeaderBackgroundBrowse_Click(object sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|GIF|*.gif|All files|*.*",
        };

        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        ReleaseHeaderBackgroundImageResources();
        HeaderBackgroundImagePath = dlg.FileName;
        SplitLayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void chkOverrideHeaderColor_CheckedChanged(object sender, EventArgs e)
    {
        label17.Enabled = btnHeaderTextColor.Enabled = label18.Enabled = btnHeaderTimesColor.Enabled = chkOverrideHeaderColor.Checked;
    }

    private void UpdateHeaderAccuracy()
    {
        if (rdoHeaderAccuracySeconds.Checked)
        {
            HeaderAccuracy = TimeAccuracy.Seconds;
        }
        else if (rdoHeaderAccuracyTenths.Checked)
        {
            HeaderAccuracy = TimeAccuracy.Tenths;
        }
        else if (rdoHeaderAccuracyHundredths.Checked)
        {
            HeaderAccuracy = TimeAccuracy.Hundredths;
        }
        else
        {
            HeaderAccuracy = TimeAccuracy.Milliseconds;
        }
    }

    private void rdoHeaderAccuracySeconds_CheckedChanged(object sender, EventArgs e)
    {
        UpdateHeaderAccuracy();
    }

    private void rdoHeaderAccuracyTenths_CheckedChanged(object sender, EventArgs e)
    {
        UpdateHeaderAccuracy();
    }

    private void rdoHeaderAccuracyHundredths_CheckedChanged(object sender, EventArgs e)
    {
        UpdateHeaderAccuracy();
    }

    private void chkSectionTimer_CheckedChanged(object sender, EventArgs e)
    {
        label19.Enabled = btnSectionTimerColor.Enabled = chkSectionTimerGradient.Enabled = groupBox14.Enabled = chkSectionTimer.Checked;
    }

    private void UpdateSectionTimerAccuracy()
    {
        if (rdoSectionTimerAccuracySeconds.Checked)
        {
            SectionTimerAccuracy = TimeAccuracy.Seconds;
        }
        else if (rdoSectionTimerAccuracyTenths.Checked)
        {
            SectionTimerAccuracy = TimeAccuracy.Tenths;
        }
        else if (rdoSectionTimerAccuracyHundredths.Checked)
        {
            SectionTimerAccuracy = TimeAccuracy.Hundredths;
        }
        else
        {
            SectionTimerAccuracy = TimeAccuracy.Milliseconds;
        }
    }

    private void rdoSectionTimerAccuracySeconds_CheckedChanged(object sender, EventArgs e)
    {
        UpdateSectionTimerAccuracy();
    }

    private void rdoSectionTimerAccuracyTenths_CheckedChanged(object sender, EventArgs e)
    {
        UpdateSectionTimerAccuracy();
    }

    private void rdoSectionTimerAccuracyHundredths_CheckedChanged(object sender, EventArgs e)
    {
        UpdateSectionTimerAccuracy();
    }

    private void cmbHeaderComparison_SelectedIndexChanged(object sender, EventArgs e)
    {
        HeaderComparison = cmbHeaderComparison.SelectedItem.ToString();
    }

    private void cmbHeaderTimingMethod_SelectedIndexChanged(object sender, EventArgs e)
    {
        HeaderTimingMethod = cmbHeaderTimingMethod.SelectedItem.ToString();
    }

    private void chkIndentSubsplits_CheckedChanged(object sender, EventArgs e)
    {
        if (!ShowSubsplits && !HideSubsplits)
        {
            chkIndentSectionSplit.Enabled = chkIndentSubsplits.Checked;
        }
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
        tableColumns.RowStyles.Add(new RowStyle(SizeType.Absolute, startingColumnSettingHeight));
        tableColumns.Size = new Size(tableColumns.Size.Width, tableColumns.Size.Height + startingColumnSettingHeight);
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

    private void chkAutomaticAbbreviation_CheckedChanged(object sender, EventArgs e)
    {
        AutomaticAbbreviation = chkAutomaticAbbreviation.Checked;
    }
}
