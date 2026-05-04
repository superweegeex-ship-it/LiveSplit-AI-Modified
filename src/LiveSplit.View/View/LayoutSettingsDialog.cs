using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Xml;

using LiveSplit.Localization;
using LiveSplit.Options;
using LiveSplit.UI;
using LiveSplit.UI.Components;

namespace LiveSplit.View;

public partial class LayoutSettingsDialog : Form
{
    private const string WindowSizeRegistryPath = "WindowSizes";
    private const string WindowWidthRegistryValue = "LayoutSettingsDialogWidth";
    private const string WindowHeightRegistryValue = "LayoutSettingsDialogHeight";
    private const string WindowXRegistryValue = "LayoutSettingsDialogX";
    private const string WindowYRegistryValue = "LayoutSettingsDialogY";
    private const int TabContentMinHeight = 560;

    public Options.LayoutSettings Settings { get; set; }
    public new UI.ILayout Layout { get; set; }

    /// <summary>Fired when layout/background options should be pushed to the main window immediately (live preview).</summary>
    public event EventHandler<BackgroundVideoLiveApplyEventArgs> LiveApplyRequested;
    public List<XmlNode> ComponentSettings { get; set; }
    public List<IComponent> Components { get; set; }

    private List<FontOverrides> _fontOverrideSnapshots;
    private List<LayoutComponent> _layoutComponents;
    private LayoutSettingsControl _layoutSettingsControl;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (FontOverrides snapshot in _fontOverrideSnapshots)
            {
                snapshot?.Dispose();
            }

            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    public LayoutSettingsDialog(Options.LayoutSettings settings, UI.ILayout layout, IComponent tabComponent = null)
    {
        InitializeComponent();
        RestoreWindowSize();
        Settings = settings;
        Layout = layout;
        ComponentSettings = [];
        Components = [];
        _fontOverrideSnapshots = [];
        _layoutComponents = [];
        _layoutSettingsControl = new LayoutSettingsControl(settings, layout);
        _layoutSettingsControl.LiveApplyRequested += (_, e) => LiveApplyRequested?.Invoke(this, e);
        AddNewTab("Layout", _layoutSettingsControl);
        AddComponents(tabComponent);
        UiLocalizer.Apply(this, LanguageResolver.ResolveCurrentCultureLanguage());
        WinFormsTheme.Apply(this);
        FormClosing += LayoutSettingsDialog_FormClosing;
    }

    private void LayoutSettingsDialog_FormClosing(object sender, FormClosingEventArgs e)
    {
        SaveWindowSize();
    }

    private void btnOK_Click(object sender, EventArgs e)
    {
        DialogResult = DialogResult.OK;
        Close();
    }

    private void btnCancel_Click(object sender, EventArgs e)
    {
        for (int i = 0; i < Components.Count; i++)
        {
            Components[i].SetSettings(ComponentSettings[i]);
        }

        // Restore font overrides from snapshots, disposing fonts set during the dialog session
        for (int i = 0; i < _layoutComponents.Count; i++)
        {
            FontOverrides snapshot = _fontOverrideSnapshots[i];
            FontOverrides current = _layoutComponents[i].FontOverrides;

            if (current.TimerFont != snapshot.TimerFont)
            {
                current.TimerFont?.Dispose();
            }

            if (current.TimesFont != snapshot.TimesFont)
            {
                current.TimesFont?.Dispose();
            }

            if (current.TextFont != snapshot.TextFont)
            {
                current.TextFont?.Dispose();
            }

            current.OverrideTimerFont = snapshot.OverrideTimerFont;
            current.TimerFont = snapshot.TimerFont?.Clone() as Font;
            current.OverrideTimesFont = snapshot.OverrideTimesFont;
            current.TimesFont = snapshot.TimesFont?.Clone() as Font;
            current.OverrideTextFont = snapshot.OverrideTextFont;
            current.TextFont = snapshot.TextFont?.Clone() as Font;
        }

        DialogResult = DialogResult.Cancel;
        Close();
    }

    protected void AddComponents(IComponent tabComponent = null)
    {
        foreach (ILayoutComponent layoutComponent in Layout.LayoutComponents)
        {
            IComponent component = layoutComponent.Component;
            Control settingsControl = component.GetSettingsControl(Layout.Mode);
            var lc = layoutComponent as LayoutComponent;

            var fontAttr = component.GetType().GetCustomAttributes(typeof(GlobalFontConsumerAttribute), true);
            GlobalFont usedFonts = fontAttr.Length > 0
                ? ((GlobalFontConsumerAttribute)fontAttr[0]).UsedGlobalFonts
                : GlobalFont.None;

            // Snapshot font overrides for cancel
            if (lc != null)
            {
                _fontOverrideSnapshots.Add((FontOverrides)lc.FontOverrides.Clone());
                _layoutComponents.Add(lc);
            }

            bool showFontPanel = lc != null && usedFonts != GlobalFont.None;

            // Create a tab if component has settings OR if it should show font overrides
            if (settingsControl != null || showFontPanel)
            {
                Control tabContent;
                if (settingsControl != null && showFontPanel)
                {
                    // Both: stack font panel above settings
                    var container = new Panel
                    {
                        Dock = DockStyle.Top
                    };
                    var fontPanel = new FontOverridePanel();
                    fontPanel.Bind(lc.FontOverrides, Layout.Settings, usedFonts);
                    fontPanel.Location = Point.Empty;
                    fontPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                    settingsControl.Dock = DockStyle.None;
                    settingsControl.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

                    void UpdateStackedSettingsHeight()
                    {
                        int width = Math.Max(0, container.ClientSize.Width);
                        if (width > 0)
                        {
                            if (fontPanel.Width != width)
                            {
                                fontPanel.Width = width;
                            }

                            if (settingsControl.Width != width)
                            {
                                settingsControl.Width = width;
                            }
                        }

                        if (settingsControl.Top != fontPanel.Bottom)
                        {
                            settingsControl.Top = fontPanel.Bottom;
                        }

                        int height = settingsControl.Bottom + settingsControl.Margin.Bottom;
                        if (container.Height != height)
                        {
                            container.Height = height;
                        }

                        if (container.MinimumSize.Height != height)
                        {
                            container.MinimumSize = new Size(container.MinimumSize.Width, height);
                        }
                    }

                    container.Resize += (_, _) => UpdateStackedSettingsHeight();
                    fontPanel.SizeChanged += (_, _) => UpdateStackedSettingsHeight();
                    settingsControl.SizeChanged += (_, _) => UpdateStackedSettingsHeight();
                    container.Controls.Add(settingsControl);
                    container.Controls.Add(fontPanel);
                    UpdateStackedSettingsHeight();
                    tabContent = container;
                }
                else if (showFontPanel)
                {
                    // No settings control: show only font panel
                    var fontPanel = new FontOverridePanel();
                    fontPanel.Bind(lc.FontOverrides, Layout.Settings, usedFonts);
                    tabContent = fontPanel;
                }
                else
                {
                    tabContent = settingsControl;
                }

                AddNewTab(component.ComponentName, tabContent);
                ComponentSettings.Add(component.GetSettings(new XmlDocument()));
                Components.Add(component);

                if (component == tabComponent)
                {
                    tabControl.SelectTab(tabControl.TabPages.Count - 1);
                }
            }
        }
    }

    protected void AddNewTab(string name, Control control)
    {
        var page = new TabPage(name);
        var contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(3, 3, SystemInformation.VerticalScrollBarWidth + 8, 8)
        };
        var scrollContent = new Panel
        {
            Location = new Point(contentPanel.Padding.Left, contentPanel.Padding.Top),
            Margin = Padding.Empty
        };

        control.Location = Point.Empty;
        control.Dock = DockStyle.None;
        control.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        control.Margin = new Padding(0, 0, 0, 8);

        scrollContent.Controls.Add(control);
        contentPanel.Controls.Add(scrollContent);
        bool updatingScrollExtent = false;
        bool scrollExtentUpdatePending = false;
        bool suppressScrollExtentUpdate = false;
        var scrollExtentHooks = new HashSet<Control>();
        int MeasureNestedHeight(Control current)
        {
            int height = current.Controls.Count == 0 ? current.Height : current.Padding.Vertical;
            foreach (Control child in current.Controls)
            {
                if (!child.Visible)
                {
                    continue;
                }

                height = Math.Max(height, child.Top + MeasureNestedHeight(child) + child.Margin.Bottom);
            }

            return Math.Max(height, current.MinimumSize.Height);
        }

        void ScheduleScrollExtentUpdate()
        {
            if (suppressScrollExtentUpdate || updatingScrollExtent || scrollExtentUpdatePending
                || contentPanel.IsDisposed || scrollContent.IsDisposed)
            {
                return;
            }

            if (!contentPanel.IsHandleCreated)
            {
                UpdateScrollExtent();
                return;
            }

            scrollExtentUpdatePending = true;
            contentPanel.BeginInvoke(new Action(() =>
            {
                scrollExtentUpdatePending = false;
                if (!contentPanel.IsDisposed && !scrollContent.IsDisposed)
                {
                    UpdateScrollExtent();
                }
            }));
        }

        void HookScrollExtentRefresh(Control current)
        {
            if (current == null || !scrollExtentHooks.Add(current))
            {
                return;
            }

            current.SizeChanged += (_, _) => ScheduleScrollExtentUpdate();
            current.ControlAdded += (_, e) =>
            {
                HookScrollExtentRefresh(e.Control);
                ScheduleScrollExtentUpdate();
            };

            foreach (Control child in current.Controls)
            {
                HookScrollExtentRefresh(child);
            }
        }

        void UpdateScrollExtent()
        {
            if (updatingScrollExtent)
            {
                return;
            }

            updatingScrollExtent = true;
            try
            {
                contentPanel.SuspendLayout();
                scrollContent.SuspendLayout();
                suppressScrollExtentUpdate = true;

                int availableWidth = Math.Max(0, contentPanel.ClientSize.Width - contentPanel.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
                if (scrollContent.Width != availableWidth)
                {
                    scrollContent.Width = availableWidth;
                }

                if (availableWidth > 0 && control.Width != availableWidth)
                {
                    control.Width = availableWidth;
                }

                control.PerformLayout();
                HookScrollExtentRefresh(control);
                Size preferredSize = control.GetPreferredSize(new Size(availableWidth, 0));
                int actualHeight = Math.Max(control.Height, Math.Max(preferredSize.Height, MeasureNestedHeight(control)));
                if (control.Height != actualHeight)
                {
                    control.Height = actualHeight;
                }

                int scrollContentHeight = actualHeight + control.Margin.Bottom;
                if (scrollContent.Height != scrollContentHeight)
                {
                    scrollContent.Height = scrollContentHeight;
                }

                int contentHeight = Math.Max(TabContentMinHeight, scrollContent.Bottom + contentPanel.Padding.Bottom);
                var scrollMinSize = new Size(0, contentHeight);
                if (!contentPanel.AutoScrollMinSize.Equals(scrollMinSize))
                {
                    contentPanel.AutoScrollMinSize = scrollMinSize;
                }

                contentPanel.HorizontalScroll.Enabled = false;
                contentPanel.HorizontalScroll.Visible = false;
                contentPanel.HorizontalScroll.Maximum = 0;
                if (contentPanel.AutoScrollPosition.X != 0)
                {
                    int scrollY = Math.Abs(contentPanel.AutoScrollPosition.Y);
                    contentPanel.AutoScrollPosition = new Point(0, scrollY);
                }
            }
            finally
            {
                suppressScrollExtentUpdate = false;
                scrollContent.ResumeLayout(false);
                contentPanel.ResumeLayout(false);
                updatingScrollExtent = false;
            }
        }

        HookScrollExtentRefresh(control);
        control.HandleCreated += (_, _) => ScheduleScrollExtentUpdate();
        control.VisibleChanged += (_, _) => ScheduleScrollExtentUpdate();
        control.Layout += (_, _) => ScheduleScrollExtentUpdate();
        contentPanel.HandleCreated += (_, _) => ScheduleScrollExtentUpdate();
        page.Layout += (_, _) => ScheduleScrollExtentUpdate();
        contentPanel.Layout += (_, _) => ScheduleScrollExtentUpdate();
        tabControl.SelectedIndexChanged += (_, _) =>
        {
            if (tabControl.SelectedTab == page)
            {
                ScheduleScrollExtentUpdate();
            }
        };
        page.Controls.Add(contentPanel);
        page.Name = name;
        tabControl.TabPages.Add(page);
        WinFormsTheme.Apply(page);
        UpdateScrollExtent();
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
                Rectangle bounds = WinFormsTheme.ClampToVisibleScreen(new Rectangle(x, y, Width, Height), MinimumSize);
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
}
