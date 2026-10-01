using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using System.Xml;

namespace LiveSplit.UI.Components;

// Linked into each consuming component so installing a component needs no new Core DLL.
internal sealed class ComponentIconOptions : UserControl
{
    private readonly CheckBox useCustom;
    private readonly Label status;
    private readonly TrackBar sizeSlider;
    public int IconSizePercent
    {
        get => sizeSlider.Value;
        set => sizeSlider.Value = Math.Max(10, Math.Min(200, value));
    }
    private Image customImage;
    private string imageData = string.Empty;
    private string imagePath = string.Empty;
    private int imageHash = string.Empty.GetHashCode();

    public ComponentIconOptions()
    {
        Name = "customIconOptions";
        Height = 100;
        Dock = DockStyle.Fill;
        var rows = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        useCustom = new CheckBox { Name = "chkUseCustomIcon", Text = "Use custom icon", AutoSize = true, Anchor = AnchorStyles.Left };
        var choose = new Button { Name = "btnChooseCustomIcon", Text = "Choose image...", Dock = DockStyle.Fill };
        status = new Label { Name = "lblCustomIconStatus", AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        rows.Controls.Add(useCustom, 0, 0);
        rows.Controls.Add(choose, 1, 0);
        rows.Controls.Add(status, 0, 1);
        rows.SetColumnSpan(status, 2);
        var sizeRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        sizeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        sizeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sizeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
        sizeSlider = new TrackBar { Name = "trkIconSizePercent", AccessibleName = "Icon size", Minimum = 10, Maximum = 200, Value = 100, TickStyle = TickStyle.None, Dock = DockStyle.Fill };
        var sizeValue = new Label { Text = "100%", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight };
        sizeSlider.ValueChanged += (_, _) => sizeValue.Text = sizeSlider.Value + "%";
        sizeRow.Controls.Add(new Label { Text = "Icon size:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        sizeRow.Controls.Add(sizeSlider, 1, 0);
        sizeRow.Controls.Add(sizeValue, 2, 0);
        rows.Controls.Add(sizeRow, 0, 2);
        rows.SetColumnSpan(sizeRow, 2);
        Controls.Add(rows);
        useCustom.CheckedChanged += (_, _) => UpdateStatus();
        choose.Click += (_, _) => ChooseImage();
        UpdateStatus();
    }

    public Image Resolve(Image gameIcon) => useCustom.Checked && customImage != null ? customImage : gameIcon;

    public void LoadFile(string path)
    {
        // Keep an owned bitmap in memory, but save only its source path.
        using var source = Image.FromFile(path);
        var copy = new Bitmap(source);
        customImage?.Dispose();
        customImage = copy;
        imagePath = Path.GetFullPath(path);
        imageData = string.Empty;
        imageHash = 0;
        useCustom.Checked = true;
        UpdateStatus();
    }

    private void ChooseImage()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose component icon",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico|All files|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { LoadFile(dialog.FileName); }
        catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is OutOfMemoryException
            || ex is UnauthorizedAccessException || ex is System.Runtime.InteropServices.ExternalException)
        {
            MessageBox.Show(this, "The selected file could not be loaded as an image.", "Choose image",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SetImage(string data)
    {
        if (data == imageData && string.IsNullOrEmpty(imagePath)) return;
        Image replacement = null;
        if (!string.IsNullOrEmpty(data))
        {
            using var bytes = new MemoryStream(Convert.FromBase64String(data));
            using var source = Image.FromStream(bytes);
            replacement = new Bitmap(source);
        }
        customImage?.Dispose();
        customImage = replacement;
        imageData = data ?? string.Empty;
        imagePath = string.Empty;
        imageHash = imageData.GetHashCode();
        UpdateStatus();
    }

    private void UpdateStatus() => status.Text = useCustom.Checked && customImage != null
        ? (string.IsNullOrEmpty(imagePath) ? "Custom image saved in layout" : "Image file: " + Path.GetFileName(imagePath)) : "Using game icon";

    public void Read(XmlElement element)
    {
        IconSizePercent = SettingsHelper.ParseInt(element["IconSizePercent"], 100);
        string path = SettingsHelper.ParseString(element["CustomIconPath"], string.Empty);
        try
        {
            if (string.IsNullOrWhiteSpace(path)) SetImage(SettingsHelper.ParseString(element["CustomIconImage"], string.Empty));
            else if (path != imagePath || customImage == null) LoadFile(path);
        }
        catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is OutOfMemoryException || ex is IOException || ex is UnauthorizedAccessException)
        {
            SetImage(string.Empty);
            imagePath = path;
            imageHash = 0;
        }
        useCustom.Checked = SettingsHelper.ParseBool(element["UseCustomIcon"], false);
        UpdateStatus();
    }

    public int Write(XmlDocument document, XmlElement parent)
    {
        int hash = SettingsHelper.CreateSetting(document, parent, "UseCustomIcon", useCustom.Checked);
        hash ^= SettingsHelper.CreateSetting(document, parent, "CustomIconPath", imagePath);
        if (document != null) SettingsHelper.CreateSetting(document, parent, "CustomIconImage", imageData);
        // Layout polling must not re-encode or hash a large image every frame.
        return hash ^ imageHash ^ SettingsHelper.CreateSetting(document, parent, "IconSizePercent", IconSizePercent);
    }

    public static void Append(Control settings, TableLayoutPanel table, Control control, int height)
    {
        table.SuspendLayout();
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        table.Controls.Add(control, 0, row);
        table.SetColumnSpan(control, table.ColumnCount);
        settings.Height += height;
        settings.MinimumSize = new Size(settings.MinimumSize.Width, settings.Height);
        table.ResumeLayout(true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            customImage?.Dispose();
            customImage = null;
        }
        base.Dispose(disposing);
    }
}
