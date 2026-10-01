using System;
using System.Drawing;
using System.Windows.Forms;
using System.Xml;

namespace LiveSplit.UI.Components
{
    public sealed class SpotifySettings : UserControl
    {
        private readonly TextBox clientIdBox = new TextBox();
        private readonly Button connectButton = new Button();
        private readonly Label statusLabel = new Label();
        private readonly NumericUpDown heightBox = new NumericUpDown();
        private readonly NumericUpDown artworkBox = new NumericUpDown();
        private readonly NumericUpDown titleBox = new NumericUpDown();
        private readonly NumericUpDown artistBox = new NumericUpDown();
        private readonly CheckBox reserveSpaceBox = new CheckBox();
        private readonly CheckBox transparentBox = new CheckBox { Text = "Transparent background", AutoSize = true, Checked = true };
        private readonly TextBox titleTextBox = new TextBox { Text = "{title}" };
        private readonly TextBox artistTextBox = new TextBox { Text = "{artist}" };
        private readonly ComboBox titleFontBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ComboBox artistFontBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly Button titleColorButton = new Button();
        private readonly Button artistColorButton = new Button();
        private readonly Button backgroundButton = new Button();
        private readonly Button barFillButton = new Button();
        private readonly Button barBackgroundButton = new Button();

        public event EventHandler AppearanceChanged;
        public bool TransparentBackground { get => transparentBox.Checked; set => transparentBox.Checked = value; }
        public string TitleText { get => titleTextBox.Text; set => titleTextBox.Text = value ?? "{title}"; }
        public string ArtistText { get => artistTextBox.Text; set => artistTextBox.Text = value ?? "{artist}"; }
        public string TitleFontFamily { get => FontName(titleFontBox); set => SelectFont(titleFontBox, value); }
        public string ArtistFontFamily { get => FontName(artistFontBox); set => SelectFont(artistFontBox, value); }
        public Color TitleColor { get => ButtonColor(titleColorButton); set => SetColor(titleColorButton, value); }
        public Color ArtistColor { get => ButtonColor(artistColorButton); set => SetColor(artistColorButton, value); }
        public Color BackgroundColor { get => ButtonColor(backgroundButton); set => SetColor(backgroundButton, value); }
        public Color BarFillColor { get => ButtonColor(barFillButton); set => SetColor(barFillButton, value); }
        public Color BarBackgroundColor { get => ButtonColor(barBackgroundButton); set => SetColor(barBackgroundButton, value); }

        public event EventHandler ConnectRequested;
        public LayoutMode Mode { get; set; }

        public string ClientId { get => clientIdBox.Text.Trim(); set => clientIdBox.Text = value ?? ""; }
        public float ComponentHeight { get => (float)heightBox.Value; set => heightBox.Value = Clamp(value, heightBox); }
        public float ArtworkSize { get => (float)artworkBox.Value; set => artworkBox.Value = Clamp(value, artworkBox); }
        public float TitleSize { get => (float)titleBox.Value; set => titleBox.Value = Clamp(value, titleBox); }
        public float ArtistSize { get => (float)artistBox.Value; set => artistBox.Value = Clamp(value, artistBox); }
        public bool ReserveSpace { get => reserveSpaceBox.Checked; set => reserveSpaceBox.Checked = value; }

        public SpotifySettings()
        {
            Dock = DockStyle.Fill;
            AutoSize = true;
            AutoScroll = true;
            Padding = new Padding(8);

            heightBox.Minimum = 32; heightBox.Maximum = 180; heightBox.Value = 58;
            artworkBox.Minimum = 20; artworkBox.Maximum = 140; artworkBox.Value = 42;
            titleBox.Minimum = 8; titleBox.Maximum = 40; titleBox.Value = 11;
            artistBox.Minimum = 7; artistBox.Maximum = 32; artistBox.Value = 9;
            titleBox.Maximum = artistBox.Maximum = 120;
            titleBox.DecimalPlaces = artistBox.DecimalPlaces = 1;
            titleBox.Increment = artistBox.Increment = 0.5m;
            heightBox.Maximum = 500;
            titleFontBox.Items.Add("(Layout font)");
            artistFontBox.Items.Add("(Layout font)");
            using (var fonts = new System.Drawing.Text.InstalledFontCollection())
                foreach (var family in fonts.Families) {
                    titleFontBox.Items.Add(family.Name);
                    artistFontBox.Items.Add(family.Name);
                }
            titleFontBox.SelectedIndex = artistFontBox.SelectedIndex = 0;
            TitleColor = Color.Empty;
            ArtistColor = Color.Empty;
            BackgroundColor = Color.FromArgb(18, 18, 18);
            BarFillColor = Color.FromArgb(205, 255, 255, 255);
            BarBackgroundColor = Color.FromArgb(38, 255, 255, 255);
            foreach (var button in new[] { titleColorButton, artistColorButton, backgroundButton, barFillButton, barBackgroundButton })
                button.Click += ChooseColor;
            reserveSpaceBox.Text = "Reserve component space when nothing is playing";
            reserveSpaceBox.Checked = true;
            reserveSpaceBox.AutoSize = true;
            connectButton.Text = "Connect Spotify";
            connectButton.AutoSize = true;
            connectButton.Click += (s, e) => ConnectRequested?.Invoke(this, EventArgs.Empty);
            statusLabel.Text = "Not connected";
            statusLabel.AutoSize = true;

            var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddRow(table, "Spotify Client ID", clientIdBox);
            AddRow(table, "Component Height", heightBox);
            AddRow(table, "Album Art Size", artworkBox);
            AddRow(table, "Background", transparentBox);
            AddRow(table, "Background color", backgroundButton);
            AddRow(table, "Song font", titleFontBox);
            AddRow(table, "Song size (pixels)", titleBox);
            AddRow(table, "Song color", titleColorButton);
            AddRow(table, "Song text", titleTextBox);
            AddRow(table, "Artist font", artistFontBox);
            AddRow(table, "Artist size (pixels)", artistBox);
            AddRow(table, "Artist color", artistColorButton);
            AddRow(table, "Artist text", artistTextBox);
            AddRow(table, "Text placeholders", new Label { Text = "{title} = song, {artist} = artist. Use an empty field to hide a line.", AutoSize = true });
            AddRow(table, "Progress bar fill", barFillButton);
            AddRow(table, "Progress bar background", barBackgroundButton);
            table.Controls.Add(reserveSpaceBox, 0, table.RowCount); table.SetColumnSpan(reserveSpaceBox, 2); table.RowCount++;
            table.Controls.Add(connectButton, 0, table.RowCount); table.Controls.Add(statusLabel, 1, table.RowCount); table.RowCount++;
            Controls.Add(table);
            foreach (var box in new[] { titleTextBox, artistTextBox }) box.TextChanged += OnAppearanceChanged;
            foreach (var box in new[] { titleFontBox, artistFontBox }) box.SelectedIndexChanged += OnAppearanceChanged;
            foreach (var box in new[] { heightBox, artworkBox, titleBox, artistBox }) box.ValueChanged += OnAppearanceChanged;
            reserveSpaceBox.CheckedChanged += OnAppearanceChanged;
            transparentBox.CheckedChanged += (s, e) => {
                backgroundButton.Enabled = !TransparentBackground;
                OnAppearanceChanged(s, e);
            };
            backgroundButton.Enabled = false;
        }

        private void OnAppearanceChanged(object sender, EventArgs e) => AppearanceChanged?.Invoke(this, EventArgs.Empty);
        private static string FontName(ComboBox box) => box.SelectedIndex <= 0 ? "" : (string)box.SelectedItem;
        private static void SelectFont(ComboBox box, string name) {
            if (string.IsNullOrEmpty(name)) { box.SelectedIndex = 0; return; }
            int index = box.Items.IndexOf(name);
            if (index < 0) index = box.Items.Add(name);
            box.SelectedIndex = index;
        }
        private static Color ButtonColor(Button button) => button.Tag is Color c ? c : Color.Empty;
        private static void SetColor(Button button, Color color) {
            button.Tag = color;
            button.Text = color.IsEmpty ? "Layout color" : "#" + color.ToArgb().ToString("X8");
            button.AutoSize = true;
        }
        private void ChooseColor(object sender, EventArgs e) {
            var button = (Button)sender;
            using (var dialog = new Form { Text = "Choose color", Width = 350, Height = 195,
                FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false, MaximizeBox = false }) {
                var color = ButtonColor(button);
                var picker = new Button { Text = "Choose RGB color…", Left = 12, Top = 12, Width = 305 };
                var opacity = new NumericUpDown { Left = 150, Top = 52, Width = 100, Minimum = 0, Maximum = 255, Value = color.IsEmpty ? 255 : color.A };
                var inherit = new CheckBox { Text = "Use layout text color", Left = 12, Top = 84, Width = 300,
                    Visible = button == titleColorButton || button == artistColorButton, Checked = color.IsEmpty };
                var ok = new Button { Text = "OK", Left = 160, Top = 116, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", Left = 243, Top = 116, DialogResult = DialogResult.Cancel };
                var rgb = color.IsEmpty ? Color.White : Color.FromArgb(255, color);
                picker.BackColor = rgb;
                picker.Click += (s, args) => { using (var chooser = new ColorDialog { Color = rgb, FullOpen = true }) {
                    if (chooser.ShowDialog(dialog) == DialogResult.OK) { rgb = chooser.Color; picker.BackColor = rgb; inherit.Checked = false; }
                } };
                dialog.Controls.AddRange(new Control[] { picker, new Label { Text = "Opacity (0–255)", Left = 12, Top = 55, AutoSize = true }, opacity, inherit, ok, cancel });
                dialog.AcceptButton = ok; dialog.CancelButton = cancel;
                if (dialog.ShowDialog(this) == DialogResult.OK) {
                    SetColor(button, inherit.Visible && inherit.Checked ? Color.Empty : Color.FromArgb((int)opacity.Value, rgb));
                    OnAppearanceChanged(this, EventArgs.Empty);
                }
            }
        }

        private static decimal Clamp(float value, NumericUpDown n) => Math.Max(n.Minimum, Math.Min(n.Maximum, (decimal)value));
        private static void AddRow(TableLayoutPanel t, string label, Control c)
        {
            int row = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 8, 3) }, 0, row);
            c.Dock = DockStyle.Fill;
            t.Controls.Add(c, 1, row);
        }

        public void SetStatus(string status)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(SetStatus), status); return; }
            statusLabel.Text = status;
        }

        public XmlNode GetSettings(XmlDocument document)
        {
            var parent = document.CreateElement("Settings");
            Append(document, parent, "Version", "1.0");
            Append(document, parent, "ClientId", ClientId);
            Append(document, parent, "ComponentHeight", ComponentHeight.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(document, parent, "ArtworkSize", ArtworkSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(document, parent, "TitleSize", TitleSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(document, parent, "ArtistSize", ArtistSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(document, parent, "ReserveSpace", ReserveSpace.ToString());
            Append(document, parent, "TransparentBackground", TransparentBackground.ToString());
            Append(document, parent, "TitleFontFamily", TitleFontFamily);
            Append(document, parent, "ArtistFontFamily", ArtistFontFamily);
            Append(document, parent, "TitleText", TitleText);
            Append(document, parent, "ArtistText", ArtistText);
            foreach (var pair in new[] { Tuple.Create("TitleColor", TitleColor), Tuple.Create("ArtistColor", ArtistColor),
                Tuple.Create("BackgroundColor", BackgroundColor), Tuple.Create("BarFillColor", BarFillColor), Tuple.Create("BarBackgroundColor", BarBackgroundColor) })
                Append(document, parent, pair.Item1, pair.Item2.IsEmpty ? "" : pair.Item2.ToArgb().ToString("X8"));
            return parent;
        }

        public void SetSettings(XmlNode node)
        {
            if (node == null) return;
            ClientId = Read(node, "ClientId", ClientId);
            ComponentHeight = ReadFloat(node, "ComponentHeight", 58);
            ArtworkSize = ReadFloat(node, "ArtworkSize", 42);
            TitleSize = ReadFloat(node, "TitleSize", 11);
            ArtistSize = ReadFloat(node, "ArtistSize", 9);
            bool reserve; ReserveSpace = bool.TryParse(Read(node, "ReserveSpace", "True"), out reserve) ? reserve : true;
            bool transparent; TransparentBackground = !bool.TryParse(Read(node, "TransparentBackground", "True"), out transparent) || transparent;
            TitleFontFamily = Read(node, "TitleFontFamily", "");
            ArtistFontFamily = Read(node, "ArtistFontFamily", "");
            TitleText = Read(node, "TitleText", "{title}");
            ArtistText = Read(node, "ArtistText", "{artist}");
            TitleColor = ReadColor(node, "TitleColor", Color.Empty);
            ArtistColor = ReadColor(node, "ArtistColor", Color.Empty);
            BackgroundColor = ReadColor(node, "BackgroundColor", Color.FromArgb(18, 18, 18));
            BarFillColor = ReadColor(node, "BarFillColor", Color.FromArgb(205, 255, 255, 255));
            BarBackgroundColor = ReadColor(node, "BarBackgroundColor", Color.FromArgb(38, 255, 255, 255));
            OnAppearanceChanged(this, EventArgs.Empty);
        }

        private static Color ReadColor(XmlNode n, string name, Color fallback) {
            string text = Read(n, name, null);
            if (text == null) return fallback;
            if (text.Length == 0) return Color.Empty;
            int argb;
            return int.TryParse(text, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out argb) ? Color.FromArgb(argb) : fallback;
        }

        private static void Append(XmlDocument d, XmlElement p, string name, string value) { var e = d.CreateElement(name); e.InnerText = value ?? ""; p.AppendChild(e); }
        private static string Read(XmlNode n, string name, string fallback) => n.SelectSingleNode(name)?.InnerText ?? fallback;
        private static float ReadFloat(XmlNode n, string name, float fallback) { float v; return float.TryParse(Read(n, name, ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : fallback; }
        public int GetSettingsHashCode() {
            unchecked {
                int hash = 17;
                foreach (var value in new object[] { ClientId, ComponentHeight, ArtworkSize, TitleSize, ArtistSize, ReserveSpace,
                    TransparentBackground, TitleFontFamily, ArtistFontFamily, TitleText, ArtistText, TitleColor, ArtistColor,
                    BackgroundColor, BarFillColor, BarBackgroundColor }) hash = hash * 31 + value.GetHashCode();
                return hash;
            }
        }
    }
}
