using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace LiveSplit.UI.Components
{
    internal sealed class N64Skin : IDisposable
    {
        public string Name { get; private set; }
        public string Author { get; private set; }
        public string Folder { get; private set; }
        public string BackgroundVariantName { get; private set; }
        public IReadOnlyList<SkinVariantInfo> BackgroundVariants { get; private set; }
        public Bitmap Background { get; private set; }
        public Color BackgroundColor { get; private set; }
        public float Width { get; private set; }
        public float Height { get; private set; }
        public float AspectRatio => Height <= 0 ? 1 : Width / Height;
        public readonly List<SkinButton> Buttons = new List<SkinButton>();
        public readonly List<SkinStick> Sticks = new List<SkinStick>();
        private readonly List<Bitmap> images = new List<Bitmap>();

        public static IReadOnlyList<SkinInfo> FindSkins(string root)
        {
            var results = new List<SkinInfo>();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return results;
            var candidates = new List<string>();
            if (File.Exists(Path.Combine(root, "skin.xml"))) candidates.Add(root);
            try
            {
                foreach (string dir in Directory.GetDirectories(root))
                    if (File.Exists(Path.Combine(dir, "skin.xml"))) candidates.Add(dir);
            }
            catch { return results; }

            foreach (string dir in candidates.OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var doc = XDocument.Load(Path.Combine(dir, "skin.xml"));
                    var type = (string)doc.Root?.Attribute("type") ?? "";
                    if (!type.Split(';').Any(t => t.Trim().StartsWith("n64", StringComparison.OrdinalIgnoreCase))) continue;
                    string name = (string)doc.Root?.Attribute("name") ?? Path.GetFileName(dir);
                    results.Add(new SkinInfo(name, dir));
                }
                catch { }
            }
            return results;
        }

        public static N64Skin Load(string folder, string variantName = null)
        {
            var doc = XDocument.Load(Path.Combine(folder, "skin.xml"));
            var root = doc.Root ?? throw new InvalidDataException("Skin XML has no root element.");
            var type = (string)root.Attribute("type") ?? "";
            if (!type.Split(';').Any(t => t.Trim().StartsWith("n64", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("This folder does not contain an N64 skin.");

            var backgrounds = root.Elements("background").ToList();
            var variants = CreateVariantInfos(backgrounds);
            var selectedVariant = variants.FirstOrDefault(v => string.Equals(v.Name, variantName, StringComparison.OrdinalIgnoreCase)) ?? variants.FirstOrDefault();
            if (selectedVariant == null) throw new InvalidDataException("Skin XML has no background element.");

            var result = new N64Skin
            {
                Name = (string)root.Attribute("name") ?? Path.GetFileName(folder),
                Author = (string)root.Attribute("author") ?? "",
                Folder = folder,
                BackgroundVariantName = selectedVariant.Name,
                BackgroundVariants = variants
            };

            try
            {
                XElement background = backgrounds[selectedVariant.Index];
                result.BackgroundColor = ParseColor((string)background.Attribute("color"));
                string image = (string)background.Attribute("image");
                if (!string.IsNullOrWhiteSpace(image)) result.Background = result.LoadImage(image);
                result.Width = ReadFloat(background, "width", result.Background?.Width ?? 0);
                result.Height = ReadFloat(background, "height", result.Background?.Height ?? 0);
                if (result.Width <= 0 || result.Height <= 0)
                    throw new InvalidDataException("Skin background needs an image or nonzero width and height.");

                foreach (var elem in root.Elements("detail"))
                    result.Buttons.Add(result.ParseButton(elem, "", false, true));
                foreach (var elem in root.Elements("button"))
                    result.Buttons.Add(result.ParseButton(elem, (string)elem.Attribute("name") ?? "", ReadBool(elem, "invert")));
                foreach (var elem in root.Elements("stick"))
                    result.Sticks.Add(result.ParseStick(elem));
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        private static List<SkinVariantInfo> CreateVariantInfos(IReadOnlyList<XElement> backgrounds)
        {
            var results = new List<SkinVariantInfo>();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < backgrounds.Count; i++)
            {
                string name = (string)backgrounds[i].Attribute("name");
                if (string.IsNullOrWhiteSpace(name)) name = "Variant " + (i + 1);
                string uniqueName = name;
                int duplicate = 2;
                while (!usedNames.Add(uniqueName)) uniqueName = name + " (" + duplicate++ + ")";
                results.Add(new SkinVariantInfo(uniqueName, i));
            }
            return results;
        }

        private SkinButton ParseButton(XElement elem, string name, bool invert, bool alwaysVisible = false)
        {
            string image = (string)elem.Attribute("image");
            if (string.IsNullOrWhiteSpace(image)) throw new InvalidDataException("A skin button is missing its image.");
            var bitmap = LoadImage(image);
            return new SkinButton(name, bitmap,
                ReadFloat(elem, "x", 0), ReadFloat(elem, "y", 0),
                ReadFloat(elem, "width", bitmap.Width), ReadFloat(elem, "height", bitmap.Height), invert, alwaysVisible);
        }

        private SkinStick ParseStick(XElement elem)
        {
            string image = (string)elem.Attribute("image");
            if (string.IsNullOrWhiteSpace(image)) throw new InvalidDataException("A skin stick is missing its image.");
            var bitmap = LoadImage(image);
            return new SkinStick(bitmap,
                ReadFloat(elem, "x", 0), ReadFloat(elem, "y", 0),
                ReadFloat(elem, "width", bitmap.Width), ReadFloat(elem, "height", bitmap.Height),
                ReadFloat(elem, "xrange", 0), ReadFloat(elem, "yrange", 0),
                ReadFloat(elem, "maxcardinal", -1), ReadFloat(elem, "maxdiagonal", -1),
                ReadFloat(elem, "diagonaldeadzone", 0),
                ReadFloat(elem, "centerx", float.NaN), ReadFloat(elem, "centery", float.NaN),
                ReadPoints((string)elem.Attribute("boundarypoints")), ReadFloat(elem, "boundarymargin", 0),
                (string)elem.Attribute("xname") ?? "stick_x", (string)elem.Attribute("yname") ?? "stick_y",
                ReadBool(elem, "xreverse"), ReadBool(elem, "yreverse"));
        }

        private static PointF[] ReadPoints(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return Array.Empty<PointF>();
            var points = new List<PointF>();
            foreach (string pair in value.Split(';'))
            {
                string[] coordinates = pair.Split(',');
                if (coordinates.Length != 2 ||
                    !float.TryParse(coordinates[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) ||
                    !float.TryParse(coordinates[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y))
                    return Array.Empty<PointF>();
                points.Add(new PointF(x, y));
            }
            return points.Count >= 3 ? points.ToArray() : Array.Empty<PointF>();
        }

        private Bitmap LoadImage(string image)
        {
            string baseFolder = Path.GetFullPath(Folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(Folder, image));
            if (!path.StartsWith(baseFolder, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Skin image paths must stay inside the selected skin folder.");
            using (var loaded = new Bitmap(path))
            {
                var copy = new Bitmap(loaded);
                images.Add(copy);
                return copy;
            }
        }

        private static float ReadFloat(XElement elem, string name, float fallback)
        {
            var attr = elem.Attribute(name);
            return attr != null && float.TryParse(attr.Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float value) ? value : fallback;
        }

        private static bool ReadBool(XElement elem, string name)
            => bool.TryParse((string)elem.Attribute(name), out bool value) && value;

        private static Color ParseColor(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return Color.Empty;
            try { return ColorTranslator.FromHtml(value); } catch { return Color.Empty; }
        }

        public void Dispose()
        {
            foreach (var bitmap in images) bitmap.Dispose();
            images.Clear();
        }
    }

    internal sealed class SkinInfo
    {
        public string Name { get; }
        public string Folder { get; }
        public SkinInfo(string name, string folder) { Name = name; Folder = folder; }
    }

    internal sealed class SkinVariantInfo
    {
        public string Name { get; }
        public int Index { get; }
        public SkinVariantInfo(string name, int index) { Name = name; Index = index; }
        public override string ToString() => Name;
    }

    internal sealed class SkinButton
    {
        public string Name { get; }
        public Bitmap Image { get; }
        public RectangleF Bounds { get; }
        public bool Invert { get; }
        public bool AlwaysVisible { get; }
        public SkinButton(string name, Bitmap image, float x, float y, float width, float height, bool invert, bool alwaysVisible)
        { Name = name; Image = image; Bounds = new RectangleF(x, y, width, height); Invert = invert; AlwaysVisible = alwaysVisible; }
    }

    internal sealed class SkinStick
    {
        public Bitmap Image { get; }
        public RectangleF Bounds { get; }
        public float XRange { get; }
        public float YRange { get; }
        public float MaxCardinalMovement { get; }
        public float MaxDiagonalMovement { get; }
        public float DiagonalDeadzone { get; }
        public float CenterX { get; }
        public float CenterY { get; }
        public PointF[] MovementBoundary { get; }
        public float BoundaryMargin { get; }
        public string XName { get; }
        public string YName { get; }
        public bool XReverse { get; }
        public bool YReverse { get; }
        public SkinStick(Bitmap image, float x, float y, float width, float height, float xrange, float yrange,
            float maxCardinalMovement, float maxDiagonalMovement, float diagonalDeadzone, float centerX, float centerY,
            PointF[] movementBoundary, float boundaryMargin,
            string xname, string yname, bool xreverse, bool yreverse)
        {
            Image = image; Bounds = new RectangleF(x, y, width, height); XRange = xrange; YRange = yrange;
            MaxCardinalMovement = maxCardinalMovement; MaxDiagonalMovement = maxDiagonalMovement;
            DiagonalDeadzone = Math.Max(0, diagonalDeadzone) / 128f;
            CenterX = centerX; CenterY = centerY; MovementBoundary = movementBoundary; BoundaryMargin = boundaryMargin;
            XName = xname; YName = yname; XReverse = xreverse; YReverse = yreverse;
        }
    }
}
