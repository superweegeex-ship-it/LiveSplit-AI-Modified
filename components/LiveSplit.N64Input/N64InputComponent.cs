using LiveSplit.Model;
using RetroSpy.Readers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;

namespace LiveSplit.UI.Components
{
    public sealed class N64InputComponent : IComponent
    {
        private readonly object stateLock = new object();
        private readonly object renderLock = new object();
        private readonly Queue<ControllerSample> recentInputs = new Queue<ControllerSample>();
        private ControllerStateEventArgs controller = ControllerStateEventArgs.Zero;
        private N64SerialReader serialReader;
        private N64Skin skin;
        private Bitmap keyedSkinBackground;
        private N64Skin keyedSkinBackgroundOwner;
        private bool keyedBlackWasApplied;
        private bool keyedMagentaWasApplied;
        private volatile bool dirty = true;
        private volatile bool captureFrameDirty = true;
        private volatile string status = "Select a COM port in the component settings";
        private readonly object captureFrameLock = new object();
        private readonly System.Threading.AutoResetEvent captureFrameUpdated = new System.Threading.AutoResetEvent(false);
        private byte[] captureFramePng;
        private TcpListener captureListener;
        private string captureSourceUrl = "";
        private long lastCaptureFrameTicks;
        private int captureRenderPending;

        public string PortName { get; private set; } = "";
        public string SkinsFolder { get; private set; } = @"C:\Users\xskil\Documents\sm64Spy-0.2.0-Windows-x64\skins";
        public string SkinName { get; private set; } = "";
        public string SkinVariantName { get; private set; } = "";
        public float DisplayHeight { get; private set; } = 160;
        public int ZoomPercent { get; private set; } = 100;
        public int HorizontalOffsetPercent { get; private set; }
        public int VerticalOffsetPercent { get; private set; }
        public int RenderDelayMilliseconds { get; private set; }
        public bool TransparentBackground { get; private set; } = true;
        public bool BlackBackgroundTransparency { get; private set; }
        public bool MagentaBackgroundTransparency { get; private set; }
        public Color BackgroundColor { get; private set; } = Color.FromArgb(220, 18, 21, 28);
        public bool ShowStatus { get; private set; } = true;
        public bool CaptureOnly { get; private set; }

        public string ComponentName => "N64 Input Viewer";
        public float HorizontalWidth => DisplayHeight * (skin?.AspectRatio ?? 1.65f);
        public float VerticalHeight => DisplayHeight;
        public float MinimumHeight => 60;
        public float MinimumWidth => 90;
        public float PaddingTop => 0;
        public float PaddingBottom => 0;
        public float PaddingLeft => 0;
        public float PaddingRight => 0;
        public IDictionary<string, Action> ContextMenuControls => null;

        private Size CaptureFrameSize
        {
            get
            {
                lock (renderLock)
                {
                    if (skin != null)
                        return new Size(Math.Max(1, (int)Math.Ceiling(skin.Width)), Math.Max(1, (int)Math.Ceiling(skin.Height)));
                }
                return new Size(Math.Max(1, (int)Math.Ceiling(DisplayHeight * 1.65f)), Math.Max(1, (int)Math.Ceiling(DisplayHeight)));
            }
        }

        public N64InputComponent()
        {
            LoadAvailableSkins();
            if (!string.IsNullOrWhiteSpace(PortName)) ConnectPort();
        }

        private void ConnectPort()
        {
            StopReader();
            if (string.IsNullOrWhiteSpace(PortName))
            {
                status = "Select a COM port in the component settings";
                MarkDirty();
                return;
            }
            try
            {
                serialReader = new N64SerialReader(PortName);
                serialReader.StateChanged += ReaderStateChanged;
                serialReader.Disconnected += ReaderDisconnected;
                status = "Listening on " + PortName;
            }
            catch (Exception ex)
            {
                serialReader = null;
                status = PortName + ": " + ex.Message;
            }
            MarkDirty();
        }

        private void ReaderStateChanged(ControllerStateEventArgs state)
        {
            long timestamp = Stopwatch.GetTimestamp();
            lock (stateLock)
            {
                controller = state;
                recentInputs.Enqueue(new ControllerSample(timestamp, state));
                int effectiveDelay = CaptureOnly ? 0 : RenderDelayMilliseconds;
                long keepTicks = (long)((effectiveDelay + 250) * (double)Stopwatch.Frequency / 1000.0);
                while (recentInputs.Count > 1 && timestamp - recentInputs.Peek().Timestamp > keepTicks)
                    recentInputs.Dequeue();
            }
            status = "N64 controller · " + PortName;
            MarkDirty();
        }

        private ControllerStateEventArgs GetRenderedInput()
        {
            if (CaptureOnly)
                return GetInterpolatedCaptureInput();
            if (RenderDelayMilliseconds <= 0)
            {
                lock (stateLock) return controller;
            }

            long renderTimestamp = Stopwatch.GetTimestamp() -
                (long)(RenderDelayMilliseconds * (double)Stopwatch.Frequency / 1000.0);
            ControllerStateEventArgs delayed = ControllerStateEventArgs.Zero;
            lock (stateLock)
            {
                foreach (var sample in recentInputs)
                {
                    if (sample.Timestamp > renderTimestamp) break;
                    delayed = sample.State;
                }
            }
            return delayed;
        }

        private ControllerStateEventArgs GetInterpolatedCaptureInput()
        {
            lock (stateLock)
            {
                if (recentInputs.Count < 2) return controller;

                long targetTimestamp = Stopwatch.GetTimestamp() - Math.Max(1, Stopwatch.Frequency / 60);
                ControllerSample earlier = null;
                ControllerSample later = null;
                foreach (ControllerSample sample in recentInputs)
                {
                    if (sample.Timestamp <= targetTimestamp) earlier = sample;
                    else { later = sample; break; }
                }

                if (earlier == null || later == null || later.Timestamp <= earlier.Timestamp)
                    return controller;

                float amount = Math.Max(0f, Math.Min(1f,
                    (float)(targetTimestamp - earlier.Timestamp) / (later.Timestamp - earlier.Timestamp)));
                var analogs = new Dictionary<string, float>();
                foreach (var analog in later.State.Analogs) analogs[analog.Key] = analog.Value;
                var rawAnalogs = new Dictionary<string, int>();
                foreach (var rawAnalog in later.State.RawAnalogs) rawAnalogs[rawAnalog.Key] = rawAnalog.Value;
                foreach (var analog in earlier.State.Analogs)
                {
                    if (!later.State.Analogs.TryGetValue(analog.Key, out float laterValue)) continue;
                    float value = analog.Value + (laterValue - analog.Value) * amount;
                    analogs[analog.Key] = value;
                    string rawKey = analog.Key + "_raw";
                    if (earlier.State.RawAnalogs.TryGetValue(rawKey, out int earlierRaw) && later.State.RawAnalogs.TryGetValue(rawKey, out int laterRaw))
                        rawAnalogs[rawKey] = (int)Math.Round(earlierRaw + (laterRaw - earlierRaw) * amount);
                }
                return new ControllerStateEventArgs(later.State.Buttons, analogs, rawAnalogs, later.State.RawPrinterData);
            }
        }

        private bool HasStickMovementBetweenRecentSamples()
        {
            lock (stateLock)
            {
                if (recentInputs.Count < 2) return false;
                ControllerSample[] samples = recentInputs.ToArray();
                ControllerStateEventArgs before = samples[samples.Length - 2].State;
                ControllerStateEventArgs latest = samples[samples.Length - 1].State;
                long targetTimestamp = Stopwatch.GetTimestamp() - Math.Max(1, Stopwatch.Frequency / 60);
                if (targetTimestamp > samples[samples.Length - 1].Timestamp + Stopwatch.Frequency / 120)
                    return false;
                foreach (var analog in latest.Analogs)
                    if (before.Analogs.TryGetValue(analog.Key, out float previous) && Math.Abs(analog.Value - previous) > 0.0001f)
                        return true;
                return false;
            }
        }

        private sealed class ControllerSample
        {
            public long Timestamp { get; }
            public ControllerStateEventArgs State { get; }
            public ControllerSample(long timestamp, ControllerStateEventArgs state)
            {
                Timestamp = timestamp;
                State = state;
            }
        }

        private void ReaderDisconnected(string reason)
        {
            var old = serialReader;
            if (old != null) System.Threading.ThreadPool.QueueUserWorkItem(_ => old.Dispose());
            status = reason;
            serialReader = null;
            MarkDirty();
        }

        private void StopReader()
        {
            var old = serialReader;
            serialReader = null;
            if (old == null) return;
            old.StateChanged -= ReaderStateChanged;
            old.Disconnected -= ReaderDisconnected;
            old.Dispose();
        }

        private void LoadAvailableSkins()
        {
            N64Skin next = null;
            try
            {
                if (string.IsNullOrWhiteSpace(SkinsFolder)) { status = "Choose a folder containing N64 skins"; }
                else
                {
                    var skins = N64Skin.FindSkins(SkinsFolder).ToList();
                    var selected = skins.FirstOrDefault(s => string.Equals(s.Name, SkinName, StringComparison.OrdinalIgnoreCase)) ?? skins.FirstOrDefault();
                    if (selected != null)
                    {
                        next = N64Skin.Load(selected.Folder, SkinVariantName);
                        SkinName = next.Name;
                        SkinVariantName = next.BackgroundVariantName;
                        if (serialReader == null && string.IsNullOrWhiteSpace(PortName)) status = "Choose a COM port in the component settings";
                    }
                    else status = "No compatible N64 skins found in the selected folder";
                }
            }
            catch (Exception ex) { status = "Skin load failed: " + ex.Message; }
            lock (renderLock)
            {
                var old = skin;
                keyedSkinBackground?.Dispose();
                keyedSkinBackground = null;
                keyedSkinBackgroundOwner = null;
                skin = next;
                old?.Dispose();
            }
            MarkDirty();
        }

        public void DrawHorizontal(Graphics g, LiveSplitState state, float height, Region clipRegion)
        {
            if (!CaptureOnly) Draw(g, HorizontalWidth, height, BlackBackgroundTransparency, MagentaBackgroundTransparency);
        }

        public void DrawVertical(Graphics g, LiveSplitState state, float width, Region clipRegion)
        {
            if (!CaptureOnly) Draw(g, width, VerticalHeight, BlackBackgroundTransparency, MagentaBackgroundTransparency);
        }

        private void Draw(Graphics g, float width, float height, bool keyBlackBackground = false, bool keyMagentaBackground = false)
        {
            lock (renderLock) DrawLocked(g, width, height, keyBlackBackground, keyMagentaBackground);
        }

        private void DrawLocked(Graphics g, float width, float height, bool keyBlackBackground, bool keyMagentaBackground)
        {
            if (width <= 1 || height <= 1) return;
            ControllerStateEventArgs input = GetRenderedInput();
            var saved = g.Save();
            try
            {
                g.SetClip(new RectangleF(0, 0, width, height), CombineMode.Intersect);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                if (!TransparentBackground)
                    using (var bg = new SolidBrush(BackgroundColor)) g.FillRectangle(bg, 0, 0, width, height);

                if (skin != null)
                {
                    var skinState = g.Save();
                    try
                    {
                    float fitScale = Math.Min(width / skin.Width, height / skin.Height);
                    float scale = fitScale * ZoomPercent / 100f;
                    float x = (width - skin.Width * scale) / 2f + width * HorizontalOffsetPercent / 100f;
                    float y = (height - skin.Height * scale) / 2f + height * VerticalOffsetPercent / 100f;
                    g.TranslateTransform(x, y);
                    g.ScaleTransform(scale, scale);
                    if (skin.Background != null)
                    {
                        var background = GetKeyedSkinBackground(keyBlackBackground, keyMagentaBackground);
                        g.DrawImage(background, new RectangleF(0, 0, skin.Width, skin.Height));
                    }
                    else if (!skin.BackgroundColor.IsEmpty && !IsKeyedColor(skin.BackgroundColor, keyBlackBackground, keyMagentaBackground))
                        using (var brush = new SolidBrush(skin.BackgroundColor)) g.FillRectangle(brush, 0, 0, skin.Width, skin.Height);

                    foreach (var button in skin.Buttons)
                    {
                        if (button.AlwaysVisible || (input.Buttons.TryGetValue(button.Name, out bool down) && (button.Invert ? !down : down)))
                            g.DrawImage(button.Image, button.Bounds);
                    }
                    foreach (var stick in skin.Sticks)
                    {
                        float sx = input.Analogs.TryGetValue(stick.XName, out float xv) ? Math.Max(-1f, Math.Min(1f, xv)) : 0f;
                        float sy = input.Analogs.TryGetValue(stick.YName, out float yv) ? Math.Max(-1f, Math.Min(1f, yv)) : 0f;
                        if (Math.Abs(sx) <= stick.DiagonalDeadzone) sx = 0;
                        if (Math.Abs(sy) <= stick.DiagonalDeadzone) sy = 0;
                        ClampToOctagonalGate(ref sx, ref sy, stick.MaxCardinalMovement, stick.MaxDiagonalMovement);
                        var bounds = stick.Bounds;
                        float xDirection = stick.XReverse ? -1 : 1;
                        float yDirection = stick.YReverse ? 1 : -1;
                        float dx = sx * stick.XRange;
                        float dy = sy * stick.YRange;
                        dx *= xDirection;
                        dy *= yDirection;
                        float boundaryScale = GetBoundaryScale(stick, dx, dy);
                        bounds.Offset(dx * boundaryScale, dy * boundaryScale);
                        g.DrawImage(stick.Image, bounds);
                    }
                    }
                    finally { g.Restore(skinState); }
                }
                if (ShowStatus) DrawStatus(g, width, height);
            }
            finally { g.Restore(saved); }
        }

        private Bitmap GetKeyedSkinBackground(bool keyBlack, bool keyMagenta)
        {
            if (!keyBlack && !keyMagenta) return skin.Background;
            if (keyedSkinBackground != null && ReferenceEquals(keyedSkinBackgroundOwner, skin)
                && keyedBlackWasApplied == keyBlack && keyedMagentaWasApplied == keyMagenta)
                return keyedSkinBackground;

            keyedSkinBackground?.Dispose();
            keyedSkinBackground = (Bitmap)skin.Background.Clone(new Rectangle(0, 0, skin.Background.Width, skin.Background.Height), PixelFormat.Format32bppArgb);
            for (int y = 0; y < keyedSkinBackground.Height; y++)
            for (int x = 0; x < keyedSkinBackground.Width; x++)
            {
                var pixel = keyedSkinBackground.GetPixel(x, y);
                if ((keyBlack && pixel.R == 0 && pixel.G == 0 && pixel.B == 0)
                    || (keyMagenta && pixel.R == 255 && pixel.G == 0 && pixel.B == 255))
                    keyedSkinBackground.SetPixel(x, y, Color.Transparent);
            }
            keyedSkinBackgroundOwner = skin;
            keyedBlackWasApplied = keyBlack;
            keyedMagentaWasApplied = keyMagenta;
            return keyedSkinBackground;
        }

        private static bool IsKeyedColor(Color color, bool keyBlack, bool keyMagenta)
        {
            return (keyBlack && color.R == 0 && color.G == 0 && color.B == 0)
                || (keyMagenta && color.R == 255 && color.G == 0 && color.B == 255);
        }

        private static void ClampToOctagonalGate(ref float x, ref float y, float cardinalLimit, float diagonalLimit)
        {
            if (cardinalLimit <= 0 || diagonalLimit <= 0 || diagonalLimit >= cardinalLimit) return;

            // The eight vertices are (±cardinal, 0), (0, ±cardinal), and
            // (±diagonal, ±diagonal). Scale the vector only when it crosses one
            // of the eight slanted edges, preserving its angle continuously.
            float absX = Math.Abs(x) * 128f;
            float absY = Math.Abs(y) * 128f;
            float major = Math.Max(absX, absY);
            float minor = Math.Min(absX, absY);
            float weightedDistance = diagonalLimit * major + (cardinalLimit - diagonalLimit) * minor;
            float boundary = cardinalLimit * diagonalLimit;
            if (weightedDistance > boundary)
            {
                float scale = boundary / weightedDistance;
                x *= scale;
                y *= scale;
            }
        }

        private static float GetBoundaryScale(SkinStick stick, float dx, float dy)
        {
            if (stick.MovementBoundary == null || stick.MovementBoundary.Length < 3 ||
                float.IsNaN(stick.CenterX) || float.IsNaN(stick.CenterY) || (dx == 0 && dy == 0)) return 1f;

            float orientation = 0;
            for (int i = 0; i < stick.MovementBoundary.Length; i++)
            {
                var a = stick.MovementBoundary[i];
                var b = stick.MovementBoundary[(i + 1) % stick.MovementBoundary.Length];
                orientation += a.X * b.Y - b.X * a.Y;
            }
            float direction = orientation >= 0 ? 1f : -1f;
            bool IsInside(float scale)
            {
                float x = stick.CenterX + dx * scale;
                float y = stick.CenterY + dy * scale;
                for (int i = 0; i < stick.MovementBoundary.Length; i++)
                {
                    var a = stick.MovementBoundary[i];
                    var b = stick.MovementBoundary[(i + 1) % stick.MovementBoundary.Length];
                    float edgeX = b.X - a.X;
                    float edgeY = b.Y - a.Y;
                    float signedDistance = direction * (edgeX * (y - a.Y) - edgeY * (x - a.X)) /
                        (float)Math.Sqrt(edgeX * edgeX + edgeY * edgeY);
                    if (signedDistance < stick.BoundaryMargin) return false;
                }
                return true;
            }

            if (IsInside(1f)) return 1f;
            float low = 0f, high = 1f;
            for (int i = 0; i < 18; i++)
            {
                float mid = (low + high) / 2f;
                if (IsInside(mid)) low = mid;
                else high = mid;
            }
            return low;
        }

        private void DrawStatus(Graphics g, float width, float height)
        {
            string current = status;
            if (string.IsNullOrEmpty(current)) return;
            using (var font = new Font("Arial", Math.Max(9f, Math.Min(12f, height / 18f)), FontStyle.Regular, GraphicsUnit.Pixel))
            using (var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            {
                float h = font.GetHeight(g) + 6;
                var rect = new RectangleF(4, height - h - 3, Math.Max(0, width - 8), h);
                using (var bg = new SolidBrush(Color.FromArgb(150, 0, 0, 0))) g.FillRectangle(bg, rect);
                using (var text = new SolidBrush(Color.White)) g.DrawString(current, font, text, rect, format);
            }
        }

        public Control GetSettingsControl(LayoutMode mode)
        {
            var panel = new TableLayoutPanel { ColumnCount = 3, RowCount = 13, AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(8) };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var ports = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
            RefreshPorts(ports);
            ports.SelectedItem = ports.Items.Cast<string>().FirstOrDefault(p => string.Equals(p, PortName, StringComparison.OrdinalIgnoreCase));
            ports.SelectedIndexChanged += (s, e) => { PortName = ports.SelectedItem as string ?? ""; ConnectPort(); };
            AddRow(panel, 0, "N64 adapter COM port", ports);
            var refresh = new Button { Text = "Refresh", AutoSize = true };
            refresh.Click += (s, e) => RefreshPorts(ports);
            panel.Controls.Add(refresh, 2, 0);

            var folder = new TextBox { Text = SkinsFolder, Width = 300, Dock = DockStyle.Fill };
            var skinPicker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
            PopulateSkins(skinPicker, SkinName);
            var variantPicker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
            bool updatingVariantPicker = false;
            void RefreshVariantPicker()
            {
                updatingVariantPicker = true;
                try
                {
                    variantPicker.Items.Clear();
                    if (skin?.BackgroundVariants != null && skin.BackgroundVariants.Count > 0)
                    {
                        foreach (var variant in skin.BackgroundVariants) variantPicker.Items.Add(variant);
                        variantPicker.SelectedItem = skin.BackgroundVariants.FirstOrDefault(v =>
                            string.Equals(v.Name, SkinVariantName, StringComparison.OrdinalIgnoreCase)) ?? skin.BackgroundVariants[0];
                        variantPicker.Enabled = skin.BackgroundVariants.Count > 1;
                    }
                    else
                    {
                        variantPicker.Items.Add("(default)");
                        variantPicker.SelectedIndex = 0;
                        variantPicker.Enabled = false;
                    }
                }
                finally { updatingVariantPicker = false; }
            }
            RefreshVariantPicker();
            var browse = new Button { Text = "Browse…", AutoSize = true };
            folder.TextChanged += (s, e) => { SkinsFolder = folder.Text; if (Directory.Exists(SkinsFolder)) PopulateSkins(skinPicker, ""); };
            browse.Click += (s, e) =>
            {
                using (var dialog = new FolderBrowserDialog { Description = "Choose the folder that contains your N64 skin folders", SelectedPath = Directory.Exists(SkinsFolder) ? SkinsFolder : "" })
                {
                    if (dialog.ShowDialog() == DialogResult.OK) folder.Text = dialog.SelectedPath;
                }
            };
            AddRow(panel, 1, "Skins folder", folder); panel.Controls.Add(browse, 2, 1);
            AddRow(panel, 2, "Controller skin", skinPicker);
            AddRow(panel, 3, "Skin variant", variantPicker);
            var captureHelp = new Label { AutoSize = true, MaximumSize = new Size(460, 0) };
            skinPicker.SelectedIndexChanged += (s, e) =>
            {
                var choice = skinPicker.SelectedItem as SkinChoice;
                if (choice != null)
                {
                    SkinName = choice.Name;
                    SkinVariantName = "";
                    LoadAvailableSkins();
                    RefreshVariantPicker();
                    UpdateCaptureHelp();
                }
            };
            variantPicker.SelectedIndexChanged += (s, e) =>
            {
                if (updatingVariantPicker) return;
                var choice = variantPicker.SelectedItem as SkinVariantInfo;
                if (choice != null && !string.Equals(SkinVariantName, choice.Name, StringComparison.OrdinalIgnoreCase))
                {
                    SkinVariantName = choice.Name;
                    LoadAvailableSkins();
                    UpdateCaptureHelp();
                }
            };

            var size = new NumericUpDown { Minimum = 60, Maximum = 500, Value = (decimal)DisplayHeight, Width = 110, Increment = 10 };
            size.ValueChanged += (s, e) => { DisplayHeight = (float)size.Value; MarkDirty(); };
            AddRow(panel, 4, "Display height", size);
            var options = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            var transparent = new CheckBox { Text = "Transparent outside the skin", Checked = TransparentBackground, AutoSize = true };
            transparent.CheckedChanged += (s, e) => { TransparentBackground = transparent.Checked; MarkDirty(); };
            var keyBlack = new CheckBox { Text = "Make black skin background transparent", Checked = BlackBackgroundTransparency, AutoSize = true };
            keyBlack.CheckedChanged += (s, e) => { BlackBackgroundTransparency = keyBlack.Checked; MarkDirty(); };
            var keyMagenta = new CheckBox { Text = "Make magenta skin background transparent", Checked = MagentaBackgroundTransparency, AutoSize = true };
            keyMagenta.CheckedChanged += (s, e) => { MagentaBackgroundTransparency = keyMagenta.Checked; MarkDirty(); };
            var labels = new CheckBox { Text = "Show connection status", Checked = ShowStatus, AutoSize = true };
            labels.CheckedChanged += (s, e) => { ShowStatus = labels.Checked; MarkDirty(); };
            var background = new Button { Text = "Background color…", AutoSize = true };
            background.Click += (s, e) =>
            {
                using (var dialog = new ColorDialog { Color = BackgroundColor, FullOpen = true })
                    if (dialog.ShowDialog() == DialogResult.OK) { BackgroundColor = dialog.Color; MarkDirty(); }
            };
            options.Controls.Add(transparent); options.Controls.Add(keyBlack); options.Controls.Add(keyMagenta); options.Controls.Add(labels); options.Controls.Add(background);
            AddRow(panel, 5, "Appearance", options);

            AddRow(panel, 6, "Zoom", CreateSlider(25, 300, ZoomPercent, v => ZoomPercent = v, v => v + "%"));
            AddRow(panel, 7, "Move horizontally", CreateSlider(-100, 100, HorizontalOffsetPercent, v => HorizontalOffsetPercent = v, v => v + "%"));
            AddRow(panel, 8, "Move vertically", CreateSlider(-100, 100, VerticalOffsetPercent, v => VerticalOffsetPercent = v, v => v + "%"));
            var renderDelay = new NumericUpDown { Minimum = 0, Maximum = 5000, Value = RenderDelayMilliseconds, Increment = 10, Width = 110 };
            renderDelay.ValueChanged += (s, e) => { RenderDelayMilliseconds = (int)renderDelay.Value; MarkDirty(); };
            AddRow(panel, 9, "Render delay (ms)", renderDelay);

            var captureOnly = new CheckBox { Text = "OBS-only output (leave blank space in LiveSplit)", Checked = CaptureOnly, AutoSize = true };
            var sourceUrl = new TextBox { ReadOnly = true, Width = 285, Text = CaptureOnly ? captureSourceUrl : "Enable OBS-only output to get a URL" };
            var copyUrl = new Button { Text = "Copy URL", AutoSize = true, Enabled = CaptureOnly && !string.IsNullOrEmpty(captureSourceUrl) };
            void UpdateCaptureHelp()
            {
                Size outputSize = CaptureFrameSize;
                captureHelp.Text = "When enabled, add this URL as a Browser Source in OBS. LiveSplit keeps the viewer's space blank. Native output size: " + outputSize.Width + " × " + outputSize.Height + " pixels; set the OBS Browser Source to this size. Stick motion is interpolated for up to 60 fps; buttons remain immediate. Your existing Window Capture source will not include the viewer.";
            }
            UpdateCaptureHelp();
            bool updatingCaptureControl = false;
            void UpdateCaptureSourceControls()
            {
                sourceUrl.Text = CaptureOnly ? captureSourceUrl : "Enable OBS-only output to get a URL";
                sourceUrl.Enabled = CaptureOnly && !string.IsNullOrEmpty(captureSourceUrl);
                copyUrl.Enabled = sourceUrl.Enabled;
                UpdateCaptureHelp();
            }
            captureOnly.CheckedChanged += (s, e) =>
            {
                if (updatingCaptureControl) return;
                if (captureOnly.Checked)
                {
                    CaptureOnly = true;
                    if (!StartCaptureServer())
                    {
                        CaptureOnly = false;
                        updatingCaptureControl = true;
                        captureOnly.Checked = false;
                        updatingCaptureControl = false;
                        MessageBox.Show("The OBS browser overlay could not start. Check whether another program is using ports 39164–39173.", "N64 Input Viewer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                else
                {
                    CaptureOnly = false;
                    StopCaptureServer();
                }
                MarkDirty();
                UpdateCaptureSourceControls();
            };
            copyUrl.Click += (s, e) =>
            {
                if (!string.IsNullOrEmpty(captureSourceUrl)) Clipboard.SetText(captureSourceUrl);
            };
            AddRow(panel, 10, "Capture output", captureOnly);
            AddRow(panel, 11, "OBS Browser Source URL", sourceUrl);
            panel.Controls.Add(copyUrl, 2, 11);
            panel.Controls.Add(captureHelp, 1, 12);
            return panel;
        }

        private Control CreateSlider(int min, int max, int value, Action<int> changed, Func<int, string> format)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill, Margin = new Padding(0) };
            var slider = new TrackBar { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)), TickFrequency = min < 0 ? 25 : 25, SmallChange = 1, LargeChange = 10, Width = 250, Height = 42, TickStyle = TickStyle.BottomRight };
            var valueLabel = new Label { Text = format(slider.Value), AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(4, 12, 0, 0), MinimumSize = new Size(48, 20) };
            slider.ValueChanged += (s, e) => { changed(slider.Value); valueLabel.Text = format(slider.Value); MarkDirty(); };
            row.Controls.Add(slider); row.Controls.Add(valueLabel);
            return row;
        }

        private static void RefreshPorts(ComboBox ports)
        {
            string selected = ports.SelectedItem as string;
            ports.Items.Clear();
            foreach (string port in SerialPort.GetPortNames().OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) ports.Items.Add(port);
            if (!string.IsNullOrEmpty(selected) && ports.Items.Contains(selected)) ports.SelectedItem = selected;
        }

        private void PopulateSkins(ComboBox picker, string selectedName)
        {
            picker.Items.Clear();
            foreach (var item in N64Skin.FindSkins(SkinsFolder)) picker.Items.Add(new SkinChoice(item.Name, item.Folder));
            var choice = picker.Items.Cast<SkinChoice>().FirstOrDefault(x => x.Name == selectedName) ?? picker.Items.Cast<SkinChoice>().FirstOrDefault();
            if (choice != null) picker.SelectedItem = choice;
            if (choice == null) { SkinName = ""; LoadAvailableSkins(); }
        }

        private sealed class SkinChoice
        {
            public string Name { get; }
            public string Folder { get; }
            public SkinChoice(string name, string folder) { Name = name; Folder = folder; }
            public override string ToString() => Name;
        }

        private static void AddRow(TableLayoutPanel panel, int row, string label, Control control)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 12, 4) }, 0, row);
            panel.Controls.Add(control, 1, row);
        }

        public XmlNode GetSettings(XmlDocument document)
        {
            var root = document.CreateElement("Settings");
            void Add(string name, string value) { var e = document.CreateElement(name); e.InnerText = value; root.AppendChild(e); }
            Add("PortName", PortName); Add("SkinsFolder", SkinsFolder); Add("SkinName", SkinName); Add("SkinVariantName", SkinVariantName);
            Add("DisplayHeight", DisplayHeight.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Add("ZoomPercent", ZoomPercent.ToString()); Add("HorizontalOffsetPercent", HorizontalOffsetPercent.ToString()); Add("VerticalOffsetPercent", VerticalOffsetPercent.ToString()); Add("RenderDelayMilliseconds", RenderDelayMilliseconds.ToString());
            Add("TransparentBackground", TransparentBackground.ToString()); Add("BlackBackgroundTransparency", BlackBackgroundTransparency.ToString()); Add("MagentaBackgroundTransparency", MagentaBackgroundTransparency.ToString()); Add("BackgroundColor", BackgroundColor.ToArgb().ToString()); Add("ShowStatus", ShowStatus.ToString());
            Add("CaptureOnly", CaptureOnly.ToString());
            return root;
        }

        public void SetSettings(XmlNode settings)
        {
            PortName = settings["PortName"]?.InnerText ?? "";
            SkinsFolder = settings["SkinsFolder"]?.InnerText ?? SkinsFolder;
            SkinName = settings["SkinName"]?.InnerText ?? "";
            SkinVariantName = settings["SkinVariantName"]?.InnerText ?? "";
            if (float.TryParse(settings["DisplayHeight"]?.InnerText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float height)) DisplayHeight = Math.Max(60, Math.Min(500, height));
            if (int.TryParse(settings["ZoomPercent"]?.InnerText, out int zoom)) ZoomPercent = Math.Max(25, Math.Min(300, zoom));
            if (int.TryParse(settings["HorizontalOffsetPercent"]?.InnerText, out int horizontal)) HorizontalOffsetPercent = Math.Max(-100, Math.Min(100, horizontal));
            if (int.TryParse(settings["VerticalOffsetPercent"]?.InnerText, out int vertical)) VerticalOffsetPercent = Math.Max(-100, Math.Min(100, vertical));
            if (int.TryParse(settings["RenderDelayMilliseconds"]?.InnerText, out int renderDelay)) RenderDelayMilliseconds = Math.Max(0, Math.Min(5000, renderDelay));
            if (bool.TryParse(settings["TransparentBackground"]?.InnerText, out bool transparent)) TransparentBackground = transparent;
            if (bool.TryParse(settings["BlackBackgroundTransparency"]?.InnerText, out bool keyBlack)) BlackBackgroundTransparency = keyBlack;
            if (bool.TryParse(settings["MagentaBackgroundTransparency"]?.InnerText, out bool keyMagenta)) MagentaBackgroundTransparency = keyMagenta;
            if (int.TryParse(settings["BackgroundColor"]?.InnerText, out int bg)) BackgroundColor = Color.FromArgb(bg);
            if (bool.TryParse(settings["ShowStatus"]?.InnerText, out bool labels)) ShowStatus = labels;
            if (bool.TryParse(settings["CaptureOnly"]?.InnerText, out bool captureOnly)) CaptureOnly = captureOnly;
            LoadAvailableSkins();
            if (!string.IsNullOrWhiteSpace(PortName)) ConnectPort();
            if (CaptureOnly && !StartCaptureServer()) CaptureOnly = false;
            else if (!CaptureOnly) StopCaptureServer();
            MarkDirty();
        }

        public void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode)
        {
            if (dirty && invalidator != null) invalidator.Invalidate(0, 0, width, height);
            dirty = false;
        }

        public void Dispose()
        {
            StopCaptureServer();
            StopReader();
            lock (renderLock)
            {
                keyedSkinBackground?.Dispose(); keyedSkinBackground = null;
                skin?.Dispose(); skin = null;
            }
            GC.SuppressFinalize(this);
        }

        private void MarkDirty()
        {
            dirty = true;
            captureFrameDirty = true;
            if (CaptureOnly) QueueCaptureFrameRefresh();
        }

        private void RefreshCaptureFrame()
        {
            Size nativeSize = CaptureFrameSize;
            int width = nativeSize.Width;
            int height = nativeSize.Height;
            captureFrameDirty = false;
            lastCaptureFrameTicks = Stopwatch.GetTimestamp();
            try
            {
                using (var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
                using (var graphics = Graphics.FromImage(bitmap))
                using (var stream = new MemoryStream())
                {
                    graphics.Clear(Color.Transparent);
                    Draw(graphics, width, height);
                    bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                    lock (captureFrameLock) captureFramePng = stream.ToArray();
                }
                captureFrameUpdated.Set();
            }
            catch (Exception ex)
            {
                captureFrameDirty = true;
                status = "OBS overlay render failed: " + ex.Message;
            }
        }

        private void QueueCaptureFrameRefresh()
        {
            if (System.Threading.Interlocked.CompareExchange(ref captureRenderPending, 1, 0) != 0) return;
            Task.Run(() =>
            {
                try
                {
                    const int targetFramesPerSecond = 60;
                    long frameInterval = Math.Max(1, Stopwatch.Frequency / targetFramesPerSecond);
                    while (CaptureOnly && (captureFrameDirty || HasStickMovementBetweenRecentSamples()))
                    {
                        long elapsed = Stopwatch.GetTimestamp() - lastCaptureFrameTicks;
                        long remaining = frameInterval - elapsed;
                        if (lastCaptureFrameTicks != 0 && remaining > 0)
                        {
                            int delayMilliseconds = (int)(remaining * 1000L / Stopwatch.Frequency) - 1;
                            if (delayMilliseconds > 0) System.Threading.Thread.Sleep(delayMilliseconds);
                            while (Stopwatch.GetTimestamp() - lastCaptureFrameTicks < frameInterval)
                                System.Threading.Thread.SpinWait(32);
                        }
                        if (CaptureOnly && (captureFrameDirty || HasStickMovementBetweenRecentSamples())) RefreshCaptureFrame();
                    }
                }
                finally
                {
                    System.Threading.Interlocked.Exchange(ref captureRenderPending, 0);
                    if (CaptureOnly && (captureFrameDirty || HasStickMovementBetweenRecentSamples())) QueueCaptureFrameRefresh();
                }
            });
        }

        private bool StartCaptureServer()
        {
            if (captureListener != null) return true;

            try
            {
                RefreshCaptureFrame();
                for (int port = 39164; port < 39174; port++)
                {
                    var listener = new TcpListener(IPAddress.Loopback, port);
                    try
                    {
                        listener.Start();
                        captureListener = listener;
                        captureSourceUrl = "http://127.0.0.1:" + port + "/";
                        Task.Run(() => CaptureServerLoop(listener));
                        status = "OBS browser overlay ready";
                        return true;
                    }
                    catch (SocketException)
                    {
                        listener.Stop();
                    }
                }
            }
            catch (Exception ex)
            {
                status = "OBS browser overlay could not start: " + ex.Message;
                return false;
            }

            status = "OBS browser overlay could not start: local ports 39164–39173 are in use";
            return false;
        }

        private void StopCaptureServer()
        {
            var listener = captureListener;
            captureListener = null;
            captureSourceUrl = "";
            try { listener?.Stop(); } catch { }
            captureFrameUpdated.Set();
            lock (captureFrameLock) captureFramePng = null;
        }

        private void CaptureServerLoop(TcpListener listener)
        {
            while (ReferenceEquals(listener, captureListener))
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch { break; }
                Task.Run(() => HandleCaptureRequest(client));
            }
        }

        private void HandleCaptureRequest(TcpClient client)
        {
            using (client)
            {
                try
                {
                    using (NetworkStream stream = client.GetStream())
                    using (var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true))
                    {
                        string request = reader.ReadLine() ?? "";
                        string path = request.Split(' ').Skip(1).FirstOrDefault() ?? "/";
                        string webSocketKey = null;
                        string headerLine;
                        while (!string.IsNullOrEmpty(headerLine = reader.ReadLine() ?? ""))
                        {
                            int separator = headerLine.IndexOf(':');
                            if (separator > 0 && string.Equals(headerLine.Substring(0, separator).Trim(), "Sec-WebSocket-Key", StringComparison.OrdinalIgnoreCase))
                                webSocketKey = headerLine.Substring(separator + 1).Trim();
                        }

                        byte[] body;
                        string contentType;
                        if (path == "/" || path.StartsWith("/?", StringComparison.Ordinal))
                        {
                            body = Encoding.UTF8.GetBytes(CaptureOverlayHtml);
                            contentType = "text/html; charset=utf-8";
                        }
                        else if (path.StartsWith("/frame.png", StringComparison.Ordinal))
                        {
                            lock (captureFrameLock) body = captureFramePng;
                            contentType = "image/png";
                        }
                        else if (path.StartsWith("/ws", StringComparison.Ordinal) && !string.IsNullOrEmpty(webSocketKey))
                        {
                            HandleCaptureWebSocket(client, webSocketKey);
                            return;
                        }
                        else
                        {
                            WriteHttpResponse(stream, 404, "Not Found", "text/plain", Array.Empty<byte>());
                            return;
                        }

                        if (body == null)
                        {
                            WriteHttpResponse(stream, 503, "Frame Not Ready", "text/plain", Array.Empty<byte>());
                            return;
                        }
                        WriteHttpResponse(stream, 200, "OK", contentType, body);
                    }
                }
                catch { }
            }
        }

        private static void WriteHttpResponse(NetworkStream stream, int code, string reason, string contentType, byte[] body)
        {
            byte[] headers = Encoding.ASCII.GetBytes(
                "HTTP/1.1 " + code + " " + reason + "\r\n" +
                "Content-Type: " + contentType + "\r\n" +
                "Content-Length: " + body.Length + "\r\n" +
                "Cache-Control: no-store, no-cache, must-revalidate\r\n" +
                "Access-Control-Allow-Origin: *\r\n" +
                "Connection: close\r\n\r\n");
            stream.Write(headers, 0, headers.Length);
            if (body.Length > 0) stream.Write(body, 0, body.Length);
        }

        private void HandleCaptureWebSocket(TcpClient client, string webSocketKey)
        {
            NetworkStream stream = client.GetStream();
            string accept;
            using (var sha1 = System.Security.Cryptography.SHA1.Create())
            {
                byte[] digest = sha1.ComputeHash(Encoding.ASCII.GetBytes(webSocketKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"));
                accept = Convert.ToBase64String(digest);
            }
            byte[] header = Encoding.ASCII.GetBytes(
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                "Sec-WebSocket-Accept: " + accept + "\r\n\r\n");
            stream.Write(header, 0, header.Length);
            byte[] lastFrame = null;
            while (captureListener != null && stream.CanWrite)
            {
                if (client.Client.Poll(0, SelectMode.SelectRead))
                {
                    if (client.Available == 0) break;
                    byte[] incoming = new byte[Math.Min(256, client.Available)];
                    if (stream.Read(incoming, 0, incoming.Length) == 0) break;
                }
                byte[] frame;
                lock (captureFrameLock) frame = captureFramePng;
                if (frame != null && !ReferenceEquals(frame, lastFrame))
                {
                    WriteWebSocketBinaryFrame(stream, frame);
                    lastFrame = frame;
                }
                captureFrameUpdated.WaitOne(250);
            }
        }

        private static void WriteWebSocketBinaryFrame(NetworkStream stream, byte[] payload)
        {
            int prefixLength = payload.Length <= 125 ? 2 : payload.Length <= ushort.MaxValue ? 4 : 10;
            byte[] frameHeader = new byte[prefixLength];
            frameHeader[0] = 0x82;
            if (prefixLength == 2) frameHeader[1] = (byte)payload.Length;
            else if (prefixLength == 4)
            {
                frameHeader[1] = 126;
                frameHeader[2] = (byte)(payload.Length >> 8);
                frameHeader[3] = (byte)payload.Length;
            }
            else
            {
                frameHeader[1] = 127;
                ulong length = (ulong)payload.Length;
                for (int i = 0; i < 8; i++) frameHeader[2 + i] = (byte)(length >> (56 - i * 8));
            }
            stream.Write(frameHeader, 0, frameHeader.Length);
            stream.Write(payload, 0, payload.Length);
        }

        private const string CaptureOverlayHtml = "<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><style>html,body{margin:0;width:100%;height:100%;overflow:hidden;background:transparent}canvas{display:block;width:100vw;height:100vh;object-fit:contain}</style></head><body><canvas></canvas><script>const canvas=document.querySelector('canvas'),ctx=canvas.getContext('2d');function connect(){const ws=new WebSocket('ws://'+location.host+'/ws');ws.binaryType='blob';ws.onmessage=async e=>{try{const bmp=await createImageBitmap(e.data);if(canvas.width!==bmp.width||canvas.height!==bmp.height){canvas.width=bmp.width;canvas.height=bmp.height}ctx.clearRect(0,0,canvas.width,canvas.height);ctx.drawImage(bmp,0,0);bmp.close()}catch(_){}};ws.onclose=()=>setTimeout(connect,100)}connect();</script></body></html>";
    }
}
