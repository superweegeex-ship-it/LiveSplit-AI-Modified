using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Web;
using System.Windows.Forms;
using System.Xml;

using LiveSplit.Localization;
using LiveSplit.Model;
using LiveSplit.Options;
using LiveSplit.TimeFormatters;
using LiveSplit.UI;

namespace LiveSplit.Video;

public partial class VideoSettings : UserControl
{
    private static string T(string source) => UiLocalizer.Translate(source, LanguageResolver.ResolveCurrentCultureLanguage());

    public string MRL
    {
        get
        {
            if (InputType == VideoInputType.VlcMrl)
            {
                return VideoSource;
            }

            return HttpUtility.UrlPathEncode("file:///" + VideoPath.Replace('\\', '/').Replace("%", "%25"));
        }
    }
    public VideoInputType InputType { get; set; }
    public string VideoPath { get; set; }
    public string VideoSource { get; set; }
    public TimeSpan Offset { get; set; }
    public new float Height { get; set; }
    public new float Width { get; set; }
    public LayoutMode Mode { get; set; }

    protected ITimeFormatter TimeFormatter { get; set; }

    public string OffsetString
    {
        get => TimeFormatter.Format(Offset);
        set
        {
            if (Regex.IsMatch(value, "[^0-9:.,-]"))
            {
                return;
            }

            try
            {
                Offset = TimeSpanParser.Parse(value);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }
    }

    public VideoSettings()
    {
        InitializeComponent();

        TimeFormatter = new ShortTimeFormatter();

        VideoPath = "";
        VideoSource = "";
        InputType = VideoInputType.File;
        Width = 200;
        Height = 200;
        Offset = TimeSpan.Zero;

        cmbInputType.DataSource = Enum.GetValues(typeof(VideoInputType));
        cmbInputType.DataBindings.Add("SelectedItem", this, "InputType", false, DataSourceUpdateMode.OnPropertyChanged);
        txtVideoPath.DataBindings.Add("Text", this, "VideoPath", false, DataSourceUpdateMode.OnPropertyChanged);
        txtOffset.DataBindings.Add("Text", this, "OffsetString");
    }

    public void SetSettings(XmlNode node)
    {
        var element = (XmlElement)node;
        VideoPath = SettingsHelper.ParseString(element["VideoPath"]);
        VideoSource = SettingsHelper.ParseString(element["VideoSource"]);
        InputType = SettingsHelper.ParseEnum(element["InputType"], VideoInputType.File);
        OffsetString = SettingsHelper.ParseString(element["Offset"]);
        Height = SettingsHelper.ParseFloat(element["Height"]);
        Width = SettingsHelper.ParseFloat(element["Width"]);
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
        return SettingsHelper.CreateSetting(document, parent, "Version", "1.4") ^
        SettingsHelper.CreateSetting(document, parent, "InputType", InputType) ^
        SettingsHelper.CreateSetting(document, parent, "VideoPath", VideoPath) ^
        SettingsHelper.CreateSetting(document, parent, "VideoSource", VideoSource) ^
        SettingsHelper.CreateSetting(document, parent, "Offset", OffsetString) ^
        SettingsHelper.CreateSetting(document, parent, "Height", Height) ^
        SettingsHelper.CreateSetting(document, parent, "Width", Width);
    }

    private void btnSelectFile_Click(object sender, EventArgs e)
    {
        if (InputType == VideoInputType.VlcMrl)
        {
            string value = VideoSource;
            if (InputBox.Show(T("Set VLC MRL"), T("MRL (examples: dshow://, screen://):"), ref value) == DialogResult.OK)
            {
                VideoSource = value;
                txtVideoPath.Text = value;
            }

            return;
        }

        var dialog = new OpenFileDialog()
        {
            Filter = T("Video Files|*.avi;*.mpeg;*.mpg;*.mp4;*.mov;*.wmv;*.m4v;*.flv;*.mkv;*.ogg|All Files (*.*)|*.*")
        };
        if (File.Exists(VideoPath))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(VideoPath);
            dialog.FileName = Path.GetFileName(VideoPath);
        }

        DialogResult result = dialog.ShowDialog();
        if (result == DialogResult.OK)
        {
            VideoPath = txtVideoPath.Text = dialog.FileName;
        }
    }

    private void cmbInputType_SelectedIndexChanged(object sender, EventArgs e)
    {
        bool isMrl = InputType == VideoInputType.VlcMrl;
        btnSelectFile.Text = isMrl ? T("Set...") : T("Browse...");
        label1.Text = isMrl ? T("Video MRL:") : T("Video Path:");
        txtVideoPath.Text = isMrl ? VideoSource : VideoPath;
    }

    private void txtVideoPath_TextChanged(object sender, EventArgs e)
    {
        if (InputType == VideoInputType.VlcMrl)
        {
            VideoSource = txtVideoPath.Text;
        }
        else
        {
            VideoPath = txtVideoPath.Text;
        }
    }

    private void VideoSettings_Load(object sender, EventArgs e)
    {
        if (Mode == LayoutMode.Horizontal)
        {
            trkHeightWidth.DataBindings.Clear();
            trkHeightWidth.Minimum = 100;
            trkHeightWidth.Maximum = 400;
            trkHeightWidth.DataBindings.Add("Value", this, "Width", false, DataSourceUpdateMode.OnPropertyChanged);
            lblHeightWidth.Text = T("Width:");
        }
        else
        {
            trkHeightWidth.DataBindings.Clear();
            trkHeightWidth.Minimum = 100;
            trkHeightWidth.Maximum = 300;
            trkHeightWidth.DataBindings.Add("Value", this, "Height", false, DataSourceUpdateMode.OnPropertyChanged);
            lblHeightWidth.Text = T("Height:");
        }
    }
}

public enum VideoInputType
{
    File,
    VlcMrl,
}
