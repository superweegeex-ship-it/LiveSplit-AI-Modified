using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;

using LiveSplit.Localization;
using LiveSplit.Model;
using LiveSplit.Options;
using LiveSplit.TimeFormatters;
using LiveSplit.UI;
using LiveSplit.UI.Components;
using LiveSplit.Web.Share;

using SpeedrunComSharp;

namespace LiveSplit.WorldRecord.UI.Components;

[GlobalFontConsumer(GlobalFont.TextFont)]
public class WorldRecordComponent : IComponent
{
    private static string T(string source) => UiLocalizer.Translate(source, LanguageResolver.ResolveCurrentCultureLanguage());

    protected InfoTextComponent InternalComponent { get; set; }

    protected WorldRecordSettings Settings { get; set; }

    private GraphicsCache Cache { get; set; }
    private GeneralTimeFormatter WRTimeFormatter { get; set; }
    private GeneralTimeFormatter PBTimeFormatter { get; set; }
    private LiveSplitState State { get; set; }
    private TimeStamp LastUpdate { get; set; }
    private TimeSpan RefreshInterval { get; set; }
    private TimeSpan FailureRetryInterval { get; set; }
    private TimeStamp LastAttempt { get; set; }
    private bool LastFetchSucceeded { get; set; }
    private bool PendingUiRefresh { get; set; }
    private readonly object refreshSync = new();
    public Record WorldRecord { get; protected set; }
    public ReadOnlyCollection<Record> AllTies { get; protected set; }
    private bool IsLoading { get; set; }
    private SpeedrunComClient Client { get; set; }

    private Image OldGameIcon { get; set; }
    private Image GameIconShadow { get; set; }
    private Image GameIconShadowSource { get; set; }
    private Color _lastGameIconShadowTint;
    private float _lastGameIconShadowOffset = float.NaN;
    private float _lastGameIconShadowTransparency = float.NaN;
    private float _lastGameIconShadowBlur = float.NaN;

    public string ComponentName => T("World Record");

    public float PaddingTop => InternalComponent.PaddingTop;
    public float PaddingLeft => InternalComponent.PaddingLeft;
    public float PaddingBottom => InternalComponent.PaddingBottom;
    public float PaddingRight => InternalComponent.PaddingRight;

    public float VerticalHeight => InternalComponent.VerticalHeight;
    public float MinimumWidth => InternalComponent.MinimumWidth;

    public float HorizontalWidth => InternalComponent.HorizontalWidth;
    public float MinimumHeight => InternalComponent.MinimumHeight;

    public IDictionary<string, Action> ContextMenuControls => null;

    public WorldRecordComponent(LiveSplitState state)
    {
        State = state;

        // Keep a modest in-memory cache so repeated WR lookups don't always hit the network.
        Client = new SpeedrunComClient(userAgent: Updates.UpdateHelper.UserAgent, maxCacheElements: 256);

        RefreshInterval = TimeSpan.FromMinutes(5);
        FailureRetryInterval = TimeSpan.FromSeconds(20);
        Cache = new GraphicsCache();
        WRTimeFormatter = new AutomaticPrecisionTimeFormatter
        {
            Accuracy = TimeAccuracy.Milliseconds
        };
        PBTimeFormatter = new RegularTimeFormatter();
        InternalComponent = new InfoTextComponent(T("World Record"), TimeFormatConstants.DASH);
        Settings = new WorldRecordSettings()
        {
            CurrentState = state
        };
    }

    public void Dispose()
    {
        ClearGameIconShadow();
    }

    /// <summary>
    /// Stable cache key for subcategory / variable filters. <see cref="Dictionary{TKey,TValue}.Values"/> order is undefined,
    /// so joining values alone caused <see cref="GraphicsCache.HasChanged"/> every frame and wiped fetched WR data before it could paint.
    /// </summary>
    private static string BuildVariablesFilterFingerprint(RunMetadata metadata)
    {
        if (metadata?.VariableValueNames == null || metadata.VariableValueNames.Count == 0)
        {
            return null;
        }

        return string.Join(
            "|",
            metadata.VariableValueNames.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value));
    }

    private static string NormalizeEmptyToNull(string value) => string.IsNullOrEmpty(value) ? null : value;

    private void RefreshWorldRecord()
    {
        lock (refreshSync)
        {
            LastAttempt = TimeStamp.Now;
        }
        bool fetchSucceeded = false;

        Record fetchedWorldRecord = null;
        ReadOnlyCollection<Record> fetchedAllTies = null;

        try
        {
            if (State != null && State.Run != null
                && State.Run.Metadata.Game != null && State.Run.Metadata.Category != null)
            {
                IEnumerable<VariableValue> variableFilter = null;
                if (Settings.FilterVariables || Settings.FilterSubcategories)
                {
                    variableFilter = State.Run.Metadata.VariableValues.Values.Where(value =>
                    {
                        if (value == null)
                        {
                            return false;
                        }

                        if (value.Variable.IsSubcategory)
                        {
                            return Settings.FilterSubcategories;
                        }

                        return Settings.FilterVariables;
                    });
                }

                string regionFilter = Settings.FilterRegion && State.Run.Metadata.Region != null ? State.Run.Metadata.Region.ID : null;
                string platformFilter = Settings.FilterPlatform && State.Run.Metadata.Platform != null ? State.Run.Metadata.Platform.ID : null;
                EmulatorsFilter emulatorFilter = EmulatorsFilter.NotSet;
                if (Settings.FilterPlatform)
                {
                    if (State.Run.Metadata.UsesEmulator)
                    {
                        emulatorFilter = EmulatorsFilter.OnlyEmulators;
                    }
                    else
                    {
                        emulatorFilter = EmulatorsFilter.NoEmulators;
                    }
                }

                SpeedrunComSharp.TimingMethod? timingMethodFilter = GetTimingMethodOverride();

                string gameId = State.Run.Metadata.Game.ID;
                string categoryId = State.Run.Metadata.Category.ID;
                Leaderboard leaderboard = Client.Leaderboards.GetLeaderboardForFullGameCategory(gameId, categoryId,
                    top: 1,
                    platformId: platformFilter, regionId: regionFilter,
                    emulatorsFilter: emulatorFilter, variableFilters: variableFilter, orderBy: timingMethodFilter);

                if (leaderboard?.Records?.Any() != true)
                {
                    // Fallback query: when strict filters intermittently yield no record, fetch base category WR.
                    leaderboard = Client.Leaderboards.GetLeaderboardForFullGameCategory(gameId, categoryId, top: 1);
                }

                if (leaderboard != null)
                {
                    fetchedWorldRecord = leaderboard.Records.FirstOrDefault();
                    fetchedAllTies = leaderboard.Records;
                    fetchSucceeded = fetchedWorldRecord != null;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }

        lock (refreshSync)
        {
            WorldRecord = fetchedWorldRecord;
            AllTies = fetchedAllTies;
            if (fetchSucceeded)
            {
                LastUpdate = LastAttempt;
            }
            LastFetchSucceeded = fetchSucceeded;
            IsLoading = false;
            PendingUiRefresh = true;
        }
    }

    private void ShowWorldRecord(LayoutMode mode)
    {
        try
        {
        bool centeredText = Settings.CenteredText && !Settings.Display2Rows && mode == LayoutMode.Vertical;
        if (WorldRecord != null)
        {
            SpeedrunComSharp.TimingMethod? timingMethodOverride = GetTimingMethodOverride();
            TimeSpan? wrTime = GetWorldRecordTime(timingMethodOverride);
            Model.TimingMethod timingMethod = State.CurrentTimingMethod;
            Game game = State.Run.Metadata.Game;
            if (game != null)
            {
                if (timingMethodOverride != null)
                {
                    timingMethod = timingMethodOverride.Value.ToLiveSplitTimingMethod();
                }
                else
                {
                    timingMethod = game.Ruleset.DefaultTimingMethod.ToLiveSplitTimingMethod();
                }
                
                var isMillisecondsPrecision = CheckPrecisionMillis(wrTime);
                if(Settings.WRPrecision == WorldRecordPrecisionType.FromLeaderboard)
                {
                    PBTimeFormatter.AutomaticPrecision = true;
                    WRTimeFormatter.AutomaticPrecision = true;
                    WRTimeFormatter.Accuracy = TimeAccuracy.Milliseconds;
                }
                else
                {
                    PBTimeFormatter.AutomaticPrecision = false;
                    WRTimeFormatter.AutomaticPrecision = false;
                    WRTimeFormatter.Accuracy = isMillisecondsPrecision ? TimeAccuracy.Milliseconds : TimeAccuracy.Seconds;
                }

                // Take precision from the fetched WR to align the PB with the precision available on the leaderboards
                PBTimeFormatter.Accuracy = isMillisecondsPrecision ? TimeAccuracy.Milliseconds : TimeAccuracy.Seconds;
            }

            string formatted = WRTimeFormatter.Format(wrTime);
            bool isLoggedIn = SpeedrunCom.Client.IsAccessTokenValid;
            string userName = string.Empty;
            if (isLoggedIn)
            {
                userName = SpeedrunCom.Client.Profile.Name;
            }

            var tieRecords = AllTies ?? new ReadOnlyCollection<Record>(Array.Empty<Record>());
            string runners = string.Join(", ", tieRecords.Select(t => string.Join(" & ", t.Players.Select(p =>
                isLoggedIn && p.Name == userName ? "me" : p.Name))));
            int tieCount = tieRecords.Count;

            TimeSpan? pbTime = GetPBTime(timingMethod);
            if (IsPBTimeLower(pbTime, wrTime))
            {
                formatted = PBTimeFormatter.Format(pbTime);
                int playerCount = State.Run.Metadata.Category?.Players?.Value ?? 1;
                runners = playerCount > 1 ? "us" : "me";
                tieCount = 1;
            }

            SetRecordText(formatted, runners, tieCount, centeredText);
        }
        else if (IsLoading)
        {
            if (centeredText)
            {
                InternalComponent.InformationName = T("Loading World Record...");
                InternalComponent.AlternateNameText = new[] { T("Loading WR...") };
            }
            else
            {
                InternalComponent.InformationValue = T("Loading...");
            }
        }
        else
        {
            if (centeredText)
            {
                InternalComponent.InformationName = T("Unknown World Record");
                InternalComponent.AlternateNameText = new[] { T("Unknown WR") };
            }
            else
            {
                InternalComponent.InformationValue = TimeFormatConstants.DASH;
            }
        }
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    private void SetRecordText(string formatted, string runners, int tieCount, bool centeredText)
    {
        if (centeredText)
        {
            var textList = new List<string>
            {
                string.Format(T("World Record is {0} by {1}"), formatted, runners),
                string.Format(T("World Record: {0} by {1}"), formatted, runners),
                string.Format(T("WR: {0} by {1}"), formatted, runners),
                string.Format(T("WR is {0} by {1}"), formatted, runners)
            };

            if (tieCount > 1)
            {
                textList.Add(string.Format(T("World Record is {0} ({1}-way tie)"), formatted, tieCount));
                textList.Add(string.Format(T("World Record: {0} ({1}-way tie)"), formatted, tieCount));
                textList.Add(string.Format(T("WR: {0} ({1}-way tie)"), formatted, tieCount));
                textList.Add(string.Format(T("WR is {0} ({1}-way tie)"), formatted, tieCount));
            }

            textList.Add(string.Format(T("{0} by {1}"), formatted, runners));
            textList.Add(formatted);
            if (Settings.TextShortening == WorldRecordTextShortening.Automatic)
            {
                InternalComponent.InformationName = textList.First();
                InternalComponent.AlternateNameText = textList;
            }
            else
            {
                InternalComponent.InformationName = Settings.TextShortening switch
                {
                    WorldRecordTextShortening.TimeAndRunner => string.Format(T("{0} by {1}"), formatted, runners),
                    WorldRecordTextShortening.TimeOnly => formatted,
                    _ => textList[(int)Settings.TextShortening - 1]
                };
                InternalComponent.AlternateNameText = Array.Empty<string>();
            }
        }
        else
        {
            if (Settings.TextShortening == WorldRecordTextShortening.TimeOnly)
            {
                InternalComponent.InformationValue = formatted;
            }
            else if (tieCount > 1)
            {
                InternalComponent.InformationValue = string.Format(T("{0} ({1}-way tie)"), formatted, tieCount);
            }
            else
            {
                InternalComponent.InformationValue = string.Format(T("{0} by {1}"), formatted, runners);
            }
        }
    }

    private bool CheckPrecisionMillis(TimeSpan? recordTime)
    {
        if(Settings.WRPrecision == WorldRecordPrecisionType.FromLeaderboard)
        {
            if(!recordTime.HasValue)
            {
                // Fallback to Milliseconds
                return true;
            }

            return recordTime.Value.Milliseconds != 0;
        }

        return Settings.WRPrecision == WorldRecordPrecisionType.Milliseconds;
    }

    private bool IsPBTimeLower(TimeSpan? pbTime, TimeSpan? wrTime)
    {
        if (pbTime == null || wrTime == null)
        {
            return false;
        }

        if (CheckPrecisionMillis(wrTime))
        {
            return (int)pbTime.Value.TotalMilliseconds <= (int)wrTime.Value.TotalMilliseconds;
        }

        return (int)pbTime.Value.TotalSeconds <= (int)wrTime.Value.TotalSeconds;
    }

    private TimeSpan? GetPBTime(Model.TimingMethod method)
    {
        ISegment lastSplit = State.Run.Last();
        TimeSpan? pbTime = lastSplit.PersonalBestSplitTime[method];
        TimeSpan? splitTime = lastSplit.SplitTime[method];

        if (State.CurrentPhase == TimerPhase.Ended && splitTime < pbTime)
        {
            return splitTime;
        }

        return pbTime;
    }

    private TimeSpan? GetWorldRecordTime(SpeedrunComSharp.TimingMethod? timingMethodOverride)
    {
        if (timingMethodOverride == SpeedrunComSharp.TimingMethod.RealTime)
        {
            return WorldRecord.Times.RealTime;
        }

        if (timingMethodOverride == SpeedrunComSharp.TimingMethod.RealTimeWithoutLoads)
        {
            return WorldRecord.Times.RealTimeWithoutLoads;
        }

        if (timingMethodOverride == SpeedrunComSharp.TimingMethod.GameTime)
        {
            return WorldRecord.Times.GameTime;
        }

        return WorldRecord.Times.Primary;
    }

    private SpeedrunComSharp.TimingMethod? GetTimingMethodOverride()
    {
        if (Settings.TimingMethod == "Real Time")
        {
            return SpeedrunComSharp.TimingMethod.RealTime;
        }

        if (Settings.TimingMethod == "Real Time Without Loads")
        {
            return SpeedrunComSharp.TimingMethod.RealTimeWithoutLoads;
        }

        if (Settings.TimingMethod == "Game Time")
        {
            return SpeedrunComSharp.TimingMethod.GameTime;
        }

        return null;
    }

    public void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode)
    {
        Cache.Restart();
        Cache["Game"] = state.Run.GameName;
        Cache["Category"] = state.Run.CategoryName;
        Cache["PlatformID"] = Settings.FilterPlatform ? NormalizeEmptyToNull(state.Run.Metadata.PlatformName) : null;
        Cache["RegionID"] = Settings.FilterRegion ? NormalizeEmptyToNull(state.Run.Metadata.RegionName) : null;
        Cache["UsesEmulator"] = Settings.FilterPlatform ? (bool?)state.Run.Metadata.UsesEmulator : null;
        Cache["Variables"] = (Settings.FilterVariables || Settings.FilterSubcategories)
            ? BuildVariablesFilterFingerprint(state.Run.Metadata)
            : null;
        Cache["FilterVariables"] = Settings.FilterVariables;
        Cache["FilterSubcategories"] = Settings.FilterSubcategories;
        Cache["TimingMethod"] = Settings.TimingMethod;
        Cache["PrecisionType"] = Settings.WRPrecision;
        Cache["DisplayGameIcon"] = Settings.DisplayGameIcon;
        Cache["GameIconSide"] = Settings.GameIconSide;
        Cache["GameIconPlacing"] = Settings.GameIconPlacing;
        Cache["TextHorizontalOffset"] = Settings.TextHorizontalOffset;
        Cache["IconHorizontalOffset"] = Settings.IconHorizontalOffset;
        Cache["GameIconDropShadows"] = state.LayoutSettings.DropShadows;
        Cache["GameIconShadowTint"] = state.LayoutSettings.ShadowsColor;
        Cache["GameIconShadowOffset"] = Settings.GameIconShadowOffset;
        Cache["GameIconShadowTransparency"] = state.LayoutSettings.IconShadowTransparency;
        Cache["GameIconShadowBlur"] = state.LayoutSettings.IconShadowBlur;

        if (Cache.HasChanged)
        {
            lock (refreshSync)
            {
                IsLoading = true;
                WorldRecord = null;
                AllTies = null;
                PendingUiRefresh = false;
            }
            ShowWorldRecord(mode);
            Task.Factory.StartNew(RefreshWorldRecord);
        }
        else
        {
            TimeStamp baseline;
            TimeSpan interval;
            bool isLoading;
            bool pendingUiRefresh;
            lock (refreshSync)
            {
                baseline = LastFetchSucceeded ? LastUpdate : LastAttempt;
                interval = LastFetchSucceeded ? RefreshInterval : FailureRetryInterval;
                isLoading = IsLoading;
                pendingUiRefresh = PendingUiRefresh;
                if (pendingUiRefresh)
                {
                    PendingUiRefresh = false;
                }
            }

            if (pendingUiRefresh)
            {
                ShowWorldRecord(mode);
            }

            if (!isLoading && baseline != null && TimeStamp.Now - baseline >= interval)
            {
                lock (refreshSync)
                {
                    IsLoading = true;
                    PendingUiRefresh = false;
                }
                ShowWorldRecord(mode);
                Task.Factory.StartNew(RefreshWorldRecord);
            }

            Cache["CenteredText"] = Settings.CenteredText && !Settings.Display2Rows && mode == LayoutMode.Vertical;
            Cache["TextShortening"] = Settings.TextShortening;
            Cache["RealPBTime"] = GetPBTime(Model.TimingMethod.RealTime);
            Cache["GamePBTime"] = GetPBTime(Model.TimingMethod.GameTime);

            if (Cache.HasChanged)
            {
                ShowWorldRecord(mode);
            }
        }

        ApplyGameIconInsets(state, mode, mode == LayoutMode.Vertical ? VerticalHeight : height);
        InternalComponent.Update(invalidator, state, width, height, mode);
    }

    private const float IconTextGap = 3f;

    private void ApplyGameIconInsets(LiveSplitState state, LayoutMode mode, float height)
    {
        InternalComponent.ContentInsetLeft = 0f;
        InternalComponent.ContentInsetRight = 0f;
        if (!Settings.DisplayGameIcon || state?.Run?.GameIcon == null)
        {
            return;
        }

        GetIconDrawSize(state.Run.GameIcon, height, out float drawW, out _);
        bool centered = Settings.CenteredText && !Settings.Display2Rows && mode == LayoutMode.Vertical;
        float band = Math.Max(4f, height - 4f);
        // Use the same icon geometry as painting, including portrait aspect ratios.
        float inset = Math.Max(0f, 2f + (band + drawW) / 2f + IconTextGap
            + (Settings.GameIconSide == WorldRecordGameIconSide.Left
                ? Settings.IconHorizontalOffset : -Settings.IconHorizontalOffset));
        if (centered && Settings.GameIconPlacing == WorldRecordGameIconPlacing.NextToText)
        {
            inset = drawW + IconTextGap + Math.Abs(Settings.IconHorizontalOffset);
        }

        if (centered)
        {
            // Preserve centered text, but reserve enough room for the adjacent icon.
            InternalComponent.ContentInsetLeft = inset;
            InternalComponent.ContentInsetRight = inset;
        }
        else if (Settings.GameIconSide == WorldRecordGameIconSide.Left)
        {
            InternalComponent.ContentInsetLeft = inset;
        }
        else
        {
            InternalComponent.ContentInsetRight = inset;
        }
    }

    private static void GetIconDrawSize(Image icon, float boxOuter, out float drawW, out float drawH)
    {
        float inner = Math.Max(4f, boxOuter - 4f);
        float scale = inner / Math.Max(icon.Width, icon.Height);
        drawW = icon.Width * scale;
        drawH = icon.Height * scale;
    }

    private void ApplyWorldRecordHorizontalTextShift()
    {
        InternalComponent.NameLabel.X += Settings.TextHorizontalOffset;
        InternalComponent.ValueLabel.X += Settings.TextHorizontalOffset;
    }

    private void ConstrainTextToIcon(float width, RectangleF iconBounds)
    {
        float left = 5f;
        float right = Math.Max(left, width - 5f);
        if (!iconBounds.IsEmpty)
        {
            if (Settings.GameIconSide == WorldRecordGameIconSide.Left)
            {
                left = Math.Max(left, iconBounds.Right + IconTextGap);
            }
            else
            {
                right = Math.Min(right, iconBounds.Left - IconTextGap);
            }
        }

        // Offsets must reduce available text space instead of moving text over an icon.
        foreach (SimpleLabel label in new[] { InternalComponent.NameLabel, InternalComponent.ValueLabel })
        {
            float labelRight = Math.Min(right, label.X + label.Width);
            label.X = Math.Max(left, label.X);
            label.Width = Math.Max(0f, labelRight - label.X);
        }
    }

    private RectangleF GetGameIconBounds(Graphics g, LiveSplitState state, float width, float height, bool anchorIconToCenteredText)
    {
        if (!Settings.DisplayGameIcon || state?.Run?.GameIcon == null || width <= 4f)
        {
            return RectangleF.Empty;
        }

        GetIconDrawSize(state.Run.GameIcon, height, out float drawW, out float drawH);
        if (drawW > width - 4f)
        {
            drawH *= (width - 4f) / drawW;
            drawW = width - 4f;
        }
        float band = Math.Max(4f, height - 4f);
        float x;
        if (anchorIconToCenteredText && Settings.GameIconPlacing == WorldRecordGameIconPlacing.NextToText)
        {
            SimpleLabel name = InternalComponent.NameLabel;
            float textWidth = name.MeasureDisplayedTextWidth(g, name.Width);
            float textLeft = name.X + (name.Width - textWidth) / 2f;
            x = Settings.GameIconSide == WorldRecordGameIconSide.Left
                ? textLeft - IconTextGap - drawW
                : textLeft + textWidth + IconTextGap;
        }
        else
        {
            x = Settings.GameIconSide == WorldRecordGameIconSide.Left
                ? 7f + (band - drawW) / 2f
                : width - 7f - (band + drawW) / 2f;
        }

        x = Math.Max(2f, Math.Min(width - 2f - drawW, x + Settings.IconHorizontalOffset));
        return new RectangleF(x, 2f + (band - drawH) / 2f, drawW, drawH);
    }

    private RectangleF DrawWorldRecordGameIcon(Graphics g, LiveSplitState state, float width, float height, bool anchorIconToCenteredText)
    {
        RectangleF bounds = GetGameIconBounds(g, state, width, height, anchorIconToCenteredText);
        if (bounds.IsEmpty)
        {
            return bounds;
        }

        Image icon = state.Run.GameIcon;
        if (OldGameIcon != icon)
        {
            ImageAnimator.Animate(icon, (_, _) => { });
            OldGameIcon = icon;
        }
        ImageAnimator.UpdateFrames(icon);
        DrawGameIconWithShadow(g, state, icon, bounds.X, bounds.Y, bounds.Width, bounds.Height);
        return bounds;
    }

    private void DrawGameIconWithShadow(Graphics g, LiveSplitState state, Image icon, float x, float y, float width, float height)
    {
        if (state.LayoutSettings.DropShadows)
        {
            RefreshGameIconShadow(icon, state.LayoutSettings);
            if (GameIconShadow != null)
            {
                const float shadowScale = 5 / 4f;
                float shadowWidth = width * shadowScale;
                float shadowHeight = height * shadowScale;
                ImageAnimator.UpdateFrames(GameIconShadow);
                g.DrawImage(
                    GameIconShadow,
                    x - ((shadowWidth - width) / 2f) - 0.7f,
                    y - ((shadowHeight - height) / 2f) - 0.7f,
                    shadowWidth,
                    shadowHeight);
            }
        }

        g.DrawImage(icon, x, y, width, height);
    }

    private void RefreshGameIconShadow(Image icon, LiveSplit.Options.LayoutSettings layoutSettings)
    {
        if (!GameIconShadowKeyChanged(icon, layoutSettings))
        {
            return;
        }

        ClearGameIconShadow();
        GameIconShadow = IconShadow.Generate(
            icon,
            layoutSettings.ShadowsColor,
            Settings.GameIconShadowOffset,
            layoutSettings.IconShadowTransparency,
            layoutSettings.IconShadowBlur);
        GameIconShadowSource = icon;
        _lastGameIconShadowTint = layoutSettings.ShadowsColor;
        _lastGameIconShadowOffset = Settings.GameIconShadowOffset;
        _lastGameIconShadowTransparency = layoutSettings.IconShadowTransparency;
        _lastGameIconShadowBlur = layoutSettings.IconShadowBlur;
    }

    private bool GameIconShadowKeyChanged(Image icon, LiveSplit.Options.LayoutSettings layoutSettings)
    {
        if (GameIconShadowSource != icon || GameIconShadow == null || float.IsNaN(_lastGameIconShadowOffset))
        {
            return true;
        }

        return _lastGameIconShadowTint != layoutSettings.ShadowsColor
            || _lastGameIconShadowOffset != Settings.GameIconShadowOffset
            || _lastGameIconShadowTransparency != layoutSettings.IconShadowTransparency
            || _lastGameIconShadowBlur != layoutSettings.IconShadowBlur;
    }

    private void ClearGameIconShadow()
    {
        GameIconShadow?.Dispose();
        GameIconShadow = null;
        GameIconShadowSource = null;
        _lastGameIconShadowOffset = float.NaN;
    }

    private void DrawBackground(Graphics g, LiveSplitState state, float width, float height)
    {
        if (Settings.BackgroundColor.A > 0
            || (Settings.BackgroundGradient != GradientType.Plain
            && Settings.BackgroundColor2.A > 0))
        {
            var gradientBrush = new LinearGradientBrush(
                        new PointF(0, 0),
                        Settings.BackgroundGradient == GradientType.Horizontal
                        ? new PointF(width, 0)
                        : new PointF(0, height),
                        Settings.BackgroundColor,
                        Settings.BackgroundGradient == GradientType.Plain
                        ? Settings.BackgroundColor
                        : Settings.BackgroundColor2);
            g.FillRectangle(gradientBrush, 0, 0, width, height);
        }
    }

    private void PrepareDraw(LiveSplitState state, LayoutMode mode)
    {
        InternalComponent.DisplayTwoRows = Settings.Display2Rows;

        InternalComponent.NameLabel.HasShadow
            = InternalComponent.ValueLabel.HasShadow
            = state.LayoutSettings.DropShadows;

        if (Settings.CenteredText && !Settings.Display2Rows && mode == LayoutMode.Vertical)
        {
            InternalComponent.NameLabel.HorizontalAlignment = StringAlignment.Center;
            InternalComponent.ValueLabel.HorizontalAlignment = StringAlignment.Center;
            InternalComponent.NameLabel.VerticalAlignment = StringAlignment.Center;
            InternalComponent.ValueLabel.VerticalAlignment = StringAlignment.Center;
            InternalComponent.InformationValue = "";
        }
        else
        {
            InternalComponent.InformationName = Settings.TextShortening switch
            {
                WorldRecordTextShortening.ShortLabel or WorldRecordTextShortening.ShortSentence => T("WR"),
                WorldRecordTextShortening.TimeAndRunner or WorldRecordTextShortening.TimeOnly => "",
                _ => T("World Record")
            };
            InternalComponent.AlternateNameText = Settings.TextShortening == WorldRecordTextShortening.Automatic
                ? new[] { T("WR") } : Array.Empty<string>();
            InternalComponent.NameLabel.HorizontalAlignment = StringAlignment.Near;
            InternalComponent.ValueLabel.HorizontalAlignment = StringAlignment.Far;
            InternalComponent.NameLabel.VerticalAlignment =
                mode == LayoutMode.Horizontal || Settings.Display2Rows ? StringAlignment.Near : StringAlignment.Center;
            InternalComponent.ValueLabel.VerticalAlignment =
                mode == LayoutMode.Horizontal || Settings.Display2Rows ? StringAlignment.Far : StringAlignment.Center;
        }

        InternalComponent.NameLabel.ForeColor = Settings.OverrideTextColor ? Settings.TextColor : state.LayoutSettings.TextColor;
        InternalComponent.ValueLabel.ForeColor = Settings.OverrideTimeColor ? Settings.TimeColor : state.LayoutSettings.TextColor;
    }

    public void DrawHorizontal(Graphics g, LiveSplitState state, float height, System.Drawing.Region clipRegion)
    {
        DrawBackground(g, state, HorizontalWidth, height);
        PrepareDraw(state, LayoutMode.Horizontal);
        ApplyGameIconInsets(state, LayoutMode.Horizontal, height);
        InternalComponent.ComputeHorizontalLayout(g, state, height);
        ApplyWorldRecordHorizontalTextShift();
        RectangleF iconBounds = DrawWorldRecordGameIcon(g, state, HorizontalWidth, height, anchorIconToCenteredText: false);
        ConstrainTextToIcon(HorizontalWidth, iconBounds);
        InternalComponent.DrawHorizontalLabels(g);
    }

    public void DrawVertical(Graphics g, LiveSplitState state, float width, System.Drawing.Region clipRegion)
    {
        DrawBackground(g, state, width, VerticalHeight);
        PrepareDraw(state, LayoutMode.Vertical);

        bool anchorToText = Settings.CenteredText
            && !Settings.Display2Rows
            && Settings.DisplayGameIcon
            && state?.Run?.GameIcon != null
            && Settings.GameIconPlacing == WorldRecordGameIconPlacing.NextToText;

        float iconRowHeight = Settings.Display2Rows
            ? 1.8f * g.MeasureString("A", state.LayoutSettings.TextFont).Height : 31f;
        ApplyGameIconInsets(state, LayoutMode.Vertical, iconRowHeight);
        InternalComponent.ComputeVerticalLayout(g, state, width);
        ApplyWorldRecordHorizontalTextShift();

        RectangleF iconBounds = DrawWorldRecordGameIcon(g, state, width, iconRowHeight, anchorToText);
        ConstrainTextToIcon(width, iconBounds);

        InternalComponent.DrawVerticalLabels(g);
    }

    public Control GetSettingsControl(LayoutMode mode)
    {
        Settings.Mode = mode;
        return Settings;
    }

    public XmlNode GetSettings(XmlDocument document)
    {
        return Settings.GetSettings(document);
    }

    public void SetSettings(XmlNode settings)
    {
        Settings.SetSettings(settings);
    }

    public int GetSettingsHashCode()
    {
        return Settings.GetSettingsHashCode();
    }
}
