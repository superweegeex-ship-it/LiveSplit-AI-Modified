using System;
using System.Drawing;
using System.Windows.Forms;

namespace CustomFontDialog;

public partial class FontDialog : Form
{
    private const string SampleText = "AaBbCcXxYyZz";
    private const string WindowSizeRegistryPath = "WindowSizes";
    private const string WindowWidthRegistryValue = "FontDialogWidth";
    private const string WindowHeightRegistryValue = "FontDialogHeight";
    private const string WindowXRegistryValue = "FontDialogX";
    private const string WindowYRegistryValue = "FontDialogY";
    private bool allowResizablePanels;
    private bool resizablePanelsConfigured;

    public FontDialog()
    {
        InitializeComponent();
        RestoreWindowSize();
        FormClosing += FontDialog_FormClosing_SaveSize;

        lstFont.SelectedFontFamilyChanged += lstFont_SelectedFontFamilyChanged;
        lstFont.SelectedFontFamily = FontFamily.GenericSansSerif;
        txtSize.Text = Convert.ToString(10);
    }

    private int minSize { get; set; }
    private int maxSize { get; set; }

    public int MinSize
    {
        get => minSize; set
        {
            minSize = value;
            UpdateSizeOptions();
        }
    }
    public int MaxSize
    {
        get => maxSize; set
        {
            maxSize = value;
            UpdateSizeOptions();
        }
    }

    private Font originalFont { get; set; }
    public Font OriginalFont { get => originalFont; set => originalFont = SelectedFont = value; }
    public bool AllowResizablePanels
    {
        get => allowResizablePanels;
        set
        {
            allowResizablePanels = value;
            if (allowResizablePanels)
            {
                ConfigureResizablePanels();
            }
        }
    }

    public Font SelectedFont
    {
        get => lblSampleText.Font;
        set
        {
            lstFont.SelectedFontFamily = value.FontFamily;
            txtSize.Text = value.Size.ToString();
            chbBold.Checked = value.Bold;
            chbItalic.Checked = value.Italic;
        }
    }

    public new event EventHandler FontChanged;

    private void ConfigureResizablePanels()
    {
        if (resizablePanelsConfigured)
        {
            return;
        }

        resizablePanelsConfigured = true;
        SuspendLayout();

        Controls.Clear();
        Padding = new Padding(0);

        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            RowCount = 2
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));

        var mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Panel1MinSize = 80,
            Panel2MinSize = 120,
            SplitterWidth = 6
        };

        var fontPanel = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 2
        };
        fontPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        fontPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        label1.Dock = DockStyle.Fill;
        label1.TextAlign = ContentAlignment.MiddleLeft;
        lstFont.Dock = DockStyle.Fill;
        fontPanel.Controls.Add(label1, 0, 0);
        fontPanel.Controls.Add(lstFont, 0, 1);
        mainSplit.Panel1.Controls.Add(fontPanel);

        var rightSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Panel1MinSize = 60,
            Panel2MinSize = 110,
            SplitterWidth = 6
        };

        var sizePanel = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 3
        };
        sizePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        sizePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
        sizePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        label2.Dock = DockStyle.Fill;
        label2.TextAlign = ContentAlignment.MiddleLeft;
        txtSize.Dock = DockStyle.Top;
        lstSize.Dock = DockStyle.Fill;
        sizePanel.Controls.Add(label2, 0, 0);
        sizePanel.Controls.Add(txtSize, 0, 1);
        sizePanel.Controls.Add(lstSize, 0, 2);
        rightSplit.Panel1.Controls.Add(sizePanel);

        var stylePreviewSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            Panel1MinSize = 70,
            Panel2MinSize = 100,
            SplitterWidth = 6
        };
        groupBox1.Dock = DockStyle.Fill;
        groupBox2.Dock = DockStyle.Fill;
        stylePreviewSplit.Panel1.Controls.Add(groupBox1);
        stylePreviewSplit.Panel2.Controls.Add(groupBox2);
        rightSplit.Panel2.Controls.Add(stylePreviewSplit);
        mainSplit.Panel2.Controls.Add(rightSplit);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        btnCancel.Anchor = AnchorStyles.None;
        btnOK.Anchor = AnchorStyles.None;
        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnOK);

        root.Controls.Add(mainSplit, 0, 0);
        root.Controls.Add(buttonPanel, 0, 1);
        Controls.Add(root);

        lblSampleText.Dock = DockStyle.Fill;
        lblSampleText.TextAlign = ContentAlignment.MiddleCenter;
        ResumeLayout(true);
        Shown += (_, _) =>
        {
            SetSafeSplitterDistance(mainSplit, 190);
            SetSafeSplitterDistance(rightSplit, 82);
            SetSafeSplitterDistance(stylePreviewSplit, 88);
        };
    }

    private static void SetSafeSplitterDistance(SplitContainer splitContainer, int desiredDistance)
    {
        int available = splitContainer.Orientation == Orientation.Vertical
            ? splitContainer.Width
            : splitContainer.Height;
        int maximum = Math.Max(splitContainer.Panel1MinSize, available - splitContainer.Panel2MinSize - splitContainer.SplitterWidth);
        int distance = Math.Max(splitContainer.Panel1MinSize, Math.Min(desiredDistance, maximum));
        if (distance > 0)
        {
            splitContainer.SplitterDistance = distance;
        }
    }

    private void lstFont_SelectedFontFamilyChanged(object sender, EventArgs e)
    {
        FontFamily family = lstFont.SelectedFontFamily;
        bool bold = family.IsStyleAvailable(FontStyle.Bold);
        bool regular = family.IsStyleAvailable(FontStyle.Regular);
        bool italics = family.IsStyleAvailable(FontStyle.Italic);

        chbBold.Enabled = true;
        chbItalic.Enabled = true;

        if (!bold && !regular)
        {
            chbBold.Enabled = false;
            chbBold.Checked = false;
            if (italics)
            {
                chbItalic.Enabled = false;
                chbItalic.Checked = true;
            }
        }
        else if (!bold)
        {
            chbBold.Enabled = false;
            chbBold.Checked = false;
        }
        else if (!regular && !italics)
        {
            chbBold.Enabled = false;
            chbBold.Checked = true;
        }

        if (!italics)
        {
            chbItalic.Enabled = false;
            chbItalic.Checked = false;
        }

        UpdateSampleText();
    }

    private void lstSize_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (lstSize.SelectedItem != null)
        {
            txtSize.Text = lstSize.SelectedItem.ToString();
        }
    }

    private void txtSize_TextChanged(object sender, EventArgs e)
    {
        if (lstSize.Items.Contains(txtSize.Text))
        {
            lstSize.SelectedItem = txtSize.Text;
        }
        else
        {
            lstSize.ClearSelected();
        }

        UpdateSampleText();
    }

    private void txtSize_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.KeyData)
        {
            case Keys.D0:
            case Keys.D1:
            case Keys.D2:
            case Keys.D3:
            case Keys.D4:
            case Keys.D5:
            case Keys.D6:
            case Keys.D7:
            case Keys.D8:
            case Keys.D9:
            case Keys.End:
            case Keys.Enter:
            case Keys.Home:
            case Keys.Back:
            case Keys.Delete:
            case Keys.Escape:
            case Keys.Left:
            case Keys.Right:
                break;
            case Keys.Decimal:
            case (Keys)190: //decimal point
                if (txtSize.Text.Contains("."))
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                }

                break;
            default:
                e.SuppressKeyPress = true;
                e.Handled = true;
                break;
        }
    }

    private void UpdateSampleText()
    {
        float size = txtSize.Text != "" ? float.Parse(txtSize.Text) : 1;
        FontFamily family = lstFont.SelectedFontFamily;

        if (family != null)
        {
            FontStyle? style = null;
            if (chbBold.Checked && family.IsStyleAvailable(FontStyle.Bold))
            {
                style = FontStyle.Bold;
            }

            if (chbItalic.Checked && family.IsStyleAvailable(FontStyle.Italic))
            {
                if (style == null)
                {
                    style = FontStyle.Italic;
                }
                else
                {
                    style |= FontStyle.Italic;
                }
            }

            if (style == null && family.IsStyleAvailable(FontStyle.Regular))
            {
                style = FontStyle.Regular;
            }

            if (style.HasValue)
            {
                if (size < 1)
                {
                    size = 1;
                }
                else if (size > float.MaxValue)
                {
                    size = float.MaxValue;
                }

                lblSampleText.Font = new Font(family, size, style.Value, GraphicsUnit.Pixel);
                lblSampleText.Text = SampleText;
                lblSampleText.Invalidate();

                TriggerFontChanged();
            }
        }
    }

    /// <summary>
    /// Handles CheckedChanged event for Bold,
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void chb_CheckedChanged(object sender, EventArgs e)
    {
        UpdateSampleText();
    }

    private void UpdateSizeOptions()
    {
        object[] sizes = [
        "8",
        "9",
        "10",
        "11",
        "12",
        "14",
        "16",
        "18",
        "20",
        "22",
        "24",
        "26",
        "28",
        "36",
        "48",
        "72"];

        lstSize.Items.Clear();
        foreach (object size in sizes)
        {
            int sizeNum = int.Parse((string)size);
            if (sizeNum >= MinSize && sizeNum <= MaxSize)
            {
                lstSize.Items.Add(size);
            }
        }
    }

    private void FontDialog_FormClosing(object sender, FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.Cancel)
        {
            SelectedFont = OriginalFont;
        }
    }

    private void FontDialog_FormClosing_SaveSize(object sender, FormClosingEventArgs e)
    {
        SaveWindowSize();
    }

    private void TriggerFontChanged()
    {
        FontChanged?.Invoke(this, new FontChangedEventArgs() { NewFont = SelectedFont });
    }

    private void RestoreWindowSize()
    {
        try
        {
            using var windowSizes = Application.UserAppDataRegistry.CreateSubKey(WindowSizeRegistryPath);
            if (windowSizes == null)
            {
                return;
            }

            object widthValue = windowSizes.GetValue(WindowWidthRegistryValue);
            object heightValue = windowSizes.GetValue(WindowHeightRegistryValue);
            object xValue = windowSizes.GetValue(WindowXRegistryValue);
            object yValue = windowSizes.GetValue(WindowYRegistryValue);
            if (widthValue is int width && heightValue is int height
                && width >= MinimumSize.Width && height >= MinimumSize.Height)
            {
                Size = new Size(width, height);
            }

            if (xValue is int x && yValue is int y)
            {
                Rectangle bounds = ClampToVisibleScreen(new Rectangle(x, y, Width, Height));
                StartPosition = FormStartPosition.Manual;
                Bounds = bounds;
            }
        }
        catch
        {
            // Ignore persisted-window-size read errors.
        }
    }

    private void SaveWindowSize()
    {
        try
        {
            using var windowSizes = Application.UserAppDataRegistry.CreateSubKey(WindowSizeRegistryPath);
            if (windowSizes == null)
            {
                return;
            }

            Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            windowSizes.SetValue(WindowWidthRegistryValue, bounds.Width);
            windowSizes.SetValue(WindowHeightRegistryValue, bounds.Height);
            windowSizes.SetValue(WindowXRegistryValue, bounds.X);
            windowSizes.SetValue(WindowYRegistryValue, bounds.Y);
        }
        catch
        {
            // Ignore persisted-window-size write errors.
        }
    }

    private static bool IsWindowBoundsVisible(Rectangle bounds)
    {
        foreach (Screen screen in Screen.AllScreens)
        {
            if (screen.WorkingArea.IntersectsWith(bounds))
            {
                return true;
            }
        }

        return false;
    }

    private Rectangle ClampToVisibleScreen(Rectangle bounds)
    {
        Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
        foreach (Screen screen in Screen.AllScreens)
        {
            if (screen.WorkingArea.IntersectsWith(bounds))
            {
                workingArea = screen.WorkingArea;
                break;
            }
        }

        int width = Math.Min(Math.Max(bounds.Width, MinimumSize.Width), workingArea.Width);
        int height = Math.Min(Math.Max(bounds.Height, MinimumSize.Height), workingArea.Height);
        int x = Math.Min(Math.Max(bounds.X, workingArea.Left), workingArea.Right - width);
        int y = Math.Min(Math.Max(bounds.Y, workingArea.Top), workingArea.Bottom - height);
        return new Rectangle(x, y, width, height);
    }
}
