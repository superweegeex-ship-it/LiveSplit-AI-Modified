using System;
using System.Drawing;
using System.Windows.Forms;
using System.Xml;

using LiveSplit.Model;

namespace LiveSplit.UI.Components;

public partial class TextComponentSettings : UserControl
{
    private readonly ComponentIconOptions iconOptions;
    private TrackBar textPosition;
    private TrackBar iconPosition;
    public bool DisplayGameIcon { get; set; }
    public bool IconOnRight { get; set; }
    public int TextHorizontalOffset { get => textPosition.Value; set => textPosition.Value = Math.Max(-100, Math.Min(100, value)); }
    public int IconHorizontalOffset { get => iconPosition.Value; set => iconPosition.Value = Math.Max(-100, Math.Min(100, value)); }
    public int IconSizePercent { get => iconOptions.IconSizePercent; set => iconOptions.IconSizePercent = value; }
    public Image ResolveIcon(Image gameIcon) => iconOptions.Resolve(gameIcon);
    public void LoadCustomIcon(string path) => iconOptions.LoadFile(path);
    public Color TextColor { get; set; }
    public bool OverrideTextColor { get; set; }
    public Color TimeColor { get; set; }
    public bool OverrideTimeColor { get; set; }

    public Color BackgroundColor { get; set; }
    public Color BackgroundColor2 { get; set; }
    public GradientType BackgroundGradient { get; set; }
    public string GradientString
    {
        get => BackgroundGradient.ToString();
        set => BackgroundGradient = (GradientType)Enum.Parse(typeof(GradientType), value);
    }

    public string Text1 { get; set; }
    public string Text2 { get; set; }

    // Legacy font overrides — read from old configs, not written or exposed in UI
    public bool OverrideFont1 { get; set; }
    public Font Font1 { get; set; }
    public bool OverrideFont2 { get; set; }
    public Font Font2 { get; set; }

    public LayoutMode Mode { get; set; }
    public bool Display2Rows { get; set; }
    public bool CustomVariable { get; set; }

    public LiveSplitState CurrentState { get; set; }

    public TextComponentSettings()
    {
        InitializeComponent();
        iconOptions = new ComponentIconOptions();
        var show = new CheckBox { Name = "chkDisplayGameIcon", Text = "Show icon", AutoSize = true };
        show.DataBindings.Add("Checked", this, "DisplayGameIcon", false, DataSourceUpdateMode.OnPropertyChanged);
        var right = new CheckBox { Name = "chkIconOnRight", Text = "Icon on right", AutoSize = true };
        right.DataBindings.Add("Checked", this, "IconOnRight", false, DataSourceUpdateMode.OnPropertyChanged);
        var toggles = new FlowLayoutPanel { Dock = DockStyle.Fill };
        toggles.Controls.Add(show); toggles.Controls.Add(right);
        ComponentIconOptions.Append(this, tableLayoutPanel1, toggles, 30);
        textPosition = AddPositionSlider("Text position:", "trkTextHorizontalOffset");
        iconPosition = AddPositionSlider("Icon position:", "trkIconHorizontalOffset");
        ComponentIconOptions.Append(this, tableLayoutPanel1, iconOptions, 106);

        TextColor = Color.FromArgb(255, 255, 255);
        OverrideTextColor = false;
        TimeColor = Color.FromArgb(255, 255, 255);
        OverrideTimeColor = false;
        BackgroundColor = Color.Transparent;
        BackgroundColor2 = Color.Transparent;
        BackgroundGradient = GradientType.Plain;
        Text1 = "Text";
        Text2 = "";

        chkCustomVariable.DataBindings.Add("Checked", this, "CustomVariable", false, DataSourceUpdateMode.OnPropertyChanged);

        chkOverrideTextColor.DataBindings.Add("Checked", this, "OverrideTextColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnTextColor.DataBindings.Add("BackColor", this, "TextColor", false, DataSourceUpdateMode.OnPropertyChanged);
        chkOverrideTimeColor.DataBindings.Add("Checked", this, "OverrideTimeColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnTimeColor.DataBindings.Add("BackColor", this, "TimeColor", false, DataSourceUpdateMode.OnPropertyChanged);

        cmbGradientType.SelectedIndexChanged += cmbGradientType_SelectedIndexChanged;
        cmbGradientType.DataBindings.Add("SelectedItem", this, "GradientString", false, DataSourceUpdateMode.OnPropertyChanged);
        btnColor1.DataBindings.Add("BackColor", this, "BackgroundColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnColor2.DataBindings.Add("BackColor", this, "BackgroundColor2", false, DataSourceUpdateMode.OnPropertyChanged);
        txtOne.DataBindings.Add("Text", this, "Text1");
        txtTwo.DataBindings.Add("Text", this, "Text2");
    }

    private void chkOverrideTimeColor_CheckedChanged(object sender, EventArgs e)
    {
        label2.Enabled = btnTimeColor.Enabled = chkOverrideTimeColor.Checked;
    }

    private TrackBar AddPositionSlider(string label, string name)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
        var slider = new TrackBar { Name = name, AccessibleName = label, Dock = DockStyle.Fill, Minimum = -100, Maximum = 100, TickStyle = TickStyle.None };
        var valueLabel = new Label { Text = "0 px", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight };
        slider.ValueChanged += (_, _) => valueLabel.Text = slider.Value + " px";
        row.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        row.Controls.Add(slider, 1, 0); row.Controls.Add(valueLabel, 2, 0);
        ComponentIconOptions.Append(this, tableLayoutPanel1, row, 36);
        return slider;
    }

    private void chkOverrideTextColor_CheckedChanged(object sender, EventArgs e)
    {
        label1.Enabled = btnTextColor.Enabled = chkOverrideTextColor.Checked;
    }

    private void TextComponentSettings_Load(object sender, EventArgs e)
    {
        chkCustomVariable_CheckedChanged(null, null);
        chkOverrideTextColor_CheckedChanged(null, null);
        chkOverrideTimeColor_CheckedChanged(null, null);
        if (Mode == LayoutMode.Horizontal)
        {
            chkTwoRows.Enabled = false;
            chkTwoRows.DataBindings.Clear();
            chkTwoRows.Checked = true;
        }
        else
        {
            chkTwoRows.Enabled = true;
            chkTwoRows.DataBindings.Clear();
            chkTwoRows.DataBindings.Add("Checked", this, "Display2Rows", false, DataSourceUpdateMode.OnPropertyChanged);
        }
    }

    private void chkCustomVariable_CheckedChanged(object sender, EventArgs e)
    {
        CustomVariable = chkCustomVariable.Checked;
        label4.Text = CustomVariable ? "Custom Variable Name:" : "Text:";
    }

    private void cmbGradientType_SelectedIndexChanged(object sender, EventArgs e)
    {
        btnColor1.Visible = cmbGradientType.SelectedItem.ToString() != "Plain";
        btnColor2.DataBindings.Clear();
        btnColor2.DataBindings.Add("BackColor", this, btnColor1.Visible ? "BackgroundColor2" : "BackgroundColor", false, DataSourceUpdateMode.OnPropertyChanged);
        GradientString = cmbGradientType.SelectedItem.ToString();
    }

    public void SetSettings(XmlNode node)
    {
        var element = (XmlElement)node;
        iconOptions.Read(element);
        DisplayGameIcon = SettingsHelper.ParseBool(element["DisplayGameIcon"], false);
        IconOnRight = SettingsHelper.ParseBool(element["IconOnRight"], false);
        TextHorizontalOffset = SettingsHelper.ParseInt(element["TextHorizontalOffset"], 0);
        IconHorizontalOffset = SettingsHelper.ParseInt(element["IconHorizontalOffset"], 0);
        TextColor = SettingsHelper.ParseColor(element["TextColor"]);
        OverrideTextColor = SettingsHelper.ParseBool(element["OverrideTextColor"]);
        TimeColor = SettingsHelper.ParseColor(element["TimeColor"]);
        OverrideTimeColor = SettingsHelper.ParseBool(element["OverrideTimeColor"]);
        BackgroundColor = SettingsHelper.ParseColor(element["BackgroundColor"]);
        BackgroundColor2 = SettingsHelper.ParseColor(element["BackgroundColor2"]);
        GradientString = SettingsHelper.ParseString(element["BackgroundGradient"]);
        Text1 = SettingsHelper.ParseString(element["Text1"]);
        Text2 = SettingsHelper.ParseString(element["Text2"]);
        Font1 = SettingsHelper.GetFontFromElement(element["Font1"]);
        Font2 = SettingsHelper.GetFontFromElement(element["Font2"]);
        OverrideFont1 = SettingsHelper.ParseBool(element["OverrideFont1"]);
        OverrideFont2 = SettingsHelper.ParseBool(element["OverrideFont2"]);
        Display2Rows = SettingsHelper.ParseBool(element["Display2Rows"], false);
        CustomVariable = SettingsHelper.ParseBool(element["CustomVariable"], false);
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
        return iconOptions.Write(document, parent) ^
        SettingsHelper.CreateSetting(document, parent, "DisplayGameIcon", DisplayGameIcon) ^
        SettingsHelper.CreateSetting(document, parent, "IconOnRight", IconOnRight) ^
        SettingsHelper.CreateSetting(document, parent, "TextHorizontalOffset", TextHorizontalOffset) ^
        SettingsHelper.CreateSetting(document, parent, "IconHorizontalOffset", IconHorizontalOffset) ^
        SettingsHelper.CreateSetting(document, parent, "Version", "1.5") ^
        SettingsHelper.CreateSetting(document, parent, "TextColor", TextColor) ^
        SettingsHelper.CreateSetting(document, parent, "OverrideTextColor", OverrideTextColor) ^
        SettingsHelper.CreateSetting(document, parent, "TimeColor", TimeColor) ^
        SettingsHelper.CreateSetting(document, parent, "OverrideTimeColor", OverrideTimeColor) ^
        SettingsHelper.CreateSetting(document, parent, "BackgroundColor", BackgroundColor) ^
        SettingsHelper.CreateSetting(document, parent, "BackgroundColor2", BackgroundColor2) ^
        SettingsHelper.CreateSetting(document, parent, "BackgroundGradient", BackgroundGradient) ^
        SettingsHelper.CreateSetting(document, parent, "Text1", Text1) ^
        SettingsHelper.CreateSetting(document, parent, "Text2", Text2) ^
        SettingsHelper.CreateSetting(document, parent, "Display2Rows", Display2Rows) ^
        SettingsHelper.CreateSetting(document, parent, "CustomVariable", CustomVariable);
    }

    private void ColorButtonClick(object sender, EventArgs e)
    {
        SettingsHelper.ColorButtonClick((Button)sender, this);
    }
}
