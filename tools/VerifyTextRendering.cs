using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using LiveSplit.UI;

// Run on Windows against a Release build of LiveSplit.Core.dll.
// Arguments: original static font directory, repaired font directory, proof PNG.
class VerifyTextRendering
{
    static readonly MethodInfo Direct = typeof(SimpleLabel).GetMethod("DrawTextDirect", BindingFlags.NonPublic | BindingFlags.Instance);
    static int assertions;
    static void Check(bool ok, string message) { assertions++; if (!ok) throw new Exception(message); }
    static SimpleLabel Label(Font f, int alpha, bool shadow, float blur)
    {
        return new SimpleLabel("Roboto ffi ff tt AV 0123456789 @%&", 12, 8, f,
            new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)), 1060, 105) {
            HasShadow=shadow, OutlineColor=Color.Black, ShadowColor=Color.Black, TextShadowBlur=blur
        };
    }
    static void Settings(Graphics g) { g.TextRenderingHint=TextRenderingHint.AntiAlias; g.SmoothingMode=SmoothingMode.AntiAlias; }
    static StringFormat Format() { return new StringFormat { FormatFlags=StringFormatFlags.NoWrap, Trimming=StringTrimming.EllipsisCharacter }; }
    static Bitmap Render(SimpleLabel label, bool cached)
    {
        Bitmap b=new Bitmap(1100,125);
        using(Graphics g=Graphics.FromImage(b)) using(StringFormat f=Format()) {
            Settings(g); g.Clear(Color.Magenta);
            if(cached) label.Draw(g);
            else Direct.Invoke(label,new object[]{label.Text,g,label.X,label.Y,label.Width,label.Height,f});
        }
        return b;
    }
    static void VerifyFace(string file)
    {
        using(PrivateFontCollection fonts=new PrivateFontCollection()) {
            fonts.AddFontFile(Path.GetFullPath(file));
            bool italic=Path.GetFileNameWithoutExtension(file).EndsWith("Italic");
            FontStyle style=italic?FontStyle.Italic:FontStyle.Regular; if(!fonts.Families[0].Name.Contains("LSFix") && (Path.GetFileNameWithoutExtension(file).EndsWith("-Bold") || Path.GetFileNameWithoutExtension(file).EndsWith("-BoldItalic"))) style |= FontStyle.Bold;
            // Original Roboto weights can be style-linked as Bold.
            if(!fonts.Families[0].IsStyleAvailable(style)) style |= FontStyle.Bold;
            using(Font font=new Font(fonts.Families[0],38,style,GraphicsUnit.Pixel))
            using(Bitmap mask=new Bitmap(1100,125)) {
                var label=Label(font,255,false,0);
                using(Graphics g=Graphics.FromImage(mask)) using(GraphicsPath p=new GraphicsPath(FillMode.Winding)) using(StringFormat f=Format()) {
                    Settings(g); g.Clear(Color.Transparent);
                    p.AddString(label.Text,font.FontFamily,(int)font.Style,38,new RectangleF(12,8,1060,105),f);
                    g.FillPath(Brushes.White,p);
                }
                label.Brush.Dispose();
                foreach(int alpha in new[]{255,128}) {
                    label=Label(font,alpha,false,0);
                    using(Bitmap actual=Render(label,false)) using(Bitmap cached=Render(label,true)) {
                        int inkPixels=0;
                        for(int y=2;y<123;y++) for(int x=2;x<1098;x++) {
                            if(mask.GetPixel(x,y).A>0) inkPixels++; bool deep=true;
                            for(int dy=-2;dy<=2 && deep;dy++) for(int dx=-2;dx<=2;dx++) if(mask.GetPixel(x+dx,y+dy).A!=255){deep=false;break;}
                            if(deep) { Color c=actual.GetPixel(x,y);
                                Check(c.R==255 && c.B==255 && Math.Abs(c.G-alpha)<=1,"Internal outline/hole: "+file+" alpha="+alpha+" at "+x+","+y+" "+c);
                            }
                            Color a=actual.GetPixel(x,y), c2=cached.GetPixel(x,y); if(deep) Check(Math.Abs(a.R-c2.R)<=3 && Math.Abs(a.G-c2.G)<=3 && Math.Abs(a.B-c2.B)<=3,"Cache differs: "+file+" at "+x+","+y+" direct="+a+" cached="+c2);
                        }
                        Check(inkPixels>50,"Font did not render: "+file+" style="+style+" family="+font.FontFamily.Name);
                    }
                    label.Brush.Dispose();
                }
                label=Label(font,255,true,0);
                foreach(float blur in new[]{0f,28f}) {
                    label.TextShadowBlur=blur;
                    using(Bitmap actual=Render(label,false)) {
                        for(int y=2;y<123;y++) for(int x=2;x<1098;x++) {
                            if(mask.GetPixel(x,y).A==255 && mask.GetPixel(x-2,y).A==255 && mask.GetPixel(x+2,y).A==255 && mask.GetPixel(x,y-2).A==255 && mask.GetPixel(x,y+2).A==255)
                                Check(actual.GetPixel(x,y).ToArgb()==Color.White.ToArgb(),"Shadow reset changed winding: "+file);
                        }
                    }
                }
                label.Brush.Dispose();
            }
        }
    }
    static void Geometry()
    {
        MethodInfo outline=typeof(SimpleLabel).GetMethod("DrawExteriorTextOutline",BindingFlags.NonPublic|BindingFlags.Static);
        using(Bitmap b=new Bitmap(120,100)) using(Graphics g=Graphics.FromImage(b)) using(GraphicsPath p=new GraphicsPath(FillMode.Winding)) using(Pen pen=new Pen(Color.Black,6)) {
            g.Clear(Color.Magenta); p.AddRectangle(new Rectangle(20,20,50,40)); p.AddRectangle(new Rectangle(50,20,50,40));
            outline.Invoke(null,new object[]{g,p,pen});
            Check(b.GetPixel(50,40).ToArgb()==Color.Magenta.ToArgb(),"Internal contour seam was stroked");
            Check(b.GetPixel(18,40).ToArgb()==Color.Black.ToArgb(),"Exterior outline missing");
            g.FillRectangle(Brushes.Lime,30,30,3,3);
            Check(b.GetPixel(31,31).ToArgb()==Color.Lime.ToArgb(),"Graphics clip not restored");
        }
        using(Bitmap b=new Bitmap(100,100)) using(Graphics g=Graphics.FromImage(b)) using(GraphicsPath p=new GraphicsPath(FillMode.Winding)) using(GraphicsPath hole=new GraphicsPath()) using(Pen pen=new Pen(Color.Black,6)) {
            g.Clear(Color.Magenta); p.AddEllipse(10,10,80,80); hole.AddEllipse(30,30,40,40); hole.Reverse(); p.AddPath(hole,false);
            outline.Invoke(null,new object[]{g,p,pen});g.FillPath(Brushes.White,p);
            Check(b.GetPixel(50,50).ToArgb()==Color.Magenta.ToArgb(),"Counter was filled");
            Check(b.GetPixel(50,31).ToArgb()==Color.Black.ToArgb(),"Counter outline missing");
        }
    }
    static void Proof(string original,string repaired,string output)
    {
        using(Bitmap b=new Bitmap(1400,440)) using(Graphics g=Graphics.FromImage(b)) using(Font heading=new Font("Arial",15)) {
            Settings(g);g.Clear(Color.FromArgb(32,32,38));
            g.DrawString("Before: original renderer + original Roboto",heading,Brushes.White,16,12);
            g.DrawString("After: corrected renderer + original Roboto",heading,Brushes.White,716,12);
            using(PrivateFontCollection orig=new PrivateFontCollection()) using(PrivateFontCollection fixedFonts=new PrivateFontCollection()) {
                orig.AddFontFile(Path.Combine(original,"Roboto-Black.ttf"));fixedFonts.AddFontFile(Path.Combine(repaired,"Roboto-Black.ttf"));
                FontStyle style=orig.Families[0].IsStyleAvailable(FontStyle.Regular)?FontStyle.Regular:FontStyle.Bold;
                using(Font f=new Font(orig.Families[0],64,style,GraphicsUnit.Pixel)) using(Font ff=new Font(fixedFonts.Families[0],64,FontStyle.Regular,GraphicsUnit.Pixel)) using(StringFormat fmt=Format()) {
                    string text="Roboto ffi tt 08 @&";
                    using(GraphicsPath p=new GraphicsPath()) using(Pen pen=new Pen(Color.Black,5.62f)) {
                        pen.LineJoin=LineJoin.Round;p.AddString(text,f.FontFamily,(int)f.Style,64,new RectangleF(12,65,680,105),fmt);g.DrawPath(pen,p);g.FillPath(Brushes.White,p);
                    }
                    var label=new SimpleLabel(text,712,65,f,Brushes.White,680,105){HasShadow=false,OutlineColor=Color.Black};
                    Direct.Invoke(label,new object[]{text,g,712f,65f,680f,105f,fmt});
                    g.DrawString("After: repaired Roboto LSFix + corrected renderer",heading,Brushes.White,16,235);
                    label.Font=ff;Direct.Invoke(label,new object[]{text,g,12f,290f,1300f,110f,fmt});
                }
            }
            b.Save(output,ImageFormat.Png);
        }
    }
    static int Main(string[] args)
    {
        try { if(args.Length==1) { VerifyFace(args[0]); Console.WriteLine("PASS single face"); return 0; } Geometry(); foreach(string folder in new[]{args[0],args[1]}) foreach(string file in Directory.GetFiles(folder,"*.ttf")) {VerifyFace(file);Console.WriteLine("PASS "+Path.GetFileName(file));}
            Proof(Path.GetFullPath(args[0]),Path.GetFullPath(args[1]),args[2]);Console.WriteLine("PASS "+assertions+" assertions");return 0;
        } catch(Exception e) {Console.Error.WriteLine(e);return 1;}
    }
}
