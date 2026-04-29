using System;
using System.Drawing;
using System.Windows.Forms;
using System.Xml;

using LiveSplit.Localization;
using LiveSplit.UI;
using LiveSplit.UI.Components;

namespace LiveSplit.View;

public partial class ComponentSettingsDialog : Form
{
    public XmlNode ComponentSettings { get; set; }
    public IComponent Component { get; set; }

    public ComponentSettingsDialog(IComponent component)
    {
        InitializeComponent();
        Component = component;
        AddComponent(component);
        UiLocalizer.Apply(this, LanguageResolver.ResolveCurrentCultureLanguage());
        WinFormsTheme.Apply(this);
    }

    private void btnOK_Click(object sender, EventArgs e)
    {
        DialogResult = DialogResult.OK;
        Close();
    }

    private void btnCancel_Click(object sender, EventArgs e)
    {
        Component.SetSettings(ComponentSettings);
        DialogResult = DialogResult.Cancel;
        Close();
    }

    protected void AddComponent(IComponent component)
    {
        Control settingsControl = Component.GetSettingsControl(LayoutMode.Vertical);
        AddControl(component.ComponentName, settingsControl);
        ComponentSettings = component.GetSettings(new XmlDocument());
    }

    protected void AddControl(string name, Control control)
    {
        panel.Padding = new Padding(3, 3, SystemInformation.VerticalScrollBarWidth + 8, 8);
        control.Location = new Point(panel.Padding.Left, panel.Padding.Top);
        control.Dock = DockStyle.Top;
        control.Margin = new Padding(0, 0, 0, 8);
        if (control is Panel panelControl)
        {
            panelControl.AutoSize = true;
            panelControl.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        }

        panel.Controls.Add(control);
        panel.Layout += (_, _) =>
        {
            int availableWidth = Math.Max(0, panel.ClientSize.Width - panel.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
            Size preferredSize = control.GetPreferredSize(new Size(availableWidth, 0));
            panel.AutoScrollMinSize = new Size(preferredSize.Width + panel.Padding.Horizontal, preferredSize.Height + panel.Padding.Vertical + control.Margin.Bottom);
        };
        Name = name + " Settings";
    }
}
