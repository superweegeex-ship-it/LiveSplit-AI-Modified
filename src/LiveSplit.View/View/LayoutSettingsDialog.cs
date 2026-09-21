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

    private readonly Dictionary<TabPage, Func<Control>> _pendingTabs = [];
    private bool _buildingTabs = true;

    private List<FontOverrides> _fontOverrideSnapshots;
    private List<LayoutComponent> _layoutComponents;
    private LayoutSettingsControl _layoutSettingsControl;
    private readonly List<Action> _removeSettingsHandlers = [];
    private readonly HashSet<Control> _borrowedSettingsControls = [];

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Components own and reuse their settings controls. Remove our callbacks
            // before detaching them, then let the form dispose only its own UI.
            foreach (Action removeHandler in _removeSettingsHandlers)
            {
                removeHandler();
            }

            _removeSettingsHandlers.Clear();
            _pendingTabs.Clear();
            foreach (Control control in _borrowedSettingsControls)
            {
                control.Parent?.Controls.Remove(control);
            }

            _borrowedSettingsControls.Clear();
            if (_fontOverrideSnapshots != null)
            {
                foreach (FontOverrides snapshot in _fontOverrideSnapshots)
                {
                    snapshot?.Dispose();
                }

                _fontOverrideSnapshots.Clear();
            }

            LiveApplyRequested = null;
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
        SuspendLayout();
        tableLayoutPanel3.SuspendLayout();
        tabControl.SuspendLayout();
        try
        {
            tabControl.SelectedIndexChanged += (_, _) =>
            {
                if (!_buildingTabs)
                {
                    EnsureSelectedTabLoaded();
                }
            };
            AddDeferredTab("Layout", () =>
            {
                _layoutSettingsControl = new LayoutSettingsControl(settings, layout);
                _layoutSettingsControl.LiveApplyRequested += (_, e) => LiveApplyRequested?.Invoke(this, e);
                return _layoutSettingsControl;
            });
            AddComponents(tabComponent);
            EnsureSelectedTabLoaded();
            UiLocalizer.Apply(this, LanguageResolver.ResolveCurrentCultureLanguage());
            WinFormsTheme.Apply(this);
            _buildingTabs = false;
        }
        finally
        {
            tabControl.ResumeLayout(true);
            tableLayoutPanel3.ResumeLayout(true);
            ResumeLayout(true);
        }

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
            if (settingsControl != null)
            {
                _borrowedSettingsControls.Add(settingsControl);
            }
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
                Control CreateTabContent()
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
                        settingsControl.AutoSize = false;
                        settingsControl.Dock = DockStyle.None;
                        settingsControl.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                        bool updatingStackedSettingsHeight = false;

                        void UpdateStackedSettingsHeight()
                        {
                            if (updatingStackedSettingsHeight || container.IsDisposed)
                            {
                                return;
                            }

                            updatingStackedSettingsHeight = true;
                            try
                            {
                                int width = Math.Max(0, container.ClientSize.Width);
                                if (width > 0)
                                {
                                    if (fontPanel.Width != width)
                                    {
                                        fontPanel.Width = width;
                                    }

                                    if (!settingsControl.AutoSize && settingsControl.Width != width)
                                    {
                                        settingsControl.Width = width;
                                    }
                                }

                                if (settingsControl.Top != fontPanel.Bottom)
                                {
                                    settingsControl.Top = fontPanel.Bottom;
                                }

                                int settingsHeight = MeasureControlHeight(settingsControl, width);
                                if (settingsControl.Height != settingsHeight)
                                {
                                    settingsControl.Height = settingsHeight;
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
                            finally
                            {
                                updatingStackedSettingsHeight = false;
                            }
                        }

                        container.Resize += (_, _) => UpdateStackedSettingsHeight();
                        fontPanel.SizeChanged += (_, _) => UpdateStackedSettingsHeight();
                        EventHandler settingsSizeChanged = (_, _) => UpdateStackedSettingsHeight();
                        settingsControl.SizeChanged += settingsSizeChanged;
                        _removeSettingsHandlers.Add(() => settingsControl.SizeChanged -= settingsSizeChanged);
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

                    return tabContent;
                }

                AddDeferredTab(component.ComponentName, CreateTabContent);
                ComponentSettings.Add(component.GetSettings(new XmlDocument()));
                Components.Add(component);

                if (component == tabComponent)
                {
                    tabControl.SelectTab(tabControl.TabPages.Count - 1);
                }
            }
        }
    }

    private void AddDeferredTab(string name, Func<Control> createContent)
    {
        var page = new TabPage(name) { Name = name };
        _pendingTabs.Add(page, createContent);
        tabControl.TabPages.Add(page);
    }

    private void EnsureSelectedTabLoaded()
    {
        TabPage page = tabControl.SelectedTab;
        if (page == null || !_pendingTabs.TryGetValue(page, out Func<Control> createContent))
        {
            return;
        }

        // Remove before parenting controls: handle/layout events can re-enter.
        _pendingTabs.Remove(page);
        page.SuspendLayout();
        try
        {
            Control content = createContent();
            PopulateTabPage(page, content);
            if (!_buildingTabs)
            {
                UiLocalizer.Apply(this, LanguageResolver.ResolveCurrentCultureLanguage());
                WinFormsTheme.Apply(page);
            }
        }
        finally
        {
            page.ResumeLayout(true);
        }
    }

    protected void AddNewTab(string name, Control control)
    {
        var page = new TabPage(name) { Name = name };
        PopulateTabPage(page, control);
        tabControl.TabPages.Add(page);
    }

    private void PopulateTabPage(TabPage page, Control control)
    {
        var contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(3, 3, SystemInformation.VerticalScrollBarWidth + 8, 8)
        };

        control.AutoSize = false;
        control.Location = new Point(contentPanel.Padding.Left, contentPanel.Padding.Top);
        control.Dock = DockStyle.None;
        control.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        control.Margin = new Padding(0, 0, 0, 8);

        contentPanel.Controls.Add(control);
        bool updatingScrollExtent = false;
        bool scrollExtentUpdatePending = false;

        void UpdateScrollExtent()
        {
            if (tabControl.SelectedTab != page || updatingScrollExtent
                || contentPanel.IsDisposed || control.IsDisposed)
            {
                return;
            }

            updatingScrollExtent = true;
            try
            {
                contentPanel.SuspendLayout();

                int availableWidth = Math.Max(0, contentPanel.ClientSize.Width - contentPanel.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
                if (availableWidth > 0 && control.Width != availableWidth)
                {
                    control.Width = availableWidth;
                }

                int actualHeight = MeasureControlHeight(control, availableWidth);
                if (control.Height != actualHeight)
                {
                    control.Height = actualHeight;
                }

                int contentHeight = Math.Max(TabContentMinHeight, control.Bottom + control.Margin.Bottom + contentPanel.Padding.Bottom);
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
                contentPanel.ResumeLayout(false);
                updatingScrollExtent = false;
            }
        }

        void ScheduleScrollExtentUpdate()
        {
            if (tabControl.SelectedTab != page || updatingScrollExtent || scrollExtentUpdatePending
                || contentPanel.IsDisposed || control.IsDisposed)
            {
                return;
            }

            if (!contentPanel.IsHandleCreated)
            {
                UpdateScrollExtent();
                return;
            }

            scrollExtentUpdatePending = true;
            try
            {
                contentPanel.BeginInvoke(new Action(() =>
                {
                    scrollExtentUpdatePending = false;
                    UpdateScrollExtent();
                }));
            }
            catch (InvalidOperationException)
            {
                scrollExtentUpdatePending = false;
                UpdateScrollExtent();
            }
        }

        EventHandler handleCreated = (_, _) => ScheduleScrollExtentUpdate();
        ControlEventHandler childrenChanged = (_, _) => ScheduleScrollExtentUpdate();
        control.HandleCreated += handleCreated;
        control.ControlAdded += childrenChanged;
        control.ControlRemoved += childrenChanged;
        _removeSettingsHandlers.Add(() =>
        {
            control.HandleCreated -= handleCreated;
            control.ControlAdded -= childrenChanged;
            control.ControlRemoved -= childrenChanged;
        });
        contentPanel.HandleCreated += (_, _) => ScheduleScrollExtentUpdate();
        contentPanel.ClientSizeChanged += (_, _) => ScheduleScrollExtentUpdate();
        tabControl.SelectedIndexChanged += (_, _) =>
        {
            if (tabControl.SelectedTab == page)
            {
                ScheduleScrollExtentUpdate();
            }
        };
        page.Controls.Add(contentPanel);
        UpdateScrollExtent();
    }

    private static int MeasureControlHeight(Control control, int availableWidth)
    {
        if (control == null)
        {
            return 0;
        }

        // Width/child changes already trigger WinForms layout. Forcing it here
        // repeats the entire layout while scroll/resize handlers are measuring it.
        Size preferredSize = control.GetPreferredSize(new Size(Math.Max(1, availableWidth), 0));
        int height = Math.Max(control.Height, preferredSize.Height);
        height = Math.Max(height, MeasureNestedControlHeight(control));
        return Math.Max(height, control.MinimumSize.Height);
    }

    private static int MeasureNestedControlHeight(Control current)
    {
        int height = current.Controls.Count == 0 ? current.Height : current.Padding.Vertical;
        foreach (Control child in current.Controls)
        {
            if (!child.Visible)
            {
                continue;
            }

            height = Math.Max(height, child.Bottom + child.Margin.Bottom);
            height = Math.Max(height, child.Top + MeasureNestedControlHeight(child) + child.Margin.Bottom);
        }

        return Math.Max(height, current.MinimumSize.Height);
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
