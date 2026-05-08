using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;

using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;

using LiveSplit.Model;
using LiveSplit.Model.Comparisons;
using LiveSplit.Model.Input;
using LiveSplit.Model.RunFactories;
using LiveSplit.Model.RunImporters;
using LiveSplit.Model.RunSavers;
using LiveSplit.Localization;
using LiveSplit.Options;
using LiveSplit.Options.SettingsFactories;
using LiveSplit.Options.SettingsSavers;
using LiveSplit.Server;
using LiveSplit.TimeFormatters;
using LiveSplit.UI;
using LiveSplit.UI.Components;
using LiveSplit.UI.LayoutFactories;
using LiveSplit.UI.LayoutSavers;
using LiveSplit.Updates;
using LiveSplit.Utils;
using LiveSplit.Web.Share;
using LiveSplit.Web.SRL;
using LiveSplit.Video;
using LiveSplit.View.BackgroundVideo;

using Microsoft.WindowsAPICodePack.Taskbar;

using UpdateManager;

namespace LiveSplit.View;

public partial class TimerForm : Form
{
    private const int VideoUiPresentTargetFps = 60;
    private const int WindowCaptureTransparencyPresentTargetFps = 60;
    private const int VideoReadbackMaxFps = 60;
    private const int ModalVideoMaxReadbackPixels = 320_000;
    private const int ComponentOverlayDirtyPaddingPx = 24;
    private const int VideoPresentComponentOverlayRefreshFps = 30;
    private const int VideoDebugOverlayRefreshMs = 250;
    private const bool EnableExperimentalVideoDebugOverlay = true;
    private static readonly bool EnableExperimentalBackgroundVideo = true;
    protected IComparisonGeneratorsFactory ComparisonGeneratorsFactory { get; set; }
    protected ComponentRenderer ComponentRenderer { get; set; }
    public LiveSplitState CurrentState { get; set; }
    protected ITimerModel Model { get; set; }
    protected CompositeHook Hook { get; set; }
    protected bool IsInDialogMode { get; set; }
    protected bool ResetMessageShown { get; set; }
    public new ILayout Layout
    {
        get => CurrentState.Layout;
        set => CurrentState.Layout = value;
    }
    protected float OldSize { get; set; }
    protected int RefreshesRemaining { get; set; }
    public ISettings Settings { get; set; }
    protected Invalidator Invalidator { get; set; }
    protected bool InTimerOnlyMode { get; set; }

    private Image previousBackground { get; set; }
    private float previousOpacity { get; set; }
    private float previousBlur { get; set; }
    private float previousImagePanX { get; set; } = float.NaN;
    private float previousImagePanY { get; set; } = float.NaN;
    private float previousImageZoomExtra { get; set; } = float.NaN;
    private Image blurredBackground { get; set; }
    private Image bakedBackground { get; set; }
    private Bitmap componentOverlayBitmap;
    private bool componentOverlayDirty = true;
    private Region componentOverlayDirtyRegion;
    private int componentOverlayWidth;
    private int componentOverlayHeight;
    private LayoutMode componentOverlayMode;
    private float componentOverlayOverallSize;
    private TrackingInvalidator componentInvalidator;
    private int lastComponentOverlayRefreshTick;
    private IBackgroundVideoPlayer backgroundVideoPlayer;
    private bool windowCaptureTransparencyMediaSuspended;
    private bool isRenderingScreenshot;
    private int lastThrottledVideoUiUpdateTick;
    private string loadedBackgroundVideoSource;
    private bool backgroundVideoDisabledForSession;
    private string failedBackgroundVideoSource;
    private bool showVideoDebugOverlay = false;
    private Bitmap videoDebugOverlayBitmap;
    private int lastVideoDebugOverlayRefreshTick;
    private bool lastBackgroundVideoTimerSyncApplied;
    private bool lastBackgroundVideoStartWithTimer;
    private bool lastBackgroundVideoPauseWhenRunCompletes;
    private bool lastBackgroundVideoKeepPlaybackAcrossTimerResets;
    private float lastBackgroundVideoCompletionVolumePercent;
    private float lastBackgroundVideoStartOffsetSeconds;
    private bool videoTimerSyncEverStartedThisLoad;
    /// <summary>Compares mpv <c>time-pos</c> to run wall-clock while playing; not tied to wid vs readback.</summary>
    private System.Windows.Forms.Timer backgroundVideoRunTimerDriftSyncTimer;
    private System.Windows.Forms.Timer windowCaptureTransparencyPresentTimer;
    private bool windowCaptureTransparencyPresenterActive;
    private bool windowCaptureTransparencyPresentPending;
    private bool windowCaptureTransparencyPresenting;
    private bool windowCaptureTransparencyLayerReady;
    private WindowCaptureTransparencySurface windowCaptureTransparencySurface;
    private bool windowCaptureTransparencyTimerResolutionRaised;
    private long windowCaptureTransparencyNextPresentTicks;
    private System.Windows.Forms.Timer videoUiPresentTimer;
    private int pendingVideoUiPresentInvalidate;
    private int missedVideoUiPresentInvalidate;
    private int pendingVideoUiPresentPaint;
    private int pendingVideoUiPresentCrossThreadInvoke;
    private bool isApplyingLayoutClientSize;
    private bool isInsideMoveSizeLoop;
    /// <summary>
    /// While true, <see cref="OnTimerFormModalOwnerEnabled"/> skips video control refresh — used during
    /// <see cref="TimerForm_FormClosing"/> so multiple save prompts do not stack updates.
    /// </summary>
    private bool suppressCompositorRestoreWhileFormClosingModals;

    public CommandServer Server { get; set; }

    protected GraphicsCache GlobalCache { get; set; }
    private readonly PassiveInvalidator windowCaptureTransparencyComponentInvalidator = new();

    protected StandardFormatsRunFactory RunFactory { get; set; }
    protected IRunSaver RunSaver { get; set; }
    protected ILayoutSaver LayoutSaver { get; set; }
    protected ISettingsSaver SettingsSaver { get; set; }

    protected float TotalPosition { get; set; }

    private bool DontRedraw = false;

    private bool AllowResizing = false;
    private bool AllowMoving = false;

    protected Region UpdateRegion { get; set; }

    private ToolStripMenuItem languageMenuItem;
    private ToolStripMenuItem followSystemLanguageMenuItem;
    private readonly Dictionary<ToolStripMenuItem, AppLanguage> languageMenuItems = new();

    public const int WM_NCLBUTTONDOWN = 0xA1;
    public const int HT_CAPTION = 0x2;
    public const string SETTINGS_PATH = "settings.cfg";
    private const int WS_MINIMIZEBOX = 0x20000;
    private const int CS_DBLCLKS = 0x8;
    private const int GWL_EXSTYLE = -20;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const int ULW_ALPHA = 0x00000002;
    private const uint WindowCaptureBiRgb = 0;
    private const uint WindowCaptureDibRgbColors = 0;
    private const byte AC_SRC_OVER = 0x00;
    private const byte AC_SRC_ALPHA = 0x01;
    private const byte WindowCaptureInputAlpha = 1;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.Style |= WS_MINIMIZEBOX;
            cp.ClassStyle |= CS_DBLCLKS;
            return cp;
        }
    }

    protected bool MouseIsDown = false;
    protected Point MousePoint;
    private Size resizeStartSize;

    private readonly List<Action> RacesToRefresh = [];
    private bool ShouldRefreshRaces = false;

    protected Task RefreshTask { get; set; }
    protected bool InvalidationRequired { get; set; }

    public string BasePath { get; set; }
    protected IEnumerable<RaceProviderAPI> RaceProvider { get; set; }

    private bool MousePassThrough
    {
        set
        {
            // If we're trying to set to false and it's already false, don't bother doing anything.
            // We can't do this for setting to true because setting Opacity may have messed the GWL_EXSTYLE flags up.
            if (!value && !MousePassThroughState)
            {
                return;
            }

            MousePassThroughState = value;

            uint prevWindowLong = GetWindowLong(Handle, GWL_EXSTYLE);
            if (value)
            {
                if ((prevWindowLong & (WS_EX_LAYERED | WS_EX_TRANSPARENT)) != (WS_EX_LAYERED | WS_EX_TRANSPARENT))
                {
                    // We have to add WS_EX_LAYERED, because WS_EX_TRANSPARENT won't work otherwise.
                    prevWindowLong |= WS_EX_LAYERED | WS_EX_TRANSPARENT;
                    SetWindowLong(Handle, GWL_EXSTYLE, prevWindowLong);
                }
            }
            else
            {
                // Not removing WS_EX_LAYERED because it may still be needed if Opacity != 1.
                // It shouldn't really affect anything and setting Form.Opacity to 1 will remove it anyway:
                prevWindowLong &= ~WS_EX_TRANSPARENT;
                SetWindowLong(Handle, GWL_EXSTYLE, prevWindowLong);
            }
        }
    }
    private bool MousePassThroughState = false;

    private bool IsForegroundWindow => GetForegroundWindow() == Handle;
    private AppLanguage CurrentLanguage => LanguageResolver.ResolveCurrentCultureLanguage();
    private AppLanguage ConfiguredLanguage => LanguageResolver.Resolve(Settings?.UILanguage);
    private string T(string source) => UiLocalizer.Translate(source, CurrentLanguage);
    private string TK(string key, string fallback) => UiLocalizer.TranslateKey(key, fallback, CurrentLanguage);

    private float? ResizingInitialAspectRatio { get; set; } = null;

    [DllImport("user32.dll")]
    private static extern int GetUpdateRgn(IntPtr hWnd, IntPtr hRgn, [MarshalAs(UnmanagedType.Bool)] bool bErase);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);

    [DllImport("user32")]
    private static extern uint SetWindowLong(IntPtr hwnd, int nIndex, uint dwNewLong);

    [DllImport("user32")]
    private static extern uint GetWindowLong(IntPtr hwnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;

        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;

        public NativeSize(int width, int height)
        {
            Width = width;
            Height = height;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCaptureBitmapInfoHeader
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCaptureBitmapInfo
    {
        public WindowCaptureBitmapInfoHeader bmiHeader;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd,
        IntPtr hdcDst,
        ref NativePoint pptDst,
        ref NativeSize psize,
        IntPtr hdcSrc,
        ref NativePoint pptSrc,
        int crKey,
        ref BlendFunction pblend,
        int dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(
        IntPtr hdc,
        ref WindowCaptureBitmapInfo pbmi,
        uint iUsage,
        out IntPtr ppvBits,
        IntPtr hSection,
        uint dwOffset);

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint WindowCaptureTimeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint WindowCaptureTimeEndPeriod(uint uPeriod);

    public TimerForm(string splitsPath = null, string layoutPath = null, string basePath = "")
    {
        BasePath = basePath;
        InitializeComponent();
        Init(splitsPath, layoutPath);
    }

    private void Init(string splitsPath = null, string layoutPath = null)
    {
        LiveSplitCoreFactory.LoadLiveSplitCore();

        SetWindowTitle();

        SpeedrunCom.Authenticator = new SpeedrunComApiKeyPrompt();

        GlobalCache = new GraphicsCache();
        Invalidator = new Invalidator(this);
        componentInvalidator = new TrackingInvalidator(
            Invalidator,
            InvalidateComponentOverlayCache,
            ShouldSuppressComponentInvalidatorFormPaint);
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);

        ComponentManager.BasePath = BasePath;

        CurrentState = new LiveSplitState(null, this, null, null, null);

        ComparisonGeneratorsFactory = new StandardComparisonGeneratorsFactory();

        Model = new DoubleTapPrevention(new TimerModel());

        ComponentManager.RaceProviderFactories = ComponentManager.LoadAllFactories<IRaceProviderFactory>();
        ComponentManager.RaceProviderFactories["SRL"] = new SRLFactory();
        RunFactory = new StandardFormatsRunFactory();
        RunSaver = new XMLRunSaver();
        LayoutSaver = new XMLLayoutSaver();
        SettingsSaver = new XMLSettingsSaver();
        LoadSettings();
        ApplyAppUiSettings();
        InitializeLanguageMenu();
        WinFormsTheme.Apply(RightClickMenu);
        SetDPIAwareness();
        UiLocalizer.Apply(this, CurrentLanguage);

        CurrentState.CurrentHotkeyProfile = Settings.HotkeyProfiles.First().Key;

        UpdateRecentSplits();
        UpdateRecentLayouts();

        IRun timerOnlyRun = new StandardRunFactory().Create(ComparisonGeneratorsFactory);

        IRun run = timerOnlyRun;
        try
        {
            if (!string.IsNullOrEmpty(splitsPath))
            {
                UpdateStateFromSplitsPath(splitsPath);

                run = LoadRunFromFile(splitsPath);
            }
            else if (Settings.RecentSplits.Count > 0)
            {
                RecentSplitsFile lastSplitFile = Settings.RecentSplits.Last();
                if (!string.IsNullOrEmpty(lastSplitFile.Path))
                {
                    UpdateStateFromSplitsPath(lastSplitFile.Path);

                    run = LoadRunFromFile(lastSplitFile.Path);
                }
            }
        }
        catch (Exception e)
        {
            Log.Error(e);
        }

        run.FixSplits();
        CurrentState.Run = run;
        CurrentState.Settings = Settings;

        try
        {
            if (!string.IsNullOrEmpty(layoutPath))
            {
                Layout = LoadLayoutFromFile(layoutPath);
            }
            else
            {
                if (Settings.RecentLayouts.Count > 0
                    && !string.IsNullOrEmpty(Settings.RecentLayouts.Last()))
                {
                    Layout = LoadLayoutFromFile(Settings.RecentLayouts.Last());
                }
                else if (run == timerOnlyRun)
                {
                    Layout = new TimerOnlyLayoutFactory().Create(CurrentState);
                    InTimerOnlyMode = true;
                }
                else
                {
                    Layout = new StandardLayoutFactory().Create(CurrentState);
                }
            }
        }
        catch (Exception e)
        {
            Log.Error(e);
            Layout = new StandardLayoutFactory().Create(CurrentState);
        }

        InTimerOnlyMode = run == timerOnlyRun;
        if (InTimerOnlyMode)
        {
            SetInTimerOnlyMode();
        }

        CurrentState.LayoutSettings = Layout.Settings;
        CreateAutoSplitter();

        SwitchComparisonGenerators();
        SwitchComparison(Settings.LastComparison);
        Model.CurrentState = CurrentState;

        CurrentState.OnReset += CurrentState_OnReset;
        CurrentState.OnStart += CurrentState_OnStart;
        CurrentState.OnSplit += CurrentState_OnSplit;
        CurrentState.OnSkipSplit += CurrentState_OnSkipSplit;
        CurrentState.OnUndoSplit += CurrentState_OnUndoSplit;
        CurrentState.OnPause += CurrentState_OnPause;
        CurrentState.OnUndoAllPauses += CurrentState_OnUndoAllPauses;
        CurrentState.OnResume += CurrentState_OnResume;
        CurrentState.OnSwitchComparisonPrevious += CurrentState_OnSwitchComparisonPrevious;
        CurrentState.OnSwitchComparisonNext += CurrentState_OnSwitchComparisonNext;

        ComponentRenderer = new ComponentRenderer();

        StartPosition = FormStartPosition.Manual;

        SetLayout(Layout);

        RefreshTask = Task.Factory.StartNew(RefreshTimerWorker);

        InvalidationRequired = false;

        Hook = new CompositeHook(false);
        Hook.GamepadHookInitialized += Hook_GamepadHookInitialized;
        Hook.KeyOrButtonPressed += hook_KeyOrButtonPressed;
        Settings.RegisterHotkeys(Hook, CurrentState.CurrentHotkeyProfile);

        SizeChanged += TimerForm_SizeChanged;

        TopMost = Layout.Settings.AlwaysOnTop;
        BackColor = Color.Black;
        ApplyWindowCaptureTransparency();

        Server = new CommandServer(CurrentState);
        Server.StartNamedPipe();
        switch (Settings.ServerStartup)
        {
            case ServerStartupType.TCP:
                Server.StartTcp();
                break;
            case ServerStartupType.Websocket:
                Server.StartWs();
                break;
            case ServerStartupType.PreviousState:
                switch (Settings.ServerState)
                {
                    case ServerStateType.TCP:
                        Server.StartTcp();
                        break;
                    case ServerStateType.Websocket:
                        Server.StartWs();
                        break;
                }

                break;
        }

        UpdateServerMenuItems();

        new System.Timers.Timer(1000) { Enabled = true }.Elapsed += PerSecondTimer_Elapsed;

        InitDragAndDrop();
    }

    private void InitDragAndDrop()
    {
        AllowDrop = true;
        DragDrop += TimerForm_DragDrop;
        DragEnter += TimerForm_DragEnter;
    }

    private void UpdateRaceProviderIntegration()
    {
        if (RightClickMenu.InvokeRequired)
        {
            RightClickMenu.Invoke(new Action(UpdateRaceProviderIntegration), null);
            return;
        }

        int menuItemIndex = RightClickMenu.Items.IndexOf(shareMenuItem);
        int firstRaceProvider = menuItemIndex + 1;
        int lastRaceProvider = RightClickMenu.Items.IndexOfKey("endRaceSection") - 1;
        if (lastRaceProvider - firstRaceProvider >= 0)
        {
            for (int i = 0; i < lastRaceProvider - firstRaceProvider + 1; i++)
            {
                RightClickMenu.Items[firstRaceProvider].Tag = null;
                RightClickMenu.Items[firstRaceProvider].MouseHover -= racingMenuItem_MouseHover;
                RightClickMenu.Items[firstRaceProvider].MouseLeave -= racingMenuItem_MouseLeave;
                RightClickMenu.Items.RemoveAt(firstRaceProvider);
            }
        }

        RaceProvider = ComponentManager.RaceProviderFactories.Select(x => x.Value.Create(Model, Settings.RaceProvider.FirstOrDefault(y => y.Name == x.Key)));
        foreach (RaceProviderAPI raceProvider in RaceProvider.Reverse())
        {
            if (Settings.RaceProvider.Any(x => x.DisplayName == raceProvider.ProviderName && !x.Enabled))
            {
                continue;
            }

            raceProvider.RacesRefreshedCallback = RacesRefreshed;
            var raceProviderItem = new ToolStripMenuItem()
            {
                Name = $"{raceProvider.ProviderName}racesMenuItem",
                Text = string.Format(T("{0} Races"), raceProvider.ProviderName),
                Tag = raceProvider
            };
            raceProviderItem.MouseHover += racingMenuItem_MouseHover;
            raceProviderItem.MouseLeave += racingMenuItem_MouseLeave;
            RightClickMenu.Items.Insert(menuItemIndex + 1, raceProviderItem);
            raceProvider.RefreshRacesListAsync();
        }

        RaceProviderAPI srlRaceProvider = RaceProvider.FirstOrDefault(x => x.ProviderName == "SRL");
        if (srlRaceProvider != null)
        {
            srlRaceProvider.JoinRace = SRL_JoinRace;
            srlRaceProvider.CreateRace = SRL_NewRace;
        }
    }

    private void SetWindowTitle()
    {
        int lowestAvailableNumber = 0;
        string currentName = "LiveSplit";
        IEnumerable<string> processNames = Process.GetProcessesByName("LiveSplit").Select(x => x.MainWindowTitle);

        while (processNames.Contains(currentName))
        {
            currentName = string.Format("LiveSplit ({0})", ++lowestAvailableNumber);
        }

        Text = currentName;
    }

    private void InitializeLanguageMenu()
    {
        languageMenuItem ??= new ToolStripMenuItem();
        languageMenuItem.Text = TK(LocalizationKeys.LanguageMenu, "Language");
        languageMenuItem.DropDownItems.Clear();
        languageMenuItems.Clear();

        followSystemLanguageMenuItem = new ToolStripMenuItem(TK(LocalizationKeys.LanguageFollowSystem, "Follow System"))
        {
            CheckOnClick = true
        };
        followSystemLanguageMenuItem.Click += (s, e) => SwitchUILanguage(AppLanguage.Auto);
        languageMenuItem.DropDownItems.Add(followSystemLanguageMenuItem);
        languageMenuItem.DropDownItems.Add(new ToolStripSeparator());

        foreach (AppLanguage language in UiTextCatalog.Languages)
        {
            var languageItem = new ToolStripMenuItem(language.DisplayName)
            {
                CheckOnClick = true
            };
            languageItem.Click += LanguageMenuItem_Click;
            languageMenuItem.DropDownItems.Add(languageItem);
            languageMenuItems[languageItem] = language;
        }

        if (RightClickMenu.Items.Contains(languageMenuItem))
        {
            UpdateLanguageMenuChecks();
            return;
        }

        int insertIndex = RightClickMenu.Items.IndexOf(toolStripSeparator4);
        if (insertIndex < 0)
        {
            insertIndex = RightClickMenu.Items.IndexOf(aboutMenuItem);
        }
        if (insertIndex < 0)
        {
            insertIndex = RightClickMenu.Items.Count;
        }

        RightClickMenu.Items.Insert(insertIndex, languageMenuItem);
        UpdateLanguageMenuChecks();
    }

    private void LanguageMenuItem_Click(object sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem languageItem && languageMenuItems.TryGetValue(languageItem, out AppLanguage language))
        {
            SwitchUILanguage(language);
        }
    }

    private void UpdateLanguageMenuChecks()
    {
        bool isAuto = LanguageResolver.IsAuto(Settings?.UILanguage);
        if (followSystemLanguageMenuItem != null)
        {
            followSystemLanguageMenuItem.Checked = isAuto;
        }

        foreach (KeyValuePair<ToolStripMenuItem, AppLanguage> pair in languageMenuItems)
        {
            pair.Key.Checked = !isAuto && pair.Value.Equals(ConfiguredLanguage);
        }
    }

    private void SwitchUILanguage(AppLanguage language)
    {
        Settings.UILanguage = LanguageResolver.ToSettingValue(language);
        SaveSettingsToDisk();
        UpdateLanguageMenuChecks();

        MessageBox.Show(
            this,
            TK(LocalizationKeys.LanguageRestartRequired, "Language change will take effect after restarting LiveSplit."),
            TK(LocalizationKeys.LanguageMenu, "Language"),
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void CurrentState_OnSwitchComparisonNext(object sender, EventArgs e)
    {
        RefreshComparisonItems();
    }

    private void RefreshComparisonItems()
    {
        int numSeparators = 0;
        foreach (ToolStripItem item in comparisonMenuItem.DropDownItems.OfType<ToolStripItem>().Reverse())
        {
            if (item is ToolStripSeparator)
            {
                numSeparators++;
            }

            if (item is ToolStripMenuItem toolItem)
            {
                if (numSeparators == 0)
                {
                    toolItem.Checked = toolItem.Name == CurrentState.CurrentTimingMethod.ToString();
                }
                else
                {
                    toolItem.Checked = toolItem.Text == CurrentState.CurrentComparison.EscapeMenuItemText();
                }
            }
        }
    }

    private void CurrentState_OnSwitchComparisonPrevious(object sender, EventArgs e)
    {
        RefreshComparisonItems();
    }

    private string GetShortenedGameAndGoal(string goal)
    {
        if (goal.Length > 65)
        {
            return goal[..60] + "...";
        }

        return goal;
    }

    private void PerSecondTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
    {
        // Invalidate the entire form at least once per second, to avoid parts of the form not being redrawn when necessary in some cases
        InvalidationRequired = true;

        if (CurrentLanguage.RequiresLocalization && IsHandleCreated && !IsDisposed)
        {
            try
            {
                BeginInvoke(new Action(() => UiLocalizer.ApplyOpenForms(CurrentLanguage)));
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        if (ShouldRefreshRaces)
        {
            for (int i = 0; i < RacesToRefresh.Count; i++)
            {
                Action updateTitleAction = RacesToRefresh[i];
                updateTitleAction();
            }
        }
    }

    private void RacesRefreshed(RaceProviderAPI raceProvider)
    {
        Action<List<ToolStripItem>> replaceItems = null;

        replaceItems = x =>
        {
            if (InvokeRequired)
            {
                Invoke(replaceItems, x);
            }
            else if (RightClickMenu.InvokeRequired)
            {
                RightClickMenu.Invoke(replaceItems, x);
            }
            else
            {
                if (RightClickMenu.Items.Find($"{raceProvider.ProviderName}racesMenuItem", false).FirstOrDefault() is not ToolStripMenuItem racingMenuItem)
                {
                    return;
                }

                racingMenuItem.DropDownItems.Clear();
                racingMenuItem.DropDownItems.AddRange([.. x]);
            }
        };

        var menuItemsToAdd = new List<ToolStripItem>();

        foreach (IRaceInfo race in raceProvider.GetRaces())
        {
            if (race.State != 1)
            {
                continue;
            }

            string gameAndGoal = GetShortenedGameAndGoal(string.Format("{0} - {1}", race.GameName, race.Goal));
            int entrants = race.NumEntrants;
            string plural = entrants == 1 ? "" : "s";
            string title = string.Format("{0} ({1} Entrant{2})", gameAndGoal, entrants, plural);

            var item = new ToolStripMenuItem
            {
                Text = title.EscapeMenuItemText(),
                Tag = race.Id
            };
            item.Click += (s, e) => raceProvider.JoinRace?.Invoke(Model, race.Id);
            menuItemsToAdd.Add(item);
        }

        if (menuItemsToAdd.Count > 0)
        {
            menuItemsToAdd.Add(new ToolStripSeparator());
        }

        foreach (IRaceInfo race in raceProvider.GetRaces())
        {
            if (race.State != 3)
            {
                continue;
            }

            string gameAndGoal = GetShortenedGameAndGoal(string.Format("{0} - {1}", race.GameName, race.Goal));
            var startTime = new DateTime(1970, 1, 1, 0, 0, 0, 0);
            startTime = startTime.AddSeconds(race.Starttime);

            var tsItem = new ToolStripMenuItem();

            Action updateTitleAction = null;
            updateTitleAction = () =>
            {
                if (InvokeRequired)
                {
                    if (!IsDisposed)
                    {
                        try
                        {
                            Invoke(updateTitleAction);
                        }
                        catch { }
                    }
                }
                else
                {
                    try
                    {
                        UpdateTitle(tsItem, race, startTime, gameAndGoal);
                    }
                    catch { }
                }
            };

            UpdateTitle(tsItem, race, startTime, gameAndGoal);

            RacesToRefresh.Add(updateTitleAction);

            tsItem.Click += (s, ev) =>
            {
                if (!race.IsParticipant(raceProvider.Username))
                {
                    Settings.RaceViewer.ShowRace(race);
                }
                else
                {
                    tsItem.Tag = race.Id;
                    raceProvider.JoinRace?.Invoke(Model, race.Id);
                }
            };

            menuItemsToAdd.Add(tsItem);
        }

        if (menuItemsToAdd.Count > 0 && menuItemsToAdd[^1] is not ToolStripSeparator)
        {
            menuItemsToAdd.Add(new ToolStripSeparator());
        }

        var newRaceItem = new ToolStripMenuItem
        {
            Text = T("New Race...")
        };
        newRaceItem.Click += (s, e) => raceProvider.CreateRace?.Invoke(Model);
        menuItemsToAdd.Add(newRaceItem);

        replaceItems(menuItemsToAdd);
    }

    private void UpdateTitle(ToolStripMenuItem item, IRaceInfo race, DateTime startTime, string gameAndGoal)
    {
        TimeSpan timeSpan = TimeStamp.CurrentDateTime - startTime;
        if (timeSpan < TimeSpan.Zero)
        {
            timeSpan = TimeSpan.Zero;
        }

        string time = new RegularTimeFormatter().Format(timeSpan);
        string title = string.Format(T("[{0}] {1} ({2}/{3} Finished)"), time, gameAndGoal, race.Finishes, race.NumEntrants - race.Forfeits);
        item.Text = title.EscapeMenuItemText();
    }

    private void SRL_JoinRace(ITimerModel model, string raceId)
    {
        if (ShowSRLRules())
        {
            var form = new SpeedRunsLiveForm(CurrentState, model, raceId);
            TopMost = false;
            UiLocalizer.Apply(form, CurrentLanguage);
            form.Show(this);
            TopMost = CurrentState.LayoutSettings.AlwaysOnTop;
        }
    }

    private void SRL_NewRace(ITimerModel model)
    {
        if (ShowSRLRules())
        {
            string gameName = CurrentState.Run.GameName;
            string gameCategory = CurrentState.Run.CategoryName;
            var inputBox = new NewRaceInputBox();
            UiLocalizer.Apply(inputBox, CurrentLanguage);
            TopMost = false;
            DialogResult result = inputBox.Show(ref gameName, ref gameCategory);
            if (result == DialogResult.OK)
            {
                string id = SpeedRunsLiveAPI.Instance.GetGameIDFromName(gameName);
                if (id == null)
                {
                    id = "new";
                    gameCategory = gameName + " - " + gameCategory;
                    gameName = "New Game";
                }

                var form = new SpeedRunsLiveForm(CurrentState, model, gameName, id, gameCategory);
                UiLocalizer.Apply(form, CurrentLanguage);
                form.Show(this);
            }

            TopMost = CurrentState.LayoutSettings.AlwaysOnTop;
        }
    }

    private void TimerForm_SizeChanged(object sender, EventArgs e)
    {
        backgroundVideoPlayer?.NotifyHostClientSize(ClientSize.Width, ClientSize.Height);
        CreateBakedBackground();
        windowCaptureTransparencyLayerReady = false;
        RequestWindowCaptureTransparencyPresent();
        // Keep layout persistence in client/content pixels. Restoring Size elsewhere can add the
        // non-client resize border twice if the form style changes.
        StoreCurrentClientSizeInLayout();

        if (RefreshesRemaining <= 0)
        {
            MaintainMinimumSize();
        }
    }

    private void StoreCurrentClientSizeInLayout()
    {
        if (Layout == null || isApplyingLayoutClientSize)
        {
            return;
        }

        StoreClientSizeInLayout(Layout, ClientSize);
    }

    private static void StoreClientSizeInLayout(ILayout layout, Size clientSize)
    {
        if (layout.Mode == LayoutMode.Vertical)
        {
            layout.VerticalWidth = clientSize.Width;
            layout.VerticalHeight = clientSize.Height;
        }
        else
        {
            layout.HorizontalWidth = clientSize.Width;
            layout.HorizontalHeight = clientSize.Height;
        }
    }

    private void ApplyInternalClientSize(Size clientSize)
    {
        if (ClientSize == clientSize)
        {
            return;
        }

        try
        {
            isApplyingLayoutClientSize = true;
            ClientSize = clientSize;
        }
        finally
        {
            isApplyingLayoutClientSize = false;
        }
    }

    private void ApplyLayoutClientSize(ILayout layout)
    {
        Size? targetSize = null;
        if (layout.Mode == LayoutMode.Vertical)
        {
            if (layout.VerticalWidth != UI.Layout.InvalidSize && layout.VerticalHeight != UI.Layout.InvalidSize)
            {
                targetSize = new Size(layout.VerticalWidth, layout.VerticalHeight);
            }
        }
        else if (layout.HorizontalWidth != UI.Layout.InvalidSize && layout.HorizontalHeight != UI.Layout.InvalidSize)
        {
            targetSize = new Size(layout.HorizontalWidth, layout.HorizontalHeight);
        }

        if (!targetSize.HasValue || ClientSize == targetSize.Value)
        {
            return;
        }

        ApplyInternalClientSize(targetSize.Value);
        StoreClientSizeInLayout(layout, ClientSize);
    }

    private static bool HasExplicitLayoutClientSize(ILayout layout)
    {
        if (layout == null)
        {
            return false;
        }

        return layout.Mode == LayoutMode.Vertical
            ? layout.VerticalWidth != UI.Layout.InvalidSize && layout.VerticalHeight != UI.Layout.InvalidSize
            : layout.HorizontalWidth != UI.Layout.InvalidSize && layout.HorizontalHeight != UI.Layout.InvalidSize;
    }

    private void Hook_GamepadHookInitialized(object sender, EventArgs e)
    {
        CheckForUpdates();
    }

    private void CheckForUpdates()
    {
        UpdateHelper.Update(this, () => Invoke(new Action(() => Process.GetCurrentProcess().Kill())),
                    [
                        new LiveSplitUpdateable(),
                        UpdateManagerUpdateable.Instance,
                        .. ComponentManager.ComponentFactories.Values,
                        .. ComponentManager.RaceProviderFactories.Values, ]);
    }

    private void CurrentState_OnUndoSplit(object sender, EventArgs e)
    {
        this.InvokeIfRequired(() =>
        {
            pauseMenuItem.Enabled = true;
            splitMenuItem.Enabled = true;
            if (CurrentState.CurrentSplitIndex == 0)
            {
                undoSplitMenuItem.Enabled = false;
            }

            if (CurrentState.CurrentSplitIndex < CurrentState.Run.Count - 1)
            {
                skipSplitMenuItem.Enabled = true;
            }
        });

        // Do not call InvalidateTimerSyncSeekTarget here: it sets forceTimerSyncResync, which forces
        // a seek to VideoStartOffsetSeconds (often 0) on the next Running-phase sync — that treats
        // undo split like a fresh timer start and restarts the video. Drift correction keeps playback
        // aligned to CurrentTime while Running.
        ApplyBackgroundVideoTimerPhase();
    }

    private void UpdateServerMenuItems()
    {
        Settings.ServerState = Server.ServerState;
        switch (Server.ServerState)
        {
            case ServerStateType.Off:
                tcpServerMenuItem.Enabled = true;
                this.InvokeIfRequired(() => tcpServerMenuItem.Text = "Start TCP Server");
                webSocketMenuItem.Enabled = true;
                this.InvokeIfRequired(() => webSocketMenuItem.Text = "Start WebSocket Server");
                break;
            case ServerStateType.TCP:
                tcpServerMenuItem.Enabled = true;
                this.InvokeIfRequired(() => tcpServerMenuItem.Text = "Stop TCP Server");
                webSocketMenuItem.Enabled = false;
                this.InvokeIfRequired(() => webSocketMenuItem.Text = "Start WebSocket Server");
                break;
            case ServerStateType.Websocket:
                tcpServerMenuItem.Enabled = false;
                this.InvokeIfRequired(() => tcpServerMenuItem.Text = "Start TCP Server");
                webSocketMenuItem.Enabled = true;
                this.InvokeIfRequired(() => webSocketMenuItem.Text = "Stop WebSocket Server");
                break;
        }
    }

    private void TCPServerMenuItem_Click(object sender, EventArgs e)
    {
        if (Server.ServerState == ServerStateType.Off)
        {
            Server.StartTcp();
        }
        else if (Server.ServerState == ServerStateType.TCP)
        {
            Server.StopTcp();
        }

        UpdateServerMenuItems();
    }

    private void WebSocketMenuItem_Click(object sender, EventArgs e)
    {
        if (Server.ServerState == ServerStateType.Off)
        {
            Server.StartWs();
        }
        else if (Server.ServerState == ServerStateType.Websocket)
        {
            Server.StopWs();
        }

        UpdateServerMenuItems();
    }

    private void CurrentState_OnSkipSplit(object sender, EventArgs e)
    {
        this.InvokeIfRequired(() =>
        {
            if (CurrentState.CurrentSplitIndex >= CurrentState.Run.Count - 1)
            {
                skipSplitMenuItem.Enabled = false;
            }

            undoSplitMenuItem.Enabled = true;
        });
    }

    private void CurrentState_OnSplit(object sender, EventArgs e)
    {
        this.InvokeIfRequired(() =>
        {
            if (CurrentState.CurrentSplitIndex == CurrentState.Run.Count)
            {
                pauseMenuItem.Enabled = false;
                splitMenuItem.Enabled = false;
            }

            if (CurrentState.CurrentSplitIndex >= CurrentState.Run.Count - 1)
            {
                skipSplitMenuItem.Enabled = false;
            }

            undoSplitMenuItem.Enabled = true;
        });

        ApplyBackgroundVideoTimerPhase();
    }

    private void CurrentState_OnStart(object sender, EventArgs e)
    {
        this.InvokeIfRequired(() =>
        {
            pauseMenuItem.Enabled = true;
            resetMenuItem.Enabled = true;
            undoSplitMenuItem.Enabled = false;
            skipSplitMenuItem.Enabled = true;
            splitMenuItem.Text = "Split";
        });

        // Starting from stopped (NotRunning) must re-sync video to the run start offset. After a
        // reset with "keep playback across resets", NotRunning leaves waitingForTimerStartBeforePlayback
        // false so EnsurePlaying would only unpause; invalidate forces the seek. Pause/resume uses
        // OnResume, not OnStart, so mid-run unpauses are unchanged.
        if (Layout?.Settings != null
            && Layout.Settings.BackgroundType == BackgroundType.Video
            && Layout.Settings.VideoStartWithTimer
            && backgroundVideoPlayer != null
            && backgroundVideoPlayer.IsLoaded)
        {
            backgroundVideoPlayer.InvalidateTimerSyncSeekTarget();
        }

        videoTimerSyncEverStartedThisLoad = true;
        ApplyBackgroundVideoTimerPhase();
    }

    private void CurrentState_OnReset(object sender, TimerPhase e)
    {
        RegenerateComparisons();

        this.InvokeIfRequired(() =>
        {
            if (InTimerOnlyMode)
            {
                IRun timerOnlyRun = new StandardRunFactory().Create(ComparisonGeneratorsFactory);
                timerOnlyRun.Offset = CurrentState.Run.Offset;

                SetRun(timerOnlyRun);
            }

            resetMenuItem.Enabled = false;
            pauseMenuItem.Enabled = false;
            undoPausesMenuItem.Enabled = false;
            undoSplitMenuItem.Enabled = false;
            skipSplitMenuItem.Enabled = false;
            splitMenuItem.Enabled = true;
            splitMenuItem.Text = "Start";
        });

        ApplyBackgroundVideoTimerPhase();
    }

    private void CurrentState_OnResume(object sender, EventArgs e)
    {
        this.InvokeIfRequired(() =>
        {
            splitMenuItem.Text = "Split";
            pauseMenuItem.Enabled = true;
        });

        ApplyBackgroundVideoTimerPhase();
    }

    private void CurrentState_OnPause(object sender, EventArgs e)
    {
        this.InvokeIfRequired(() =>
        {
            splitMenuItem.Text = "Resume";
            undoPausesMenuItem.Enabled = true;
            pauseMenuItem.Enabled = false;
        });

        ApplyBackgroundVideoTimerPhase();
    }

    private void CurrentState_OnUndoAllPauses(object sender, EventArgs e)
    {
        this.InvokeIfRequired(() => undoPausesMenuItem.Enabled = false);
    }

    private void AddSplitsFileToLRU(string filePath, IRun run, TimingMethod lastTimingMethod, string lastHotkeyProfile)
    {
        Settings.AddToRecentSplits(filePath, run, lastTimingMethod, lastHotkeyProfile);
        UpdateRecentSplits();
    }

    private void AddLayoutFileToLRU(string filePath)
    {
        Settings.AddToRecentLayouts(filePath);
        UpdateRecentLayouts();
    }

    protected void SetInTimerOnlyMode()
    {
        if (Layout.Components.Count() != 1 || Layout.Components.FirstOrDefault().ComponentName != "Timer")
        {
            InTimerOnlyMode = false;
        }
    }

    private void UpdateRecentSplits()
    {
        openSplitsMenuItem.DropDownItems.Clear();

        foreach (IGrouping<string, RecentSplitsFile> game in Settings.RecentSplits
            .Reverse()
            .Where(x => !string.IsNullOrEmpty(x.Path))
            .GroupBy(x => x.GameName ?? ""))
        {
            var gameMenuItem = new ToolStripMenuItem();

            foreach (IGrouping<string, RecentSplitsFile> category in game
                .GroupBy(x => x.CategoryName ?? ""))
            {
                var categoryMenuItem = new ToolStripMenuItem
                {
                    Tag = "Category"
                };

                foreach (RecentSplitsFile splitsFile in category)
                {
                    string fileName = Path.GetFileName(splitsFile.Path);

                    var menuItem = new ToolStripMenuItem(fileName.EscapeMenuItemText())
                    {
                        Tag = "FileName"
                    };
                    menuItem.Click += (x, y) => OpenRunFromFile(splitsFile.Path);
                    categoryMenuItem.DropDownItems.Add(menuItem);
                }

                if (categoryMenuItem.DropDownItems.Count == 1)
                {
                    categoryMenuItem = (ToolStripMenuItem)categoryMenuItem.DropDownItems[0];
                    if (!string.IsNullOrEmpty(category.Key))
                    {
                        categoryMenuItem.Text = category.Key.EscapeMenuItemText();
                        categoryMenuItem.Tag = "Category";
                    }
                }
                else
                {
                    string categoryName;
                    if (string.IsNullOrEmpty(category.Key))
                    {
                        categoryName = "Unknown Category";
                    }
                    else
                    {
                        categoryName = category.Key;
                    }

                    categoryMenuItem.Text = categoryName.EscapeMenuItemText();
                }

                gameMenuItem.DropDownItems.Add(categoryMenuItem);
            }

            string gameName;
            if (string.IsNullOrEmpty(game.Key))
            {
                gameName = "Unknown Game";

                if (gameMenuItem.DropDownItems.Count == 1)
                {
                    gameMenuItem = (ToolStripMenuItem)gameMenuItem.DropDownItems[0];
                    gameName = gameMenuItem.Text;
                    if (gameMenuItem.Text == "Unknown Category")
                    {
                        gameName = "Unknown";
                    }
                }
            }
            else
            {
                gameName = game.Key;

                if (gameMenuItem.DropDownItems.Count == 1)
                {
                    gameMenuItem = (ToolStripMenuItem)gameMenuItem.DropDownItems[0];
                    if ((string)gameMenuItem.Tag == "Category")
                    {
                        if (!gameMenuItem.Text.StartsWith("Unknown Category"))
                        {
                            gameName += " - " + gameMenuItem.Text;
                        }
                    }
                    else
                    {
                        gameName += " (" + gameMenuItem.Text + ")";
                    }
                }
            }

            gameMenuItem.Text = gameName.EscapeMenuItemText();

            openSplitsMenuItem.DropDownItems.Add(gameMenuItem);
        }

        if (openSplitsMenuItem.DropDownItems.Count > 0)
        {
            openSplitsMenuItem.DropDownItems.Add(new ToolStripSeparator());
        }

        var openFromFileMenuItem = new ToolStripMenuItem("From File...");
        openFromFileMenuItem.Click += openSplitsFromFileMenuItem_Click;
        openSplitsMenuItem.DropDownItems.Add(openFromFileMenuItem);
        var openFromURLMenuItem = new ToolStripMenuItem("From URL...");
        openFromURLMenuItem.Click += openSplitsFromURLMenuItem_Click;
        openSplitsMenuItem.DropDownItems.Add(openFromURLMenuItem);
        openSplitsMenuItem.DropDownItems.Add(new ToolStripSeparator());
        var editSplitHistoryMenuItem = new ToolStripMenuItem("Edit History");
        editSplitHistoryMenuItem.Click += editSplitHistoryMenuItem_Click;
        openSplitsMenuItem.DropDownItems.Add(editSplitHistoryMenuItem);
    }

    private void editSplitHistoryMenuItem_Click(object sender, EventArgs e)
    {
        using (var editHistoryDialog = new EditHistoryDialog(Settings.RecentSplits.Select(x => x.Path)))
        {
            UiLocalizer.Apply(editHistoryDialog, CurrentLanguage);
            if (editHistoryDialog.ShowDialog(this) != DialogResult.Cancel)
            {
                Settings.RecentSplits = new List<RecentSplitsFile>(Settings.RecentSplits.Where(x => editHistoryDialog.History.Contains(x.Path)));
            }
        }

        UpdateRecentSplits();
    }

    private void UpdateRecentLayouts()
    {
        openLayoutMenuItem.DropDownItems.Clear();

        foreach (string item in Settings.RecentLayouts.Reverse().Where(x => !string.IsNullOrEmpty(x)))
        {
            var menuItem = new ToolStripMenuItem(Path.GetFileNameWithoutExtension(item).EscapeMenuItemText());
            menuItem.Click += (x, y) => OpenLayoutFromFile(item);
            openLayoutMenuItem.DropDownItems.Add(menuItem);
        }

        if (openLayoutMenuItem.DropDownItems.Count > 0)
        {
            openLayoutMenuItem.DropDownItems.Add(new ToolStripSeparator());
        }

        var openLayoutFromFileMenuItem = new ToolStripMenuItem("From File...");
        openLayoutFromFileMenuItem.Click += openLayoutFromFileMenuItem_Click;
        openLayoutMenuItem.DropDownItems.Add(openLayoutFromFileMenuItem);
        var openFromURLMenuItem = new ToolStripMenuItem("From URL...");
        openFromURLMenuItem.Click += openLayoutFromURLMenuItem_Click;
        openLayoutMenuItem.DropDownItems.Add(openFromURLMenuItem);
        var defaultLayoutMenuItem = new ToolStripMenuItem("Default");
        defaultLayoutMenuItem.Click += (x, y) => LoadDefaultLayout();
        openLayoutMenuItem.DropDownItems.Add(defaultLayoutMenuItem);
        openLayoutMenuItem.DropDownItems.Add(new ToolStripSeparator());
        var editLayoutHistoryMenuItem = new ToolStripMenuItem("Edit History");
        editLayoutHistoryMenuItem.Click += editLayoutHistoryMenuItem_Click;
        openLayoutMenuItem.DropDownItems.Add(editLayoutHistoryMenuItem);
    }

    private void editLayoutHistoryMenuItem_Click(object sender, EventArgs e)
    {
        using (var editHistoryDialog = new EditHistoryDialog(Settings.RecentLayouts))
        {
            UiLocalizer.Apply(editHistoryDialog, CurrentLanguage);

            if (editHistoryDialog.ShowDialog(this) != DialogResult.Cancel)
            {
                Settings.RecentLayouts = editHistoryDialog.History;
            }
        }

        UpdateRecentLayouts();
    }

    private void openLayoutFromURLMenuItem_Click(object sender, EventArgs e)
    {
        try
        {
            IsInDialogMode = true;
            TopMost = false;
            string url = null;

            if (DialogResult.OK == InputBox.Show(T("Open Layout from URL"), T("URL:"), ref url))
            {
                try
                {
                    var uri = new Uri(url);
                    if (uri.Host.ToLowerInvariant() == "ge.tt"
                        && uri.LocalPath.Length > 0
                        && !uri.LocalPath[1..].Contains('/'))
                    {
                        uri = new Uri(string.Format("http://ge.tt/api/1/files{0}/0/blob?download", uri.LocalPath));
                    }

                    var request = WebRequest.Create(uri);

                    using WebResponse response = request.GetResponse();
                    using Stream stream = response.GetResponseStream();
                    using var memoryStream = new MemoryStream();
                    stream.CopyTo(memoryStream);
                    memoryStream.Seek(0, SeekOrigin.Begin);

                    try
                    {
                        ILayout layout = new XMLLayoutFactory(memoryStream).Create(CurrentState);
                        layout.HasChanged = true;
                        SetLayout(layout);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex);
                        DontRedraw = true;
                        MessageBox.Show(this, T("The selected file was not recognized as a layout file. (") + ex.Message + ")", T("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                        DontRedraw = false;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                    DontRedraw = true;
                    MessageBox.Show(this, T("The layout file couldn't be downloaded."), T("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    DontRedraw = false;
                }
            }
        }
        finally
        {
            IsInDialogMode = false;
            TopMost = Layout.Settings.AlwaysOnTop;
            SyncVideoBackgroundAfterInteractionModeChange();
        }
    }

    private void openSplitsFromURLMenuItem_Click(object sender, EventArgs e)
    {
        var runImporter = new URLRunImporter();
        IRun run = runImporter.Import(this);

        if (run != null)
        {
            if (!WarnUserAboutSplitsSave())
            {
                return;
            }

            if (!WarnAndRemoveTimerOnly(true))
            {
                return;
            }

            run.HasChanged = true;
            SetRun(run);
            CurrentState.CallRunManuallyModified();
        }
    }

    private void StartOrSplit()
    {
        if (CurrentState.CurrentPhase == TimerPhase.Running)
        {
            Model.Split();
        }
        else if (CurrentState.CurrentPhase == TimerPhase.Paused)
        {
            Model.Pause();
        }
        else if (CurrentState.CurrentPhase == TimerPhase.NotRunning)
        {
            Model.Start();
        }
        else if (CurrentState.CurrentPhase == TimerPhase.Ended)
        {
            Model.Reset();
        }
    }

    private void hook_KeyOrButtonPressed(object sender, KeyOrButton e)
    {
        Action action = () =>
        {
            HotkeyProfile hotkeyProfile = Settings.HotkeyProfiles[CurrentState.CurrentHotkeyProfile];

            if ((ActiveForm == this || hotkeyProfile.GlobalHotkeysEnabled) && !ResetMessageShown && !IsInDialogMode)
            {
                if (hotkeyProfile.SplitKey == e)
                {
                    if (hotkeyProfile.HotkeyDelay > 0)
                    {
                        var splitTimer = new System.Timers.Timer(hotkeyProfile.HotkeyDelay * 1000f)
                        {
                            Enabled = true
                        };
                        splitTimer.Elapsed += splitTimer_Elapsed;
                    }
                    else
                    {
                        StartOrSplit();
                    }
                }

                else if (hotkeyProfile.UndoKey == e)
                {
                    Model.UndoSplit();
                }

                else if (hotkeyProfile.SkipKey == e)
                {
                    Model.SkipSplit();
                }

                else if (hotkeyProfile.ResetKey == e)
                {
                    Reset();
                }

                else if (hotkeyProfile.PauseKey == e)
                {
                    if (hotkeyProfile.HotkeyDelay > 0)
                    {
                        var pauseTimer = new System.Timers.Timer(hotkeyProfile.HotkeyDelay * 1000f)
                        {
                            Enabled = true
                        };
                        pauseTimer.Elapsed += pauseTimer_Elapsed;
                    }
                    else
                    {
                        Model.Pause();
                    }
                }

                else if (hotkeyProfile.SwitchComparisonPrevious == e)
                {
                    Model.SwitchComparisonPrevious();
                }
                else if (hotkeyProfile.SwitchComparisonNext == e)
                {
                    Model.SwitchComparisonNext();
                }
            }

            if (hotkeyProfile.ToggleGlobalHotkeys == e)
            {
                hotkeyProfile.GlobalHotkeysEnabled = !hotkeyProfile.GlobalHotkeysEnabled;
                SetProgressBar();
            }
            if (hotkeyProfile.ToggleVideoDebugOverlay == e && EnableExperimentalVideoDebugOverlay)
            {
                showVideoDebugOverlay = !showVideoDebugOverlay;
                DisposeVideoDebugOverlayCache();
                Invalidate();
            }
        };

        new Task(() =>
        {
            try
            {
                Invoke(action);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }).Start();
    }

    private void pauseTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
    {
        ((System.Timers.Timer)sender).Stop();
        Model.Pause();
    }

    private void splitTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
    {
        ((System.Timers.Timer)sender).Stop();
        StartOrSplit();
    }

    private void RefreshTimerWorker()
    {
        while (true)
        {
            int refreshRate = Math.Max(1, Math.Min(300, Settings.RefreshRate));

            Thread.Sleep(1000 / refreshRate);
            try
            {
                TimerElapsed();
            }
            catch { }
        }
    }

    private void TimerElapsed()
    {
        try
        {
            this.InvokeIfRequired(() =>
            {
                try
                {
                    Hook?.Poll();

                    if (CurrentState.Run.IsAutoSplitterActive())
                    {
                        CurrentState.Run.AutoSplitter.Component.Update(null, CurrentState, 0, 0, Layout.Mode);
                    }

                    if (DontRedraw)
                    {
                        return;
                    }

                    bool shouldAnimateVideo = IsVideoBackgroundActive();
                    bool requiresFullRefresh = RefreshesRemaining > 0 || InvalidationRequired;
                    bool throttleModalUiWork = ShouldThrottleModalUiWork();
                    bool throttleVideoUiWork = shouldAnimateVideo && ShouldThrottleVideoUiWork();

                    if (throttleModalUiWork)
                    {
                        InvalidationRequired = true;
                        return;
                    }

                    if (requiresFullRefresh && !throttleVideoUiWork)
                    {
                        InvalidateForm();
                        if (InvalidationRequired)
                        {
                            InvalidationRequired = false;
                        }
                    }
                    else if (shouldAnimateVideo && !throttleVideoUiWork)
                    {
                        // Keep layout components (timer/splits) updating while animating video.
                        // We still skip expensive full-layout hash checks in this path.
                        componentInvalidator.Restart();

                        // Already on the UI thread (RefreshTimerWorker always marshals here with Invoke).
                        try
                        {
                            ComponentRenderer.Update(componentInvalidator, CurrentState, ClientSize.Width, ClientSize.Height, Layout.Mode);
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex);
                        }

                        // Component invalidators queue UI repaint rectangles; mpv queues video-only paints separately.
                    }
                    else if (throttleVideoUiWork)
                    {
                        // Move/size loops need low-latency message handling. Keep a lightweight capped update.
                        int nowTick = Environment.TickCount;
                        const int throttleMs = 33;
                        if (unchecked(nowTick - lastThrottledVideoUiUpdateTick) >= throttleMs)
                        {
                            lastThrottledVideoUiUpdateTick = nowTick;
                            componentInvalidator.Restart();
                            try
                            {
                                ComponentRenderer.Update(componentInvalidator, CurrentState, ClientSize.Width, ClientSize.Height, Layout.Mode);
                            }
                            catch (Exception ex)
                            {
                                Log.Error(ex);
                            }
                        }
                    }
                    else
                    {
                        GlobalCache.Restart();
                        GlobalCache["LayoutHashCode"] = new XMLLayoutSaver().CreateLayoutNode(null, null, Layout);

                        if (GlobalCache.HasChanged)
                        {
                            InvalidateForm();
                        }
                        else
                        {
                            componentInvalidator.Restart();

                            this.InvokeIfRequired(() =>
                            {
                                try
                                {
                                    ComponentRenderer.Update(componentInvalidator, CurrentState, ClientSize.Width, ClientSize.Height, Layout.Mode);
                                }
                                catch (Exception ex)
                                {
                                    Log.Error(ex);
                                    Invalidate();
                                }
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                    Invalidate();
                }
            });
        }
        catch (Exception ex) when (ex is not ObjectDisposedException { ObjectName: "TimerForm" })
        {
            Log.Error(ex);
            Invalidate();
        }
    }

    private bool IsVideoBackgroundActive()
    {
        return EnableExperimentalBackgroundVideo
            && !backgroundVideoDisabledForSession
            && !ShouldUseWindowCaptureTransparency()
            && Layout?.Settings?.BackgroundType == BackgroundType.Video
            && backgroundVideoPlayer?.IsLoaded == true;
    }

    private bool ShouldThrottleModalUiWork()
    {
        if (DesignMode || isInsideMoveSizeLoop)
        {
            return true;
        }

        return false;
    }

    private bool IsModalLivePreviewActive()
    {
        if (IsInDialogMode)
        {
            return true;
        }

        try
        {
            foreach (Form owned in OwnedForms)
            {
                if (owned != null && !owned.IsDisposed && owned.Visible && owned.Modal)
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private bool ShouldUseModalVideoDownscale() =>
        IsModalLivePreviewActive();

    private static int GetBackgroundVideoReadbackFpsOverride() =>
        VideoReadbackMaxFps;

    private static int GetBackgroundVideoReadbackPixelsOverride(bool modalVideoDownscaleActive) =>
        modalVideoDownscaleActive ? ModalVideoMaxReadbackPixels : 0;

    internal bool IsVideoUiPresentTimerActive => videoUiPresentTimer?.Enabled == true;

    private int GetVideoUiPresentTargetFps()
    {
        int refresh = Math.Max(1, Math.Min(120, Settings?.RefreshRate ?? VideoUiPresentTargetFps));
        int videoCap = Math.Max(1, Math.Min(120, Settings?.VideoBackgroundPaintFps ?? VideoUiPresentTargetFps));
        return Math.Min(120, Math.Max(VideoUiPresentTargetFps, Math.Max(refresh, videoCap)));
    }

    private bool WantsVideoUiPresentTimer()
    {
        bool modalLivePreviewActive = IsModalLivePreviewActive();
        return EnableExperimentalBackgroundVideo
            && !DontRedraw
            && !backgroundVideoDisabledForSession
            && Layout?.Settings?.BackgroundType == BackgroundType.Video
            && backgroundVideoPlayer?.IsLoaded == true
            && (Enabled || modalLivePreviewActive);
    }

    private void SyncVideoUiPresentTimer(bool want)
    {
        if (!want)
        {
            if (videoUiPresentTimer != null)
            {
                videoUiPresentTimer.Enabled = false;
            }

            Interlocked.Exchange(ref pendingVideoUiPresentInvalidate, 0);
            Interlocked.Exchange(ref missedVideoUiPresentInvalidate, 0);
            Interlocked.Exchange(ref pendingVideoUiPresentPaint, 0);
            Interlocked.Exchange(ref pendingVideoUiPresentCrossThreadInvoke, 0);
            return;
        }

        if (videoUiPresentTimer == null)
        {
            videoUiPresentTimer = new System.Windows.Forms.Timer
            {
                Interval = Math.Max(1, 1000 / GetVideoUiPresentTargetFps())
            };
            videoUiPresentTimer.Tick += VideoUiPresentTimerOnTick;
        }

        int interval = Math.Max(1, 1000 / GetVideoUiPresentTargetFps());
        if (videoUiPresentTimer.Interval != interval)
        {
            videoUiPresentTimer.Interval = interval;
        }

        if (!videoUiPresentTimer.Enabled)
        {
            Interlocked.Exchange(ref pendingVideoUiPresentInvalidate, 0);
            Interlocked.Exchange(ref missedVideoUiPresentInvalidate, 0);
            videoUiPresentTimer.Enabled = true;
        }
    }

    private void VideoUiPresentTimerOnTick(object sender, EventArgs e)
    {
        try
        {
            if (!WantsVideoUiPresentTimer())
            {
                SyncVideoUiPresentTimer(false);
                return;
            }

            QueueVideoUiPresentInvalidate();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            SyncVideoUiPresentTimer(false);
        }
    }

    private void QueueVideoUiPresentInvalidate(bool queueMissedPresent = true)
    {
        if (IsDisposed || !IsHandleCreated || DontRedraw)
        {
            return;
        }

        if (Interlocked.Exchange(ref pendingVideoUiPresentInvalidate, 1) != 0)
        {
            Interlocked.Exchange(ref pendingVideoUiPresentPaint, 1);
            if (queueMissedPresent)
            {
                Interlocked.Exchange(ref missedVideoUiPresentInvalidate, 1);
            }
            return;
        }

        Interlocked.Exchange(ref pendingVideoUiPresentPaint, 1);
        Invalidate(ClientRectangle, false);
    }

    internal void RequestHeldVideoFramePresentFromBackgroundWorker()
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            if (Interlocked.Exchange(ref pendingVideoUiPresentCrossThreadInvoke, 1) != 0)
            {
                return;
            }

            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    Interlocked.Exchange(ref pendingVideoUiPresentCrossThreadInvoke, 0);
                    RequestHeldVideoFramePresentFromBackgroundWorker();
                });
            }
            catch
            {
                Interlocked.Exchange(ref pendingVideoUiPresentCrossThreadInvoke, 0);
            }

            return;
        }

        if (!WantsVideoUiPresentTimer())
        {
            return;
        }

        SyncVideoUiPresentTimer(true);
        QueueVideoUiPresentInvalidate(queueMissedPresent: false);
    }

    private void ReleaseVideoUiPresentInvalidateGateAfterPaint()
    {
        Interlocked.Exchange(ref pendingVideoUiPresentInvalidate, 0);
        if (Interlocked.Exchange(ref missedVideoUiPresentInvalidate, 0) != 0
            && WantsVideoUiPresentTimer()
            && !IsModalLivePreviewActive())
        {
            QueueVideoUiPresentInvalidate();
        }
    }

    private void DisposeVideoUiPresentTimer()
    {
        if (videoUiPresentTimer == null)
        {
            Interlocked.Exchange(ref pendingVideoUiPresentCrossThreadInvoke, 0);
            return;
        }

        videoUiPresentTimer.Enabled = false;
        videoUiPresentTimer.Tick -= VideoUiPresentTimerOnTick;
        videoUiPresentTimer.Dispose();
        videoUiPresentTimer = null;
        Interlocked.Exchange(ref pendingVideoUiPresentInvalidate, 0);
        Interlocked.Exchange(ref missedVideoUiPresentInvalidate, 0);
        Interlocked.Exchange(ref pendingVideoUiPresentPaint, 0);
        Interlocked.Exchange(ref pendingVideoUiPresentCrossThreadInvoke, 0);
    }

    private void DisposeWindowCaptureTransparencyPresentTimer()
    {
        if (windowCaptureTransparencyPresentTimer == null)
        {
            return;
        }

        windowCaptureTransparencyPresentTimer.Enabled = false;
        windowCaptureTransparencyPresentTimer.Tick -= WindowCaptureTransparencyPresentTimer_Tick;
        windowCaptureTransparencyPresentTimer.Dispose();
        windowCaptureTransparencyPresentTimer = null;
        windowCaptureTransparencyPresentPending = false;
        windowCaptureTransparencyNextPresentTicks = 0;
        ReleaseWindowCaptureTransparencyTimerResolution();
    }

    private void DisposeWindowCaptureTransparencySurface()
    {
        windowCaptureTransparencySurface?.Dispose();
        windowCaptureTransparencySurface = null;
    }

    private void SyncVideoBackgroundPoolTimer()
    {
        bool modalLivePreviewActive = IsModalLivePreviewActive();
        bool modalVideoDownscaleActive = ShouldUseModalVideoDownscale();
        bool want = EnableExperimentalBackgroundVideo
            && !backgroundVideoDisabledForSession
            && Layout?.Settings?.BackgroundType == BackgroundType.Video
            && backgroundVideoPlayer?.IsLoaded == true
            && (Enabled || modalLivePreviewActive);

        if (backgroundVideoPlayer != null)
        {
            backgroundVideoPlayer.MaxReadbackFpsOverride = GetBackgroundVideoReadbackFpsOverride();
            backgroundVideoPlayer.MaxReadbackPixelsOverride = GetBackgroundVideoReadbackPixelsOverride(modalVideoDownscaleActive);
            backgroundVideoPlayer.SyncPresentationWithHost(want, GetVideoUiPresentTargetFps());
        }

        SyncVideoUiPresentTimer(want);
    }

    /// <summary>
    /// Re-syncs mpv readback presentation after modal dialogs or <see cref="Enabled"/> toggles.
    /// </summary>
    private void SyncVideoBackgroundAfterInteractionModeChange()
    {
        if (DesignMode || !IsHandleCreated)
        {
            return;
        }

        try
        {
            SyncVideoBackgroundPoolTimer();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    private void SyncBackgroundVideoRunTimerDriftSyncTimer()
    {
        if (backgroundVideoRunTimerDriftSyncTimer == null)
        {
            backgroundVideoRunTimerDriftSyncTimer = new System.Windows.Forms.Timer();
            backgroundVideoRunTimerDriftSyncTimer.Interval = 2500;
            backgroundVideoRunTimerDriftSyncTimer.Tick += BackgroundVideoRunTimerDriftSyncTimerOnTick;
        }

        bool want = IsVideoBackgroundActive()
            && Layout?.Settings != null
            && Layout.Settings.VideoStartWithTimer
            && !Layout.Settings.LoopVideo;

        backgroundVideoRunTimerDriftSyncTimer.Enabled = want;
    }

    private void BackgroundVideoRunTimerDriftSyncTimerOnTick(object sender, EventArgs e)
    {
        try
        {
            if (IsDisposed || CurrentState == null || !IsVideoBackgroundActive() || Layout?.Settings == null)
            {
                return;
            }

            if (!Layout.Settings.VideoStartWithTimer || Layout.Settings.LoopVideo)
            {
                return;
            }

            if (CurrentState.CurrentPhase != TimerPhase.Running)
            {
                return;
            }

            TimeSpan? runRt = CurrentState.CurrentTime.RealTime;
            if (!runRt.HasValue || runRt.Value < TimeSpan.Zero)
            {
                return;
            }

            backgroundVideoPlayer?.TickPlaybackRunTimerDriftCorrection(CurrentState.CurrentPhase, runRt.Value);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    private void FixSize()
    {
        ComponentRenderer.CalculateOverallSize(Layout.Mode);
        float currentSize = ComponentRenderer.OverallSize;
        if (RefreshesRemaining <= 0)
        {
            if (OldSize != currentSize)
            {
                MinimumSize = new Size(0, 0);
                if (!HasExplicitLayoutClientSize(Layout))
                {
                    Size clientSize = ClientSize;
                    if (Layout.Mode == LayoutMode.Vertical)
                    {
                        ApplyInternalClientSize(new Size(clientSize.Width, (int)((currentSize / (double)OldSize * clientSize.Height) + 0.5)));
                    }
                    else
                    {
                        ApplyInternalClientSize(new Size((int)((currentSize / (double)OldSize * clientSize.Width) + 0.5), clientSize.Height));
                    }
                }

                OldSize = currentSize;
            }

            int minSize = (int)((currentSize / 5) + 0.5f);
            if (Layout.Mode == LayoutMode.Vertical)
            {
                MinimumSize = new Size(25, Math.Max(minSize, 25));
            }
            else
            {
                MinimumSize = new Size(Math.Max(minSize, 25), 25);
            }
        }
    }

    private void KeepLayoutSize()
    {
        if (RefreshesRemaining > 0)
        {
            ApplyLayoutClientSize(Layout);

            if (OldSize != ComponentRenderer.OverallSize)
            {
                UpdateRefreshesRemaining();
            }
            else
            {
                RefreshesRemaining--;
            }

            OldSize = ComponentRenderer.OverallSize;
        }
    }

    private void UpdateRefreshesRemaining()
    {
        InvalidateComponentOverlayCache();
        RefreshesRemaining = 5;
    }

    protected void InvalidateForm()
    {
        InvalidateComponentOverlayCache();
        this.InvokeIfRequired(() =>
        {
            try
            {
                UpdateAllComponents();
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        });

        InvalidateLayoutPaintSurface();
    }

    private void InvalidateLayoutPaintSurface()
    {
        if (ShouldUseWindowCaptureTransparency() && !isRenderingScreenshot)
        {
            RequestWindowCaptureTransparencyPresent();
            return;
        }

        Invalidate();
    }

    private void InvalidateComponentOverlayCache()
    {
        componentOverlayDirty = true;
        componentOverlayDirtyRegion?.Dispose();
        componentOverlayDirtyRegion = null;
        lastComponentOverlayRefreshTick = 0;
    }

    private void InvalidateComponentOverlayCache(Rectangle bounds)
    {
        if (componentOverlayDirty)
        {
            return;
        }

        Rectangle padded = bounds;
        padded.Inflate(ComponentOverlayDirtyPaddingPx, ComponentOverlayDirtyPaddingPx);
        Rectangle clipped = Rectangle.Intersect(padded, new Rectangle(0, 0, Math.Max(0, Width), Math.Max(0, Height)));
        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            return;
        }

        if (componentOverlayDirtyRegion == null)
        {
            componentOverlayDirtyRegion = new Region(clipped);
        }
        else
        {
            componentOverlayDirtyRegion.Union(clipped);
        }
    }

    private bool ShouldSuppressComponentInvalidatorFormPaint()
    {
        return ShouldUseComponentOverlayCache() && IsVideoUiPresentTimerActive;
    }

    private void DisposeComponentOverlayCache()
    {
        componentOverlayBitmap?.Dispose();
        componentOverlayBitmap = null;
        componentOverlayDirtyRegion?.Dispose();
        componentOverlayDirtyRegion = null;
        componentOverlayWidth = 0;
        componentOverlayHeight = 0;
        componentOverlayOverallSize = 0f;
        componentOverlayDirty = true;
    }

    protected void UpdateAllComponents()
    {
        foreach (UI.Components.IComponent component in Layout.Components)
        {
            component.Update(null, CurrentState, ClientSize.Width, ClientSize.Height, Layout.Mode);
        }
    }

    private void ConfigureComponentGraphics(Graphics graphics)
    {
        if (Layout.Settings.AntiAliasing || ShouldUseWindowCaptureTransparency())
        {
            graphics.TextRenderingHint = TextRenderingHint.AntiAlias;
        }
        else
        {
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }

        graphics.CompositingQuality = IsVideoBackgroundActive()
            ? CompositingQuality.HighSpeed
            : CompositingQuality.GammaCorrected;
        graphics.InterpolationMode = InterpolationMode.Bilinear;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
    }

    private void DrawComponentLayer(Graphics graphics, bool isVideoPresentPaint)
    {
        if (!ShouldUseComponentOverlayCache())
        {
            DisposeComponentOverlayCache();
            ConfigureComponentGraphics(graphics);
            RenderComponentsDirect(graphics, UpdateRegion);
            return;
        }

        if (!EnsureComponentOverlayBitmap())
        {
            ConfigureComponentGraphics(graphics);
            RenderComponentsDirect(graphics, UpdateRegion);
            return;
        }

        if (ShouldRebuildComponentOverlayForPaint(isVideoPresentPaint))
        {
            RebuildComponentOverlayBitmap();
        }

        graphics.ResetTransform();
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.DrawImageUnscaled(componentOverlayBitmap, 0, 0);
    }

    private bool ShouldUseComponentOverlayCache()
    {
        // This cache can leave stale text/shadow pixels when video and modal preview
        // paints run on different cadences. Keep component painting authoritative.
        return false;
    }

    private bool ShouldRebuildComponentOverlayForPaint(bool isVideoPresentPaint)
    {
        if (componentOverlayDirty
            || componentOverlayMode != Layout.Mode
            || componentOverlayOverallSize != ComponentRenderer.OverallSize)
        {
            return true;
        }

        if (componentOverlayDirtyRegion == null)
        {
            return false;
        }

        if (!isVideoPresentPaint)
        {
            return true;
        }

        int nowTick = Environment.TickCount;
        int minRefreshMs = Math.Max(1, 1000 / VideoPresentComponentOverlayRefreshFps);
        if (lastComponentOverlayRefreshTick == 0
            || unchecked(nowTick - lastComponentOverlayRefreshTick) >= minRefreshMs)
        {
            return true;
        }

        return false;
    }

    private bool EnsureComponentOverlayBitmap()
    {
        int width = Math.Max(1, Width);
        int height = Math.Max(1, Height);
        if (componentOverlayBitmap != null
            && componentOverlayWidth == width
            && componentOverlayHeight == height)
        {
            return true;
        }

        DisposeComponentOverlayCache();
        try
        {
            componentOverlayBitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            componentOverlayWidth = width;
            componentOverlayHeight = height;
            componentOverlayMode = Layout.Mode;
            componentOverlayOverallSize = ComponentRenderer.OverallSize;
            componentOverlayDirty = true;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            DisposeComponentOverlayCache();
            return false;
        }
    }

    private void RebuildComponentOverlayBitmap()
    {
        if (componentOverlayBitmap == null)
        {
            return;
        }

        bool fullRebuild = componentOverlayDirty
            || componentOverlayDirtyRegion == null
            || componentOverlayMode != Layout.Mode
            || componentOverlayOverallSize != ComponentRenderer.OverallSize;

        using (Graphics overlayGraphics = Graphics.FromImage(componentOverlayBitmap))
        {
            Region renderRegion = fullRebuild
                ? new Region(new Rectangle(0, 0, componentOverlayWidth, componentOverlayHeight))
                : componentOverlayDirtyRegion.Clone();

            try
            {
                overlayGraphics.CompositingMode = CompositingMode.SourceCopy;
                overlayGraphics.SetClip(renderRegion, CombineMode.Replace);
                overlayGraphics.Clear(Color.Transparent);
                overlayGraphics.ResetClip();
                overlayGraphics.CompositingMode = CompositingMode.SourceOver;
                ConfigureComponentGraphics(overlayGraphics);
                RenderComponentsDirect(overlayGraphics, renderRegion);
            }
            finally
            {
                overlayGraphics.ResetClip();
                renderRegion.Dispose();
            }
        }

        componentOverlayMode = Layout.Mode;
        componentOverlayOverallSize = ComponentRenderer.OverallSize;
        componentOverlayDirty = false;
        lastComponentOverlayRefreshTick = Environment.TickCount;
        componentOverlayDirtyRegion?.Dispose();
        componentOverlayDirtyRegion = null;
    }

    private void RenderComponentsDirect(Graphics graphics, Region clipRegion)
    {
        if (!TryGetComponentRenderScaleFactor(out float scaleFactor))
        {
            return;
        }

        RenderComponentsDirect(graphics, clipRegion, scaleFactor);
    }

    private void RenderComponentsDirect(Graphics graphics, Region clipRegion, float scaleFactor)
    {
        graphics.ResetTransform();
        graphics.TranslateTransform(-0.5f, -0.5f);
        graphics.ScaleTransform(scaleFactor, scaleFactor);
        float transformedWidth = ClientSize.Width;
        float transformedHeight = ClientSize.Height;

        if (Layout.Mode == LayoutMode.Vertical)
        {
            transformedWidth /= scaleFactor;
        }
        else
        {
            transformedHeight /= scaleFactor;
        }

        ComponentRenderer.Render(graphics, CurrentState, transformedWidth, transformedHeight, Layout.Mode, clipRegion);
    }

    private bool TryGetComponentRenderScaleFactor(out float scaleFactor)
    {
        scaleFactor = 1f;
        if (Layout == null || ComponentRenderer == null)
        {
            return false;
        }

        ComponentRenderer.CalculateOverallSize(Layout.Mode);
        float overallSize = ComponentRenderer.OverallSize;
        if (overallSize <= 0f || float.IsNaN(overallSize) || float.IsInfinity(overallSize))
        {
            return false;
        }

        float clientAxisSize = Layout.Mode == LayoutMode.Vertical
            ? ClientSize.Height
            : ClientSize.Width;
        if (clientAxisSize <= 0f)
        {
            return false;
        }

        scaleFactor = clientAxisSize / overallSize;
        return scaleFactor > 0f && !float.IsNaN(scaleFactor) && !float.IsInfinity(scaleFactor);
    }

    private void ReleaseMpvReadbackHostInvalidateGateAfterPaintForm()
    {
        if (backgroundVideoPlayer is LibMpvBackgroundPlayer mpvReadback)
        {
            mpvReadback.ReleasePendingHostInvalidateAfterHostPaint();
        }
    }

    private void PaintForm(Graphics g, Region clip)
    {
        bool isVideoPresentPaint = Interlocked.Exchange(ref pendingVideoUiPresentPaint, 0) != 0
            && IsVideoUiPresentTimerActive
            && IsVideoBackgroundActive();

        ApplyWindowCaptureTransparency();

        if (ShouldUseWindowCaptureTransparency() && !isRenderingScreenshot)
        {
            if (!isVideoPresentPaint)
            {
                MousePassThrough = Layout.Settings.MousePassThroughWhileRunning && Model.CurrentState.CurrentPhase == TimerPhase.Running && !IsForegroundWindow;
                AllowResizing = Layout.Settings.AllowResizing;
                AllowMoving = Layout.Settings.AllowMoving;
                FixSize();
            }

            if (!windowCaptureTransparencyLayerReady)
            {
                RequestWindowCaptureTransparencyPresent();
            }

            if (!isVideoPresentPaint)
            {
                FixSize();
                KeepLayoutSize();
                MaintainMinimumSize();
            }

            return;
        }

        if (!isVideoPresentPaint && !clip.GetBounds(g).Equals(UpdateRegion.GetBounds(g)))
        {
            UpdateRegion.Union(clip);
        }

        long backgroundProfilerStart = UiPaintProfiler.Begin();
        try
        {
            if (!ShouldUseWindowCaptureTransparency())
            {
                DrawBackground(g, isVideoPresentPaint);
            }
        }
        finally
        {
            UiPaintProfiler.Record(UiPaintProfilerSection.Background, backgroundProfilerStart);
        }

        if (!isVideoPresentPaint)
        {
            Opacity = SupportsWindowOpacityForCurrentLayout()
                ? Layout.Settings.Opacity
                : 1.0;

            // Set MousePassThrough after setting Opacity, because setting Opacity can reset the Form's WS_EX_LAYERED flag.
            MousePassThrough = Layout.Settings.MousePassThroughWhileRunning && Model.CurrentState.CurrentPhase == TimerPhase.Running && !IsForegroundWindow;

            AllowResizing = Layout.Settings.AllowResizing;
            AllowMoving = Layout.Settings.AllowMoving;
        }

        if (!isVideoPresentPaint)
        {
            FixSize();
        }

        long componentsProfilerStart = UiPaintProfiler.Begin();
        try
        {
            DrawComponentLayer(g, isVideoPresentPaint);
        }
        finally
        {
            UiPaintProfiler.Record(UiPaintProfilerSection.Components, componentsProfilerStart);
        }

        if (!isVideoPresentPaint)
        {
            FixSize();
            KeepLayoutSize();
            MaintainMinimumSize();
        }
    }

    private bool SupportsWindowOpacityForCurrentLayout()
    {
        return Layout?.Settings != null;
    }

    private bool ShouldUseWindowCaptureTransparency()
    {
        return Layout?.Settings?.TransparentBackgroundForCapture == true;
    }

    private void ApplyWindowCaptureTransparency()
    {
        bool enabled = ShouldUseWindowCaptureTransparency();
        if (TransparencyKey != Color.Empty)
        {
            TransparencyKey = Color.Empty;
        }

        if (!enabled)
        {
            windowCaptureTransparencyMediaSuspended = false;
            DisableWindowCaptureTransparencyPresenter();
        }
        else
        {
            EnableWindowCaptureTransparencyPresenter();
        }

        if (BackColor != Color.Black)
        {
            BackColor = Color.Black;
        }
    }

    private void EnableWindowCaptureTransparencyPresenter()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        if (Opacity != 1.0)
        {
            Opacity = 1.0;
        }

        uint prevWindowLong = GetWindowLong(Handle, GWL_EXSTYLE);
        if (!windowCaptureTransparencyPresenterActive)
        {
            // Clear and reapply WS_EX_LAYERED once so WinForms opacity/color-key state cannot poison UpdateLayeredWindow.
            SetWindowLong(Handle, GWL_EXSTYLE, prevWindowLong & ~WS_EX_LAYERED);
            prevWindowLong = GetWindowLong(Handle, GWL_EXSTYLE);
            windowCaptureTransparencyPresenterActive = true;
            windowCaptureTransparencyLayerReady = false;
        }

        uint targetWindowLong = prevWindowLong | WS_EX_LAYERED;
        if (MousePassThroughState)
        {
            targetWindowLong |= WS_EX_TRANSPARENT;
        }

        if (targetWindowLong != prevWindowLong)
        {
            SetWindowLong(Handle, GWL_EXSTYLE, targetWindowLong);
        }

        EnsureWindowCaptureTransparencyPresentTimer();
        RaiseWindowCaptureTransparencyTimerResolution();
        SuspendBackgroundMediaForTransparentCapture();
        if (!windowCaptureTransparencyPresenting)
        {
            RequestWindowCaptureTransparencyPresent();
        }
    }

    private void DisableWindowCaptureTransparencyPresenter()
    {
        windowCaptureTransparencyPresentPending = false;
        windowCaptureTransparencyLayerReady = false;
        windowCaptureTransparencyNextPresentTicks = 0;
        windowCaptureTransparencyPresentTimer?.Stop();
        ReleaseWindowCaptureTransparencyTimerResolution();
        DisposeWindowCaptureTransparencySurface();

        if (!windowCaptureTransparencyPresenterActive || !IsHandleCreated)
        {
            windowCaptureTransparencyPresenterActive = false;
            return;
        }

        uint prevWindowLong = GetWindowLong(Handle, GWL_EXSTYLE);
        SetWindowLong(Handle, GWL_EXSTYLE, prevWindowLong & ~(WS_EX_LAYERED | WS_EX_TRANSPARENT));
        windowCaptureTransparencyPresenterActive = false;
        MousePassThroughState = false;
    }

    private void SuspendBackgroundMediaForTransparentCapture()
    {
        if (windowCaptureTransparencyMediaSuspended)
        {
            return;
        }

        windowCaptureTransparencyMediaSuspended = true;

        if (backgroundVideoPlayer == null)
        {
            return;
        }

        backgroundVideoPlayer.Stop();
        loadedBackgroundVideoSource = null;
        failedBackgroundVideoSource = null;
        lastBackgroundVideoTimerSyncApplied = false;
    }

    private void EnsureWindowCaptureTransparencyPresentTimer()
    {
        if (windowCaptureTransparencyPresentTimer != null)
        {
            return;
        }

        windowCaptureTransparencyPresentTimer = new System.Windows.Forms.Timer
        {
            Interval = 1
        };
        windowCaptureTransparencyPresentTimer.Tick += WindowCaptureTransparencyPresentTimer_Tick;
    }

    private void RaiseWindowCaptureTransparencyTimerResolution()
    {
        if (windowCaptureTransparencyTimerResolutionRaised)
        {
            return;
        }

        if (WindowCaptureTimeBeginPeriod(1) == 0)
        {
            windowCaptureTransparencyTimerResolutionRaised = true;
        }
    }

    private void ReleaseWindowCaptureTransparencyTimerResolution()
    {
        if (!windowCaptureTransparencyTimerResolutionRaised)
        {
            return;
        }

        _ = WindowCaptureTimeEndPeriod(1);
        windowCaptureTransparencyTimerResolutionRaised = false;
    }

    private void RequestWindowCaptureTransparencyPresent()
    {
        if (!ShouldUseWindowCaptureTransparency() || isRenderingScreenshot || IsDisposed || !IsHandleCreated)
        {
            return;
        }

        windowCaptureTransparencyPresentPending = true;
        windowCaptureTransparencyNextPresentTicks = 0;
        EnsureWindowCaptureTransparencyPresentTimer();
        RaiseWindowCaptureTransparencyTimerResolution();
        if (!windowCaptureTransparencyPresentTimer.Enabled)
        {
            windowCaptureTransparencyPresentTimer.Start();
        }
    }

    private void WindowCaptureTransparencyPresentTimer_Tick(object sender, EventArgs e)
    {
        if (!WantsWindowCaptureTransparencyPresenter())
        {
            windowCaptureTransparencyPresentPending = false;
            windowCaptureTransparencyNextPresentTicks = 0;
            windowCaptureTransparencyPresentTimer.Stop();
            ReleaseWindowCaptureTransparencyTimerResolution();
            return;
        }

        if ((ShouldThrottleModalUiWork() || !Enabled) && windowCaptureTransparencyLayerReady && !windowCaptureTransparencyPresentPending)
        {
            return;
        }

        long nowTicks = Stopwatch.GetTimestamp();
        long presentPeriodTicks = Math.Max(1, Stopwatch.Frequency / WindowCaptureTransparencyPresentTargetFps);
        if (windowCaptureTransparencyNextPresentTicks == 0)
        {
            windowCaptureTransparencyNextPresentTicks = nowTicks;
        }

        if (nowTicks < windowCaptureTransparencyNextPresentTicks)
        {
            return;
        }

        windowCaptureTransparencyPresentPending = false;
        bool presented = false;
        try
        {
            presented = PresentWindowCaptureTransparencyLayer();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            windowCaptureTransparencyPresentTimer.Stop();
            ReleaseWindowCaptureTransparencyTimerResolution();
            return;
        }

        if (!ShouldUseWindowCaptureTransparency())
        {
            windowCaptureTransparencyPresentTimer.Stop();
            ReleaseWindowCaptureTransparencyTimerResolution();
            return;
        }

        if (!presented && !windowCaptureTransparencyLayerReady)
        {
            windowCaptureTransparencyPresentPending = true;
            return;
        }

        if (!presented)
        {
            windowCaptureTransparencyNextPresentTicks = Stopwatch.GetTimestamp() + presentPeriodTicks;
        }
        else
        {
            long afterPresentTicks = Stopwatch.GetTimestamp();
            long nextTicks = windowCaptureTransparencyNextPresentTicks + presentPeriodTicks;
            windowCaptureTransparencyNextPresentTicks = nextTicks <= afterPresentTicks
                ? afterPresentTicks + presentPeriodTicks
                : nextTicks;
        }
    }

    private bool PresentWindowCaptureTransparencyLayer()
    {
        if (windowCaptureTransparencyPresenting
            || !WantsWindowCaptureTransparencyPresenter()
            || IsDisposed
            || !IsHandleCreated)
        {
            return false;
        }

        windowCaptureTransparencyPresenting = true;
        try
        {
            EnableWindowCaptureTransparencyPresenter();
            UpdateComponentsForWindowCaptureTransparencyPresent();
            if (!TryGetComponentRenderScaleFactor(out float scaleFactor))
            {
                return false;
            }

            int width = Math.Max(1, ClientSize.Width);
            int height = Math.Max(1, ClientSize.Height);
            if (!EnsureWindowCaptureTransparencySurface(width, height))
            {
                return false;
            }

            using (Graphics bitmapGraphics = Graphics.FromImage(windowCaptureTransparencySurface.Bitmap))
            {
                RenderWindowCaptureTransparencyContent(bitmapGraphics, width, height, scaleFactor);
            }

            UpdatePerPixelAlphaWindow(windowCaptureTransparencySurface);
            windowCaptureTransparencyLayerReady = true;
            return true;
        }
        finally
        {
            windowCaptureTransparencyPresenting = false;
        }
    }

    private bool WantsWindowCaptureTransparencyPresenter()
    {
        return !DontRedraw
            && !isRenderingScreenshot
            && !IsDisposed
            && IsHandleCreated
            && Visible
            && WindowState != FormWindowState.Minimized
            && ShouldUseWindowCaptureTransparency();
    }

    private void UpdateComponentsForWindowCaptureTransparencyPresent()
    {
        if (Layout == null || ComponentRenderer == null || CurrentState == null)
        {
            return;
        }

        if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        ComponentRenderer.CalculateOverallSize(Layout.Mode);
        windowCaptureTransparencyComponentInvalidator.Restart();
        ComponentRenderer.Update(
            windowCaptureTransparencyComponentInvalidator,
            CurrentState,
            ClientSize.Width,
            ClientSize.Height,
            Layout.Mode);
    }

    private bool EnsureWindowCaptureTransparencySurface(int width, int height)
    {
        if (windowCaptureTransparencySurface != null
            && windowCaptureTransparencySurface.Width == width
            && windowCaptureTransparencySurface.Height == height)
        {
            return true;
        }

        DisposeWindowCaptureTransparencySurface();
        try
        {
            windowCaptureTransparencySurface = WindowCaptureTransparencySurface.Create(width, height);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            DisposeWindowCaptureTransparencySurface();
            return false;
        }
    }

    private void RenderWindowCaptureTransparencyContent(Graphics graphics, int width, int height, float scaleFactor)
    {
        graphics.ResetClip();
        graphics.ResetTransform();
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.Clear(Color.FromArgb(WindowCaptureInputAlpha, 0, 0, 0));
        graphics.CompositingMode = CompositingMode.SourceOver;
        ConfigureComponentGraphics(graphics);
        using var fullRegion = new Region(new Rectangle(0, 0, width, height));
        RenderComponentsDirect(graphics, fullRegion, scaleFactor);
    }

    private void UpdatePerPixelAlphaWindow(WindowCaptureTransparencySurface surface)
    {
        IntPtr screenDc = IntPtr.Zero;

        try
        {
            screenDc = GetDC(IntPtr.Zero);

            var topPosition = new NativePoint(Left, Top);
            var bitmapSize = new NativeSize(surface.Width, surface.Height);
            var sourcePosition = new NativePoint(0, 0);
            var blend = new BlendFunction
            {
                BlendOp = AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = GetWindowCaptureOpacityByte(),
                AlphaFormat = AC_SRC_ALPHA
            };

            if (!UpdateLayeredWindow(Handle, screenDc, ref topPosition, ref bitmapSize, surface.MemoryDc, ref sourcePosition, 0, ref blend, ULW_ALPHA))
            {
                Trace.TraceError($"UpdateLayeredWindow failed with Win32 error {Marshal.GetLastWin32Error()}.");
            }
        }
        finally
        {
            if (screenDc != IntPtr.Zero)
            {
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
    }

    private byte GetWindowCaptureOpacityByte()
    {
        double opacity = Math.Max(0.0, Math.Min(1.0, Layout?.Settings?.Opacity ?? 1.0));
        return (byte)Math.Round(opacity * byte.MaxValue);
    }

    private void TimerForm_Paint(object sender, PaintEventArgs e)
    {
        UiPaintProfiler.Enabled = EnableExperimentalVideoDebugOverlay && showVideoDebugOverlay;
        long profilerStart = UiPaintProfiler.Begin();
        try
        {
            using Region clip = e.Graphics.Clip;
            e.Graphics.ResetClip();
            PaintForm(e.Graphics, clip);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            Invalidate();
        }
        finally
        {
            UiPaintProfiler.Record(UiPaintProfilerSection.Paint, profilerStart);
            ReleaseVideoUiPresentInvalidateGateAfterPaint();
            ReleaseMpvReadbackHostInvalidateGateAfterPaintForm();
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (ShouldUseWindowCaptureTransparency() && !isRenderingScreenshot)
        {
            return;
        }

        base.OnPaintBackground(e);
    }

    private void OnTimerFormModalOwnerDisabled()
    {
        try
        {
            InvalidationRequired = true;
            SyncVideoBackgroundAfterInteractionModeChange();
            Invalidate();

            bool savedTopMost = TopMost;
            Activate();
            TopMost = true;
            TopMost = savedTopMost;
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    private void OnTimerFormModalOwnerEnabled()
    {
        if (suppressCompositorRestoreWhileFormClosingModals)
        {
            return;
        }

        try
        {
            UpdateBackgroundVideoControl();

            InvalidationRequired = true;
            InvalidateForm();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    private bool ShouldThrottleVideoUiWork()
    {
        if (!IsVideoBackgroundActive())
        {
            return false;
        }

        // ShowDialog disables the owner; keep readback/present timers from flooding WM_PAINT while a modal is open.
        if (IsHandleCreated && !Enabled)
        {
            return true;
        }

        if (isInsideMoveSizeLoop)
        {
            return true;
        }

        bool hasVisibleModalOwned = false;
        try
        {
            foreach (Form owned in OwnedForms)
            {
                if (owned != null && !owned.IsDisposed && owned.Visible && owned.Modal)
                {
                    hasVisibleModalOwned = true;
                    break;
                }
            }
        }
        catch
        {
            // Fail open: if modal probing fails, keep rendering active.
        }

        if (hasVisibleModalOwned)
        {
            return true;
        }

        // Fallback for dialogs that are not in OwnedForms. Avoid relying solely on IsInDialogMode
        // so stale flag values cannot leave video UI work throttled forever after dialog close.
        if (IsInDialogMode && Form.ActiveForm != this)
        {
            return true;
        }

        return false;
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        SyncVideoBackgroundAfterInteractionModeChange();
    }

    private void DrawBackground(Graphics g, bool isVideoPresentPaint = false)
    {
        if (!EnableExperimentalBackgroundVideo && Layout.Settings.BackgroundType == BackgroundType.Video)
        {
            Layout.Settings.BackgroundType = BackgroundType.SolidColor;
        }

        if (Layout.Settings.BackgroundType == BackgroundType.Video)
        {
            if (backgroundVideoDisabledForSession)
            {
                DrawColorOrGradientBackground(g);
                return;
            }

            if (!isVideoPresentPaint || backgroundVideoPlayer?.IsLoaded != true)
            {
                UpdateBackgroundVideoControl();
            }
            if (HasBackgroundVideoSource() && backgroundVideoPlayer?.IsLoaded == true)
            {
                try
                {
                    backgroundVideoPlayer.UpdatePlacement();
                    backgroundVideoPlayer.SetOpacity(Layout.Settings.VideoOpacity);
                    backgroundVideoPlayer.Render(g, Width, Height);
                    if (EnableExperimentalVideoDebugOverlay && showVideoDebugOverlay)
                    {
                        DrawVideoDebugOverlay(g);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                    backgroundVideoDisabledForSession = true;
                    Layout.Settings.BackgroundType = BackgroundType.SolidColor;
                    TearDownBackgroundVideoControl();
                    DrawColorOrGradientBackground(g);
                }
                return;
            }

            DrawColorOrGradientBackground(g);
            return;
        }

        UpdateBackgroundVideoControl();

        if (Layout.Settings.BackgroundType == BackgroundType.Image)
        {
            if (Layout.Settings.BackgroundImage != null)
            {
                if (Layout.Settings.BackgroundImage != previousBackground
                    || Layout.Settings.ImageOpacity != previousOpacity
                    || Layout.Settings.ImageBlur != previousBlur
                    || Layout.Settings.ImagePanX != previousImagePanX
                    || Layout.Settings.ImagePanY != previousImagePanY
                    || Layout.Settings.ImageZoomExtra != previousImageZoomExtra)
                {
                    CreateBakedBackground();
                }

                foreach (RectangleF rectangle in UpdateRegion.GetRegionScans(g.Transform))
                {
                    var rect = Rectangle.Round(rectangle);
                    g.DrawImage(bakedBackground, rect, rect, GraphicsUnit.Pixel);
                }
            }
        }
        else
        {
            DrawColorOrGradientBackground(g);
        }
    }

    private void DrawColorOrGradientBackground(Graphics g)
    {
        if (Layout.Settings.BackgroundColor != Color.Transparent
            || (Layout.Settings.BackgroundType != BackgroundType.SolidColor
            && Layout.Settings.BackgroundColor2 != Color.Transparent))
        {
            var gradientBrush = new LinearGradientBrush(
                        new PointF(0, 0),
                        Layout.Settings.BackgroundType == BackgroundType.HorizontalGradient
                        ? new PointF(Size.Width, 0)
                        : new PointF(0, Size.Height),
                        Layout.Settings.BackgroundColor,
                        Layout.Settings.BackgroundType == BackgroundType.SolidColor
                        ? Layout.Settings.BackgroundColor
                        : Layout.Settings.BackgroundColor2);
            g.FillRectangle(gradientBrush, 0, 0, Size.Width, Size.Height);
        }
    }

    private void DrawVideoDebugOverlay(Graphics g)
    {
        if (backgroundVideoPlayer == null)
        {
            return;
        }

        int nowTick = Environment.TickCount;
        if (videoDebugOverlayBitmap == null
            || unchecked(nowTick - lastVideoDebugOverlayRefreshTick) >= VideoDebugOverlayRefreshMs)
        {
            RebuildVideoDebugOverlayBitmap(g, nowTick);
        }

        if (videoDebugOverlayBitmap != null)
        {
            g.DrawImageUnscaled(videoDebugOverlayBitmap, 8, 8);
        }
    }

    private void RebuildVideoDebugOverlayBitmap(Graphics g, int nowTick)
    {
        DisposeVideoDebugOverlayCache();

        string debugText = backgroundVideoPlayer.GetDebugOverlayText();
        debugText += Environment.NewLine
            + $"UI presenter: {(IsVideoUiPresentTimerActive ? "on" : "off")} target {GetVideoUiPresentTargetFps()} Hz";
        string uiDebugText = UiPaintProfiler.GetOverlayText();
        if (!string.IsNullOrWhiteSpace(uiDebugText))
        {
            debugText += Environment.NewLine + Environment.NewLine + uiDebugText;
        }

        if (string.IsNullOrWhiteSpace(debugText))
        {
            return;
        }

        using var font = new Font("Consolas", 9f, FontStyle.Regular, GraphicsUnit.Point);
        SizeF textSize = g.MeasureString(debugText, font);
        int width = Math.Max(1, (int)Math.Ceiling(textSize.Width + 12f));
        int height = Math.Max(1, (int)Math.Ceiling(textSize.Height + 10f));
        var rect = new RectangleF(0f, 0f, width, height);
        videoDebugOverlayBitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var bgBrush = new SolidBrush(Color.FromArgb(160, 0, 0, 0));
        using var textBrush = new SolidBrush(Color.FromArgb(240, 255, 255, 255));
        using Graphics overlayGraphics = Graphics.FromImage(videoDebugOverlayBitmap);
        overlayGraphics.TextRenderingHint = TextRenderingHint.AntiAlias;
        overlayGraphics.FillRectangle(bgBrush, rect);
        overlayGraphics.DrawString(debugText, font, textBrush, 6f, 5f);
        lastVideoDebugOverlayRefreshTick = nowTick;
    }

    private void DisposeVideoDebugOverlayCache()
    {
        videoDebugOverlayBitmap?.Dispose();
        videoDebugOverlayBitmap = null;
        lastVideoDebugOverlayRefreshTick = 0;
    }

    private bool HasBackgroundVideoSource()
    {
        if (Layout.Settings.BackgroundVideoInputType == BackgroundVideoInputType.VlcMrl)
        {
            return !string.IsNullOrWhiteSpace(Layout.Settings.BackgroundVideoSource);
        }

        string path = Layout.Settings.BackgroundVideoPath;
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    }

    private string GetBackgroundVideoMrl()
    {
        if (Layout.Settings.BackgroundVideoInputType == BackgroundVideoInputType.VlcMrl)
        {
            return Layout.Settings.BackgroundVideoSource?.Trim();
        }

        string path = Layout.Settings.BackgroundVideoPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        try
        {
            string fullPath = Path.GetFullPath(path);
            return new Uri(fullPath).AbsoluteUri;
        }
        catch
        {
            return "";
        }
    }

    private string GetBackgroundVideoSource()
    {
        if (!HasBackgroundVideoSource())
        {
            return "";
        }

        if (Layout.Settings.BackgroundVideoInputType == BackgroundVideoInputType.VlcMrl)
        {
            return Layout.Settings.BackgroundVideoSource.Trim();
        }

        try
        {
            return Path.GetFullPath(Layout.Settings.BackgroundVideoPath);
        }
        catch
        {
            return "";
        }
    }

    private void UpdateBackgroundVideoControl()
    {
        try
        {
        if (ShouldUseWindowCaptureTransparency())
        {
            SuspendBackgroundMediaForTransparentCapture();
            return;
        }

        if (backgroundVideoDisabledForSession)
        {
            if (Layout.Settings.BackgroundType == BackgroundType.Video)
            {
                Layout.Settings.BackgroundType = BackgroundType.SolidColor;
            }

            backgroundVideoPlayer?.Stop();
            loadedBackgroundVideoSource = null;
            failedBackgroundVideoSource = null;
            return;
        }

        if (!EnableExperimentalBackgroundVideo)
        {
            backgroundVideoDisabledForSession = true;
            if (Layout.Settings.BackgroundType == BackgroundType.Video)
            {
                Layout.Settings.BackgroundType = BackgroundType.SolidColor;
            }
            TearDownBackgroundVideoControl();
            return;
        }

        bool shouldPlayVideo = Layout.Settings.BackgroundType == BackgroundType.Video;
        if (!shouldPlayVideo)
        {
            backgroundVideoPlayer?.Stop();
            loadedBackgroundVideoSource = null;
            failedBackgroundVideoSource = null;
            lastBackgroundVideoTimerSyncApplied = false;
            return;
        }

        string source = GetBackgroundVideoSource();
        if (string.IsNullOrWhiteSpace(source))
        {
            backgroundVideoPlayer?.Stop();
            loadedBackgroundVideoSource = null;
            failedBackgroundVideoSource = null;
            lastBackgroundVideoTimerSyncApplied = false;
            return;
        }

        if (backgroundVideoPlayer != null
            && (backgroundVideoPlayer is not LibMpvBackgroundPlayer mpvExisting || !mpvExisting.UsesDedicatedScheduler))
        {
            TearDownBackgroundVideoControl();
        }

        if (backgroundVideoPlayer == null)
        {
            backgroundVideoPlayer = new LibMpvBackgroundPlayer(this, useDedicatedScheduler: true);
        }

        backgroundVideoPlayer.UseHardwareDecoding = Layout.Settings.UseHardwareVideoDecoding;
        backgroundVideoPlayer.LoopVideo = Layout.Settings.LoopVideo;
        backgroundVideoPlayer.PlayAudio = Layout.Settings.PlayVideoAudio;
        backgroundVideoPlayer.AudioVolume = GetActiveBackgroundVideoAudioVolume();
        backgroundVideoPlayer.VideoPanX = Layout.Settings.VideoPanX;
        backgroundVideoPlayer.VideoPanY = Layout.Settings.VideoPanY;
        backgroundVideoPlayer.VideoZoomExtra = Layout.Settings.VideoZoomExtra;
        backgroundVideoPlayer.VideoBlurScale = Layout.Settings.VideoBlurScale;
        backgroundVideoPlayer.VideoBlurType = Layout.Settings.VideoBlurType;
        backgroundVideoPlayer.VideoBlurDegrees = Layout.Settings.VideoBlurDegrees;
        backgroundVideoPlayer.StartVideoWithTimer = Layout.Settings.VideoStartWithTimer;
        backgroundVideoPlayer.VideoStartOffsetSeconds = Layout.Settings.VideoStartOffsetSeconds;
        bool modalVideoDownscaleActive = ShouldUseModalVideoDownscale();
        backgroundVideoPlayer.MaxPresentFps = GetVideoUiPresentTargetFps();
        backgroundVideoPlayer.MaxReadbackFpsOverride = GetBackgroundVideoReadbackFpsOverride();
        backgroundVideoPlayer.MaxReadbackPixelsOverride = GetBackgroundVideoReadbackPixelsOverride(modalVideoDownscaleActive);
        backgroundVideoPlayer.NotifyHostClientSize(ClientSize.Width, ClientSize.Height);
        backgroundVideoPlayer.PingRuntimeOptionsToMpv();

        if (!backgroundVideoPlayer.IsInitialized)
        {
            if (!backgroundVideoPlayer.TryInitialize())
            {
                string reason = backgroundVideoPlayer.LastError ?? "Unknown video backend initialization error.";
                Trace.TraceError("Background video initialization failed: " + reason);
                ShowBackgroundVideoError(T("Video background could not be initialized.\n\n") + reason);
                TearDownBackgroundVideoControl();

                return;
            }
        }

        // If mpv restarted internally (e.g. shutdown event), force re-load even when source string is unchanged.
        if (backgroundVideoPlayer.IsInitialized && !backgroundVideoPlayer.IsLoaded)
        {
            loadedBackgroundVideoSource = null;
        }

        bool videoLoadedThisCall = false;

        if (!string.Equals(loadedBackgroundVideoSource, source, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(failedBackgroundVideoSource, source, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (backgroundVideoPlayer.Load(source))
            {
                loadedBackgroundVideoSource = source;
                failedBackgroundVideoSource = null;
                videoLoadedThisCall = true;
                videoTimerSyncEverStartedThisLoad = false;
            }
            else
            {
                string reason = backgroundVideoPlayer.LastError ?? "Unknown video load error.";
                Trace.TraceError("Background video load failed for source '" + source + "': " + reason);
                ShowBackgroundVideoError(T("Video background could not be loaded.\n\n") + reason);
                failedBackgroundVideoSource = source;
                loadedBackgroundVideoSource = null;
                lastBackgroundVideoTimerSyncApplied = false;
                backgroundVideoPlayer.Stop();
            }
        }

        if (backgroundVideoPlayer.IsLoaded)
        {
            bool timerSyncLayoutChanged = lastBackgroundVideoTimerSyncApplied
                && (lastBackgroundVideoStartWithTimer != Layout.Settings.VideoStartWithTimer
                    || lastBackgroundVideoPauseWhenRunCompletes != Layout.Settings.VideoPauseWhenRunCompletes
                    || lastBackgroundVideoKeepPlaybackAcrossTimerResets != Layout.Settings.VideoKeepPlaybackAcrossTimerResets
                    || Math.Abs(lastBackgroundVideoCompletionVolumePercent - Layout.Settings.VideoVolumePercentWhenRunCompletes) > 0.0005f
                    || Math.Abs(lastBackgroundVideoStartOffsetSeconds - Layout.Settings.VideoStartOffsetSeconds) > 0.0005f);

            bool shouldApplyTimerPhase = videoLoadedThisCall || timerSyncLayoutChanged || !lastBackgroundVideoTimerSyncApplied;

            lastBackgroundVideoStartWithTimer = Layout.Settings.VideoStartWithTimer;
            lastBackgroundVideoPauseWhenRunCompletes = Layout.Settings.VideoPauseWhenRunCompletes;
            lastBackgroundVideoKeepPlaybackAcrossTimerResets = Layout.Settings.VideoKeepPlaybackAcrossTimerResets;
            lastBackgroundVideoCompletionVolumePercent = Layout.Settings.VideoVolumePercentWhenRunCompletes;
            lastBackgroundVideoStartOffsetSeconds = Layout.Settings.VideoStartOffsetSeconds;
            lastBackgroundVideoTimerSyncApplied = true;

            if (shouldApplyTimerPhase)
            {
                ApplyBackgroundVideoTimerPhase();
            }
        }
        }
        finally
        {
            SyncVideoBackgroundPoolTimer();
            SyncBackgroundVideoRunTimerDriftSyncTimer();
        }
    }

    private void ShowBackgroundVideoError(string message)
    {
        MessageBox.Show(
            this,
            message,
            T("Video Background Error"),
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private void CrashOnVideoBackendFailure(string reason)
    {
        string message = T("LiveSplit failed to initialize the mpv video backend and must close.\n\n") + reason;

        try
        {
            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mpv-fatal-error.txt");
            File.WriteAllText(
                logPath,
                DateTime.Now.ToString("O") + Environment.NewLine +
                "Fatal mpv backend initialization failure" + Environment.NewLine +
                reason + Environment.NewLine);
        }
        catch
        {
            // Best effort logging only.
        }

        try
        {
            MessageBox.Show(
                message,
                T("Fatal Video Backend Error"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error,
                MessageBoxDefaultButton.Button1,
                MessageBoxOptions.DefaultDesktopOnly | MessageBoxOptions.ServiceNotification);
        }
        catch
        {
            // Best effort dialog only.
        }

        Environment.Exit(unchecked((int)0xDEAD0001));
    }

    private void TearDownBackgroundVideoControl()
    {
        DisposeVideoUiPresentTimer();

        if (backgroundVideoRunTimerDriftSyncTimer != null)
        {
            backgroundVideoRunTimerDriftSyncTimer.Enabled = false;
            backgroundVideoRunTimerDriftSyncTimer.Tick -= BackgroundVideoRunTimerDriftSyncTimerOnTick;
            backgroundVideoRunTimerDriftSyncTimer.Dispose();
            backgroundVideoRunTimerDriftSyncTimer = null;
        }

        backgroundVideoPlayer?.Dispose();
        backgroundVideoPlayer = null;
        loadedBackgroundVideoSource = null;
        failedBackgroundVideoSource = null;
        lastBackgroundVideoTimerSyncApplied = false;
    }

    private void ApplyBackgroundVideoTimerPhase()
    {
        this.InvokeIfRequired(() =>
        {
            if (backgroundVideoPlayer == null || !backgroundVideoPlayer.IsLoaded)
            {
                return;
            }

            if (Layout?.Settings == null || Layout.Settings.BackgroundType != BackgroundType.Video)
            {
                return;
            }

            if (!Layout.Settings.VideoStartWithTimer)
            {
                backgroundVideoPlayer.ExitTimerStartSyncToNormalAutoplay();
                ApplyLayoutVideoAudioVolumeToMpv();
                return;
            }

            switch (CurrentState.CurrentPhase)
            {
                case TimerPhase.NotRunning:
                    ApplyLayoutVideoAudioVolumeToMpv();
                    if (Layout.Settings.VideoKeepPlaybackAcrossTimerResets && videoTimerSyncEverStartedThisLoad)
                    {
                        backgroundVideoPlayer.RunTimerSyncNotRunningKeepPlayback();
                    }
                    else
                    {
                        backgroundVideoPlayer.RunTimerSyncWaitAtBeginning();
                    }

                    break;
                case TimerPhase.Running:
                    ApplyLayoutVideoAudioVolumeToMpv();
                    backgroundVideoPlayer.RunTimerSyncEnsurePlayingForRunningPhase();
                    break;
                case TimerPhase.Paused:
                    ApplyLayoutVideoAudioVolumeToMpv();
                    backgroundVideoPlayer.RunTimerSyncPausePlayback();
                    break;
                case TimerPhase.Ended:
                    if (Layout.Settings.VideoPauseWhenRunCompletes)
                    {
                        backgroundVideoPlayer.RunTimerSyncPausePlayback();
                    }
                    else
                    {
                        backgroundVideoPlayer.RunTimerSyncUnpauseAfterRunCompletes();
                    }

                    ApplyRunCompleteVideoVolumeToMpv();
                    break;
                default:
                    break;
            }
        });
    }

    private void ApplyLayoutVideoAudioVolumeToMpv()
    {
        if (backgroundVideoPlayer == null || !backgroundVideoPlayer.IsLoaded || Layout?.Settings == null)
        {
            return;
        }

        backgroundVideoPlayer.AudioVolume = Layout.Settings.VideoAudioVolume;
        if (backgroundVideoPlayer.IsInitialized)
        {
            backgroundVideoPlayer.PushAudioVolumeToMpvNow();
        }
    }

    private bool ShouldUseRunCompleteVideoVolume()
    {
        return Layout?.Settings != null
            && Layout.Settings.VideoStartWithTimer
            && CurrentState?.CurrentPhase == TimerPhase.Ended;
    }

    private float GetRunCompleteVideoVolume()
    {
        return Math.Min(100f, Math.Max(0f, Layout.Settings.VideoVolumePercentWhenRunCompletes)) / 100f;
    }

    private float GetActiveBackgroundVideoAudioVolume()
    {
        if (Layout?.Settings == null)
        {
            return 1f;
        }

        return ShouldUseRunCompleteVideoVolume()
            ? GetRunCompleteVideoVolume()
            : Layout.Settings.VideoAudioVolume;
    }

    private void ApplyRunCompleteVideoVolumeToMpv()
    {
        if (backgroundVideoPlayer == null || !backgroundVideoPlayer.IsLoaded || Layout?.Settings == null)
        {
            return;
        }

        backgroundVideoPlayer.AudioVolume = GetRunCompleteVideoVolume();
        if (backgroundVideoPlayer.IsInitialized)
        {
            backgroundVideoPlayer.PushAudioVolumeToMpvNow();
        }
    }

    private void DrawBackgroundImage(Graphics g, Image image, float opacity)
    {
        GetBackgroundImageCoverSourceRect(
            image,
            Width,
            Height,
            Layout.Settings.ImagePanX,
            Layout.Settings.ImagePanY,
            Layout.Settings.ImageZoomExtra,
            out float srcX,
            out float srcY,
            out float srcW,
            out float srcH);

        var matrix = new ColorMatrix
        {
            Matrix33 = opacity
        };
        var attributes = new ImageAttributes();
        attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

        g.InterpolationMode = InterpolationMode.Bilinear;
        foreach (RectangleF rectangle in UpdateRegion.GetRegionScans(g.Transform))
        {
            var rect = Rectangle.Round(rectangle);
            g.DrawImage(
                image,
                rect,
                srcX,
                srcY,
                srcW,
                srcH,
                GraphicsUnit.Pixel,
                attributes);
        }
    }

    /// <summary>
    /// Cover-fit source rectangle in image pixel space (same baseline as the legacy centered crop),
    /// with optional pan (-1..1) and zoom (0..1) matching layout video controls.
    /// </summary>
    internal static void GetBackgroundImageCoverSourceRect(
        Image image,
        float clientWidth,
        float clientHeight,
        float imagePanX,
        float imagePanY,
        float imageZoomExtra,
        out float srcX,
        out float srcY,
        out float srcW,
        out float srcH)
    {
        float iw = image.Width;
        float ih = image.Height;
        if (iw <= 0f || ih <= 0f || clientWidth <= 0f || clientHeight <= 0f)
        {
            srcX = srcY = 0f;
            srcW = Math.Max(1f, iw);
            srcH = Math.Max(1f, ih);
            return;
        }

        float croppedWidth;
        float croppedHeight;
        if (iw / ih > clientWidth / clientHeight)
        {
            croppedHeight = ih;
            croppedWidth = ih * (clientWidth / clientHeight);
        }
        else
        {
            croppedWidth = iw;
            croppedHeight = iw * (clientHeight / clientWidth);
        }

        float zoom = Math.Max(0f, Math.Min(1f, imageZoomExtra));
        float shrink = 1f - 0.55f * zoom;
        croppedWidth *= shrink;
        croppedHeight *= shrink;
        croppedWidth = Math.Min(croppedWidth, iw);
        croppedHeight = Math.Min(croppedHeight, ih);

        float panX = Math.Max(-1f, Math.Min(1f, imagePanX));
        float panY = Math.Max(-1f, Math.Min(1f, imagePanY));

        float maxShiftX = Math.Max(0f, iw - croppedWidth);
        float maxShiftY = Math.Max(0f, ih - croppedHeight);

        float sxCenter = maxShiftX * 0.5f + panX * maxShiftX * 0.5f;
        float syCenter = maxShiftY * 0.5f + panY * maxShiftY * 0.5f;

        srcX = Math.Max(0f, Math.Min(maxShiftX, sxCenter));
        srcY = Math.Max(0f, Math.Min(maxShiftY, syCenter));
        srcW = croppedWidth;
        srcH = croppedHeight;
    }

    private void CreateBakedBackground()
    {
        Image image = Layout.Settings.BackgroundImage;
        float opacity = Layout.Settings.ImageOpacity;
        float blur = Layout.Settings.ImageBlur;

        if (image != null)
        {
            if (blur > 0)
            {
                if (blur != previousBlur || image != previousBackground)
                {
                    blurredBackground?.Dispose();

                    blurredBackground = ImageBlur.Generate(image, blur * 10);
                }

                image = blurredBackground;
            }

            var bitmap = new Bitmap(Width, Height, image.PixelFormat);

            using (var graphics = Graphics.FromImage(bitmap))
            {
                DrawBackgroundImage(graphics, image, opacity);
            }

            bakedBackground?.Dispose();

            bakedBackground = bitmap;
            previousBackground = Layout.Settings.BackgroundImage;
            previousOpacity = opacity;
            previousBlur = blur;
            previousImagePanX = Layout.Settings.ImagePanX;
            previousImagePanY = Layout.Settings.ImagePanY;
            previousImageZoomExtra = Layout.Settings.ImageZoomExtra;
        }
    }

    private void TimerForm_MouseDown(object sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            MousePoint = new Point(e.X, e.Y);
            MouseIsDown = true;
        }
    }

    private void TimerForm_MouseMove(object sender, MouseEventArgs e)
    {
        if (AllowMoving && MouseIsDown)
        {
            int x = Location.X - MousePoint.X + e.Location.X;
            int y = Location.Y - MousePoint.Y + e.Location.Y;
            Location = new Point(x, y);
        }
    }

    private void TimerForm_MouseUp(object sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            MouseIsDown = false;
        }

        if (e.Button == MouseButtons.Right)
        {
            RightClickMenu.Show(this, e.Location);
            MouseIsDown = false;
        }
    }

    private void TimerForm_MouseWheel(object sender, MouseEventArgs e)
    {
        if (e.Delta > 0)
        {
            Model.ScrollUp();
        }
        else if (e.Delta < 0)
        {
            Model.ScrollDown();
        }
    }

    protected bool ShowSRLRules()
    {
        if (!Settings.AgreedToSRLRules)
        {
            Process.Start(SRLSettings.SRLRulesLink);
            DialogResult result = MessageBox.Show(this, T("Please read through the rules of SpeedRunsLive carefully.\r\nDo you agree to these rules?"), T("SpeedRunsLive Rules"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
            {
                Settings.AgreedToSRLRules = true;
                return true;
            }

            return false;
        }

        return true;
    }

    protected override void WndProc(ref Message m)
    {
        const uint WM_ENABLE = 0x000A;
        const uint WM_NCHITTEST = 0x0084;
        const uint WM_MOUSEMOVE = 0x0200;
        const uint WM_PAINT = 0x000F;
        const uint WM_SIZING = 0x0214;
        const uint WM_ENTERSIZEMOVE = 0x0231;
        const uint WM_EXITSIZEMOVE = 0x0232;

        const uint HTLEFT = 10;
        const uint HTRIGHT = 11;
        const uint HTBOTTOMRIGHT = 17;
        const uint HTBOTTOM = 15;
        const uint HTBOTTOMLEFT = 16;
        const uint HTTOP = 12;
        const uint HTTOPLEFT = 13;
        const uint HTTOPRIGHT = 14;

        const int RESIZE_HANDLE_SIZE = 10;
        bool handled = false;

        if (m.Msg == (int)WM_ENABLE)
        {
            bool enabled = m.WParam.ToInt32() != 0;
            // Modal dialogs disable the owner; keep layered-window / readback video state consistent.
            if (!enabled)
            {
                OnTimerFormModalOwnerDisabled();
            }

            try
            {
                base.WndProc(ref m);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }

            if (enabled)
            {
                OnTimerFormModalOwnerEnabled();
            }

            return;
        }

        if (AllowResizing && m.Msg is (int)WM_NCHITTEST or (int)WM_MOUSEMOVE)
        {
            Size formSize = Size;
            var screenPoint = new Point(m.LParam.ToInt32());
            Point clientPoint = PointToClient(screenPoint);

            var boxes = new Dictionary<uint, Rectangle>() {
                {HTBOTTOMLEFT, new Rectangle(0, formSize.Height - RESIZE_HANDLE_SIZE, RESIZE_HANDLE_SIZE, RESIZE_HANDLE_SIZE)},
                {HTBOTTOM, new Rectangle(RESIZE_HANDLE_SIZE, formSize.Height - RESIZE_HANDLE_SIZE, formSize.Width - (2*RESIZE_HANDLE_SIZE), RESIZE_HANDLE_SIZE)},
                {HTBOTTOMRIGHT, new Rectangle(formSize.Width - RESIZE_HANDLE_SIZE, formSize.Height - RESIZE_HANDLE_SIZE, RESIZE_HANDLE_SIZE, RESIZE_HANDLE_SIZE)},
                {HTRIGHT, new Rectangle(formSize.Width - RESIZE_HANDLE_SIZE, RESIZE_HANDLE_SIZE, RESIZE_HANDLE_SIZE, formSize.Height - (2*RESIZE_HANDLE_SIZE))},
                {HTTOPRIGHT, new Rectangle(formSize.Width - RESIZE_HANDLE_SIZE, 0, RESIZE_HANDLE_SIZE, RESIZE_HANDLE_SIZE) },
                {HTTOP, new Rectangle(RESIZE_HANDLE_SIZE, 0, formSize.Width - (2*RESIZE_HANDLE_SIZE), RESIZE_HANDLE_SIZE) },
                {HTTOPLEFT, new Rectangle(0, 0, RESIZE_HANDLE_SIZE, RESIZE_HANDLE_SIZE) },
                {HTLEFT, new Rectangle(0, RESIZE_HANDLE_SIZE, RESIZE_HANDLE_SIZE, formSize.Height - (2*RESIZE_HANDLE_SIZE)) }
            };

            foreach (KeyValuePair<uint, Rectangle> hitBox in boxes)
            {
                if (hitBox.Value.Contains(clientPoint))
                {
                    m.Result = (IntPtr)hitBox.Key;
                    handled = true;
                    break;
                }
            }
        }

        if (m.Msg == WM_SIZING)
        {
            handled = WmSizingProc(ref m);
        }
        else if (m.Msg == WM_ENTERSIZEMOVE)
        {
            isInsideMoveSizeLoop = true;
        }
        else if (m.Msg == WM_EXITSIZEMOVE)
        {
            isInsideMoveSizeLoop = false;
        }

        if (m.Msg == WM_PAINT)
        {
            if (hRgn != IntPtr.Zero)
            {
                DeleteObject(hRgn);
            }

            hRgn = CreateRectRgn(0, 0, 0, 0);
            int x = GetUpdateRgn(Handle, hRgn, false);
            try
            {
                UpdateRegion = Region.FromHrgn(hRgn);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        if (!handled)
        {
            try
            {
                base.WndProc(ref m);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }
    }

    private bool WmSizingProc(ref Message m)
    {
        const uint WMSZ_TOPLEFT = 4;
        const uint WMSZ_TOPRIGHT = 5;
        const uint WMSZ_BOTTOMLEFT = 7;
        const uint WMSZ_BOTTOMRIGHT = 8;

        if (!ResizingInitialAspectRatio.HasValue)
        {
            return false;
        }

        if (!ModifierKeys.HasFlag(Keys.Shift))
        {
            return false;
        }

        bool anchorLeft;
        bool anchorTop;

        switch ((uint)m.WParam)
        {
            case WMSZ_TOPLEFT:
                anchorLeft = false;
                anchorTop = false;
                break;
            case WMSZ_TOPRIGHT:
                anchorLeft = true;
                anchorTop = false;
                break;
            case WMSZ_BOTTOMLEFT:
                anchorLeft = false;
                anchorTop = true;
                break;
            case WMSZ_BOTTOMRIGHT:
                anchorLeft = true;
                anchorTop = true;
                break;

            default:
                return false;
        }

        var rect = (Win32.RECT)Marshal.PtrToStructure(m.LParam, typeof(Win32.RECT));
        float currentAspectRatio = (float)rect.Width / rect.Height;

        if (currentAspectRatio >= ResizingInitialAspectRatio.Value)
        {
            int newWidth = (int)(rect.Height * ResizingInitialAspectRatio.Value);
            if (anchorLeft)
            {
                rect.Right = rect.Left + newWidth;
            }
            else
            {
                rect.Left = rect.Right - newWidth;
            }
        }
        else
        {
            int newHeight = (int)(rect.Width / ResizingInitialAspectRatio.Value);
            if (anchorTop)
            {
                rect.Bottom = rect.Top + newHeight;
            }
            else
            {
                rect.Top = rect.Bottom - newHeight;
            }
        }

        Marshal.StructureToPtr(rect, m.LParam, false);

        return true;
    }

    private IntPtr hRgn = IntPtr.Zero;
    [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject([In] IntPtr hObject);

    private void exitToolStripMenuItem_Click(object sender, EventArgs e)
    {
        Exit();
    }

    private void Exit()
    {
        Close();
    }

    private void SetRun(IRun run)
    {
        foreach (Image icon in CurrentState.Run.Select(x => x.Icon).Except(run.Select(x => x.Icon)))
        {
            icon?.Dispose();
        }

        if (CurrentState.Run.GameIcon != null && CurrentState.Run.GameIcon != run.GameIcon)
        {
            CurrentState.Run.GameIcon.Dispose();
        }

        run.ComparisonGenerators = new List<IComparisonGenerator>(CurrentState.Run.ComparisonGenerators);
        foreach (IComparisonGenerator generator in run.ComparisonGenerators)
        {
            generator.Run = run;
        }

        run.FixSplits();
        DeactivateAutoSplitter();
        CurrentState.Run = run;
        InvalidationRequired = true;
        RegenerateComparisons();
        SwitchComparison(CurrentState.CurrentComparison);
        CreateAutoSplitter();
        UpdateRefreshesRemaining();
        if (!string.IsNullOrEmpty(run.LayoutPath))
        {
            if (run.LayoutPath == "?default")
            {
                LoadDefaultLayout();
            }
            else if (CurrentState.Layout.FilePath != run.LayoutPath)
            {
                OpenLayoutFromFile(run.LayoutPath);
            }
        }
    }

    private void CreateAutoSplitter()
    {
        AutoSplitter splitter = AutoSplitterFactory.Instance.Create(CurrentState.Run.GameName);
        CurrentState.Run.AutoSplitter = splitter;
        if (splitter != null && CurrentState.Settings.ActiveAutoSplitters.Contains(CurrentState.Run.GameName))
        {
            splitter.Activate(CurrentState);
            if (splitter.IsActivated
            && CurrentState.Run.AutoSplitterSettings != null
            && CurrentState.Run.AutoSplitterSettings.GetAttribute("gameName") == CurrentState.Run.GameName)
            {
                CurrentState.Run.AutoSplitter.Component.SetSettings(CurrentState.Run.AutoSplitterSettings);
            }
        }
    }

    private void DeactivateAutoSplitter()
    {
        CurrentState.Run.AutoSplitter?.Deactivate();
    }

    private void AddCurrentSplitsToLRU(TimingMethod lastTimingMethod, string lastHotkeyProfile)
    {
        if (CurrentState.Run != null && Settings.RecentSplits.Any(x => x.Path == CurrentState.Run.FilePath))
        {
            AddSplitsFileToLRU(CurrentState.Run.FilePath, CurrentState.Run, lastTimingMethod, lastHotkeyProfile);
        }
    }

    private IRun LoadRunFromFile(string filePath, TimingMethod? previousTimingMethod = null, string previousHotkeyProfile = null)
    {
        IRun run;

        using (FileStream stream = File.OpenRead(filePath))
        {
            RunFactory.Stream = stream;
            RunFactory.FilePath = filePath;

            run = RunFactory.Create(ComparisonGeneratorsFactory);
        }

        if (previousTimingMethod.HasValue && previousHotkeyProfile != null)
        {
            AddCurrentSplitsToLRU(previousTimingMethod.Value, previousHotkeyProfile);
        }

        AddSplitsFileToLRU(filePath, run, CurrentState.CurrentTimingMethod, CurrentState.CurrentHotkeyProfile);

        return run;
    }

    private ILayout LoadLayoutFromFile(string filePath)
    {
        using FileStream stream = File.OpenRead(filePath);
        ILayout layout = new XMLLayoutFactory(stream).Create(CurrentState);
        layout.FilePath = filePath;
        AddLayoutFileToLRU(filePath);
        return layout;
    }

    private void OpenRunFromFile(string filePath)
    {
        Cursor.Current = Cursors.WaitCursor;
        try
        {
            if (!WarnUserAboutSplitsSave())
            {
                return;
            }

            if (!WarnAndRemoveTimerOnly(true))
            {
                return;
            }

            TimingMethod previousTimingMethod = CurrentState.CurrentTimingMethod;
            string previousHotkeyProfile = CurrentState.CurrentHotkeyProfile;

            UpdateStateFromSplitsPath(filePath);

            IRun run = LoadRunFromFile(filePath, previousTimingMethod, previousHotkeyProfile);
            SetRun(run);
            CurrentState.CallRunManuallyModified();
        }
        catch (Exception e)
        {
            Log.Error(e);
            DontRedraw = true;
            MessageBox.Show(this, T("The selected file was not recognized as a splits file."), T("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            DontRedraw = false;
        }

        Cursor.Current = Cursors.Arrow;
    }

    private void UpdateStateFromSplitsPath(string filePath)
    {
        RecentSplitsFile recentSplitsFile = Settings.RecentSplits.LastOrDefault(splitsFile => splitsFile.Path == filePath);
        if (recentSplitsFile.Path != null)
        {
            CurrentState.CurrentTimingMethod = recentSplitsFile.LastTimingMethod;
            if (Settings.HotkeyProfiles.ContainsKey(recentSplitsFile.LastHotkeyProfile))
            {
                CurrentState.CurrentHotkeyProfile = recentSplitsFile.LastHotkeyProfile;
                if (Hook != null)
                {
                    Settings.UnregisterAllHotkeys(Hook);
                    Settings.RegisterHotkeys(Hook, CurrentState.CurrentHotkeyProfile);
                }
            }
        }
    }

    private void OpenSplits()
    {
        using var splitDialog = new OpenFileDialog();
        splitDialog.Filter = "LiveSplit Splits (*.lss)|*.lss|All files (*.*)|*.*";
        IsInDialogMode = true;
        try
        {
            if (Settings.RecentSplits.Any() && !string.IsNullOrEmpty(Settings.RecentSplits.Last().Path))
            {
                splitDialog.InitialDirectory = Path.GetDirectoryName(Settings.RecentSplits.Last().Path);
            }

            DialogResult result = splitDialog.ShowDialog(this);
            if (result == DialogResult.OK)
            {
                OpenRunFromFile(splitDialog.FileName);
            }
        }
        finally
        {
            IsInDialogMode = false;
            SyncVideoBackgroundAfterInteractionModeChange();
        }
    }

    private bool SaveSplitsAs(bool promptPBMessage)
    {
        using var splitDialog = new SaveFileDialog();
        splitDialog.FileName = CurrentState.Run.GetExtendedFileName();
        splitDialog.Filter = "LiveSplit Splits (*.lss)|*.lss|All Files (*.*)|*.*";
        IsInDialogMode = true;
        try
        {
            DialogResult result = splitDialog.ShowDialog(this);
            if (result == DialogResult.OK)
            {
                if (!splitDialog.FileName.EndsWith(".lss"))
                {
                    MessageBox.Show(this, T("Cannot save splits with a file type that is not .lss"), T("Save Failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }

                CurrentState.Run.FilePath = splitDialog.FileName;
                return SaveSplits(promptPBMessage);
            }

            return false;
        }
        finally
        {
            IsInDialogMode = false;
            SyncVideoBackgroundAfterInteractionModeChange();
        }
    }

    private bool SaveSplits(bool promptPBMessage)
    {
        string savePath = CurrentState.Run.FilePath;

        if (savePath == null)
        {
            return SaveSplitsAs(promptPBMessage);
        }

        CurrentState.Run.FixSplits();

        DialogResult result = DialogResult.No;

        if (promptPBMessage && ((CurrentState.CurrentPhase == TimerPhase.Ended
            && CurrentState.Run.Last().PersonalBestSplitTime[CurrentState.CurrentTimingMethod] != null
            && CurrentState.Run.Last().SplitTime[CurrentState.CurrentTimingMethod] >= CurrentState.Run.Last().PersonalBestSplitTime[CurrentState.CurrentTimingMethod])
            || CurrentState.CurrentPhase == TimerPhase.Running
            || CurrentState.CurrentPhase == TimerPhase.Paused))
        {
            DontRedraw = true;
            result = MessageBox.Show(this, T("This run did not beat your current splits. Would you like to save this run as a Personal Best?"), T("Save as Personal Best?"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            DontRedraw = false;
            if (result == DialogResult.Yes)
            {
                Model.ResetAndSetAttemptAsPB();
            }
            else if (result == DialogResult.Cancel)
            {
                return false;
            }
        }

        LiveSplitState stateCopy = CurrentState;
        if (result == DialogResult.No)
        {
            var modelCopy = new TimerModel();
            stateCopy = CurrentState.Clone() as LiveSplitState;
            modelCopy.CurrentState = stateCopy;
            modelCopy.Reset();
        }

        try
        {
            if (!File.Exists(savePath))
            {
                File.Create(savePath).Close();
            }

            using (var memoryStream = new MemoryStream())
            {
                RunSaver.Save(stateCopy.Run, memoryStream);

                using (FileStream stream = File.Open(savePath, FileMode.Create, FileAccess.Write))
                {
                    byte[] buffer = memoryStream.GetBuffer();
                    stream.Write(buffer, 0, (int)memoryStream.Length);
                }

                CurrentState.Run.HasChanged = false;
            }

            AddSplitsFileToLRU(savePath, stateCopy.Run, CurrentState.CurrentTimingMethod, CurrentState.CurrentHotkeyProfile);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, T("Splits could not be saved!"), T("Save Failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            Log.Error(ex);
            return false;
        }

        return true;
    }

    private bool SaveLayout()
    {
        string savePath = Layout.FilePath;
        EnsureLayoutHasClientSizeForSave();

        Layout.X = Location.X;
        Layout.Y = Location.Y;

        if (savePath == null)
        {
            return SaveLayoutAs();
        }

        try
        {
            if (!File.Exists(savePath))
            {
                File.Create(savePath).Close();
            }

            using (var memoryStream = new MemoryStream())
            {
                LayoutSaver.Save(Layout, memoryStream);

                using (FileStream stream = File.Open(savePath, FileMode.Create, FileAccess.Write))
                {
                    byte[] buffer = memoryStream.GetBuffer();
                    stream.Write(buffer, 0, (int)memoryStream.Length);
                }

                Layout.HasChanged = false;
            }

            AddLayoutFileToLRU(savePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, T("Layout could not be saved!"), T("Save Failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            Log.Error(ex);
            return false;
        }

        return true;
    }

    private void EnsureLayoutHasClientSizeForSave()
    {
        if (Layout == null)
        {
            return;
        }

        if (!HasExplicitLayoutClientSize(Layout))
        {
            StoreCurrentClientSizeInLayout();
        }
    }

    private void EditSplits()
    {
        var runCopy = CurrentState.Run.Clone() as IRun;
        var activeAutoSplitters = new List<string>(CurrentState.Settings.ActiveAutoSplitters);
        using var editor = new RunEditorDialog(CurrentState);
        editor.RunEdited += editor_RunEdited;
        editor.ComparisonRenamed += editor_ComparisonRenamed;
        editor.SegmentRemovedOrAdded += editor_SegmentRemovedOrAdded;
        try
        {
            TopMost = false;
            IsInDialogMode = true;
            SyncVideoBackgroundAfterInteractionModeChange();
            if (CurrentState.CurrentPhase == TimerPhase.NotRunning)
            {
                editor.AllowChangingSegments = true;
            }

            UiLocalizer.Apply(editor, CurrentLanguage);
            DialogResult result = editor.ShowDialog(this);
            if (result == DialogResult.Cancel)
            {
                foreach (Image image in runCopy.Select(x => x.Icon))
                {
                    editor.ImagesToDispose.Remove(image);
                }

                editor.ImagesToDispose.Remove(runCopy.GameIcon);

                CurrentState.Settings.ActiveAutoSplitters = activeAutoSplitters;
                SetRun(runCopy);
                CurrentState.CallRunManuallyModified();
            }

            foreach (Image image in editor.ImagesToDispose)
            {
                image.Dispose();
            }
        }
        finally
        {
            TopMost = Layout.Settings.AlwaysOnTop;
            IsInDialogMode = false;
            SyncVideoBackgroundAfterInteractionModeChange();

            editor.RunEdited -= editor_RunEdited;
            editor.ComparisonRenamed -= editor_ComparisonRenamed;
            editor.SegmentRemovedOrAdded -= editor_SegmentRemovedOrAdded;
        }
    }

    private void editor_SegmentRemovedOrAdded(object sender, EventArgs e)
    {
        InvalidationRequired = true;
    }

    private void editor_ComparisonRenamed(object sender, EventArgs e)
    {
        CurrentState.CallComparisonRenamed(e);
    }

    private void editor_RunEdited(object sender, EventArgs e)
    {
        RegenerateComparisons();
        CurrentState.CallRunManuallyModified();
        WarnAndRemoveTimerOnly(false);
    }

    protected bool WarnAndRemoveTimerOnly(bool canCancel)
    {
        if (InTimerOnlyMode)
        {
            if (!WarnUserAboutLayoutSave(canCancel))
            {
                return false;
            }

            InTimerOnlyMode = false;
            ILayout layout;
            try
            {
                string lastLayoutPath = Settings.RecentLayouts.LastOrDefault(x => !string.IsNullOrEmpty(x));
                if (lastLayoutPath != null)
                {
                    layout = LoadLayoutFromFile(lastLayoutPath);
                }
                else
                {
                    layout = new StandardLayoutFactory().Create(CurrentState);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                layout = new StandardLayoutFactory().Create(CurrentState);
            }

            layout.X = Location.X;
            layout.Y = Location.Y;
            SetLayout(layout);
        }

        return true;
    }

    private void EditLayout()
    {
        using var editor = new LayoutEditorDialog(Layout, CurrentState, this);
        editor.OrientationSwitched += editor_OrientationSwitched;
        editor.LayoutResized += editor_LayoutResized;
        editor.LayoutSettingsAssigned += editor_LayoutSettingsAssigned;
        editor.LayoutSettingsLiveVideoApply += editor_LayoutSettingsLiveVideoApply;
        Layout.X = Location.X;
        Layout.Y = Location.Y;
        StoreCurrentClientSizeInLayout();

        var layoutCopy = (ILayout)Layout.Clone();
        var document = new XmlDocument();
        var componentSettings = Layout.Components.Select(x =>
            {
                try
                {
                    return x.GetSettings(document);
                }
                catch (Exception e)
                {
                    Log.Error(e);
                    return null;
                }
            }).ToList();
        try
        {
            TopMost = false;
            IsInDialogMode = true;
            UiLocalizer.Apply(editor, CurrentLanguage);
            editor.ShowDialog(this);
            if (editor.DialogResult == DialogResult.Cancel)
            {
                foreach (UI.Components.IComponent component in layoutCopy.Components)
                {
                    editor.ComponentsToDispose.Remove(component);
                }

                editor.ImagesToDispose.Remove(layoutCopy.Settings.BackgroundImage);

                using (List<XmlNode>.Enumerator enumerator = componentSettings.GetEnumerator())
                {
                    foreach (UI.Components.IComponent component in layoutCopy.Components)
                    {
                        if (enumerator.MoveNext())
                        {
                            component.SetSettings(enumerator.Current);
                        }
                    }
                }

                SetLayout(layoutCopy);
            }

            foreach (UI.Components.IComponent component in editor.ComponentsToDispose)
            {
                component.Dispose();
            }

            foreach (Image image in editor.ImagesToDispose)
            {
                image.Dispose();
            }
        }
        finally
        {
            TopMost = Layout.Settings.AlwaysOnTop;
            IsInDialogMode = false;
            editor.OrientationSwitched -= editor_OrientationSwitched;
            editor.LayoutResized -= editor_LayoutResized;
            editor.LayoutSettingsAssigned -= editor_LayoutSettingsAssigned;
            editor.LayoutSettingsLiveVideoApply -= editor_LayoutSettingsLiveVideoApply;

            // Propagate layout setting changes (loop, source, zoom, etc.) as soon as the editor closes.
            try
            {
                ApplyVideoBackgroundSettingsImmediate();
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }

            SyncVideoBackgroundAfterInteractionModeChange();
        }
    }

    private void editor_LayoutSettingsAssigned(object sender, EventArgs e)
    {
        InvalidationRequired = true;
        // Fires when the Layout Settings sub-dialog is cancelled (settings restored). Make sure
        // the runtime player picks up the restored values right away.
        try
        {
            ApplyVideoBackgroundSettingsImmediate();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    private void editor_LayoutSettingsLiveVideoApply(object sender, BackgroundVideoLiveApplyEventArgs e)
    {
        InvalidationRequired = true;
        try
        {
            ApplyBackgroundVideoLiveApply(e.Scope);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    private void ApplyBackgroundVideoLiveApply(BackgroundVideoLiveApplyScope scope)
    {
        if (Layout?.Settings == null)
        {
            return;
        }

        if (scope == BackgroundVideoLiveApplyScope.WindowTransparencyOnly)
        {
            ApplyWindowCaptureTransparency();
            InvalidationRequired = true;
            InvalidateForm();
            return;
        }

        if (backgroundVideoDisabledForSession)
        {
            return;
        }

        if (scope == BackgroundVideoLiveApplyScope.FullLayoutVideo)
        {
            ApplyVideoBackgroundSettingsImmediate();
            return;
        }

        if (ShouldUseWindowCaptureTransparency())
        {
            ApplyWindowCaptureTransparency();
            InvalidationRequired = true;
            InvalidateForm();
            return;
        }

        if (Layout.Settings.BackgroundType != BackgroundType.Video || backgroundVideoPlayer == null)
        {
            return;
        }

        if (scope == BackgroundVideoLiveApplyScope.MpvLoopOnly)
        {
            ApplyMpvLoopOptionNarrow();
            return;
        }

        if (scope == BackgroundVideoLiveApplyScope.TimerStartSyncOnly)
        {
            if (!backgroundVideoPlayer.IsInitialized)
            {
                backgroundVideoPlayer.StartVideoWithTimer = Layout.Settings.VideoStartWithTimer;
                backgroundVideoPlayer.VideoStartOffsetSeconds = Layout.Settings.VideoStartOffsetSeconds;
                lastBackgroundVideoPauseWhenRunCompletes = Layout.Settings.VideoPauseWhenRunCompletes;
                lastBackgroundVideoKeepPlaybackAcrossTimerResets = Layout.Settings.VideoKeepPlaybackAcrossTimerResets;
                lastBackgroundVideoCompletionVolumePercent = Layout.Settings.VideoVolumePercentWhenRunCompletes;
                return;
            }

            ApplyTimerSyncOptionsNarrow();
            return;
        }

        if (scope == BackgroundVideoLiveApplyScope.MpvVolumeOnly)
        {
            ApplyMpvVolumeOptionNarrow();
            return;
        }

        if (scope == BackgroundVideoLiveApplyScope.VideoVisualEffectsOnly)
        {
            ApplyVideoVisualEffectsOptionNarrow();
            return;
        }
    }

    private void ApplyMpvLoopOptionNarrow()
    {
        backgroundVideoPlayer.LoopVideo = Layout.Settings.LoopVideo;
        if (backgroundVideoPlayer.IsInitialized)
        {
            backgroundVideoPlayer.PushLoopFileOptionToMpvNow();
        }

        SyncBackgroundVideoRunTimerDriftSyncTimer();
    }

    private void ApplyMpvVolumeOptionNarrow()
    {
        backgroundVideoPlayer.AudioVolume = GetActiveBackgroundVideoAudioVolume();
        backgroundVideoPlayer.PlayAudio = Layout.Settings.PlayVideoAudio;
        if (!backgroundVideoPlayer.IsInitialized)
        {
            return;
        }

        backgroundVideoPlayer.PushAudioVolumeToMpvNow();
    }

    private void ApplyVideoVisualEffectsOptionNarrow()
    {
        backgroundVideoPlayer.VideoBlurScale = Layout.Settings.VideoBlurScale;
        backgroundVideoPlayer.VideoBlurType = Layout.Settings.VideoBlurType;
        backgroundVideoPlayer.VideoBlurDegrees = Layout.Settings.VideoBlurDegrees;
        backgroundVideoPlayer.SetOpacity(Layout.Settings.VideoOpacity);
        if (backgroundVideoPlayer.IsInitialized)
        {
            backgroundVideoPlayer.PingRuntimeOptionsToMpv();
        }

        InvalidationRequired = true;
        if (ShouldThrottleModalUiWork())
        {
            return;
        }

        InvalidateForm();
    }

    private void ApplyTimerSyncOptionsNarrow()
    {
        bool hadAppliedSyncState = lastBackgroundVideoTimerSyncApplied;
        bool startWithTimerChanged = hadAppliedSyncState
            && lastBackgroundVideoStartWithTimer != Layout.Settings.VideoStartWithTimer;
        bool completionPauseChanged = hadAppliedSyncState
            && lastBackgroundVideoPauseWhenRunCompletes != Layout.Settings.VideoPauseWhenRunCompletes;
        bool completionVolumeChanged = hadAppliedSyncState
            && Math.Abs(lastBackgroundVideoCompletionVolumePercent - Layout.Settings.VideoVolumePercentWhenRunCompletes) > 0.0005f;

        backgroundVideoPlayer.StartVideoWithTimer = Layout.Settings.VideoStartWithTimer;
        backgroundVideoPlayer.VideoStartOffsetSeconds = Layout.Settings.VideoStartOffsetSeconds;
        lastBackgroundVideoStartWithTimer = Layout.Settings.VideoStartWithTimer;
        lastBackgroundVideoPauseWhenRunCompletes = Layout.Settings.VideoPauseWhenRunCompletes;
        lastBackgroundVideoKeepPlaybackAcrossTimerResets = Layout.Settings.VideoKeepPlaybackAcrossTimerResets;
        lastBackgroundVideoCompletionVolumePercent = Layout.Settings.VideoVolumePercentWhenRunCompletes;
        lastBackgroundVideoStartOffsetSeconds = Layout.Settings.VideoStartOffsetSeconds;
        lastBackgroundVideoTimerSyncApplied = true;

        if (backgroundVideoPlayer.IsLoaded && !hadAppliedSyncState)
        {
            ApplyBackgroundVideoTimerPhase();
        }
        else if (backgroundVideoPlayer.IsLoaded)
        {
            // Toggling "start with timer" while a video is already playing should update future
            // phase handling only; seeking belongs to actual timer transitions, not the checkbox.
            if (startWithTimerChanged && !Layout.Settings.VideoStartWithTimer)
            {
                backgroundVideoPlayer.ExitTimerStartSyncToNormalAutoplay();
                ApplyLayoutVideoAudioVolumeToMpv();
            }
            else if ((completionPauseChanged || completionVolumeChanged)
                && Layout.Settings.VideoStartWithTimer
                && CurrentState.CurrentPhase == TimerPhase.Ended)
            {
                if (Layout.Settings.VideoPauseWhenRunCompletes)
                {
                    backgroundVideoPlayer.RunTimerSyncPausePlayback();
                }
                else
                {
                    backgroundVideoPlayer.RunTimerSyncUnpauseAfterRunCompletes();
                }

                ApplyRunCompleteVideoVolumeToMpv();
            }
        }

        SyncBackgroundVideoRunTimerDriftSyncTimer();
    }

    /// <summary>
    /// Runs <see cref="UpdateBackgroundVideoControl"/> outside of the paint path. mpv set commands
    /// take effect immediately; if the file source changed, mpv loadfile-replace will swap files.
    /// </summary>
    private void ApplyVideoBackgroundSettingsImmediate()
    {
        if (Layout?.Settings == null || backgroundVideoDisabledForSession)
        {
            return;
        }

        ApplyWindowCaptureTransparency();
        if (ShouldUseWindowCaptureTransparency())
        {
            InvalidationRequired = true;
            InvalidateForm();
            return;
        }

        if (ShouldThrottleModalUiWork())
        {
            InvalidationRequired = true;
            return;
        }

        UpdateBackgroundVideoControl();

        InvalidationRequired = true;
        InvalidateForm();
    }

    private void editor_LayoutResized(object sender, EventArgs e)
    {
        SetInTimerOnlyMode();
        if (Layout.Mode == LayoutMode.Horizontal)
        {
            Layout.VerticalWidth = UI.Layout.InvalidSize;
            Layout.VerticalHeight = UI.Layout.InvalidSize;
        }
        else
        {
            Layout.HorizontalWidth = UI.Layout.InvalidSize;
            Layout.HorizontalHeight = UI.Layout.InvalidSize;
        }
    }

    private void editor_OrientationSwitched(object sender, EventArgs e)
    {
        ComponentRenderer.CalculateOverallSize(Layout.Mode);
        Size clientSize = ClientSize;
        if (Layout.Mode == LayoutMode.Vertical)
        {
            Layout.HorizontalWidth = clientSize.Width;
            Layout.HorizontalHeight = clientSize.Height;
            if (Layout.VerticalHeight == UI.Layout.InvalidSize || Layout.VerticalWidth == UI.Layout.InvalidSize)
            {
                Layout.VerticalWidth = 300;
                Layout.VerticalHeight = (int)(ComponentRenderer.OverallSize + 0.5);
            }
        }
        else
        {
            Layout.VerticalWidth = clientSize.Width;
            Layout.VerticalHeight = clientSize.Height;
            if (Layout.HorizontalWidth == UI.Layout.InvalidSize || Layout.HorizontalHeight == UI.Layout.InvalidSize)
            {
                Layout.HorizontalWidth = (int)(ComponentRenderer.OverallSize + 0.5);
                Layout.HorizontalHeight = 45;
            }
        }

        TopMost = false;
        SetLayout(Layout);
    }

    private bool SaveLayoutAs()
    {
        using var layoutDialog = new SaveFileDialog();
        layoutDialog.Filter = "LiveSplit Layout (*.lsl)|*.lsl|All Files (*.*)|*.*";
        IsInDialogMode = true;
        try
        {
            DialogResult result = layoutDialog.ShowDialog(this);
            if (result == DialogResult.OK)
            {
                if (!layoutDialog.FileName.EndsWith(".lsl"))
                {
                    MessageBox.Show(this, T("Cannot save layout with a file type that is not .lsl"), T("Save Failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }

                Layout.FilePath = layoutDialog.FileName;
                return SaveLayout();
            }

            return false;
        }
        finally
        {
            IsInDialogMode = false;
            SyncVideoBackgroundAfterInteractionModeChange();
        }
    }

    private void OpenAboutBox()
    {
        using var aboutBox = new AboutBox();
        try
        {
            TopMost = false;
            UiLocalizer.Apply(aboutBox, CurrentLanguage);
            aboutBox.ShowDialog(this);
        }
        finally
        {
            TopMost = Layout.Settings.AlwaysOnTop;
        }
    }

    private void OpenLayout()
    {
        using var layoutDialog = new OpenFileDialog();
        layoutDialog.Filter = "LiveSplit Layout (*.lsl)|*.lsl|All Files (*.*)|*.*";
        IsInDialogMode = true;
        try
        {
            if (Settings.RecentLayouts.Any() && !string.IsNullOrEmpty(Settings.RecentLayouts.Last()))
            {
                layoutDialog.InitialDirectory = Path.GetDirectoryName(Settings.RecentLayouts.Last());
            }

            DialogResult result = layoutDialog.ShowDialog(this);
            if (result == DialogResult.OK)
            {
                OpenLayoutFromFile(layoutDialog.FileName);
            }
        }
        finally
        {
            IsInDialogMode = false;
            SyncVideoBackgroundAfterInteractionModeChange();
        }
    }

    public bool OpenLayoutFromFile(string filePath)
    {
        bool success = false;
        if (WarnUserAboutLayoutSave(true))
        {
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                ILayout layout = LoadLayoutFromFile(filePath);
                SetLayout(layout);
                success = true;
            }
            catch (Exception e)
            {
                Log.Error(e);
                DontRedraw = true;
                MessageBox.Show(this, T("The selected file was not recognized as a layout file. (") + e.Message + ")", T("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                DontRedraw = false;
            }

            Cursor.Current = Cursors.Arrow;
        }

        return success;
    }

    public void LoadDefaultLayout()
    {
        if (WarnUserAboutLayoutSave(true))
        {
            ILayout layout = new StandardLayoutFactory().Create(CurrentState);
            layout.X = Location.X;
            layout.Y = Location.Y;
            SetLayout(layout);
            Settings.AddToRecentLayouts("");
        }
    }

    private void SetLayout(ILayout layout)
    {
        if (Layout != null && Layout != layout)
        {
            if (Layout.Settings.BackgroundImage != null && Layout.Settings.BackgroundImage != layout.Settings.BackgroundImage)
            {
                Layout.Settings.BackgroundImage.Dispose();
            }

            foreach (UI.Components.IComponent component in Layout.Components.Except(layout.Components))
            {
                component.Dispose();
            }

            foreach (IDeactivatableComponent component in layout.Components.Except(Layout.Components).OfType<IDeactivatableComponent>())
            {
                component.Activated = true;
            }
        }

        Layout = layout;
        ComponentRenderer.VisibleComponents = Layout.Components;
        CurrentState.LayoutSettings = layout.Settings;
        UpdateRefreshesRemaining();
        MinimumSize = new Size(0, 0);
        ApplyLayoutClientSize(Layout);

        int x = Math.Max(SystemInformation.VirtualScreen.X, Math.Min(Layout.X, SystemInformation.VirtualScreen.X + SystemInformation.VirtualScreen.Width - Width));
        int y = Math.Max(SystemInformation.VirtualScreen.Y, Math.Min(Layout.Y, SystemInformation.VirtualScreen.Y + SystemInformation.VirtualScreen.Height - Height));
        Location = new Point(x, y);
        TopMost = Layout.Settings.AlwaysOnTop;
        SetInTimerOnlyMode();
        ApplyWindowCaptureTransparency();
    }

    private void CloseSplits()
    {
        bool needToChangeLayout = Layout.Components.Count() != 1 || Layout.Components.FirstOrDefault().ComponentName != "Timer";

        if (!WarnUserAboutSplitsSave())
        {
            return;
        }

        if (needToChangeLayout && !WarnUserAboutLayoutSave(true))
        {
            return;
        }

        AddCurrentSplitsToLRU(CurrentState.CurrentTimingMethod, CurrentState.CurrentHotkeyProfile);

        IRun run = new StandardRunFactory().Create(ComparisonGeneratorsFactory);
        Model.Reset();
        SetRun(run);
        Settings.AddToRecentSplits("", null, TimingMethod.RealTime, CurrentState.CurrentHotkeyProfile);
        InTimerOnlyMode = true;

        if (needToChangeLayout)
        {
            ILayout layout = new TimerOnlyLayoutFactory().Create(CurrentState);
            layout.Settings = Layout.Settings;
            layout.X = Location.X;
            layout.Y = Location.Y;
            layout.Mode = Layout.Mode;
            SetLayout(layout);
            Settings.AddToRecentLayouts("");
        }
    }

    private void saveAsMenuItem_Click(object sender, EventArgs e)
    {
        SaveSplitsAs(true);
    }

    private void saveSplitsMenuItem_Click(object sender, EventArgs e)
    {
        SaveSplits(true);
    }

    private void editSplitsMenuItem_Click(object sender, EventArgs e)
    {
        EditSplits();
    }

    private void editLayoutMenuItem_Click(object sender, EventArgs e)
    {
        EditLayout();
    }

    private void aboutMenuItem_Click(object sender, EventArgs e)
    {
        OpenAboutBox();
    }

    private void openSplitsFromFileMenuItem_Click(object sender, EventArgs e)
    {
        OpenSplits();
    }

    private void openLayoutFromFileMenuItem_Click(object sender, EventArgs e)
    {
        OpenLayout();
    }

    private void resetLayoutMenuItem_Click(object sender, EventArgs e)
    {
        LoadDefaultLayout();
    }

    private void saveLayoutAsMenuItem_Click(object sender, EventArgs e)
    {
        SaveLayoutAs();
    }

    private void saveLayoutMenuItem_Click(object sender, EventArgs e)
    {
        SaveLayout();
    }

    private bool WarnUserAboutSplitsSave()
    {
        if (InTimerOnlyMode)
        {
            Model.Reset();
            return true;
        }

        bool safeToContinue = true;
        if (CurrentState.Run.HasChanged)
        {
            try
            {
                DontRedraw = true;
                DialogResult result = MessageBox.Show(this, T("Your splits have been updated but not yet saved.\nDo you want to save your splits now?"), T("Save Splits?"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (result == DialogResult.Yes)
                {
                    safeToContinue = SaveSplits(false);
                }
                else if (result == DialogResult.Cancel)
                {
                    return false;
                }
            }
            finally
            {
                DontRedraw = false;
            }
        }

        if (safeToContinue)
        {
            Model.Reset();
        }

        return safeToContinue;
    }

    private bool WarnUserAboutLayoutSave(bool canCancel)
    {
        bool safeToContinue = true;
        if (Layout.HasChanged)
        {
            try
            {
                DontRedraw = true;
                MessageBoxButtons buttons = canCancel ? MessageBoxButtons.YesNoCancel : MessageBoxButtons.YesNo;
                DialogResult result = MessageBox.Show(this, T("Your layout has been updated but not yet saved.\nDo you want to save your layout now?"), T("Save Layout?"), buttons, MessageBoxIcon.Question);
                if (result == DialogResult.Yes)
                {
                    safeToContinue = SaveLayout();
                }
                else if (result == DialogResult.Cancel)
                {
                    return false;
                }
            }
            finally
            {
                DontRedraw = false;
            }
        }

        return safeToContinue;
    }

    private void TimerForm_FormClosing(object sender, FormClosingEventArgs e)
    {
        string shutdownLogPath = Path.Combine(BasePath ?? AppDomain.CurrentDomain.BaseDirectory, "shutdown-debug.log");
        File.AppendAllText(shutdownLogPath, $"[{DateTime.Now:O}] Shutdown start{Environment.NewLine}");

        suppressCompositorRestoreWhileFormClosingModals = true;

        void RestoreAfterClosingDialogCancelled()
        {
            suppressCompositorRestoreWhileFormClosingModals = false;
            UpdateBackgroundVideoControl();
        }

        if (!WarnUserAboutSplitsSave())
        {
            File.AppendAllText(shutdownLogPath, $"[{DateTime.Now:O}] Cancelled: WarnUserAboutSplitsSave{Environment.NewLine}");
            RestoreAfterClosingDialogCancelled();
            e.Cancel = true;
            return;
        }

        if (!WarnUserAboutLayoutSave(true))
        {
            File.AppendAllText(shutdownLogPath, $"[{DateTime.Now:O}] Cancelled: WarnUserAboutLayoutSave{Environment.NewLine}");
            RestoreAfterClosingDialogCancelled();
            e.Cancel = true;
            return;
        }

        Settings.LastComparison = CurrentState.CurrentComparison;
        AddCurrentSplitsToLRU(CurrentState.CurrentTimingMethod, CurrentState.CurrentHotkeyProfile);
        SaveSettingsToDisk();
        DisposeWindowCaptureTransparencyPresentTimer();
        DisposeWindowCaptureTransparencySurface();
        windowCaptureTransparencyComponentInvalidator.Dispose();
        DisposeVideoUiPresentTimer();
        DisposeComponentOverlayCache();
        DisposeVideoDebugOverlayCache();

        foreach (UI.Components.IComponent component in Layout.Components)
        {
            RunShutdownStep($"Dispose component: {component.ComponentName}", () => component.Dispose(), shutdownLogPath, 1500);
        }

        RunShutdownStep("TearDownBackgroundVideoControl", TearDownBackgroundVideoControl, shutdownLogPath, 1000);
        RunShutdownStep("DeactivateAutoSplitter", DeactivateAutoSplitter, shutdownLogPath, 2000);
        RunShutdownStep("Server.StopAll", Server.StopAll, shutdownLogPath, 2000);
        File.AppendAllText(shutdownLogPath, $"[{DateTime.Now:O}] Shutdown end{Environment.NewLine}");
    }

    private static void RunShutdownStep(string name, Action action, string logPath, int timeoutMs)
    {
        try
        {
            var task = Task.Run(action);
            if (!task.Wait(timeoutMs))
            {
                File.AppendAllText(logPath, $"[{DateTime.Now:O}] TIMEOUT: {name}{Environment.NewLine}");
            }
            else
            {
                File.AppendAllText(logPath, $"[{DateTime.Now:O}] OK: {name}{Environment.NewLine}");
            }
        }
        catch (Exception ex)
        {
            File.AppendAllText(logPath, $"[{DateTime.Now:O}] ERROR: {name} -> {ex.Message}{Environment.NewLine}");
        }
    }

    private bool SaveSettingsToDisk()
    {
        try
        {
            string settingsPath = Path.Combine(BasePath, SETTINGS_PATH);
            if (!File.Exists(settingsPath))
            {
                File.Create(settingsPath).Close();
            }

            using var memoryStream = new MemoryStream();
            SettingsSaver.Save(Settings, memoryStream);

            using FileStream stream = File.Open(settingsPath, FileMode.Create, FileAccess.Write);
            byte[] buffer = memoryStream.GetBuffer();
            stream.Write(buffer, 0, (int)memoryStream.Length);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, T("Settings could not be saved!"), T("Save Failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            Log.Error(ex);
            return false;
        }
    }

    private void settingsMenuItem_Click(object sender, EventArgs e)
    {
        using var editor = new SettingsDialog(Hook, Settings, CurrentState.CurrentHotkeyProfile);
        editor.SumOfBestModeChanged += editor_SumOfBestModeChanged;
        try
        {
            TopMost = false;
            IsInDialogMode = true;
            var oldSettings = (ISettings)Settings.Clone();
            Settings.UnregisterAllHotkeys(Hook);
            UiLocalizer.Apply(editor, CurrentLanguage);
            DialogResult result = editor.ShowDialog(this);
            if (result == DialogResult.Cancel)
            {
                bool regenerate = Settings.SimpleSumOfBest != oldSettings.SimpleSumOfBest;
                CurrentState.Settings = Settings = oldSettings;
                ApplyAppUiSettings();
                if (regenerate)
                {
                    RegenerateComparisons();
                }
            }
            else
            {
                ApplyAppUiSettings();
                SwitchComparisonGenerators();
                CurrentState.CurrentHotkeyProfile = editor.SelectedHotkeyProfile;
            }

            Settings.RegisterHotkeys(Hook, CurrentState.CurrentHotkeyProfile);
            UpdateRaceProviderIntegration();
            SyncVideoBackgroundPoolTimer();
            SyncBackgroundVideoRunTimerDriftSyncTimer();
        }
        finally
        {
            editor.SumOfBestModeChanged -= editor_SumOfBestModeChanged;
            SetProgressBar();
            TopMost = Layout.Settings.AlwaysOnTop;
            IsInDialogMode = false;
            SyncVideoBackgroundAfterInteractionModeChange();
        }
    }

    private void editor_SumOfBestModeChanged(object sender, EventArgs e)
    {
        RegenerateComparisons();
    }

    private void LoadSettings()
    {
        try
        {
            string settingsPath = Path.Combine(BasePath, SETTINGS_PATH);
            if (File.Exists(settingsPath))
            {
                using FileStream stream = File.OpenRead(Path.Combine(BasePath, SETTINGS_PATH));
                Settings = new XMLSettingsFactory(stream).Create();
                LanguageResolver.SetCurrentLanguageSetting(Settings.UILanguage);
                ApplyAppUiSettings();
                return;
            }
        }
        catch (Exception e)
        {
            Log.Error(e);
        }

        Settings = new StandardSettingsFactory().Create();
        LanguageResolver.SetCurrentLanguageSetting(Settings.UILanguage);
        ApplyAppUiSettings();
    }

    private void ApplyAppUiSettings()
    {
        WinFormsTheme.CurrentTheme = Settings.AppTheme;
        WinFormsTheme.AllowDialogPanelResizing = true;
        WinFormsTheme.Apply(RightClickMenu);
    }

    private void SetDPIAwareness()
    {
        if (Environment.OSVersion.Version.Major >= LiveSplit.Options.Settings.DPI_AWARENESS_OS_MIN_VERSION && Settings.EnableDPIAwareness)
        {
            try
            {
                SetProcessDPIAware();
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }
    }

    private void closeSplitsMenuItem_Click(object sender, EventArgs e)
    {
        CloseSplits();
    }

    private void MaintainMinimumSize()
    {
        Size clientSize = ClientSize;
        if (Layout.Mode == LayoutMode.Vertical)
        {
            float minimumWidth = ComponentRenderer.MinimumWidth * (clientSize.Height / ComponentRenderer.OverallSize);
            if (clientSize.Width < minimumWidth)
            {
                ApplyInternalClientSize(new Size(clientSize.Width, (int)((clientSize.Height / (minimumWidth / clientSize.Width)) + 0.5f)));
            }
        }
        else
        {
            float minimumHeight = ComponentRenderer.MinimumHeight * (clientSize.Width / ComponentRenderer.OverallSize);
            if (clientSize.Height < minimumHeight)
            {
                ApplyInternalClientSize(new Size((int)((clientSize.Width / (minimumHeight / clientSize.Height)) + 0.5f), clientSize.Height));
            }
        }
    }

    private Image MakeScreenShot()
    {
        var image = new Bitmap(Width, Height);
        var graphics = Graphics.FromImage(image);

        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.FillRectangle(Brushes.Transparent, 0, 0, Width, Height);
        graphics.CompositingMode = CompositingMode.SourceOver;

        if (!ShouldUseWindowCaptureTransparency())
        {
            // Start with a black background because normal screenshots do not capture a transparent window surface.
            graphics.FillRectangle(new SolidBrush(Color.Black), 0, 0, Width, Height);
        }

        var drawRegion = new Region(new Rectangle(0, 0, Width, Height));
        UpdateRegion = drawRegion;
        try
        {
            isRenderingScreenshot = true;
            PaintForm(graphics, drawRegion);
        }
        finally
        {
            isRenderingScreenshot = false;
            ReleaseMpvReadbackHostInvalidateGateAfterPaintForm();
        }

        return image;
    }

    private void shareMenuItem_Click(object sender, EventArgs e)
    {
        using var dialog = new ShareRunDialog(
                (LiveSplitState)CurrentState.Clone(),
                Settings,
                MakeScreenShot);
        try
        {
            TopMost = false;
            IsInDialogMode = true;
            Settings.UnregisterAllHotkeys(Hook);
            UiLocalizer.Apply(dialog, CurrentLanguage);
            dialog.ShowDialog(this);
            Settings.RegisterHotkeys(Hook, CurrentState.CurrentHotkeyProfile);
        }
        finally
        {
            TopMost = Layout.Settings.AlwaysOnTop;
            IsInDialogMode = false;
            SyncVideoBackgroundAfterInteractionModeChange();
        }
    }

    private DialogResult WarnAboutResetting()
    {
        bool warnUser = false;
        for (int index = 0; index < CurrentState.Run.Count; index++)
        {
            if (LiveSplitStateHelper.CheckBestSegment(CurrentState, index, CurrentState.CurrentTimingMethod))
            {
                warnUser = true;
                break;
            }
        }

        if ((!warnUser && CurrentState.Run.Last().SplitTime[CurrentState.CurrentTimingMethod] != null && CurrentState.Run.Last().PersonalBestSplitTime[CurrentState.CurrentTimingMethod] == null) || CurrentState.Run.Last().SplitTime[CurrentState.CurrentTimingMethod] < CurrentState.Run.Last().PersonalBestSplitTime[CurrentState.CurrentTimingMethod])
        {
            warnUser = true;
        }

        if (warnUser)
        {
            DontRedraw = true;
            DialogResult result = MessageBox.Show(this, T("You have beaten some of your best times.\r\nDo you want to update them?"), T("Update Times?"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            DontRedraw = false;
            return result;
        }

        return DialogResult.Yes;
    }

    private void Reset()
    {
        if (InvokeRequired)
        {
            Invoke(new Action(Reset));
            return;
        }

        if (!ResetMessageShown)
        {
            DialogResult result = DialogResult.Yes;
            if (Settings.WarnOnReset && (!InTimerOnlyMode))
            {
                ResetMessageShown = true;
                result = WarnAboutResetting();
            }

            if (result == DialogResult.Yes)
            {
                Model.Reset();
            }
            else if (result == DialogResult.No)
            {
                Model.Reset(false);
            }

            ResetMessageShown = false;
        }
    }

    private void racingMenuItem_MouseHover(object sender, EventArgs e)
    {
        var raceProvider = (RaceProviderAPI)(sender as ToolStripMenuItem)?.Tag;
        raceProvider?.RefreshRacesListAsync();
        ShouldRefreshRaces = true;
    }

    private void racingMenuItem_MouseLeave(object sender, EventArgs e)
    {
        ShouldRefreshRaces = false;
    }

    private void resetMenuItem_Click(object sender, EventArgs e)
    {
        Reset();
    }

    private void pauseMenuItem_Click(object sender, EventArgs e)
    {
        Model.Pause();
    }

    private void undoPausesMenuItem_Click(object sender, EventArgs e)
    {
        Model.UndoAllPauses();
    }

    private void hotkeysMenuItem_Click(object sender, EventArgs e)
    {
        HotkeyProfile hotkeyProfile = Settings.HotkeyProfiles[CurrentState.CurrentHotkeyProfile];

        if (hotkeysMenuItem.Checked)
        {
            hotkeysMenuItem.Checked = hotkeyProfile.GlobalHotkeysEnabled = false;
        }
        else
        {
            hotkeysMenuItem.Checked = hotkeyProfile.GlobalHotkeysEnabled = true;
        }

        SetProgressBar();
    }

    private void splitMenuItem_Click(object sender, EventArgs e)
    {
        StartOrSplit();
    }

    private void undoSplitMenuItem_Click(object sender, EventArgs e)
    {
        Model.UndoSplit();
    }

    private void skipSplitMenuItem_Click(object sender, EventArgs e)
    {
        Model.SkipSplit();
    }

    private void SetProgressBar()
    {
        try
        {
            HotkeyProfile hotkeyProfile = Settings.HotkeyProfiles[CurrentState.CurrentHotkeyProfile];
            if (hotkeyProfile.ToggleGlobalHotkeys != null)
            {
                TaskbarManager.Instance.SetProgressState(hotkeyProfile.GlobalHotkeysEnabled ? TaskbarProgressBarState.Normal : TaskbarProgressBarState.Error);
                TaskbarManager.Instance.SetProgressValue(100, 100);
            }
            else
            {
                TaskbarManager.Instance.SetProgressState(TaskbarProgressBarState.NoProgress);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    private void TimerForm_Shown(object sender, EventArgs e)
    {
        SetProgressBar();
        UpdateRaceProviderIntegration();
    }

    private void RebuildComparisonsMenu()
    {
        comparisonMenuItem.DropDownItems.Clear();

        foreach (string customComparison in CurrentState.Run.CustomComparisons)
        {
            AddActionToComparisonsMenu(customComparison);
        }

        if (CurrentState.Run.ComparisonGenerators.Count > 0)
        {
            comparisonMenuItem.DropDownItems.Add(new ToolStripSeparator());
        }

        bool raceSeparatorAdded = false;
        foreach (IComparisonGenerator generator in CurrentState.Run.ComparisonGenerators)
        {
            if (!raceSeparatorAdded && generator is SRLComparisonGenerator)
            {
                comparisonMenuItem.DropDownItems.Add(new ToolStripSeparator());
                raceSeparatorAdded = true;
            }

            AddActionToComparisonsMenu(generator.Name);
        }

        comparisonMenuItem.DropDownItems.Add(new ToolStripSeparator());

        var realTimeMenuItem = new ToolStripMenuItem("Real Time");
        realTimeMenuItem.Click += realTimeMenuItem_Click;
        realTimeMenuItem.Name = "RealTime";
        comparisonMenuItem.DropDownItems.Add(realTimeMenuItem);

        var gameTimeMenuItem = new ToolStripMenuItem("Game Time");
        gameTimeMenuItem.Click += gameTimeMenuItem_Click;
        gameTimeMenuItem.Name = "GameTime";
        comparisonMenuItem.DropDownItems.Add(gameTimeMenuItem);

        RefreshComparisonItems();
    }

    private void gameTimeMenuItem_Click(object sender, EventArgs e)
    {
        CurrentState.CurrentTimingMethod = TimingMethod.GameTime;
        RefreshComparisonItems();
    }

    private void realTimeMenuItem_Click(object sender, EventArgs e)
    {
        CurrentState.CurrentTimingMethod = TimingMethod.RealTime;
        RefreshComparisonItems();
    }

    private void RegenerateComparisons()
    {
        if (CurrentState != null && CurrentState.Run != null)
        {
            foreach (IComparisonGenerator generator in CurrentState.Run.ComparisonGenerators)
            {
                generator.Generate(CurrentState.Settings);
            }
        }
    }

    private void SwitchComparisonGenerators()
    {
        IEnumerable<IComparisonGenerator> allGenerators = new StandardComparisonGeneratorsFactory().GetAllGenerators(CurrentState.Run);
        foreach (IComparisonGenerator generator in allGenerators)
        {
            IComparisonGenerator generatorInRun = CurrentState.Run.ComparisonGenerators.FirstOrDefault(x => x.Name == generator.Name);
            if (generatorInRun != null)
            {
                CurrentState.Run.ComparisonGenerators.Remove(generatorInRun);
            }

            if (Settings.ComparisonGeneratorStates[generator.Name])
            {
                CurrentState.Run.ComparisonGenerators.Add(generator);
            }
        }

        SwitchComparison(CurrentState.CurrentComparison);
        RegenerateComparisons();
    }

    private void SwitchComparison(string name)
    {
        if (!CurrentState.Run.Comparisons.Contains(name))
        {
            name = Run.PersonalBestComparisonName;
        }

        CurrentState.CurrentComparison = name;
    }

    private void AddActionToComparisonsMenu(string name)
    {
        var menuItem = new ToolStripMenuItem(name.EscapeMenuItemText());
        menuItem.Click += (s, e) => SwitchComparison(name);
        comparisonMenuItem.DropDownItems.Add(menuItem);
    }

    private void AddActionToControlMenu(string name, Action action)
    {
        var menuItem = new ToolStripMenuItem(name.EscapeMenuItemText());
        menuItem.Click += (s, e) => action();
        controlMenuItem.DropDownItems.Add(menuItem);
    }

    private void RebuildControlMenu()
    {
        controlMenuItem.DropDownItems.Clear();
        controlMenuItem.DropDownItems.AddRange([
        splitMenuItem,
        resetMenuItem,
        undoSplitMenuItem,
        skipSplitMenuItem,
        pauseMenuItem,
        undoPausesMenuItem]);

        controlMenuItem.DropDownItems.Add(new ToolStripSeparator());
        controlMenuItem.DropDownItems.Add(hotkeysMenuItem);

        hotkeysMenuItem.Checked = Settings.HotkeyProfiles[CurrentState.CurrentHotkeyProfile].GlobalHotkeysEnabled;

        controlMenuItem.DropDownItems.Add(new ToolStripSeparator());
        controlMenuItem.DropDownItems.Add(tcpServerMenuItem);
        controlMenuItem.DropDownItems.Add(webSocketMenuItem);

        IEnumerable<UI.Components.IComponent> components = Layout.Components;
        if (CurrentState.Run.IsAutoSplitterActive())
        {
            components = components.Concat(new[] { CurrentState.Run.AutoSplitter.Component });
        }

        IEnumerable<IDictionary<string, Action>> componentControls =
            components
            .Select(x => x.ContextMenuControls)
            .Where(x => x != null && x.Any());

        foreach (IDictionary<string, Action> componentControlSection in componentControls)
        {
            controlMenuItem.DropDownItems.Add(new ToolStripSeparator());

            foreach (KeyValuePair<string, Action> componentControl in componentControlSection)
            {
                AddActionToControlMenu(componentControl.Key, componentControl.Value);
            }
        }
    }

    private void RightClickMenu_Opening(object sender, CancelEventArgs e)
    {
        RebuildControlMenu();
        RebuildComparisonsMenu();
        UpdateLanguageMenuChecks();
        UiLocalizer.Apply(this, CurrentLanguage);
        WinFormsTheme.Apply(RightClickMenu);
    }

    private void TimerForm_ResizeBegin(object sender, EventArgs e)
    {
        resizeStartSize = Size;
        if (Size.Height > 0)
        {
            ResizingInitialAspectRatio = (float)Size.Width / Size.Height;
        }
    }

    private void TimerForm_ResizeEnd(object sender, EventArgs e)
    {
        if (AllowResizing && Size != resizeStartSize)
        {
            Layout.HasChanged = true;
        }
        ResizingInitialAspectRatio = null;
    }

    private void TimerForm_DragDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop, false) is string[] fileList)
        {
            bool lssOpened = false;
            bool lslOpened = false;

            foreach (string fileToOpen in fileList)
            {
                if (File.Exists(fileToOpen))
                {
                    string extension = Path.GetExtension(fileToOpen).ToLower();

                    if (!lslOpened && extension == ".lsl")
                    {
                        lslOpened = true;
                        OpenLayoutFromFile(fileToOpen);
                    }
                    else if (!lssOpened)
                    {
                        lssOpened = true;
                        OpenRunFromFile(fileToOpen);
                    }
                }
            }
        }
    }

    private void TimerForm_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effect = DragDropEffects.Copy;
        }
        else
        {
            e.Effect = DragDropEffects.None;
        }
    }

    private sealed class PassiveInvalidator : IInvalidator, IDisposable
    {
        private Matrix transform = new();

        public Matrix Transform
        {
            get => transform;
            set
            {
                if (ReferenceEquals(transform, value))
                {
                    return;
                }

                transform?.Dispose();
                transform = value ?? new Matrix();
            }
        }

        public void Restart()
        {
            Transform = new Matrix();
        }

        public void Invalidate(float x, float y, float width, float height)
        {
        }

        public void Dispose()
        {
            transform?.Dispose();
            transform = null;
        }
    }

    private sealed class WindowCaptureTransparencySurface : IDisposable
    {
        private IntPtr hBitmap;
        private IntPtr oldBitmap;

        private WindowCaptureTransparencySurface(int width, int height, Bitmap bitmap, IntPtr memoryDc, IntPtr hBitmap, IntPtr oldBitmap)
        {
            Width = width;
            Height = height;
            Bitmap = bitmap;
            MemoryDc = memoryDc;
            this.hBitmap = hBitmap;
            this.oldBitmap = oldBitmap;
        }

        public int Width { get; }
        public int Height { get; }
        public Bitmap Bitmap { get; private set; }
        public IntPtr MemoryDc { get; private set; }

        public static WindowCaptureTransparencySurface Create(int width, int height)
        {
            IntPtr screenDc = IntPtr.Zero;
            IntPtr memoryDc = IntPtr.Zero;
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;
            Bitmap bitmap = null;

            try
            {
                int stride = checked(width * 4);
                var bitmapInfo = new WindowCaptureBitmapInfo
                {
                    bmiHeader = new WindowCaptureBitmapInfoHeader
                    {
                        biSize = (uint)Marshal.SizeOf(typeof(WindowCaptureBitmapInfoHeader)),
                        biWidth = width,
                        biHeight = -height,
                        biPlanes = 1,
                        biBitCount = 32,
                        biCompression = WindowCaptureBiRgb,
                        biSizeImage = (uint)checked(stride * height)
                    }
                };

                screenDc = GetDC(IntPtr.Zero);
                if (screenDc == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "GetDC failed for transparent capture surface.");
                }

                memoryDc = CreateCompatibleDC(screenDc);
                if (memoryDc == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateCompatibleDC failed for transparent capture surface.");
                }

                hBitmap = CreateDIBSection(memoryDc, ref bitmapInfo, WindowCaptureDibRgbColors, out IntPtr bits, IntPtr.Zero, 0);
                if (hBitmap == IntPtr.Zero || bits == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateDIBSection failed for transparent capture surface.");
                }

                oldBitmap = SelectObject(memoryDc, hBitmap);
                bitmap = new Bitmap(width, height, stride, System.Drawing.Imaging.PixelFormat.Format32bppPArgb, bits);
                return new WindowCaptureTransparencySurface(width, height, bitmap, memoryDc, hBitmap, oldBitmap);
            }
            catch
            {
                bitmap?.Dispose();
                if (oldBitmap != IntPtr.Zero && memoryDc != IntPtr.Zero)
                {
                    SelectObject(memoryDc, oldBitmap);
                }

                if (hBitmap != IntPtr.Zero)
                {
                    DeleteObject(hBitmap);
                }

                if (memoryDc != IntPtr.Zero)
                {
                    DeleteDC(memoryDc);
                }

                throw;
            }
            finally
            {
                if (screenDc != IntPtr.Zero)
                {
                    ReleaseDC(IntPtr.Zero, screenDc);
                }
            }
        }

        public void Dispose()
        {
            Bitmap?.Dispose();
            Bitmap = null;

            if (oldBitmap != IntPtr.Zero && MemoryDc != IntPtr.Zero)
            {
                SelectObject(MemoryDc, oldBitmap);
                oldBitmap = IntPtr.Zero;
            }

            if (hBitmap != IntPtr.Zero)
            {
                DeleteObject(hBitmap);
                hBitmap = IntPtr.Zero;
            }

            if (MemoryDc != IntPtr.Zero)
            {
                DeleteDC(MemoryDc);
                MemoryDc = IntPtr.Zero;
            }
        }
    }

    private sealed class TrackingInvalidator : IInvalidator
    {
        private readonly Invalidator inner;
        private readonly Action<Rectangle> invalidateOverlay;
        private readonly Func<bool> suppressFormInvalidate;
        private const double Offset = 0.535;

        public TrackingInvalidator(
            Invalidator inner,
            Action<Rectangle> invalidateOverlay,
            Func<bool> suppressFormInvalidate)
        {
            this.inner = inner;
            this.invalidateOverlay = invalidateOverlay;
            this.suppressFormInvalidate = suppressFormInvalidate;
        }

        public Matrix Transform
        {
            get => inner.Transform;
            set => inner.Transform = value;
        }

        public bool HasInvalidated { get; private set; }

        public void Restart()
        {
            HasInvalidated = false;
            inner.Restart();
        }

        public void Invalidate(float x, float y, float width, float height)
        {
            HasInvalidated = true;
            Rectangle bounds = GetScreenBounds(x, y, width, height);
            invalidateOverlay?.Invoke(bounds);
            if (suppressFormInvalidate == null || !suppressFormInvalidate())
            {
                inner.Invalidate(x, y, width, height);
            }
        }

        private Rectangle GetScreenBounds(float x, float y, float width, float height)
        {
            PointF[] points =
            [
                new PointF(x, y),
                new PointF(x + width, y + height)
            ];
            Transform.TransformPoints(points);
            double offsetX = points[0].X - Offset;
            double offsetY = points[0].Y - Offset;
            return new Rectangle(
                (int)Math.Ceiling(offsetX),
                (int)Math.Ceiling(offsetY),
                (int)Math.Ceiling(points[1].X - offsetX - Offset),
                (int)Math.Ceiling(points[1].Y - offsetY - Offset));
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();
}

internal sealed class LibMpvBackgroundPlayer : IBackgroundVideoPlayer
{
    private static readonly bool EnableMpvRenderApiBackend = true;
    /// <summary>
    /// Mpv <c>hwdec</c> when hardware decode is enabled. <c>auto-copy</c> prefers copy-back decoders
    /// (on Windows typically <c>d3d11va-copy</c>), which usually cooperate better with <c>vo=libmpv</c>,
    /// video filters, and CPU readback than zero-copy <c>d3d11va</c>. Suitable for AMD and NVIDIA on Windows.
    /// </summary>
    private const string MpvHwdecWhenEnabled = "auto-copy";
    /// <summary>
    /// Each frame does <c>glReadPixels</c> into a <see cref="Bitmap"/>; cost scales with pixel count.
    /// The final UI blit is now cheap enough that readback resolution should favor image quality.
    /// </summary>
    private const int MpvReadbackMaxPixelsHighFps = 720_000;
    private const int MpvReadbackMaxPixelsBalanced = 900_000;
    private const int MpvReadbackMaxPixelsQuality = 1_200_000;
    private const int MpvVideoFilterLiveApplyMinMs = 100;
    /// <summary>Bounds <see cref="dynamicReadbackScale"/> after adaptive nudges (local strong-PC default).</summary>
    private const float MpvReadbackDynamicScaleMin = 0.85f;
    private const float MpvReadbackDynamicScaleMax = 1.00f;
    private readonly Form hostForm;
    private readonly bool useDedicatedScheduler;
    private OpenTK.NativeWindow mpvNativeWindow;
    private IGraphicsContext mpvGraphicsContext;
    private Thread mpvWorkerThread;
    private int mpvWorkerThreadId;
    private volatile bool mpvWorkerLoopRunning;
    private readonly AutoResetEvent mpvWorkerWakeEvent = new AutoResetEvent(false);
    private readonly ConcurrentQueue<Action> mpvWorkerActions = new ConcurrentQueue<Action>();
    private readonly ManualResetEventSlim mpvWorkerGlReady = new ManualResetEventSlim(false);
    private readonly object mpvWorkerStartLock = new object();
    private System.Windows.Forms.Timer mpvPresentTimer;
    private volatile bool dedicatedPresentEnabled;
    private int cachedHostClientW;
    private int cachedHostClientH;
    private readonly object mpvFrameBitmapSync = new object();
    private IntPtr mpvHandle;
    private IntPtr mpvRenderContext;
    /// <summary>Reused native memory for <see cref="TryUpdateMpvVideoTexture"/> (avoids per-frame AllocHGlobal).</summary>
    private IntPtr mpvRenderScratchNative;
    private int mpvRenderScratchParamSize;
    private int mpvRenderScratchFlipOffset;
    private int mpvRenderScratchBlockOffset;
    private int mpvRenderScratchParamsOffset;
    private Bitmap mpvFrameBitmapFront;
    private Bitmap mpvFrameBitmapBack;
    private GCHandle mpvApiTypeHandle;
    private bool mpvApiTypeHandleAllocated;
    private bool appliedMpvRuntimeOptions;
    private bool lastAppliedLoopVideo;
    private bool lastAppliedPlayAudio;
    private int lastAppliedVolume = -1;
    private int lastAppliedScaleHeight = -1;
    private string lastAppliedVideoFilter = null;
    private long nextVideoFilterApplyTicks;
    private string loadedSource;
    private float renderOpacity = 1f;
    private string lastFrameBlitPath = "none";
    private float videoBlurScale;
    private float lastAppliedVideoBlurScale = float.NaN;
    private float videoBlurDegrees;
    private float lastAppliedVideoBlurDegrees = float.NaN;
    private BackgroundVideoBlurType videoBlurType = BackgroundVideoBlurType.Gaussian;
    private BackgroundVideoBlurType lastAppliedVideoBlurType = BackgroundVideoBlurType.Gaussian;
    private float audioVolume = 1f;
    private readonly Stopwatch metricsClock = Stopwatch.StartNew();
    private readonly Stopwatch presentPacingClock = Stopwatch.StartNew();
    private double presentPeriodTicks = Stopwatch.Frequency / 30.0;
    private double nextPresentDueTicks;
    private int presentTargetHz = 30;
    private int lastRequestedPresentationWant = -1;
    private int lastRequestedPresentationCapHz = -1;
    private int maxReadbackFpsOverride;
    private int maxReadbackPixelsOverride;
    private long nextReadbackDueTicks;
    private int pendingHostVideoInvalidate;
    private float dynamicReadbackScale = 1.0f;
    private bool timerResolutionRaised;
    /// <summary>Host <see cref="Render"/> drew the mpv bitmap (may differ from worker readback if paints coalesce).</summary>
    private int uiVideoPaintsSinceMetrics;
    /// <summary>mpv worker completed OpenGL render + <c>ReadPixels</c> (actual readback throughput).</summary>
    private int workerReadbacksSinceMetrics;
    private int lastViewWidth;
    private int lastViewHeight;
    private int lastInternalReadbackWidth;
    private int lastInternalReadbackHeight;
    private int inputWidth;
    private int inputHeight;
    /// <summary>Last <see cref="TryApplyMpvScalerForDecodedVideoSize"/> <c>dwidth</c>/<c>dheight</c> pair; reset on load/stop.</summary>
    private int lastAppliedMpvScalerSourceW = -1;
    private int lastAppliedMpvScalerSourceH = -1;
    /// <summary>GDI+ upscale from readback bitmap to layout: nearest for retro line counts, else bilinear.</summary>
    private volatile bool readbackBlitUsesNearestNeighbor;
    private double uiVideoPaintHz;
    private double videoFps;
    private double videoBitrateMbps;
    private double videoDurationSeconds;
    private double workerReadbackHz;
    private string lastHwdecCurrent = string.Empty;
    /// <summary>Mpv's <c>hwdec</c> option value (what is configured), distinct from <c>hwdec-current</c>.</summary>
    private string lastHwdecOptionFromMpv = string.Empty;
    private bool lastAppliedHardwareDecoding;
    private float lastAppliedVideoPanX = float.NaN;
    private float lastAppliedVideoPanY = float.NaN;
    private float lastAppliedVideoZoom = float.NaN;
    /// <summary>When true, playback stays paused until the first decoded frame is rendered (A/V startup with audio).</summary>
    private bool holdPlaybackForAvStartupSync;
    private int startupPlaybackHoldRenderAttempts;
    /// <summary>When <see cref="StartVideoWithTimer"/> is set, playback stays paused at the file start until the run timer enters Running phase.</summary>
    private bool waitingForTimerStartBeforePlayback;
    private double lastTimerSyncSeekPositionSeconds = double.NaN;
    private bool forceTimerSyncResync;
    private bool disposed;

    public bool IsInitialized { get; private set; }
    public bool IsLoaded { get; private set; }
    public string LastError { get; private set; }
    public bool UsesDedicatedScheduler => useDedicatedScheduler;

    public int TargetPlaybackFps { get; private set; } = 30;
    public bool UseHardwareDecoding { get; set; }

    /// <summary>Maximum mpv worker presentation tick rate (Hz); readback can be capped separately.</summary>
    public int MaxPresentFps { get; set; } = 60;
    public int MaxReadbackFpsOverride
    {
        get => Volatile.Read(ref maxReadbackFpsOverride);
        set
        {
            int normalized = Math.Max(0, Math.Min(120, value));
            if (Interlocked.Exchange(ref maxReadbackFpsOverride, normalized) != normalized)
            {
                Volatile.Write(ref nextReadbackDueTicks, 0);
            }
        }
    }

    public int MaxReadbackPixelsOverride
    {
        get => Volatile.Read(ref maxReadbackPixelsOverride);
        set => Volatile.Write(ref maxReadbackPixelsOverride, Math.Max(0, value));
    }
    public bool LoopVideo { get; set; }
    public bool PlayAudio { get; set; }
    /// <summary>Horizontal pan (-1..1), passed to mpv video-pan-x.</summary>
    public float VideoPanX { get; set; }
    /// <summary>Vertical pan (-1..1), passed to mpv video-pan-y.</summary>
    public float VideoPanY { get; set; }
    /// <summary>Extra zoom amount (0..1), scaled into mpv video-zoom.</summary>
    public float VideoZoomExtra { get; set; }
    public float VideoBlurScale
    {
        get => videoBlurScale;
        set => videoBlurScale = Math.Max(0f, Math.Min(1f, value));
    }

    public BackgroundVideoBlurType VideoBlurType
    {
        get => videoBlurType;
        set => videoBlurType = value;
    }

    public float VideoBlurDegrees
    {
        get => videoBlurDegrees;
        set => videoBlurDegrees = Math.Max(0f, Math.Min(360f, value));
    }

    public float AudioVolume
    {
        get => audioVolume;
        set => audioVolume = Math.Max(0f, Math.Min(1f, value));
    }

    /// <summary>When true, the file stays paused at the beginning until the timer starts, then seeks to <see cref="VideoStartOffsetSeconds"/>.</summary>
    public bool StartVideoWithTimer { get; set; }

    private double videoStartOffsetSeconds;

    /// <summary>Seconds into the media to seek when the timer starts (clamped non-negative).</summary>
    public double VideoStartOffsetSeconds
    {
        get => videoStartOffsetSeconds;
        set => videoStartOffsetSeconds = Math.Max(0, Math.Min(86400, value));
    }

    public string GetDebugOverlayText()
    {
        try
        {
            if (mpvWorkerThread == null || !mpvWorkerThread.IsAlive)
            {
                return "Video Debug" + Environment.NewLine + "(worker not ready)";
            }

            string text = "Video Debug" + Environment.NewLine + "(collecting...)";
            RunOnMpvSurface(() =>
            {
                text = BuildDebugOverlayTextCore();
            });
            return text;
        }
        catch
        {
            return "Video Debug" + Environment.NewLine + "(unavailable)";
        }
    }

    private string BuildDebugOverlayTextCore()
    {
        return
            "Video Debug" + Environment.NewLine +
            "Backend: mpv (worker thread)" + Environment.NewLine +
            $"Input Res: {(inputWidth > 0 && inputHeight > 0 ? $"{inputWidth}x{inputHeight}" : "?x?")}" + Environment.NewLine +
            $"View Res: {(lastViewWidth > 0 && lastViewHeight > 0 ? $"{lastViewWidth}x{lastViewHeight}" : "?x?")}" + Environment.NewLine +
            $"Readback Res: {(lastInternalReadbackWidth > 0 && lastInternalReadbackHeight > 0 ? $"{lastInternalReadbackWidth}x{lastInternalReadbackHeight}" : "?x?")}" + Environment.NewLine +
            $"UI video draws (Hz): {uiVideoPaintHz:0.0} (WinForms paint; held frames can exceed source FPS)" + Environment.NewLine +
            $"Readback on worker (Hz): {workerReadbackHz:0.0} (present target {presentTargetHz}, readback expected {GetExpectedReadbackHz():0.0}; new frames only)" + Environment.NewLine +
            $"Readback scale: {dynamicReadbackScale:0.00}" + Environment.NewLine +
            $"Readback cap: {(MaxReadbackFpsOverride > 0 ? $"{MaxReadbackFpsOverride} Hz, " : string.Empty)}{(MaxReadbackPixelsOverride > 0 ? $"{MaxReadbackPixelsOverride / 1000}k" : "auto")}" + Environment.NewLine +
            $"LastError: {(string.IsNullOrWhiteSpace(LastError) ? "none" : LastError)}" + Environment.NewLine +
            $"Video FPS: {(videoFps > 0.05 ? videoFps.ToString("0.0") : "?")} (from file / decoder — not on-screen Hz)" + Environment.NewLine +
            $"Video Duration: {(videoDurationSeconds > 0.0 ? FormatVideoDuration(videoDurationSeconds) : "?")}" + Environment.NewLine +
            $"Video Bitrate: {videoBitrateMbps:0.00} Mbps" + Environment.NewLine +
            $"Video Opacity: {renderOpacity * 100f:0}% | Blit: {lastFrameBlitPath}" + Environment.NewLine +
            "Perf Mode: always fast" + Environment.NewLine +
            $"Target FPS: {TargetPlaybackFps}" + Environment.NewLine +
            $"hwdec (mpv option): {(string.IsNullOrEmpty(lastHwdecOptionFromMpv) ? "?" : lastHwdecOptionFromMpv)}" + Environment.NewLine +
            $"hwdec-current: {(string.IsNullOrEmpty(lastHwdecCurrent) ? "?" : lastHwdecCurrent)}";
    }

    public LibMpvBackgroundPlayer(Form hostForm, bool useDedicatedScheduler)
    {
        this.hostForm = hostForm;
        this.useDedicatedScheduler = useDedicatedScheduler;
    }

    public bool TryInitialize()
    {
        if (IsInitialized)
        {
            return true;
        }

        if (!EnsureLibMpvAvailable(out string libMpvError))
        {
            LastError = libMpvError;
            return false;
        }

        try
        {
            StartMpvWorkerAndWaitForGl();
            if (!IsInitialized)
            {
                LastError = string.IsNullOrWhiteSpace(LastError)
                    ? "libmpv initialization failed."
                    : LastError;
                Trace.TraceError("Video background initialization failed: " + LastError);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Trace.TraceError("Video background initialization exception: " + ex);
            return false;
        }
    }

    public bool Load(string source)
    {
        if (!IsInitialized || string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        try
        {
            bool ok = false;
            RunOnMpvSurface(() => { ok = LoadCore(source); });
            return ok;
        }
        catch (Exception ex)
        {
            IsLoaded = false;
            LastError = "libmpv load failed: " + ex.Message;
            Trace.TraceError("Video background load exception: " + ex);
            return false;
        }
    }

    private bool LoadCore(string source)
    {
        loadedSource = source;
        lastAppliedMpvScalerSourceW = -1;
        lastAppliedMpvScalerSourceH = -1;
        readbackBlitUsesNearestNeighbor = false;
        forceTimerSyncResync = false;
        ApplyMpvRuntimeOptions();
        int mpvResult = MpvCommand("loadfile", source, "replace");
        IsLoaded = mpvResult >= 0;
        if (!IsLoaded)
        {
            LastError = "libmpv failed to load media source.";
            return false;
        }

        _ = MpvCommand("set", "vid", "auto");
        _ = MpvCommand("seek", "0", "absolute+keyframes");
        if (StartVideoWithTimer)
        {
            waitingForTimerStartBeforePlayback = true;
            lastTimerSyncSeekPositionSeconds = double.NaN;
            holdPlaybackForAvStartupSync = false;
            startupPlaybackHoldRenderAttempts = 0;
        dynamicReadbackScale = 1.0f;
            _ = MpvCommand("set", "pause", "yes");
        }
        else if (PlayAudio)
        {
            waitingForTimerStartBeforePlayback = false;
            lastTimerSyncSeekPositionSeconds = double.NaN;
            holdPlaybackForAvStartupSync = true;
            startupPlaybackHoldRenderAttempts = 0;
        dynamicReadbackScale = 1.0f;
            _ = MpvCommand("set", "pause", "yes");
        }
        else
        {
            waitingForTimerStartBeforePlayback = false;
            lastTimerSyncSeekPositionSeconds = double.NaN;
            holdPlaybackForAvStartupSync = false;
            startupPlaybackHoldRenderAttempts = 0;
        dynamicReadbackScale = 1.0f;
            _ = MpvCommand("set", "pause", "no");
        }

        LastError = null;
        lastAppliedScaleHeight = -1;
        lastAppliedVideoFilter = null;
        lastAppliedVideoPanX = float.NaN;
        lastAppliedVideoPanY = float.NaN;
        lastAppliedVideoZoom = float.NaN;
        RefreshTargetPlaybackFpsFromMpv();
        SyncPresentationWithHost(true, MaxPresentFps);
        return true;
    }

    public void Stop()
    {
        try
        {
            RunOnMpvSurface(StopCore);
        }
        catch
        {
        }
    }

    private void StopCore()
    {
        _ = MpvCommand("stop");
        IsLoaded = false;
        TargetPlaybackFps = 30;
        _ = Interlocked.Exchange(ref uiVideoPaintsSinceMetrics, 0);
        _ = Interlocked.Exchange(ref workerReadbacksSinceMetrics, 0);
        uiVideoPaintHz = 0.0;
        videoFps = 0.0;
        videoBitrateMbps = 0.0;
        videoDurationSeconds = 0.0;
        workerReadbackHz = 0.0;
        lastHwdecCurrent = string.Empty;
        lastHwdecOptionFromMpv = string.Empty;
        lastAppliedScaleHeight = -1;
        lastAppliedVideoFilter = null;
        lastAppliedVideoPanX = float.NaN;
        lastAppliedVideoPanY = float.NaN;
        lastAppliedVideoZoom = float.NaN;
        holdPlaybackForAvStartupSync = false;
        startupPlaybackHoldRenderAttempts = 0;
        dynamicReadbackScale = 1.0f;
        waitingForTimerStartBeforePlayback = false;
        lastTimerSyncSeekPositionSeconds = double.NaN;
        forceTimerSyncResync = false;
        lastAppliedMpvScalerSourceW = -1;
        lastAppliedMpvScalerSourceH = -1;
        readbackBlitUsesNearestNeighbor = false;
        metricsClock.Restart();
        SyncPresentationWithHost(false, MaxPresentFps);
    }

    public void RunTimerSyncWaitAtBeginning()
    {
        RunOnMpvSurface(RunTimerSyncWaitAtBeginningCore);
    }

    private void RunTimerSyncWaitAtBeginningCore()
    {
        if (!IsLoaded || !StartVideoWithTimer)
        {
            return;
        }

        waitingForTimerStartBeforePlayback = true;
        lastTimerSyncSeekPositionSeconds = double.NaN;
        _ = MpvCommand("seek", "0", "absolute+keyframes");
        _ = MpvCommand("set", "pause", "yes");
    }

    public void RunTimerSyncNotRunningKeepPlayback()
    {
        RunOnMpvSurface(RunTimerSyncNotRunningKeepPlaybackCore);
    }

    private void RunTimerSyncNotRunningKeepPlaybackCore()
    {
        if (!IsLoaded || !StartVideoWithTimer || mpvHandle == IntPtr.Zero)
        {
            return;
        }

        waitingForTimerStartBeforePlayback = false;
        _ = MpvCommand("set", "pause", "no");
    }

    /// <summary>Forces the next <see cref="RunTimerSyncEnsurePlayingForRunningPhase"/> call to seek again (e.g. after undoing a completed run).</summary>
    public void InvalidateTimerSyncSeekTarget()
    {
        RunOnMpvSurface(() => { forceTimerSyncResync = true; });
    }

    public void RunTimerSyncEnsurePlayingForRunningPhase()
    {
        RunOnMpvSurface(RunTimerSyncEnsurePlayingForRunningPhaseCore);
    }

    private void RunTimerSyncEnsurePlayingForRunningPhaseCore()
    {
        if (!IsLoaded || !StartVideoWithTimer)
        {
            return;
        }

        bool layoutOffsetChanged = !double.IsNaN(lastTimerSyncSeekPositionSeconds)
            && Math.Abs(lastTimerSyncSeekPositionSeconds - VideoStartOffsetSeconds) > 0.02;

        if (waitingForTimerStartBeforePlayback)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            _ = MpvCommand("seek", VideoStartOffsetSeconds.ToString(inv), "absolute");
            _ = MpvCommand("set", "pause", "no");
            waitingForTimerStartBeforePlayback = false;
            lastTimerSyncSeekPositionSeconds = VideoStartOffsetSeconds;
            forceTimerSyncResync = false;
            return;
        }

        if (forceTimerSyncResync || layoutOffsetChanged)
        {
            forceTimerSyncResync = false;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            _ = MpvCommand("seek", VideoStartOffsetSeconds.ToString(inv), "absolute");
            _ = MpvCommand("set", "pause", "no");
            lastTimerSyncSeekPositionSeconds = VideoStartOffsetSeconds;
            return;
        }

        _ = MpvCommand("set", "pause", "no");
    }

    public void RunTimerSyncPausePlayback()
    {
        RunOnMpvSurface(RunTimerSyncPausePlaybackCore);
    }

    private void RunTimerSyncPausePlaybackCore()
    {
        if (!IsLoaded || !StartVideoWithTimer)
        {
            return;
        }

        _ = MpvCommand("set", "pause", "yes");
    }

    public void RunTimerSyncUnpauseAfterRunCompletes()
    {
        RunOnMpvSurface(RunTimerSyncUnpauseAfterRunCompletesCore);
    }

    private void RunTimerSyncUnpauseAfterRunCompletesCore()
    {
        if (!IsLoaded || !StartVideoWithTimer || mpvHandle == IntPtr.Zero)
        {
            return;
        }

        _ = MpvCommand("set", "pause", "no");
    }

    public void ExitTimerStartSyncToNormalAutoplay()
    {
        RunOnMpvSurface(ExitTimerStartSyncToNormalAutoplayCore);
    }

    private void ExitTimerStartSyncToNormalAutoplayCore()
    {
        waitingForTimerStartBeforePlayback = false;
        lastTimerSyncSeekPositionSeconds = double.NaN;
        if (!IsLoaded)
        {
            return;
        }

        // Don't force a seek when timer-start sync is disabled; keep current playback position.
        // This prevents unwanted restarts when the run timer starts.
        if (PlayAudio)
        {
            holdPlaybackForAvStartupSync = true;
            startupPlaybackHoldRenderAttempts = 0;
            _ = MpvCommand("set", "pause", "yes");
        }
        else
        {
            holdPlaybackForAvStartupSync = false;
            startupPlaybackHoldRenderAttempts = 0;
            _ = MpvCommand("set", "pause", "no");
        }
    }

    private const double RunTimerDriftCorrectAboveSeconds = 0.22;
    private const double RunTimerDriftAbsoluteSeekAboveSeconds = 1.25;

    public void TickPlaybackRunTimerDriftCorrection(TimerPhase runPhase, TimeSpan runElapsedWallClock)
    {
        RunOnMpvSurface(() => TickPlaybackRunTimerDriftCorrectionCore(runPhase, runElapsedWallClock));
    }

    private void TickPlaybackRunTimerDriftCorrectionCore(TimerPhase runPhase, TimeSpan runElapsedWallClock)
    {
        if (!IsLoaded || !StartVideoWithTimer || LoopVideo)
        {
            return;
        }

        if (runPhase != TimerPhase.Running || runElapsedWallClock < TimeSpan.Zero)
        {
            return;
        }

        if (TryGetMpvStringProperty("pause", out string pauseText) && pauseText.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!TryGetPlaybackTimePosSeconds(out double timePos))
        {
            return;
        }

        double expected = videoStartOffsetSeconds + runElapsedWallClock.TotalSeconds;
        double delta = expected - timePos;
        if (Math.Abs(delta) < RunTimerDriftCorrectAboveSeconds)
        {
            return;
        }

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (Math.Abs(delta) >= RunTimerDriftAbsoluteSeekAboveSeconds)
        {
            _ = MpvCommand("seek", expected.ToString(inv), "absolute+keyframes");
        }
        else
        {
            _ = MpvCommand("seek", delta.ToString(inv), "relative", "exact");
        }
    }

    private bool TryGetPlaybackTimePosSeconds(out double seconds)
    {
        seconds = 0.0;
        if (mpvHandle == IntPtr.Zero)
        {
            return false;
        }

        double propValue = 0.0;
        int result = mpv_get_property(mpvHandle, "time-pos", MPV_FORMAT_DOUBLE, ref propValue);
        if (result < 0 || double.IsNaN(propValue) || double.IsInfinity(propValue) || propValue < 0.0)
        {
            return false;
        }

        seconds = propValue;
        return true;
    }

    internal void ReleasePendingHostInvalidateAfterHostPaint()
    {
        Interlocked.Exchange(ref pendingHostVideoInvalidate, 0);
    }

    private void QueueHostVideoInvalidate()
    {
        if (hostForm == null || !hostForm.IsHandleCreated || hostForm.IsDisposed)
        {
            return;
        }

        if (hostForm is TimerForm timerForm && timerForm.IsVideoUiPresentTimerActive)
        {
            return;
        }

        if (Interlocked.Exchange(ref pendingHostVideoInvalidate, 1) != 0)
        {
            return;
        }

        try
        {
            hostForm.BeginInvoke((MethodInvoker)delegate
            {
                try
                {
                    if (hostForm != null && hostForm.IsHandleCreated && !hostForm.IsDisposed)
                    {
                        hostForm.Invalidate();
                    }
                    else
                    {
                        Interlocked.Exchange(ref pendingHostVideoInvalidate, 0);
                    }
                }
                catch
                {
                    Interlocked.Exchange(ref pendingHostVideoInvalidate, 0);
                }
            });
        }
        catch
        {
            Interlocked.Exchange(ref pendingHostVideoInvalidate, 0);
        }
    }

    private bool HasMpvReadbackFrame()
    {
        lock (mpvFrameBitmapSync)
        {
            return mpvFrameBitmapFront != null;
        }
    }

    private void QueueHeldFrameHostPresent()
    {
        if (hostForm is TimerForm timerForm
            && !timerForm.IsVideoUiPresentTimerActive
            && HasMpvReadbackFrame())
        {
            timerForm.RequestHeldVideoFramePresentFromBackgroundWorker();
        }
    }

    private bool IsReadbackDue(long nowTicks)
    {
        int readbackCapHz = MaxReadbackFpsOverride;
        if (readbackCapHz <= 0)
        {
            return true;
        }

        double readbackPeriodTicks = Stopwatch.Frequency / (double)readbackCapHz;
        long dueTicks = Volatile.Read(ref nextReadbackDueTicks);
        if (dueTicks <= 0 || nowTicks - dueTicks > readbackPeriodTicks * 4.0)
        {
            dueTicks = nowTicks;
        }

        if (nowTicks < dueTicks)
        {
            return false;
        }

        Volatile.Write(ref nextReadbackDueTicks, (long)(dueTicks + readbackPeriodTicks));
        return true;
    }

    public void Render(Graphics graphics, int width, int height)
    {
        lastViewWidth = width;
        lastViewHeight = height;
        lock (mpvFrameBitmapSync)
        {
            if (mpvFrameBitmapFront != null)
            {
                DrawMpvFrame(graphics, width, height);
                _ = Interlocked.Increment(ref uiVideoPaintsSinceMetrics);
            }
        }
    }

    public void SetOpacity(float opacity)
    {
        renderOpacity = Math.Max(0f, Math.Min(1f, opacity));
    }

    public void UpdatePlacement()
    {
        // mpv render API path does not need external placement.
    }

    private static float ClampDynamicReadbackScale(float value) =>
        Math.Min(MpvReadbackDynamicScaleMax, Math.Max(MpvReadbackDynamicScaleMin, value));

    private double GetExpectedReadbackHz()
    {
        double sourceHz = videoFps > 0.05 ? videoFps : TargetPlaybackFps;
        int readbackCapHz = MaxReadbackFpsOverride;
        if (sourceHz <= 0.05)
        {
            return readbackCapHz > 0 ? Math.Min(presentTargetHz, readbackCapHz) : presentTargetHz;
        }

        double expected = Math.Min(presentTargetHz, sourceHz);
        if (readbackCapHz > 0)
        {
            expected = Math.Min(expected, readbackCapHz);
        }

        return Math.Max(1.0, expected);
    }

    private void UpdateDebugMetricsIfNeeded()
    {
        if (metricsClock.Elapsed < TimeSpan.FromSeconds(1))
        {
            return;
        }

        double elapsed = metricsClock.Elapsed.TotalSeconds;
        if (elapsed <= 0.0)
        {
            return;
        }

        int uiPaints = Interlocked.Exchange(ref uiVideoPaintsSinceMetrics, 0);
        int readbacks = Interlocked.Exchange(ref workerReadbacksSinceMetrics, 0);
        uiVideoPaintHz = uiPaints / elapsed;
        workerReadbackHz = readbacks / elapsed;

        RefreshTargetPlaybackFpsFromMpv();
        if (!TryGetMpvDoubleProperty("estimated-vf-fps", out videoFps)
            && !TryGetMpvDoubleProperty("container-fps", out videoFps))
        {
            videoFps = 0.0;
        }

        double expectedReadbackHz = GetExpectedReadbackHz();
        if (expectedReadbackHz > 0.0 && readbacks > 0)
        {
            double ratio = workerReadbackHz / expectedReadbackHz;
            if (ratio < 0.85)
            {
                dynamicReadbackScale = ClampDynamicReadbackScale(dynamicReadbackScale - 0.10f);
            }
            else if (ratio < 0.95)
            {
                dynamicReadbackScale = ClampDynamicReadbackScale(dynamicReadbackScale - 0.05f);
            }
            else if (ratio > 1.10)
            {
                dynamicReadbackScale = ClampDynamicReadbackScale(dynamicReadbackScale + 0.02f);
            }
        }
        if (TryGetMpvIntProperty("width", out long sourceW) && TryGetMpvIntProperty("height", out long sourceH))
        {
            inputWidth = (int)Math.Max(0, Math.Min(int.MaxValue, sourceW));
            inputHeight = (int)Math.Max(0, Math.Min(int.MaxValue, sourceH));
        }

        if (!TryGetMpvDoubleProperty("duration", out videoDurationSeconds))
        {
            videoDurationSeconds = 0.0;
        }

        if (TryGetMpvDoubleProperty("video-bitrate", out double bitrateBps))
        {
            videoBitrateMbps = bitrateBps / 1_000_000.0;
        }
        else
        {
            videoBitrateMbps = 0.0;
        }

        if (TryGetMpvStringProperty("hwdec", out string hwdecOpt))
        {
            lastHwdecOptionFromMpv = hwdecOpt;
        }

        if (TryGetMpvStringProperty("hwdec-current", out string hwdecCur))
        {
            lastHwdecCurrent = hwdecCur;
        }

        metricsClock.Restart();
    }

    private void RefreshTargetPlaybackFpsFromMpv()
    {
        if (mpvHandle == IntPtr.Zero)
        {
            return;
        }

        if (!TryGetMpvDoubleProperty("estimated-vf-fps", out double fps)
            && !TryGetMpvDoubleProperty("container-fps", out fps))
        {
            return;
        }

        if (double.IsNaN(fps) || double.IsInfinity(fps) || fps <= 0.0)
        {
            return;
        }

        int rounded = (int)Math.Round(fps);
        // Reported stream FPS for metrics/debug (UI paint uses Settings.VideoBackgroundPaintFps).
        TargetPlaybackFps = Math.Max(1, Math.Min(120, rounded));
    }

    private bool TryGetMpvDoubleProperty(string propertyName, out double value)
    {
        value = 0.0;
        if (mpvHandle == IntPtr.Zero)
        {
            return false;
        }

        double propValue = 0.0;
        int result = mpv_get_property(mpvHandle, propertyName, MPV_FORMAT_DOUBLE, ref propValue);
        if (result < 0 || double.IsNaN(propValue) || double.IsInfinity(propValue) || propValue <= 0.0)
        {
            return false;
        }

        value = propValue;
        return true;
    }

    private static string FormatVideoDuration(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0.0)
        {
            return "?";
        }

        TimeSpan duration = TimeSpan.FromSeconds(seconds);
        int fractional = (int)((duration.Ticks % TimeSpan.TicksPerSecond) / 1000);
        if (duration.TotalHours >= 1.0)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}.{3:0000}", (int)duration.TotalHours, duration.Minutes, duration.Seconds, fractional);
        }

        if (duration.TotalMinutes >= 1.0)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}.{2:0000}", (int)duration.TotalMinutes, duration.Seconds, fractional);
        }

        return seconds.ToString("0.0000", CultureInfo.InvariantCulture);
    }

    private bool TryGetMpvIntProperty(string propertyName, out long value)
    {
        value = 0;
        if (mpvHandle == IntPtr.Zero)
        {
            return false;
        }

        long propValue = 0;
        int result = mpv_get_property(mpvHandle, propertyName, MPV_FORMAT_INT64, ref propValue);
        if (result < 0 || propValue <= 0)
        {
            return false;
        }

        value = propValue;
        return true;
    }

    private static readonly int[] RetroLineCountDimensionsForNearestScaling = { 224, 226, 240, 448, 452, 480 };

    /// <summary>
    /// When the coded picture width or height matches one of these line counts, use nearest-neighbor in mpv and for the readback blit.
    /// </summary>
    private static bool ShouldUseNearestMpvScalingForCodedDimensions(int codedWidth, int codedHeight)
    {
        if (codedWidth <= 0 || codedHeight <= 0)
        {
            return false;
        }

        foreach (int v in RetroLineCountDimensionsForNearestScaling)
        {
            if (codedWidth == v || codedHeight == v)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetDecodedVideoSize(out int w, out int h)
    {
        w = 0;
        h = 0;
        if (TryGetMpvIntProperty("dwidth", out long dw) && TryGetMpvIntProperty("dheight", out long dh))
        {
            w = (int)Math.Min(int.MaxValue, dw);
            h = (int)Math.Min(int.MaxValue, dh);
            return w > 0 && h > 0;
        }

        if (TryGetMpvIntProperty("width", out long sw) && TryGetMpvIntProperty("height", out long sh))
        {
            w = (int)Math.Min(int.MaxValue, sw);
            h = (int)Math.Min(int.MaxValue, sh);
            return w > 0 && h > 0;
        }

        return false;
    }

    private void TryApplyMpvScalerForDecodedVideoSize()
    {
        if (mpvHandle == IntPtr.Zero)
        {
            return;
        }

        if (!TryGetDecodedVideoSize(out int w, out int h))
        {
            return;
        }

        if (w == lastAppliedMpvScalerSourceW && h == lastAppliedMpvScalerSourceH)
        {
            return;
        }

        bool nearest = ShouldUseNearestMpvScalingForCodedDimensions(w, h);
        lastAppliedMpvScalerSourceW = w;
        lastAppliedMpvScalerSourceH = h;
        string scaler = nearest ? "nearest" : "bilinear";
        _ = MpvCommand("set", "scale", scaler);
        _ = MpvCommand("set", "cscale", scaler);
        _ = MpvCommand("set", "dscale", scaler);
        readbackBlitUsesNearestNeighbor = nearest;
    }

    private static string PtrToStringUtf8(IntPtr nativeUtf8)
    {
        if (nativeUtf8 == IntPtr.Zero)
        {
            return string.Empty;
        }

        int byteLen = 0;
        while (Marshal.ReadByte(nativeUtf8, byteLen) != 0)
        {
            byteLen++;
            if (byteLen > 4096)
            {
                break;
            }
        }

        if (byteLen == 0)
        {
            return string.Empty;
        }

        byte[] buffer = new byte[byteLen];
        Marshal.Copy(nativeUtf8, buffer, 0, byteLen);
        return Encoding.UTF8.GetString(buffer);
    }

    private bool TryGetMpvStringProperty(string propertyName, out string value)
    {
        value = string.Empty;
        if (mpvHandle == IntPtr.Zero)
        {
            return false;
        }

        IntPtr strPtr = IntPtr.Zero;
        try
        {
            int result = mpv_get_property_string(mpvHandle, propertyName, MPV_FORMAT_STRING, ref strPtr);
            if (result < 0 || strPtr == IntPtr.Zero)
            {
                return false;
            }

            value = PtrToStringUtf8(strPtr);
            return true;
        }
        finally
        {
            if (strPtr != IntPtr.Zero)
            {
                mpv_free(strPtr);
            }
        }
    }

    private bool TryInitializeMpv()
    {
        if (!EnableMpvRenderApiBackend)
        {
            return false;
        }

        try
        {
            mpvHandle = mpv_create();
            if (mpvHandle == IntPtr.Zero)
            {
                LastError = "libmpv initialization failed: mpv_create returned null.";
                return false;
            }

            _ = mpv_set_option_string(mpvHandle, "keep-open", "yes");
            _ = mpv_set_option_string(mpvHandle, "terminal", "no");
            _ = mpv_set_option_string(mpvHandle, "keepaspect", "yes");
            _ = mpv_set_option_string(mpvHandle, "panscan", "1.0");
            // libmpv render API requires vo=libmpv; then we tune libplacebo/scalers for speed.
            _ = mpv_set_option_string(mpvHandle, "vo", "libmpv");
            _ = mpv_set_option_string(mpvHandle, "gpu-context", "angle");
            _ = mpv_set_option_string(mpvHandle, "gpu-api", "d3d11");
            _ = mpv_set_option_string(mpvHandle, "hwdec", UseHardwareDecoding ? MpvHwdecWhenEnabled : "no");
            _ = mpv_set_option_string(mpvHandle, "profile", "fast");
            // Default bilinear; <see cref="TryApplyMpvScalerForDecodedVideoSize"/> switches to nearest for classic line counts.
            _ = mpv_set_option_string(mpvHandle, "scale", "bilinear");
            _ = mpv_set_option_string(mpvHandle, "cscale", "bilinear");
            _ = mpv_set_option_string(mpvHandle, "dscale", "bilinear");
            _ = mpv_set_option_string(mpvHandle, "correct-downscaling", "no");
            _ = mpv_set_option_string(mpvHandle, "deband", "no");
            _ = mpv_set_option_string(mpvHandle, "sigmoid-upscaling", "no");
            _ = mpv_set_option_string(mpvHandle, "vd-lavc-fast", "yes");
            _ = mpv_set_option_string(mpvHandle, "vd-lavc-skiploopfilter", "all");
            _ = mpv_set_option_string(mpvHandle, "vd-lavc-skipidct", "nonref");
            _ = mpv_set_option_string(mpvHandle, "vd-lavc-skipframe", "nonref");
            // Long / high-bitrate files default to large demuxer caches; cap readahead/RAM so the UI thread
            // stays responsive next to GL readback (bitrate itself does not change readback size).
            _ = mpv_set_option_string(mpvHandle, "demuxer-readahead-secs", "2");
            _ = mpv_set_option_string(mpvHandle, "demuxer-max-bytes", "48MiB");
            _ = mpv_set_option_string(mpvHandle, "demuxer-max-back-bytes", "16MiB");
            _ = mpv_set_option_string(mpvHandle, "framedrop", "decoder+vo");
            _ = mpv_set_option_string(mpvHandle, "video-latency-hacks", "yes");
            // Without this, mpv_render_context_render() can block on target display time and cap readback to ~display cadence.
            _ = mpv_set_option_string(mpvHandle, "video-timing-offset", "0");

            int initResult = mpv_initialize(mpvHandle);
            if (initResult < 0)
            {
                LastError = $"libmpv initialization failed: mpv_initialize returned {initResult}.";
                mpv_terminate_destroy(mpvHandle);
                mpvHandle = IntPtr.Zero;
                return false;
            }

            if (EnableMpvRenderApiBackend)
            {
                if (!TryInitializeMpvRenderContext())
                {
                    mpv_terminate_destroy(mpvHandle);
                    mpvHandle = IntPtr.Zero;
                    return false;
                }
            }

            appliedMpvRuntimeOptions = false;
            lastAppliedLoopVideo = !LoopVideo;
            lastAppliedPlayAudio = !PlayAudio;
            lastAppliedVolume = -1;
            lastAppliedVideoPanX = float.NaN;
            lastAppliedVideoPanY = float.NaN;
            lastAppliedVideoZoom = float.NaN;
            return true;
        }
        catch (DllNotFoundException)
        {
            LastError = "libmpv initialization failed: libmpv-2.dll was not found.";
            mpvHandle = IntPtr.Zero;
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            LastError = "libmpv initialization failed: missing required mpv API entry points (check DLL version).";
            mpvHandle = IntPtr.Zero;
            return false;
        }
        catch (Exception ex)
        {
            LastError = "libmpv initialization failed: " + ex.Message;
            if (mpvHandle != IntPtr.Zero)
            {
                mpv_terminate_destroy(mpvHandle);
                mpvHandle = IntPtr.Zero;
            }

            return false;
        }
    }

    private void MpvWorkerThreadProc()
    {
        mpvWorkerThreadId = Thread.CurrentThread.ManagedThreadId;
        try
        {
            if (!InitializeOpenTkWorkerContext())
            {
                IsInitialized = false;
                mpvWorkerGlReady.Set();
                return;
            }

            if (TryInitializeMpv())
            {
                IsInitialized = true;
                LastError = null;
                Trace.WriteLine("Video background initialized with libmpv backend (render worker).");
                EnsureMpvPresentTimerOnWorker();
                mpvWorkerLoopRunning = true;
            }
            else
            {
                IsInitialized = false;
                Trace.TraceError("Video background initialization failed: " + LastError);
            }
        }
        catch (Exception ex)
        {
            IsInitialized = false;
            LastError = string.IsNullOrWhiteSpace(LastError) ? ex.Message : LastError;
            Trace.TraceError("Video background initialization exception: " + ex);
        }
        finally
        {
            mpvWorkerGlReady.Set();
        }

        while (mpvWorkerLoopRunning && !disposed)
        {
            try
            {
                while (mpvWorkerActions.TryDequeue(out Action action))
                {
                    action?.Invoke();
                }

                if (useDedicatedScheduler && dedicatedPresentEnabled && IsLoaded)
                {
                    PresentTickCore();
                }
            }
            catch
            {
            }

            int waitMs = (useDedicatedScheduler && dedicatedPresentEnabled) ? 1 : 8;
            mpvWorkerWakeEvent.WaitOne(waitMs);
        }

        try
        {
            if (mpvRenderContext != IntPtr.Zero)
            {
                mpv_render_context_free(mpvRenderContext);
                mpvRenderContext = IntPtr.Zero;
            }

            ReleaseMpvRenderScratchNative();

            if (mpvApiTypeHandleAllocated)
            {
                mpvApiTypeHandle.Free();
                mpvApiTypeHandleAllocated = false;
            }

            lock (mpvFrameBitmapSync)
            {
                mpvFrameBitmapFront?.Dispose();
                mpvFrameBitmapFront = null;
                mpvFrameBitmapBack?.Dispose();
                mpvFrameBitmapBack = null;
            }

            if (mpvHandle != IntPtr.Zero)
            {
                mpv_terminate_destroy(mpvHandle);
                mpvHandle = IntPtr.Zero;
            }

            try
            {
                mpvGraphicsContext?.MakeCurrent(null);
            }
            catch
            {
            }

            mpvGraphicsContext?.Dispose();
            mpvGraphicsContext = null;
            mpvNativeWindow?.Dispose();
            mpvNativeWindow = null;
        }
        catch
        {
        }
    }

    private void StartMpvWorkerAndWaitForGl()
    {
        lock (mpvWorkerStartLock)
        {
            if (mpvWorkerThread != null && mpvWorkerThread.IsAlive)
            {
                _ = mpvWorkerGlReady.Wait(120000);
                return;
            }

            mpvWorkerGlReady.Reset();
            mpvWorkerThread = new Thread(MpvWorkerThreadProc)
            {
                IsBackground = true,
                Name = "LiveSplitLibMpv"
            };
            mpvWorkerThread.SetApartmentState(ApartmentState.STA);
            mpvWorkerThread.Start();
        }

        if (!mpvWorkerGlReady.Wait(120000) && !IsInitialized)
        {
            LastError = string.IsNullOrWhiteSpace(LastError)
                ? "libmpv render worker did not become ready in time."
                : LastError;
        }
    }

    private bool InitializeOpenTkWorkerContext()
    {
        try
        {
            mpvNativeWindow = new OpenTK.NativeWindow(
                1,
                1,
                "LiveSplitMpvWorker",
                GameWindowFlags.Default,
                new GraphicsMode(32, 24, 0, 0),
                DisplayDevice.Default);
            mpvNativeWindow.Visible = false;
            mpvGraphicsContext = new GraphicsContext(GraphicsMode.Default, mpvNativeWindow.WindowInfo);
            mpvGraphicsContext.MakeCurrent(mpvNativeWindow.WindowInfo);
            (mpvGraphicsContext as IGraphicsContextInternal)?.LoadAll();
            return true;
        }
        catch (Exception ex)
        {
            LastError = "libmpv render API init failed: OpenTK worker context initialization failed. " + ex.Message;
            return false;
        }
    }

    private void RunOnMpvSurface(Action action)
    {
        if (action == null)
        {
            return;
        }

        try
        {
            if (Thread.CurrentThread.ManagedThreadId == mpvWorkerThreadId)
            {
                action();
                return;
            }

            if (mpvWorkerThread == null || !mpvWorkerThread.IsAlive)
            {
                return;
            }

            using var done = new ManualResetEventSlim(false);
            Exception thrown = null;
            mpvWorkerActions.Enqueue(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    thrown = ex;
                }
                finally
                {
                    done.Set();
                }
            });
            mpvWorkerWakeEvent.Set();
            _ = done.Wait(5000);
            if (thrown != null)
            {
                throw thrown;
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void EnsureMpvPresentTimerOnWorker()
    {
        if (useDedicatedScheduler)
        {
            return;
        }

        if (mpvPresentTimer != null)
        {
            return;
        }

        mpvPresentTimer = new System.Windows.Forms.Timer();
        mpvPresentTimer.Tick += MpvPresentTimerOnTick;
        mpvPresentTimer.Interval = 1;
        mpvPresentTimer.Enabled = false;
    }

    private void MpvPresentTimerOnTick(object sender, EventArgs e)
    {
        PresentTickCore();
    }

    private void PresentTickCore()
    {
        if (disposed || mpvRenderContext == IntPtr.Zero || !IsLoaded)
        {
            return;
        }

        long nowTicks = presentPacingClock.ElapsedTicks;
        if (nowTicks < nextPresentDueTicks)
        {
            return;
        }

        if (nowTicks - nextPresentDueTicks > presentPeriodTicks * 4.0)
        {
            nextPresentDueTicks = nowTicks;
        }

        nextPresentDueTicks += presentPeriodTicks;

        int vw = Volatile.Read(ref cachedHostClientW);
        int vh = Volatile.Read(ref cachedHostClientH);
        if (vw <= 0 || vh <= 0)
        {
            return;
        }

        lastViewWidth = vw;
        lastViewHeight = vh;
        ApplyMpvRuntimeOptions();
        bool shouldReadback = IsReadbackDue(nowTicks);
        bool didReadback = false;
        if (shouldReadback && !TryUpdateMpvVideoTexture(vw, vh, out didReadback))
        {
            return;
        }

        if (shouldReadback && didReadback)
        {
            _ = Interlocked.Increment(ref workerReadbacksSinceMetrics);
            QueueHostVideoInvalidate();
        }

        UpdateDebugMetricsIfNeeded();
        QueueHeldFrameHostPresent();


        if (holdPlaybackForAvStartupSync)
        {
            startupPlaybackHoldRenderAttempts++;
            if (startupPlaybackHoldRenderAttempts > 240)
            {
                ReleaseStartupPlaybackHold();
            }
        }
    }

    public void SyncPresentationWithHost(bool want, int videoPaintFps)
    {
        int normalizedCapHz = Math.Max(1, Math.Min(120, videoPaintFps));
        int wantInt = want ? 1 : 0;
        if (Volatile.Read(ref lastRequestedPresentationWant) == wantInt
            && Volatile.Read(ref lastRequestedPresentationCapHz) == normalizedCapHz)
        {
            return;
        }

        Volatile.Write(ref lastRequestedPresentationWant, wantInt);
        Volatile.Write(ref lastRequestedPresentationCapHz, normalizedCapHz);

        void Inner()
        {
            if (useDedicatedScheduler)
            {
                // Dedicated scheduler uses the existing mpv worker loop directly.
            }
            else
            {
                EnsureMpvPresentTimerOnWorker();
                if (mpvPresentTimer == null)
                {
                    return;
                }
            }

            if (!want || !IsLoaded)
            {
                if (useDedicatedScheduler)
                {
                    dedicatedPresentEnabled = false;
                }
                else
                {
                    mpvPresentTimer.Enabled = false;
                }

                if (timerResolutionRaised)
                {
                    _ = timeEndPeriod(1);
                    timerResolutionRaised = false;
                }
                return;
            }

            int capHz = normalizedCapHz;
            // Drive readback cadence from host settings, not container/stream metadata. Some files
            // report pathological fps values (e.g. ~1), which would throttle presentation to ~1 Hz
            // and make the timer appear to update only once per second.
            int hz = capHz;
            presentTargetHz = hz;
            presentPeriodTicks = Stopwatch.Frequency / (double)hz;
            nextPresentDueTicks = presentPacingClock.ElapsedTicks;
            if (!timerResolutionRaised)
            {
                _ = timeBeginPeriod(1);
                timerResolutionRaised = true;
            }
            if (useDedicatedScheduler)
            {
                dedicatedPresentEnabled = true;
            }
            else
            {
                mpvPresentTimer.Interval = 1;
                mpvPresentTimer.Enabled = true;
            }
        }

        RunOnMpvSurface(Inner);
    }

    public void NotifyHostClientSize(int w, int h)
    {
        Volatile.Write(ref cachedHostClientW, Math.Max(0, w));
        Volatile.Write(ref cachedHostClientH, Math.Max(0, h));
    }

    public void PingRuntimeOptionsToMpv()
    {
        if (useDedicatedScheduler)
        {
            mpvWorkerWakeEvent.Set();
            return;
        }

        RunOnMpvSurface(ApplyMpvRuntimeOptions);
    }

    public void PushLoopFileOptionToMpvNow()
    {
        RunOnMpvSurface(PushLoopFileOptionToMpvNowCore);
    }

    private void PushLoopFileOptionToMpvNowCore()
    {
        if (mpvHandle == IntPtr.Zero)
        {
            return;
        }

        _ = MpvCommand("set", "loop-file", LoopVideo ? "yes" : "no");
        lastAppliedLoopVideo = LoopVideo;
    }

    public void PushAudioVolumeToMpvNow()
    {
        RunOnMpvSurface(PushAudioVolumeToMpvNowCore);
    }

    private void PushAudioVolumeToMpvNowCore()
    {
        if (mpvHandle == IntPtr.Zero)
        {
            return;
        }

        if (!PlayAudio && holdPlaybackForAvStartupSync)
        {
            ReleaseStartupPlaybackHold();
        }

        int volume = Math.Max(0, Math.Min(100, (int)Math.Round(AudioVolume * 100f)));
        _ = MpvCommand("set", "mute", PlayAudio ? "no" : "yes");
        _ = MpvCommand("set", "volume", volume.ToString());
        _ = MpvCommand("set", "video-sync", PlayAudio ? "audio" : "display-vdrop");
        if (PlayAudio)
        {
            _ = MpvCommand("set", "audio-buffer", "0.2");
        }

        lastAppliedVolume = volume;
        lastAppliedPlayAudio = PlayAudio;
    }

    private static bool EnsureLibMpvAvailable(out string error)
    {
        IntPtr module = LoadLibrary("libmpv-2.dll");
        if (module == IntPtr.Zero)
        {
            string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libmpv-2.dll");
            error = "libmpv initialization failed: could not load libmpv-2.dll." + Environment.NewLine +
                "Expected in app directory or PATH." + Environment.NewLine +
                "Checked app path: " + localPath;
            return false;
        }

        _ = FreeLibrary(module);
        error = null;
        return true;
    }

    private void ApplyMpvRuntimeOptions()
    {
        if (mpvHandle == IntPtr.Zero)
        {
            return;
        }

        if (!PlayAudio && holdPlaybackForAvStartupSync)
        {
            ReleaseStartupPlaybackHold();
        }

        int volume = Math.Max(0, Math.Min(100, (int)Math.Round(AudioVolume * 100f)));
        float panX = Math.Max(-1f, Math.Min(1f, VideoPanX));
        float panY = Math.Max(-1f, Math.Min(1f, VideoPanY));
        float zoomMpv = Math.Max(0f, Math.Min(1f, VideoZoomExtra)) * 2f;
        float blurScale = Math.Max(0f, Math.Min(1f, VideoBlurScale));
        float blurDegrees = Math.Max(0f, Math.Min(360f, VideoBlurDegrees));
        bool needsApply = !appliedMpvRuntimeOptions
            || lastAppliedLoopVideo != LoopVideo
            || lastAppliedPlayAudio != PlayAudio
            || lastAppliedVolume != volume
            || lastAppliedHardwareDecoding != UseHardwareDecoding
            || float.IsNaN(lastAppliedVideoPanX) || Math.Abs(lastAppliedVideoPanX - panX) > 0.0005f
            || float.IsNaN(lastAppliedVideoPanY) || Math.Abs(lastAppliedVideoPanY - panY) > 0.0005f
            || float.IsNaN(lastAppliedVideoZoom) || Math.Abs(lastAppliedVideoZoom - zoomMpv) > 0.0005f
            || float.IsNaN(lastAppliedVideoBlurScale) || Math.Abs(lastAppliedVideoBlurScale - blurScale) > 0.0005f
            || float.IsNaN(lastAppliedVideoBlurDegrees) || Math.Abs(lastAppliedVideoBlurDegrees - blurDegrees) > 0.0005f
            || lastAppliedVideoBlurType != VideoBlurType;

        if (!needsApply)
        {
            return;
        }

        _ = MpvCommand("set", "mute", PlayAudio ? "no" : "yes");
        _ = MpvCommand("set", "volume", volume.ToString());
        // When layout audio is off, avoid mastering display time on an empty/muted audio clock (can stall
        // high-bitrate or long MP4 more than a lightweight export). With audio on, sync to audio as usual.
        _ = MpvCommand("set", "video-sync", PlayAudio ? "audio" : "display-vdrop");
        if (PlayAudio)
        {
            _ = MpvCommand("set", "audio-buffer", "0.2");
        }

        _ = MpvCommand("set", "loop-file", LoopVideo ? "yes" : "no");
        _ = MpvCommand("set", "hwdec", UseHardwareDecoding ? MpvHwdecWhenEnabled : "no");
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        _ = MpvCommand("set", "video-pan-x", panX.ToString(inv));
        _ = MpvCommand("set", "video-pan-y", panY.ToString(inv));
        _ = MpvCommand("set", "video-zoom", zoomMpv.ToString(inv));
        ApplyMpvVideoFilters(lastAppliedScaleHeight);

        appliedMpvRuntimeOptions = true;
        lastAppliedLoopVideo = LoopVideo;
        lastAppliedPlayAudio = PlayAudio;
        lastAppliedVolume = volume;
        lastAppliedHardwareDecoding = UseHardwareDecoding;
        lastAppliedVideoPanX = panX;
        lastAppliedVideoPanY = panY;
        lastAppliedVideoZoom = zoomMpv;
        lastAppliedVideoBlurScale = blurScale;
        lastAppliedVideoBlurDegrees = blurDegrees;
        lastAppliedVideoBlurType = VideoBlurType;
    }

    private int MpvCommand(params string[] args)
    {
        if (mpvHandle == IntPtr.Zero)
        {
            return -1;
        }

        IntPtr argvPtr = IntPtr.Zero;
        IntPtr[] allocatedStrings = new IntPtr[args.Length + 1];
        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                allocatedStrings[i] = Marshal.StringToHGlobalAnsi(args[i]);
            }

            allocatedStrings[args.Length] = IntPtr.Zero;
            int size = IntPtr.Size * allocatedStrings.Length;
            argvPtr = Marshal.AllocHGlobal(size);
            for (int i = 0; i < allocatedStrings.Length; i++)
            {
                Marshal.WriteIntPtr(argvPtr, i * IntPtr.Size, allocatedStrings[i]);
            }

            return mpv_command(mpvHandle, argvPtr);
        }
        catch
        {
            return -1;
        }
        finally
        {
            if (argvPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(argvPtr);
            }

            for (int i = 0; i < allocatedStrings.Length; i++)
            {
                if (allocatedStrings[i] != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(allocatedStrings[i]);
                }
            }
        }
    }

    private bool TryInitializeMpvRenderContext()
    {
        try
        {
            if (!EnsureMpvGlControl())
            {
                return false;
            }

            if (mpvGraphicsContext == null)
            {
                LastError = "libmpv render API init failed: OpenGL context is null.";
                return false;
            }

            mpvGraphicsContext.MakeCurrent(mpvNativeWindow.WindowInfo);

            byte[] apiTypeBytes = Encoding.ASCII.GetBytes("opengl\0");
            mpvApiTypeHandle = GCHandle.Alloc(apiTypeBytes, GCHandleType.Pinned);
            mpvApiTypeHandleAllocated = true;

            var getProcDelegate = new mpv_opengl_init_params_get_proc_address_fn(GetOpenGlProcAddress);
            IntPtr getProcPtr = Marshal.GetFunctionPointerForDelegate(getProcDelegate);
            IntPtr initParamsPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(mpv_opengl_init_params)));
            IntPtr paramArrayPtr = IntPtr.Zero;
            try
            {
                var initParams = new mpv_opengl_init_params
                {
                    get_proc_address = getProcPtr,
                    get_proc_address_ctx = IntPtr.Zero,
                };
                Marshal.StructureToPtr(initParams, initParamsPtr, false);

                var renderParams = new mpv_render_param[3];
                renderParams[0] = new mpv_render_param
                {
                    type = MPV_RENDER_PARAM_API_TYPE,
                    data = mpvApiTypeHandle.AddrOfPinnedObject()
                };
                renderParams[1] = new mpv_render_param
                {
                    type = MPV_RENDER_PARAM_OPENGL_INIT_PARAMS,
                    data = initParamsPtr
                };
                renderParams[2] = new mpv_render_param
                {
                    type = MPV_RENDER_PARAM_INVALID,
                    data = IntPtr.Zero
                };

                int size = Marshal.SizeOf(typeof(mpv_render_param));
                paramArrayPtr = Marshal.AllocHGlobal(size * renderParams.Length);
                for (int i = 0; i < renderParams.Length; i++)
                {
                    Marshal.StructureToPtr(renderParams[i], IntPtr.Add(paramArrayPtr, i * size), false);
                }

                int result = mpv_render_context_create(out mpvRenderContext, mpvHandle, paramArrayPtr);
                if (result < 0 || mpvRenderContext == IntPtr.Zero)
                {
                    LastError = "libmpv render context creation failed.";
                    return false;
                }
            }
            finally
            {
                if (paramArrayPtr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(paramArrayPtr);
                }

                Marshal.FreeHGlobal(initParamsPtr);
            }

            return true;
        }
        catch (Exception ex)
        {
            LastError = "libmpv render API init failed: " + ex;
            return false;
        }
    }

    private bool EnsureMpvGlControl()
    {
        if (mpvGraphicsContext == null || mpvNativeWindow == null)
        {
            LastError = "libmpv render API init failed: worker OpenGL context is unavailable.";
            return false;
        }

        try
        {
            mpvGraphicsContext.MakeCurrent(mpvNativeWindow.WindowInfo);
            return true;
        }
        catch (Exception ex)
        {
            LastError = "libmpv render API init failed: failed to activate OpenGL context. " + ex.Message;
            return false;
        }
    }

    private void ReleaseStartupPlaybackHold()
    {
        if (!holdPlaybackForAvStartupSync)
        {
            return;
        }

        holdPlaybackForAvStartupSync = false;
        startupPlaybackHoldRenderAttempts = 0;
        if (mpvHandle != IntPtr.Zero)
        {
            _ = MpvCommand("set", "pause", "no");
        }
    }

    private int GetAdaptiveReadbackMaxPixels()
    {
        int basePixels;
        if (presentTargetHz >= 55)
        {
            basePixels = MpvReadbackMaxPixelsHighFps;
        }
        else if (presentTargetHz >= 40)
        {
            basePixels = MpvReadbackMaxPixelsBalanced;
        }
        else
        {
            basePixels = MpvReadbackMaxPixelsQuality;
        }

        float blurCost = 1f;
        if (hostForm is TimerForm timer
            && timer.Layout?.Settings is { } ls
            && ls.BackgroundType == BackgroundType.Video)
        {
            float blurStrength = Math.Max(0f, Math.Min(1f, ls.VideoBlurScale));
            if (blurStrength > 0.02f)
            {
                // Video blur runs in libavfilter (almost always CPU) at decoded frame size — far more
                // expensive than GL readback alone. Tighten readback budget so the worker can keep up;
                // blur itself is additionally capped in <see cref="BuildVideoFilterChain"/>.
                float t = blurStrength * blurStrength;
                blurCost = 1f - 0.55f * blurStrength - 0.35f * t;
            }
        }

        int pixels = (int)(basePixels * ClampDynamicReadbackScale(dynamicReadbackScale) * blurCost);
        int overridePixels = Math.Max(0, MaxReadbackPixelsOverride);
        if (overridePixels > 0)
        {
            pixels = Math.Min(pixels, overridePixels);
        }

        return Math.Max(45_000, pixels);
    }

    private void ComputeInternalReadbackSize(int viewWidth, int viewHeight, out int rw, out int rh)
    {
        rw = 0;
        rh = 0;
        if (viewWidth <= 0 || viewHeight <= 0)
        {
            return;
        }

        int maxPixels = GetAdaptiveReadbackMaxPixels();
        long area = (long)viewWidth * viewHeight;
        if (area <= maxPixels)
        {
            rw = viewWidth;
            rh = viewHeight;
            return;
        }

        double scale = Math.Sqrt(maxPixels / (double)area);
        rw = Math.Max(1, (int)(viewWidth * scale));
        rh = Math.Max(1, (int)(viewHeight * scale));
    }

    private void EnsureMpvRenderScratchNative()
    {
        if (mpvRenderScratchNative != IntPtr.Zero)
        {
            return;
        }

        int fboSize = Marshal.SizeOf(typeof(mpv_opengl_fbo));
        mpvRenderScratchParamSize = Marshal.SizeOf(typeof(mpv_render_param));
        mpvRenderScratchFlipOffset = (fboSize + 3) & ~3;
        mpvRenderScratchBlockOffset = mpvRenderScratchFlipOffset + 4;
        int afterBlock = mpvRenderScratchBlockOffset + 4;
        mpvRenderScratchParamsOffset = (afterBlock + 7) & ~7;
        int total = mpvRenderScratchParamsOffset + 4 * mpvRenderScratchParamSize;
        mpvRenderScratchNative = Marshal.AllocHGlobal(Math.Max(256, total));
    }

    private void ReleaseMpvRenderScratchNative()
    {
        if (mpvRenderScratchNative != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(mpvRenderScratchNative);
            mpvRenderScratchNative = IntPtr.Zero;
        }
    }

    /// <summary>OpenGL render + readback only (no GDI+ blit). Must run on the UI thread.</summary>
    /// <param name="didReadback">True when mpv drew new pixels and we ran <c>ReadPixels</c> (not a no-op skip).</param>
    private bool TryUpdateMpvVideoTexture(int width, int height, out bool didReadback)
    {
        didReadback = false;
        if (mpvRenderContext == IntPtr.Zero || mpvGraphicsContext == null || mpvNativeWindow == null || width <= 0 || height <= 0)
        {
            return false;
        }

        ComputeInternalReadbackSize(width, height, out int rw, out int rh);
        if (rw <= 0 || rh <= 0)
        {
            return false;
        }

        lastInternalReadbackWidth = rw;
        lastInternalReadbackHeight = rh;

        try
        {
            if (mpvNativeWindow.Width != rw || mpvNativeWindow.Height != rh)
            {
                mpvNativeWindow.Width = rw;
                mpvNativeWindow.Height = rh;
            }
            mpvNativeWindow.ProcessEvents();

            ApplyMpvViewportScaling(rh);
            TryApplyMpvScalerForDecodedVideoSize();
            mpvGraphicsContext.MakeCurrent(mpvNativeWindow.WindowInfo);

            // Render/readback only when mpv reports a new frame/update.
            ulong updateFlags = mpv_render_context_update(mpvRenderContext);
            if ((updateFlags & MPV_RENDER_UPDATE_FRAME) == 0)
            {
                return true;
            }

            EnsureMpvRenderScratchNative();

            var fbo = new mpv_opengl_fbo
            {
                fbo = 0,
                w = rw,
                h = rh,
                internal_format = (int)All.Rgba8
            };

            Marshal.StructureToPtr(fbo, mpvRenderScratchNative, false);
            IntPtr flipPtr = IntPtr.Add(mpvRenderScratchNative, mpvRenderScratchFlipOffset);
            IntPtr blockPtr = IntPtr.Add(mpvRenderScratchNative, mpvRenderScratchBlockOffset);
            Marshal.WriteInt32(flipPtr, 0);
            // Never block on target presentation time in the readback worker: it caps observed Hz (~15–20)
            // with some drivers/files. A/V alignment is handled via mpv video-sync / audio-buffer instead.
            Marshal.WriteInt32(blockPtr, 0);

            IntPtr paramArrayPtr = IntPtr.Add(mpvRenderScratchNative, mpvRenderScratchParamsOffset);
            int psz = mpvRenderScratchParamSize;
            var rp0 = new mpv_render_param { type = MPV_RENDER_PARAM_OPENGL_FBO, data = mpvRenderScratchNative };
            var rp1 = new mpv_render_param { type = MPV_RENDER_PARAM_FLIP_Y, data = flipPtr };
            var rp2 = new mpv_render_param { type = MPV_RENDER_PARAM_BLOCK_FOR_TARGET_TIME, data = blockPtr };
            var rp3 = new mpv_render_param { type = MPV_RENDER_PARAM_INVALID, data = IntPtr.Zero };
            Marshal.StructureToPtr(rp0, paramArrayPtr, false);
            Marshal.StructureToPtr(rp1, IntPtr.Add(paramArrayPtr, psz), false);
            Marshal.StructureToPtr(rp2, IntPtr.Add(paramArrayPtr, 2 * psz), false);
            Marshal.StructureToPtr(rp3, IntPtr.Add(paramArrayPtr, 3 * psz), false);

            GL.Viewport(0, 0, rw, rh);
            mpv_render_context_render(mpvRenderContext, paramArrayPtr);
            GL.ReadBuffer(ReadBufferMode.Back);
            // Flush only: Finish() forces a full GPU drain every frame and often caps Paint FPS well below 60.
            // ReadPixels still blocks until the framebuffer is ready.
            GL.Flush();
            lock (mpvFrameBitmapSync)
            {
                EnsureMpvFrameBitmap(rw, rh);
                if (mpvFrameBitmapFront == null || mpvFrameBitmapBack == null)
                {
                    return false;
                }

                ReadMpvFrameBitmap(rw, rh);
            }

            didReadback = true;
            ReleaseStartupPlaybackHold();

            return true;
        }
        catch (Exception ex)
        {
            LastError = "libmpv render failed: " + ex.Message;
            return false;
        }
    }

    private void ApplyMpvViewportScaling(int targetHeight)
    {
        if (mpvHandle == IntPtr.Zero || targetHeight <= 0)
        {
            return;
        }

        ApplyMpvVideoFilters(targetHeight);
    }

    private void ApplyMpvVideoFilters(int targetHeight)
    {
        if (mpvHandle == IntPtr.Zero)
        {
            return;
        }

        string videoFilter = BuildVideoFilterChain(targetHeight, VideoBlurScale, VideoBlurType, VideoBlurDegrees);
        if (targetHeight == lastAppliedScaleHeight
            && string.Equals(videoFilter, lastAppliedVideoFilter, StringComparison.Ordinal))
        {
            return;
        }

        long nowTicks = Stopwatch.GetTimestamp();
        if (nextVideoFilterApplyTicks > 0 && nowTicks < nextVideoFilterApplyTicks)
        {
            return;
        }

        int result = MpvCommand("set", "vf", videoFilter);
        nextVideoFilterApplyTicks = nowTicks + (Stopwatch.Frequency * MpvVideoFilterLiveApplyMinMs / 1000);
        if (result < 0)
        {
            LastError = "mpv rejected video filter: " + videoFilter;
            return;
        }

        lastAppliedScaleHeight = targetHeight;
        lastAppliedVideoFilter = videoFilter;
    }

    /// <summary>
    /// Max frame height at which libavfilter blur runs (then mpv scales to the render target).
    /// Blur cost grows roughly with area; capping this is the largest practical win while keeping blur on.
    /// </summary>
    private const int MpvBlurPassMaxHeightGaussian = 432;

    private const int MpvBlurPassMaxHeightBoxDirectional = 432;
    private const int MpvBlurPassMaxHeightRotational = 320;

    private static int GetBlurPassHeight(int targetHeight, int maxPassHeight)
    {
        int cap = Math.Max(200, Math.Min(720, maxPassHeight));
        if (targetHeight <= 0)
        {
            return cap;
        }

        // When internal readback is already small, avoid an extra upscale/downscale hop larger than needed.
        return Math.Min(cap, Math.Max(200, targetHeight));
    }

    private static string BuildLavfiPrefixedBlurGraph(int blurPassHeight, string innerCommaSeparatedFilters)
    {
        int h = Math.Max(160, Math.Min(720, blurPassHeight));
        return "lavfi=[scale=-2:"
            + h.ToString(CultureInfo.InvariantCulture)
            + ":flags=bilinear,"
            + innerCommaSeparatedFilters
            + "]";
    }

    private static string BuildVideoFilterChain(int targetHeight, float blurScale, BackgroundVideoBlurType blurType, float blurDegrees)
    {
        var filters = new List<string>();

        blurScale = Math.Max(0f, Math.Min(1f, blurScale));
        if (blurScale > 0.0005f)
        {
            switch (blurType)
            {
                case BackgroundVideoBlurType.Directional:
                {
                    int bh = GetBlurPassHeight(targetHeight, MpvBlurPassMaxHeightBoxDirectional);
                    // dblur can hard-crash inside mpv on this path. Use the same stable blur family as Gaussian/Box.
                    float directionalRadius = Math.Max(0.5f, blurScale * 16f);
                    string r = directionalRadius.ToString("0.###", CultureInfo.InvariantCulture);
                    string inner = "format=gbrp,boxblur=lr="
                        + r
                        + ":lp=2:cr="
                        + r
                        + ":cp=2";
                    filters.Add(BuildLavfiPrefixedBlurGraph(bh, inner));
                    break;
                }
                case BackgroundVideoBlurType.Box:
                {
                    int bh = GetBlurPassHeight(targetHeight, MpvBlurPassMaxHeightBoxDirectional);
                    float boxRadius = Math.Max(1f, blurScale * 20f);
                    string boxR = boxRadius.ToString("0.###", CultureInfo.InvariantCulture);
                    // YUV-style luma/chroma radii on RGB-ish input causes split green/flat corruption; convert to planar RGB first (same as LibMpv path).
                    string inner = "format=gbrp,boxblur=lr="
                        + boxR
                        + ":lp=1:cr="
                        + boxR
                        + ":cp=1";
                    filters.Add(BuildLavfiPrefixedBlurGraph(bh, inner));
                    break;
                }
                case BackgroundVideoBlurType.Rotational:
                    filters.Add(BuildRotationalBlurFilter(
                        blurScale,
                        Math.Max(0f, Math.Min(360f, blurDegrees)),
                        GetBlurPassHeight(targetHeight, MpvBlurPassMaxHeightRotational)));
                    break;
                case BackgroundVideoBlurType.Gaussian:
                default:
                {
                    int bh = GetBlurPassHeight(targetHeight, MpvBlurPassMaxHeightGaussian);
                    // libavfilter's gblur path can hard-crash inside mpv on some Windows builds while
                    // filters are changed live. A multi-pass box blur is stable and close enough visually.
                    float radius = Math.Max(0.5f, blurScale * 14f);
                    string r = radius.ToString("0.###", CultureInfo.InvariantCulture);
                    string inner = "format=gbrp,boxblur=lr="
                        + r
                        + ":lp=3:cr="
                        + r
                        + ":cp=3";
                    filters.Add(BuildLavfiPrefixedBlurGraph(bh, inner));
                    break;
                }
            }
        }

        return string.Join(",", filters);
    }

    private static string BuildRotationalBlurFilter(float blurScale, float blurDegrees, int blurPassHeight)
    {
        float spreadDegrees = Math.Max(0.1f, blurScale * Math.Max(1f, blurDegrees));
        double spreadRadians = spreadDegrees * Math.PI / 180.0;
        string r1 = spreadRadians.ToString("0.######", CultureInfo.InvariantCulture);
        string r2 = (spreadRadians * 0.5).ToString("0.######", CultureInfo.InvariantCulture);
        int h = Math.Max(160, Math.Min(480, blurPassHeight));
        string pre = "scale=-2:" + h.ToString(CultureInfo.InvariantCulture) + ":flags=bilinear,";
        return "lavfi=["
            + pre
            + "split=5[o][a][b][c][d];"
            + "[a]rotate=" + r1 + ":ow=iw:oh=ih:c=black@0[a1];"
            + "[b]rotate=-" + r1 + ":ow=iw:oh=ih:c=black@0[b1];"
            + "[c]rotate=" + r2 + ":ow=iw:oh=ih:c=black@0[c1];"
            + "[d]rotate=-" + r2 + ":ow=iw:oh=ih:c=black@0[d1];"
            + "[o][a1]blend=all_mode=average[t1];"
            + "[t1][b1]blend=all_mode=average[t2];"
            + "[t2][c1]blend=all_mode=average[t3];"
            + "[t3][d1]blend=all_mode=average]";
    }

    private void EnsureMpvFrameBitmap(int width, int height)
    {
        if (mpvFrameBitmapFront != null
            && mpvFrameBitmapBack != null
            && mpvFrameBitmapFront.Width == width
            && mpvFrameBitmapFront.Height == height
            && mpvFrameBitmapBack.Width == width
            && mpvFrameBitmapBack.Height == height)
        {
            return;
        }

        mpvFrameBitmapFront?.Dispose();
        mpvFrameBitmapBack?.Dispose();
        mpvFrameBitmapFront = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        mpvFrameBitmapBack = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    }

    private void ReadMpvFrameBitmap(int width, int height)
    {
        if (mpvFrameBitmapBack == null)
        {
            return;
        }

        var rect = new Rectangle(0, 0, width, height);
        BitmapData data = mpvFrameBitmapBack.LockBits(rect, ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            GL.ReadPixels(0, 0, width, height, OpenTK.Graphics.OpenGL.PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0);
        }
        finally
        {
            mpvFrameBitmapBack.UnlockBits(data);
        }

        Bitmap previousFront = mpvFrameBitmapFront;
        mpvFrameBitmapFront = mpvFrameBitmapBack;
        mpvFrameBitmapBack = previousFront;
    }

    private void DrawMpvFrame(Graphics graphics, int viewWidth, int viewHeight)
    {
        if (mpvFrameBitmapFront == null || viewWidth <= 0 || viewHeight <= 0)
        {
            return;
        }

        // Caller must hold mpvFrameBitmapSync when invoking from the UI paint path.
        if (TryDrawMpvFrameWithGdi(graphics, viewWidth, viewHeight))
        {
            return;
        }

        var dest = new Rectangle(0, 0, viewWidth, viewHeight);
        InterpolationMode savedInterpolation = graphics.InterpolationMode;
        CompositingQuality savedCompositingQuality = graphics.CompositingQuality;
        try
        {
            // Upscale from readback buffer: nearest for retro line counts; plain bilinear keeps UI paint cheap.
            graphics.InterpolationMode = readbackBlitUsesNearestNeighbor
                ? InterpolationMode.NearestNeighbor
                : InterpolationMode.Bilinear;
            graphics.CompositingQuality = CompositingQuality.HighSpeed;
            if (renderOpacity >= 0.999f)
            {
                string scaleMode = readbackBlitUsesNearestNeighbor ? "nearest" : "bilinear";
                lastFrameBlitPath = lastFrameBlitPath.StartsWith("gdi dib fail", StringComparison.Ordinal)
                    ? lastFrameBlitPath + " -> gdi+ " + scaleMode
                    : "gdi+ " + scaleMode;
                graphics.DrawImage(mpvFrameBitmapFront, dest);
                return;
            }

            var matrix = new ColorMatrix
            {
                Matrix00 = renderOpacity,
                Matrix11 = renderOpacity,
                Matrix22 = renderOpacity,
                Matrix33 = 1f
            };
            using var attributes = new ImageAttributes();
            attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
            string matrixScaleMode = readbackBlitUsesNearestNeighbor ? "nearest" : "bilinear";
            lastFrameBlitPath = lastFrameBlitPath.StartsWith("gdi dib fail", StringComparison.Ordinal)
                ? lastFrameBlitPath + " -> gdi+ " + matrixScaleMode + " matrix"
                : "gdi+ " + matrixScaleMode + " matrix";
            graphics.DrawImage(
                mpvFrameBitmapFront,
                dest,
                0,
                0,
                mpvFrameBitmapFront.Width,
                mpvFrameBitmapFront.Height,
                GraphicsUnit.Pixel,
                attributes);
        }
        finally
        {
            graphics.InterpolationMode = savedInterpolation;
            graphics.CompositingQuality = savedCompositingQuality;
        }
    }

    private bool TryDrawMpvFrameWithGdi(Graphics graphics, int viewWidth, int viewHeight)
    {
        if (mpvFrameBitmapFront == null || graphics == null)
        {
            return false;
        }

        IntPtr destDc = IntPtr.Zero;
        BitmapData data = null;
        bool drawn = false;
        try
        {
            int sourceWidth = mpvFrameBitmapFront.Width;
            int sourceHeight = mpvFrameBitmapFront.Height;
            var sourceRect = new Rectangle(0, 0, sourceWidth, sourceHeight);
            data = mpvFrameBitmapFront.LockBits(
                sourceRect,
                ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            IntPtr bits = data.Scan0;
            int dibHeight = -sourceHeight;
            if (data.Stride < 0)
            {
                bits = IntPtr.Add(data.Scan0, data.Stride * (sourceHeight - 1));
                dibHeight = sourceHeight;
            }

            var bitmapInfo = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER)),
                    biWidth = sourceWidth,
                    biHeight = dibHeight,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = BI_RGB,
                    biSizeImage = (uint)Math.Abs(data.Stride * sourceHeight)
                }
            };

            bool smoothScale = !readbackBlitUsesNearestNeighbor;
            destDc = graphics.GetHdc();
            _ = SetStretchBltMode(destDc, smoothScale ? HALFTONE : COLORONCOLOR);
            if (smoothScale)
            {
                _ = SetBrushOrgEx(destDc, 0, 0, out _);
            }

            int result = StretchDIBits(
                destDc,
                0,
                0,
                viewWidth,
                viewHeight,
                0,
                0,
                sourceWidth,
                sourceHeight,
                bits,
                ref bitmapInfo,
                DIB_RGB_COLORS,
                SRCCOPY);
            drawn = result != 0 && result != GDI_ERROR;
            if (!drawn)
            {
                lastFrameBlitPath = $"gdi dib fail r={result} err={Marshal.GetLastWin32Error()}";
            }
        }
        catch (Exception ex)
        {
            lastFrameBlitPath = "gdi dib fail " + ex.GetType().Name;
            return false;
        }
        finally
        {
            if (destDc != IntPtr.Zero)
            {
                graphics.ReleaseHdc(destDc);
            }

            if (data != null && mpvFrameBitmapFront != null)
            {
                mpvFrameBitmapFront.UnlockBits(data);
            }
        }

        if (!drawn)
        {
            return false;
        }

        if (renderOpacity < 0.999f)
        {
            int shadeAlpha = Math.Max(0, Math.Min(255, (int)Math.Round((1f - renderOpacity) * 255f)));
            if (shadeAlpha > 0)
            {
                using var shade = new SolidBrush(Color.FromArgb(shadeAlpha, Color.Black));
                graphics.FillRectangle(shade, 0, 0, viewWidth, viewHeight);
            }
        }

        string scaleMode = readbackBlitUsesNearestNeighbor ? "nearest" : "halftone";
        lastFrameBlitPath = renderOpacity < 0.999f
            ? $"gdi dib {scaleMode}+shade"
            : $"gdi dib {scaleMode}";
        return true;
    }

    private static IntPtr GetOpenGlProcAddress(IntPtr _, string name)
    {
        IntPtr addr = wglGetProcAddress(name);
        if (addr != IntPtr.Zero)
        {
            return addr;
        }

        IntPtr module = GetModuleHandle("opengl32.dll");
        if (module == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        return GetProcAddress(module, name);
    }

    private const int COLORONCOLOR = 3;
    private const int HALFTONE = 4;
    private const int SRCCOPY = 0x00CC0020;
    private const int GDI_ERROR = -1;
    private const uint BI_RGB = 0;
    private const uint DIB_RGB_COLORS = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr mpv_opengl_init_params_get_proc_address_fn(IntPtr ctx, [MarshalAs(UnmanagedType.LPStr)] string name);

    [StructLayout(LayoutKind.Sequential)]
    private struct mpv_opengl_init_params
    {
        public IntPtr get_proc_address;
        public IntPtr get_proc_address_ctx;
        public IntPtr extra_exts;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct mpv_opengl_fbo
    {
        public int fbo;
        public int w;
        public int h;
        public int internal_format;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct mpv_render_param
    {
        public int type;
        public IntPtr data;
    }

    private const int MPV_RENDER_PARAM_INVALID = 0;
    private const int MPV_RENDER_PARAM_API_TYPE = 1;
    private const int MPV_RENDER_PARAM_OPENGL_INIT_PARAMS = 2;
    private const int MPV_RENDER_PARAM_OPENGL_FBO = 3;
    private const int MPV_RENDER_PARAM_FLIP_Y = 4;
    private const int MPV_RENDER_PARAM_BLOCK_FOR_TARGET_TIME = 12;
    private const ulong MPV_RENDER_UPDATE_FRAME = 1;
    private const int MPV_FORMAT_STRING = 1;
    private const int MPV_FORMAT_INT64 = 4;
    private const int MPV_FORMAT_DOUBLE = 5;

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_render_context_create(out IntPtr res, IntPtr mpv, IntPtr paramsPtr);

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void mpv_render_context_free(IntPtr ctx);

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong mpv_render_context_update(IntPtr ctx);

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void mpv_render_context_render(IntPtr ctx, IntPtr paramsPtr);

    [DllImport("opengl32.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private static extern IntPtr wglGetProcAddress(string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int SetStretchBltMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetBrushOrgEx(IntPtr hdc, int x, int y, out POINT lppt);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int StretchDIBits(
        IntPtr hdcDest,
        int xoriginDest,
        int yoriginDest,
        int wDest,
        int hDest,
        int xoriginSrc,
        int yoriginSrc,
        int wSrc,
        int hSrc,
        IntPtr lpBits,
        ref BITMAPINFO lpbmi,
        uint iUsage,
        int rop);

    private static Rectangle CalculateCoverDestinationRect(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || targetWidth <= 0 || targetHeight <= 0)
        {
            return Rectangle.Empty;
        }

        float scale = Math.Max(targetWidth / (float)sourceWidth, targetHeight / (float)sourceHeight);
        int drawWidth = (int)Math.Ceiling(sourceWidth * scale);
        int drawHeight = (int)Math.Ceiling(sourceHeight * scale);
        int x = (targetWidth - drawWidth) / 2;
        int y = (targetHeight - drawHeight) / 2;
        return new Rectangle(x, y, drawWidth, drawHeight);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        dedicatedPresentEnabled = false;
        IsLoaded = false;
        IsInitialized = false;
        loadedSource = null;
        TargetPlaybackFps = 30;

        mpvWorkerLoopRunning = false;
        mpvWorkerWakeEvent.Set();
        if (timerResolutionRaised)
        {
            _ = timeEndPeriod(1);
            timerResolutionRaised = false;
        }
        mpvPresentTimer?.Stop();
        mpvPresentTimer?.Dispose();
        mpvPresentTimer = null;

        try
        {
            mpvWorkerThread?.Join(15000);
        }
        catch
        {
        }

        mpvWorkerThread = null;
        mpvNativeWindow = null;
        mpvGraphicsContext = null;
        LastError = null;
        try
        {
            mpvWorkerGlReady.Dispose();
        }
        catch
        {
        }
    }

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mpv_create();

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_initialize(IntPtr ctx);

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_set_option_string(IntPtr ctx, string name, string value);

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_command(IntPtr ctx, IntPtr args);

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_get_property(IntPtr ctx, string name, int format, ref double data);

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_get_property(IntPtr ctx, string name, int format, ref long data);

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_get_property")]
    private static extern int mpv_get_property_string(IntPtr ctx, string name, int format, ref IntPtr data);

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void mpv_free(IntPtr data);

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void mpv_terminate_destroy(IntPtr ctx);

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint timeEndPeriod(uint uPeriod);
}
