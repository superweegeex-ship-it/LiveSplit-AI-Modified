using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Drawing2D;
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
    }

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
                runners = State.Run.Metadata.Category.Players.Value > 1 ? "us" : "me";
                tieCount = 1;
            }

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

                InternalComponent.InformationName = textList.First();
                InternalComponent.AlternateNameText = textList;
            }
            else
            {
                if (tieCount > 1)
                {
                    InternalComponent.InformationValue = string.Format(T("{0} ({1}-way tie)"), formatted, tieCount);
                }
                else
                {
                    InternalComponent.InformationValue = string.Format(T("{0} by {1}"), formatted, runners);
                }
            }
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
        Cache["PlatformID"] = Settings.FilterPlatform ? state.Run.Metadata.PlatformName : null;
        Cache["RegionID"] = Settings.FilterRegion ? state.Run.Metadata.RegionName : null;
        Cache["UsesEmulator"] = Settings.FilterPlatform ? (bool?)state.Run.Metadata.UsesEmulator : null;
        Cache["Variables"] = (Settings.FilterVariables || Settings.FilterSubcategories) ? string.Join(",", state.Run.Metadata.VariableValueNames.Values) : null;
        Cache["FilterVariables"] = Settings.FilterVariables;
        Cache["FilterSubcategories"] = Settings.FilterSubcategories;
        Cache["TimingMethod"] = Settings.TimingMethod;
        Cache["PrecisionType"] = Settings.WRPrecision;

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
            Cache["RealPBTime"] = GetPBTime(Model.TimingMethod.RealTime);
            Cache["GamePBTime"] = GetPBTime(Model.TimingMethod.GameTime);

            if (Cache.HasChanged)
            {
                ShowWorldRecord(mode);
            }
        }

        InternalComponent.Update(invalidator, state, width, height, mode);
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
            InternalComponent.InformationName = "World Record";
            InternalComponent.AlternateNameText = new[]
            {
                "WR"
            };
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
        InternalComponent.DrawHorizontal(g, state, height, clipRegion);
    }

    public void DrawVertical(Graphics g, LiveSplitState state, float width, System.Drawing.Region clipRegion)
    {
        DrawBackground(g, state, width, VerticalHeight);
        PrepareDraw(state, LayoutMode.Vertical);
        InternalComponent.DrawVertical(g, state, width, clipRegion);
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
