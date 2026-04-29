using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Windows.Forms;
using System.Xml;

using Fetze.WinFormsColor;
using LiveSplit.Options;
using Microsoft.Win32;

namespace LiveSplit.UI;

public class SettingsHelper
{
    public static CustomFontDialog.FontDialog GetFontDialog(Font previousFont, int minSize, int maxSize)
    {
        var dialog = new CustomFontDialog.FontDialog
        {
            OriginalFont = previousFont,
            MinSize = minSize,
            MaxSize = maxSize,
            AllowResizablePanels = WinFormsTheme.AllowDialogPanelResizing
        };
        WinFormsTheme.Apply(dialog);
        return dialog;
    }

    public static string FormatFont(Font font)
    {
        return $"{font.FontFamily.Name} {font.Style}";
    }

    public static void ColorButtonClick(Button button, Control control)
    {
        var picker = new ColorPickerDialog();
        Color initial = button.BackColor;
        picker.OldColor = initial;
        picker.SelectedColor = initial;
        // Do not assign button.BackColor from SelectedColorChanged: rapid updates while dragging
        // push through DataBindings and can intermittently throw ArgumentOutOfRangeException from the binder.
        if (picker.ShowDialog(control) == DialogResult.OK)
        {
            button.BackColor = picker.SelectedColor;
        }
    }

    public static Color ParseColor(XmlElement colorElement, Color defaultColor = default)
    {
        return colorElement != null
            ? Color.FromArgb(int.Parse(colorElement.InnerText, NumberStyles.HexNumber))
            : defaultColor;
    }

    public static Font GetFontFromElement(XmlElement element)
    {
        if (element != null && !element.IsEmpty)
        {
            var bf = new BinaryFormatter();

            string base64String = element.InnerText;
            byte[] data = Convert.FromBase64String(base64String);
            var ms = new MemoryStream(data);
            return (Font)bf.Deserialize(ms);
        }

        return null;
    }

    public static int CreateSetting(XmlDocument document, XmlElement parent, string elementName, Font font)
    {
        if (document != null)
        {
            XmlElement element = document.CreateElement(elementName);

            if (font != null)
            {
                using var ms = new MemoryStream();
                var bf = new BinaryFormatter();

                bf.Serialize(ms, font);
                byte[] data = ms.ToArray();
                XmlCDataSection cdata = document.CreateCDataSection(Convert.ToBase64String(data));
                element.InnerXml = cdata.OuterXml;
            }

            parent.AppendChild(element);
        }

        return getFontHashCode(font);
    }

    private static int getFontHashCode(Font font)
    {
        int hash = 17;
        unchecked
        {
            hash = (hash * 23) + font.Name.GetHashCode();
            hash = (hash * 23) + font.FontFamily.GetHashCode();
            hash = (hash * 23) + font.Size.GetHashCode();
            hash = (hash * 23) + font.Style.GetHashCode();
        }

        return hash;
    }

    public static int CreateSetting(XmlDocument document, XmlElement parent, string elementName, Image image)
    {
        if (document != null)
        {
            XmlElement element = document.CreateElement(elementName);

            if (image != null)
            {
                using var ms = new MemoryStream();
                var bf = new BinaryFormatter();

                bf.Serialize(ms, image);
                byte[] data = ms.ToArray();
                XmlCDataSection cdata = document.CreateCDataSection(Convert.ToBase64String(data));
                element.InnerXml = cdata.OuterXml;
            }

            parent.AppendChild(element);
        }

        return image != null ? image.GetHashCode() : 0;
    }

    public static Image GetImageFromElement(XmlElement element)
    {
        if (element != null && !element.IsEmpty)
        {
            var bf = new BinaryFormatter();

            string base64String = element.InnerText;
            byte[] data = Convert.FromBase64String(base64String);

            using var ms = new MemoryStream(data);
            return (Image)bf.Deserialize(ms);
        }

        return null;
    }

    public static bool ParseBool(XmlElement boolElement, bool defaultBool = false)
    {
        return boolElement != null
            ? bool.Parse(boolElement.InnerText)
            : defaultBool;
    }

    public static bool TryParseBool(XmlElement boolElement, out bool result, bool defaultBool = false)
    {
        if (boolElement != null && bool.TryParse(boolElement.InnerText, out result))
        {
            return true;
        }

        result = defaultBool;
        return false;
    }

    public static int ParseInt(XmlElement intElement, int defaultInt = 0)
    {
        return intElement != null
            ? int.Parse(intElement.InnerText)
            : defaultInt;
    }

    public static bool TryParseInt(XmlElement intElement, out int result, int defaultInt = 0)
    {
        if (intElement != null && int.TryParse(intElement.InnerText, out result))
        {
            return true;
        }

        result = defaultInt;
        return false;
    }

    public static float ParseFloat(XmlElement floatElement, float defaultFloat = 0f)
    {
        return floatElement != null
            ? float.Parse(floatElement.InnerText.Replace(',', '.'), CultureInfo.InvariantCulture)
            : defaultFloat;
    }

    public static bool TryParseFloat(XmlElement floatElement, out float result, float defaultFloat = 0f)
    {
        if (floatElement != null && float.TryParse(floatElement.InnerText, out result))
        {
            return true;
        }

        result = defaultFloat;
        return false;
    }

    public static double ParseDouble(XmlElement doubleElement, double defaultDouble = 0.0)
    {
        return doubleElement != null
            ? double.Parse(doubleElement.InnerText, CultureInfo.InvariantCulture)
            : defaultDouble;
    }

    public static bool TryParseDouble(XmlElement doubleElement, out double result, double defaultDouble = 0.0)
    {
        if (doubleElement != null && double.TryParse(doubleElement.InnerText, out result))
        {
            return true;
        }

        result = defaultDouble;
        return false;
    }

    public static string ParseString(XmlElement stringElement, string defaultString = null)
    {
        defaultString ??= string.Empty;

        return stringElement != null
            ? stringElement.InnerText
            : defaultString;
    }

    public static TimeSpan ParseTimeSpan(XmlElement timeSpanElement, TimeSpan defaultTimeSpan = default)
    {
        return timeSpanElement != null
            ? TimeSpan.Parse(timeSpanElement.InnerText)
            : defaultTimeSpan;
    }

    public static bool TryParseTimeSpan(XmlElement timeSpanElement, out TimeSpan result,
        TimeSpan defaultTimeSpan = default)
    {
        if (timeSpanElement != null && TimeSpan.TryParse(timeSpanElement.InnerText, out result))
        {
            return true;
        }

        result = defaultTimeSpan;
        return false;
    }

    public static XmlElement ToElement<T>(XmlDocument document, string name, T value)
    {
        XmlElement element = document.CreateElement(name);
        element.InnerText = value?.ToString();
        return element;
    }

    public static int CreateSetting(XmlDocument document, XmlElement parent, string name, Color color)
    {
        if (document != null)
        {
            XmlElement element = document.CreateElement(name);
            element.InnerText = color.ToArgb().ToString("X8");
            parent.AppendChild(element);
        }

        return color.GetHashCode();
    }

    public static int CreateSetting<T>(XmlDocument document, XmlElement parent, string name, T value)
    {
        if (document != null)
        {
            XmlElement element = document.CreateElement(name);
            element.InnerText = value?.ToString();
            parent.AppendChild(element);
        }

        return value != null ? value.GetHashCode() : 0;
    }

    public static int CreateSetting(XmlDocument document, XmlElement parent, string name, float value)
    {
        if (document != null)
        {
            XmlElement element = document.CreateElement(name);
            element.InnerText = value.ToString(CultureInfo.InvariantCulture);
            parent.AppendChild(element);
        }

        return value.GetHashCode();
    }

    public static int CreateSetting(XmlDocument document, XmlElement parent, string name, double value)
    {
        if (document != null)
        {
            XmlElement element = document.CreateElement(name);
            element.InnerText = value.ToString(CultureInfo.InvariantCulture);
            parent.AppendChild(element);
        }

        return value.GetHashCode();
    }

    public static XmlAttribute ToAttribute<T>(XmlDocument document, string name, T value)
    {
        XmlAttribute element = document.CreateAttribute(name);
        element.Value = value?.ToString();
        return element;
    }

    public static T ParseEnum<T>(XmlElement element, T defaultEnum = default)
    {
        return element != null
            ? (T)Enum.Parse(typeof(T), element.InnerText)
            : defaultEnum;
    }

    public static bool TryParseEnum<T>(XmlElement element, out T result, T defaultEnum = default) where T : struct
    {
        if (element != null && Enum.TryParse(element.InnerText, out result))
        {
            return true;
        }

        result = defaultEnum;
        return false;
    }

    public static Version ParseVersion(XmlElement element)
    {
        return element != null
            ? Version.Parse(element.InnerText)
            : new Version(1, 0, 0, 0);
    }

    public static bool TryParseVersion(XmlElement element, out Version result)
    {
        if (element != null && Version.TryParse(element.InnerText, out result))
        {
            return true;
        }

        result = new Version(1, 0, 0, 0);
        return false;
    }

    public static Version ParseAttributeVersion(XmlElement element)
    {
        return element.HasAttribute("version")
            ? Version.Parse(element.GetAttribute("version"))
            : new Version(1, 0, 0, 0);
    }

    public static bool TryParseAttributeVersion(XmlElement element, out Version result)
    {
        if (element != null && element.HasAttribute("version")
            && Version.TryParse(element.GetAttribute("version"), out result))
        {
            return true;
        }

        result = new Version(1, 0, 0, 0);
        return false;
    }
}

public static class WinFormsTheme
{
    private const string WindowsPersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;
    private const int DWMWA_COLOR_DEFAULT = -1;

    private static readonly Color DarkBackColor = Color.FromArgb(32, 32, 32);
    private static readonly Color DarkPanelColor = Color.FromArgb(38, 38, 38);
    private static readonly Color DarkInputColor = Color.FromArgb(50, 50, 50);
    private static readonly Color DarkBorderColor = Color.FromArgb(48, 48, 48);
    private static readonly Color DarkDisabledButtonBorderColor = Color.FromArgb(190, 70, 70);
    private static readonly Color DarkSelectionColor = Color.FromArgb(82, 82, 82);
    private static readonly Color DarkForeColor = Color.White;
    private static readonly Color DarkDisabledForeColor = Color.FromArgb(145, 145, 145);
    private static readonly ToolStripRenderer DarkToolStripRenderer = new ToolStripProfessionalRenderer(new DarkToolStripColorTable());

    public static AppTheme CurrentTheme { get; set; } = AppTheme.Light;
    public static bool AllowDialogPanelResizing { get; set; }

    public static bool IsDarkActive => Resolve(CurrentTheme) == AppTheme.Dark;

    public static AppTheme Resolve(AppTheme theme)
    {
        if (theme != AppTheme.MatchSystem)
        {
            return theme;
        }

        try
        {
            using RegistryKey key = Registry.CurrentUser.OpenSubKey(WindowsPersonalizeKey);
            object value = key?.GetValue(AppsUseLightThemeValue);
            if (value is int intValue)
            {
                return intValue == 0 ? AppTheme.Dark : AppTheme.Light;
            }
        }
        catch
        {
            // Registry access can be denied in constrained environments; Light is the safest default.
        }

        return AppTheme.Light;
    }

    public static void Apply(Control root)
    {
        if (root == null)
        {
            return;
        }

        if (IsDarkActive)
        {
            ApplyDark(root);
        }
        else
        {
            ApplyLight(root);
        }
    }

    public static void Apply(ToolStrip toolStrip)
    {
        if (toolStrip == null)
        {
            return;
        }

        if (IsDarkActive)
        {
            ApplyDarkToolStrip(toolStrip);
        }
        else
        {
            ApplyLightToolStrip(toolStrip);
        }
    }

    public static Rectangle ClampToVisibleScreen(Rectangle bounds, Size minimumSize)
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

        int width = Math.Min(Math.Max(bounds.Width, minimumSize.Width), workingArea.Width);
        int height = Math.Min(Math.Max(bounds.Height, minimumSize.Height), workingArea.Height);
        int x = Math.Min(Math.Max(bounds.X, workingArea.Left), workingArea.Right - width);
        int y = Math.Min(Math.Max(bounds.Y, workingArea.Top), workingArea.Bottom - height);
        return new Rectangle(x, y, width, height);
    }

    private static void ApplyDark(Control control)
    {
        if (control is Form form)
        {
            form.BackColor = DarkBackColor;
            form.ForeColor = DarkForeColor;
            ApplyWindowChrome(form, true);
        }
        else if (control is TextBoxBase || control is DomainUpDown || control is NumericUpDown)
        {
            control.BackColor = DarkInputColor;
            control.ForeColor = DarkForeColor;
        }
        else if (control is ComboBox comboBox)
        {
            ApplyDarkComboBox(comboBox);
        }
        else if (control is ListBox listBox)
        {
            ApplyDarkListBox(listBox);
        }
        else if (control is Button button)
        {
            ApplyDarkButton(button);
        }
        else if (control is TrackBar trackBar)
        {
            trackBar.BackColor = DarkPanelColor;
            trackBar.ForeColor = DarkForeColor;
        }
        else if (control is ProgressBar progressBar)
        {
            progressBar.BackColor = DarkInputColor;
            progressBar.ForeColor = DarkForeColor;
        }
        else if (control is ScrollBar scrollBar)
        {
            scrollBar.BackColor = DarkPanelColor;
            scrollBar.ForeColor = DarkForeColor;
        }
        else if (control is CheckBox checkBox)
        {
            checkBox.UseVisualStyleBackColor = false;
            checkBox.BackColor = Color.Transparent;
            checkBox.ForeColor = control.Enabled ? DarkForeColor : DarkDisabledForeColor;
        }
        else if (control is RadioButton radioButton)
        {
            radioButton.UseVisualStyleBackColor = false;
            radioButton.BackColor = Color.Transparent;
            radioButton.ForeColor = control.Enabled ? DarkForeColor : DarkDisabledForeColor;
        }
        else if (control is GroupBox groupBox)
        {
            groupBox.BackColor = DarkPanelColor;
            groupBox.ForeColor = DarkForeColor;
            groupBox.Paint -= DarkGroupBox_Paint;
            groupBox.Paint += DarkGroupBox_Paint;
        }
        else if (control is Label || control is LinkLabel)
        {
            control.BackColor = Color.Transparent;
            control.ForeColor = control.Enabled ? DarkForeColor : DarkDisabledForeColor;
        }
        else if (control is TabControl tabControl)
        {
            ApplyDarkTabControl(tabControl);
        }
        else if (control is TabPage || control is Panel || control is TableLayoutPanel || control is FlowLayoutPanel || control is UserControl)
        {
            control.BackColor = DarkPanelColor;
            control.ForeColor = DarkForeColor;
        }
        else
        {
            control.BackColor = DarkBackColor;
            control.ForeColor = DarkForeColor;
        }

        if (control.ContextMenuStrip != null)
        {
            ApplyDarkToolStrip(control.ContextMenuStrip);
        }

        if (control is DataGridView grid)
        {
            ApplyDarkGrid(grid);
        }

        foreach (Control child in control.Controls)
        {
            ApplyDark(child);
        }
    }

    private static void ApplyDarkButton(Button button)
    {
        bool preservesExplicitColor = PreservesExplicitColor(button);
        button.UseVisualStyleBackColor = false;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = button.Enabled ? DarkBorderColor : DarkDisabledButtonBorderColor;
        button.FlatAppearance.MouseOverBackColor = DarkSelectionColor;
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(64, 64, 64);
        button.ForeColor = DarkForeColor;
        button.EnabledChanged -= DarkButton_EnabledChanged;
        button.EnabledChanged += DarkButton_EnabledChanged;
        button.Paint -= DarkDisabledButton_Paint;
        button.Paint += DarkDisabledButton_Paint;

        if (!preservesExplicitColor)
        {
            button.BackColor = DarkInputColor;
        }

        button.Invalidate();
    }

    private static void ApplyLight(Control control)
    {
        if (control is Form form)
        {
            form.BackColor = SystemColors.Control;
            form.ForeColor = SystemColors.ControlText;
            ApplyWindowChrome(form, false);
        }

        if (control is TextBoxBase || control is ListBox || control is ComboBox || control is DomainUpDown || control is NumericUpDown)
        {
            control.BackColor = SystemColors.Window;
            control.ForeColor = SystemColors.ControlText;
        }
        if (control is ComboBox comboBox)
        {
            comboBox.DrawItem -= DarkComboBox_DrawItem;
            comboBox.DrawMode = DrawMode.Normal;
            comboBox.FlatStyle = FlatStyle.Standard;
        }
        else if (control is ListBox listBox)
        {
            listBox.DrawItem -= DarkListBox_DrawItem;
            listBox.DrawMode = DrawMode.Normal;
        }
        else if (control is Button button)
        {
            button.EnabledChanged -= DarkButton_EnabledChanged;
            button.Paint -= DarkDisabledButton_Paint;
            button.FlatStyle = FlatStyle.Standard;
            button.FlatAppearance.BorderColor = SystemColors.ControlDark;
            if (!PreservesExplicitColor(button))
            {
                button.UseVisualStyleBackColor = true;
                button.BackColor = SystemColors.Control;
                button.ForeColor = SystemColors.ControlText;
            }
        }
        else if (control is TrackBar trackBar)
        {
            trackBar.BackColor = SystemColors.Control;
            trackBar.ForeColor = SystemColors.ControlText;
        }
        else if (control is CheckBox checkBox)
        {
            checkBox.UseVisualStyleBackColor = true;
            checkBox.BackColor = Color.Transparent;
            checkBox.ForeColor = SystemColors.ControlText;
        }
        else if (control is RadioButton radioButton)
        {
            radioButton.UseVisualStyleBackColor = true;
            radioButton.BackColor = Color.Transparent;
            radioButton.ForeColor = SystemColors.ControlText;
        }
        else if (control is GroupBox groupBox)
        {
            groupBox.Paint -= DarkGroupBox_Paint;
            groupBox.BackColor = Color.Transparent;
            groupBox.ForeColor = SystemColors.ControlText;
        }
        else if (control is Label || control is LinkLabel)
        {
            control.BackColor = Color.Transparent;
            control.ForeColor = SystemColors.ControlText;
        }
        else if (control is TabControl tabControl)
        {
            tabControl.DrawItem -= DarkTabControl_DrawItem;
            tabControl.DrawMode = TabDrawMode.Normal;
            tabControl.BackColor = SystemColors.Control;
            tabControl.ForeColor = SystemColors.ControlText;
        }
        else if (control is TabPage || control is Panel || control is TableLayoutPanel || control is FlowLayoutPanel || control is UserControl)
        {
            control.BackColor = SystemColors.Control;
            control.ForeColor = SystemColors.ControlText;
        }

        if (control.ContextMenuStrip != null)
        {
            ApplyLightToolStrip(control.ContextMenuStrip);
        }

        if (control is DataGridView grid)
        {
            grid.BackgroundColor = SystemColors.Control;
            grid.GridColor = SystemColors.ControlDark;
            grid.DefaultCellStyle.BackColor = SystemColors.Window;
            grid.DefaultCellStyle.ForeColor = SystemColors.ControlText;
            grid.DefaultCellStyle.SelectionBackColor = SystemColors.Highlight;
            grid.DefaultCellStyle.SelectionForeColor = SystemColors.HighlightText;
            grid.ColumnHeadersDefaultCellStyle.BackColor = SystemColors.Control;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = SystemColors.ControlText;
            grid.EnableHeadersVisualStyles = true;
        }

        foreach (Control child in control.Controls)
        {
            ApplyLight(child);
        }
    }

    private static bool PreservesExplicitColor(Button button)
    {
        return button.DataBindings["BackColor"] != null
            || button.FlatStyle == FlatStyle.Popup
            || button.BackgroundImage != null;
    }

    private static void DarkButton_EnabledChanged(object sender, EventArgs e)
    {
        if (sender is Button button)
        {
            button.FlatAppearance.BorderColor = button.Enabled ? DarkBorderColor : DarkDisabledButtonBorderColor;
            button.ForeColor = DarkForeColor;
            button.Invalidate();
        }
    }

    private static void DarkDisabledButton_Paint(object sender, PaintEventArgs e)
    {
        if (sender is not Button button || button.Enabled || !IsDarkActive)
        {
            return;
        }

        using var border = new Pen(DarkDisabledButtonBorderColor);
        e.Graphics.DrawRectangle(border, 0, 0, button.Width - 1, button.Height - 1);
        if (!string.IsNullOrEmpty(button.Text))
        {
            TextRenderer.DrawText(
                e.Graphics,
                button.Text,
                button.Font,
                button.ClientRectangle,
                Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    private static void ApplyDarkGrid(DataGridView grid)
    {
        grid.BackgroundColor = DarkPanelColor;
        grid.GridColor = DarkBorderColor;
        grid.DefaultCellStyle.BackColor = DarkInputColor;
        grid.DefaultCellStyle.ForeColor = DarkForeColor;
        grid.DefaultCellStyle.SelectionBackColor = DarkSelectionColor;
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.ColumnHeadersDefaultCellStyle.BackColor = DarkBackColor;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = DarkForeColor;
        grid.EnableHeadersVisualStyles = false;
    }

    private static void ApplyDarkComboBox(ComboBox comboBox)
    {
        comboBox.BackColor = DarkInputColor;
        comboBox.ForeColor = DarkForeColor;
        comboBox.FlatStyle = FlatStyle.Flat;
        comboBox.DrawItem -= DarkComboBox_DrawItem;
        comboBox.DrawItem += DarkComboBox_DrawItem;
        comboBox.DrawMode = DrawMode.OwnerDrawFixed;
    }

    private static void ApplyDarkListBox(ListBox listBox)
    {
        listBox.BackColor = DarkInputColor;
        listBox.ForeColor = DarkForeColor;
        listBox.DrawItem -= DarkListBox_DrawItem;
        listBox.DrawItem += DarkListBox_DrawItem;
        listBox.DrawMode = DrawMode.OwnerDrawFixed;
    }

    private static void ApplyDarkTabControl(TabControl tabControl)
    {
        tabControl.BackColor = DarkPanelColor;
        tabControl.ForeColor = DarkForeColor;
        tabControl.DrawItem -= DarkTabControl_DrawItem;
        tabControl.DrawItem += DarkTabControl_DrawItem;
        tabControl.DrawMode = TabDrawMode.OwnerDrawFixed;

        foreach (TabPage page in tabControl.TabPages)
        {
            page.BackColor = DarkPanelColor;
            page.ForeColor = DarkForeColor;
        }
    }

    private static void DarkTabControl_DrawItem(object sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabControl || e.Index < 0 || e.Index >= tabControl.TabPages.Count)
        {
            return;
        }

        bool selected = e.Index == tabControl.SelectedIndex;
        Rectangle bounds = tabControl.GetTabRect(e.Index);
        using var background = new SolidBrush(selected ? DarkInputColor : DarkPanelColor);
        using var border = new Pen(DarkBorderColor);
        e.Graphics.FillRectangle(background, bounds);
        e.Graphics.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        TextRenderer.DrawText(
            e.Graphics,
            tabControl.TabPages[e.Index].Text,
            e.Font,
            bounds,
            DarkForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static void DarkGroupBox_Paint(object sender, PaintEventArgs e)
    {
        if (sender is not GroupBox groupBox)
        {
            return;
        }

        e.Graphics.Clear(DarkPanelColor);
        Size textSize = TextRenderer.MeasureText(groupBox.Text, groupBox.Font);
        var borderRect = new Rectangle(
            0,
            Math.Max(textSize.Height / 2, 1),
            groupBox.Width - 1,
            groupBox.Height - Math.Max(textSize.Height / 2, 1) - 1);
        using var border = new Pen(DarkBorderColor);
        e.Graphics.DrawRectangle(border, borderRect);
        TextRenderer.DrawText(
            e.Graphics,
            groupBox.Text,
            groupBox.Font,
            new Point(8, 0),
            groupBox.Enabled ? DarkForeColor : DarkDisabledForeColor,
            TextFormatFlags.NoPadding);
    }

    private static void DarkComboBox_DrawItem(object sender, DrawItemEventArgs e)
    {
        if (sender is not ComboBox comboBox || e.Index < 0)
        {
            return;
        }

        DrawDarkItem(e, comboBox.Items[e.Index]?.ToString(), comboBox.Enabled);
    }

    private static void DarkListBox_DrawItem(object sender, DrawItemEventArgs e)
    {
        if (sender is not ListBox listBox || e.Index < 0)
        {
            return;
        }

        DrawDarkItem(e, listBox.Items[e.Index]?.ToString(), listBox.Enabled);
    }

    private static void DrawDarkItem(DrawItemEventArgs e, string text, bool enabled)
    {
        bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        using var background = new SolidBrush(selected ? DarkSelectionColor : DarkInputColor);
        e.Graphics.FillRectangle(background, e.Bounds);
        TextRenderer.DrawText(
            e.Graphics,
            text ?? string.Empty,
            e.Font,
            e.Bounds,
            enabled ? DarkForeColor : DarkDisabledForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static void ApplyDarkToolStrip(ToolStrip toolStrip)
    {
        toolStrip.BackColor = DarkInputColor;
        toolStrip.ForeColor = DarkForeColor;
        toolStrip.Renderer = DarkToolStripRenderer;
        ApplyDarkToolStripItems(toolStrip.Items);
    }

    private static void ApplyDarkToolStripItems(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            item.BackColor = DarkInputColor;
            item.ForeColor = item.Enabled ? DarkForeColor : DarkDisabledForeColor;
            if (item is ToolStripDropDownItem dropDownItem)
            {
                dropDownItem.DropDown.BackColor = DarkInputColor;
                dropDownItem.DropDown.ForeColor = DarkForeColor;
                dropDownItem.DropDown.Renderer = DarkToolStripRenderer;
                ApplyDarkToolStripItems(dropDownItem.DropDownItems);
            }
        }
    }

    private static void ApplyLightToolStrip(ToolStrip toolStrip)
    {
        toolStrip.BackColor = SystemColors.Control;
        toolStrip.ForeColor = SystemColors.ControlText;
        toolStrip.Renderer = null;
        ApplyLightToolStripItems(toolStrip.Items);
    }

    private static void ApplyLightToolStripItems(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            item.BackColor = SystemColors.Control;
            item.ForeColor = SystemColors.ControlText;
            if (item is ToolStripDropDownItem dropDownItem)
            {
                dropDownItem.DropDown.BackColor = SystemColors.Control;
                dropDownItem.DropDown.ForeColor = SystemColors.ControlText;
                dropDownItem.DropDown.Renderer = null;
                ApplyLightToolStripItems(dropDownItem.DropDownItems);
            }
        }
    }

    private static void ApplyWindowChrome(Form form, bool dark)
    {
        form.HandleCreated -= Form_HandleCreatedApplyTheme;
        form.HandleCreated += Form_HandleCreatedApplyTheme;
        if (!form.IsHandleCreated)
        {
            return;
        }

        TrySetWindowChrome(form.Handle, dark);
    }

    private static void Form_HandleCreatedApplyTheme(object sender, EventArgs e)
    {
        if (sender is Form form)
        {
            TrySetWindowChrome(form.Handle, IsDarkActive);
        }
    }

    private static void TrySetWindowChrome(IntPtr handle, bool dark)
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            return;
        }

        try
        {
            int useDark = dark ? 1 : 0;
            _ = DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
            _ = DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDark, sizeof(int));

            int borderColor = dark ? ColorTranslator.ToWin32(DarkBorderColor) : DWMWA_COLOR_DEFAULT;
            int captionColor = dark ? ColorTranslator.ToWin32(DarkBackColor) : DWMWA_COLOR_DEFAULT;
            int textColor = dark ? ColorTranslator.ToWin32(Color.White) : DWMWA_COLOR_DEFAULT;
            _ = DwmSetWindowAttribute(handle, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
            _ = DwmSetWindowAttribute(handle, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));
            _ = DwmSetWindowAttribute(handle, DWMWA_TEXT_COLOR, ref textColor, sizeof(int));
        }
        catch
        {
            // Older Windows builds and non-DWM hosts ignore these optional chrome colors.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    private sealed class DarkToolStripColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => DarkInputColor;
        public override Color ImageMarginGradientBegin => DarkInputColor;
        public override Color ImageMarginGradientMiddle => DarkInputColor;
        public override Color ImageMarginGradientEnd => DarkInputColor;
        public override Color MenuBorder => DarkBorderColor;
        public override Color MenuItemBorder => DarkSelectionColor;
        public override Color MenuItemSelected => DarkSelectionColor;
        public override Color MenuItemSelectedGradientBegin => DarkSelectionColor;
        public override Color MenuItemSelectedGradientEnd => DarkSelectionColor;
        public override Color MenuItemPressedGradientBegin => DarkSelectionColor;
        public override Color MenuItemPressedGradientMiddle => DarkSelectionColor;
        public override Color MenuItemPressedGradientEnd => DarkSelectionColor;
        public override Color SeparatorDark => DarkBorderColor;
        public override Color SeparatorLight => DarkBorderColor;
    }
}
