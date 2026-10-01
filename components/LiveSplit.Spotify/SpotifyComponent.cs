using LiveSplit.Model;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using System.Xml;

namespace LiveSplit.UI.Components
{
    public sealed class SpotifyComponent : IComponent
    {
        private readonly SpotifyService service = new SpotifyService();
        public SpotifySettings Settings { get; } = new SpotifySettings();
        private volatile bool dirty = true;
        private static bool connecting;
        private readonly IDictionary<string, Action> menuControls;

        public SpotifyComponent(LiveSplitState state)
        {
            service.Changed += () => dirty = true;
            service.StatusChanged += Settings.SetStatus;
            Settings.ConnectRequested += (s, e) => ConnectSpotify();
            Settings.AppearanceChanged += (s, e) => dirty = true;
            menuControls = new Dictionary<string, Action> { { "Connect Spotify", ConnectSpotify } };
        }

        public string ComponentName => "Spotify Now Playing";
        public float VerticalHeight => Settings.ReserveSpace || service.Snapshot != null ? Settings.ComponentHeight : 0f;
        public float MinimumWidth => 80f;
        public float HorizontalWidth => Math.Max(180f, Settings.ComponentHeight * 4f);
        public float MinimumHeight => Settings.ReserveSpace ? Settings.ComponentHeight : 0f;
        public float PaddingTop => 0;
        public float PaddingBottom => 0;
        public float PaddingLeft => 0;
        public float PaddingRight => 0;
        public IDictionary<string, Action> ContextMenuControls => menuControls;

        private async void ConnectSpotify()
        {
            if (connecting) return;
            if (string.IsNullOrWhiteSpace(Settings.ClientId)) {
                using (var prompt = new Form { Text = "Connect Spotify", Width = 470, Height = 190,
                    StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
                    MinimizeBox = false, MaximizeBox = false }) {
                    var label = new Label { Left = 12, Top = 12, Width = 430, Height = 45,
                        Text = "Enter your Spotify app Client ID once. Register this redirect URI:\nhttp://127.0.0.1:43821/callback/" };
                    var id = new TextBox { Left = 12, Top = 63, Width = 430 };
                    var connect = new Button { Text = "Connect", Left = 275, Top = 101, DialogResult = DialogResult.OK };
                    var cancel = new Button { Text = "Cancel", Left = 360, Top = 101, DialogResult = DialogResult.Cancel };
                    prompt.Controls.AddRange(new Control[] { label, id, connect, cancel });
                    prompt.AcceptButton = connect; prompt.CancelButton = cancel;
                    if (prompt.ShowDialog(Form.ActiveForm) != DialogResult.OK || string.IsNullOrWhiteSpace(id.Text)) return;
                    Settings.ClientId = id.Text;
                }
            }
            connecting = true;
            try { await service.ConnectAsync(Settings.ClientId); }
            finally { connecting = false; }
        }

        public void DrawVertical(Graphics g, LiveSplitState state, float width, Region clipRegion) => Draw(g, state, width, VerticalHeight);
        public void DrawHorizontal(Graphics g, LiveSplitState state, float height, Region clipRegion) => Draw(g, state, HorizontalWidth, height);

        private void Draw(Graphics g, LiveSplitState state, float width, float height)
        {
            if (height <= 0 || width <= 0) return;
            var graphicsState = g.Save();
            try {
            g.SetClip(new RectangleF(0, 0, width, height), CombineMode.Intersect);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            if (!Settings.TransparentBackground)
                using (var bg = new SolidBrush(Settings.BackgroundColor)) g.FillRectangle(bg, 0, 0, width, height);

            var t = service.Snapshot;
            if (t == null) return;
            float pad = 7f;
            float art = Math.Min(Settings.ArtworkSize, Math.Max(0, height - 8));
            float artY = (height - art) / 2f;
            if (t.Artwork != null && art > 0) g.DrawImage(t.Artwork, new RectangleF(pad, artY, art, art));

            float x = pad + art + 8f;
            float available = Math.Max(1, width - x - pad);
            using (var titleFont = CreateFont(Settings.TitleFontFamily, state.LayoutSettings.TextFont, Settings.TitleSize, FontStyle.Bold))
            using (var artistFont = CreateFont(Settings.ArtistFontFamily, state.LayoutSettings.TextFont, Settings.ArtistSize, FontStyle.Regular))
            using (var titleBrush = new SolidBrush(Settings.TitleColor.IsEmpty ? state.LayoutSettings.TextColor : Settings.TitleColor))
            using (var artistBrush = new SolidBrush(Settings.ArtistColor.IsEmpty ? Color.FromArgb(175, state.LayoutSettings.TextColor) : Settings.ArtistColor))
            using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            {
                float titleHeight = Settings.TitleText.Length == 0 ? 0 : titleFont.GetHeight(g) + 2;
                float artistHeight = Settings.ArtistText.Length == 0 ? 0 : artistFont.GetHeight(g) + 2;
                float room = Math.Max(0, height - 18);
                float total = titleHeight + artistHeight;
                if (total > room && total > 0) { titleHeight *= room / total; artistHeight *= room / total; }
                float textY = 3 + Math.Max(0, (room - titleHeight - artistHeight) / 2);
                if (titleHeight > 0) g.DrawString(FormatText(Settings.TitleText, t), titleFont, titleBrush, new RectangleF(x, textY, available, titleHeight), sf);
                if (artistHeight > 0) g.DrawString(FormatText(Settings.ArtistText, t), artistFont, artistBrush, new RectangleF(x, textY + titleHeight, available, artistHeight), sf);
            }

            float barY = height - 9f, barH = 3f;
            using (var baseBrush = new SolidBrush(Settings.BarBackgroundColor)) g.FillRectangle(baseBrush, x, barY, available, barH);
            float p = t.DurationMs > 0 ? Math.Max(0f, Math.Min(1f, (float)t.CurrentProgressMs / t.DurationMs)) : 0f;
            using (var fill = new SolidBrush(Settings.BarFillColor)) g.FillRectangle(fill, x, barY, available * p, barH);
            } finally { g.Restore(graphicsState); }
        }

        private static Font CreateFont(string family, Font fallback, float size, FontStyle style) {
            try { return new Font(string.IsNullOrEmpty(family) ? fallback.FontFamily.Name : family, size, style, GraphicsUnit.Pixel); }
            catch (ArgumentException) { return new Font(FontFamily.GenericSansSerif, size, FontStyle.Regular, GraphicsUnit.Pixel); }
        }

        internal static string FormatText(string template, SpotifyTrack track) =>
            System.Text.RegularExpressions.Regex.Replace(template ?? "", "\\{(title|artist)\\}",
                match => match.Groups[1].Value == "title" ? track.Title ?? "" : track.Artist ?? "");

        public Control GetSettingsControl(LayoutMode mode) { Settings.Mode = mode; return Settings; }
        public XmlNode GetSettings(XmlDocument document) => Settings.GetSettings(document);
        public void SetSettings(XmlNode settings) => Settings.SetSettings(settings);
        public int GetSettingsHashCode() => Settings.GetSettingsHashCode();

        public void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode)
        {
            // Progress animates locally between Spotify polls, so repaint while a track exists.
            if (invalidator != null && (dirty || service.Snapshot != null)) { dirty = false; invalidator.Invalidate(0, 0, width, height); }
        }

        public void Dispose() => service.Dispose();
    }
}
