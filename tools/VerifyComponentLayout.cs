using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Xml;
using LiveSplit.Model;
using LiveSplit.Options.SettingsFactories;
using LiveSplit.UI;
using LiveSplit.UI.Components;
using LiveSplit.WorldRecord.UI.Components;

// Windows regression runner. See COMPONENT-LAYOUT-FIX.md for build/run commands.
class VerifyComponentLayout
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(obj,args);
    static T Property<T>(object obj,string name) => (T)obj.GetType().GetProperty(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).GetValue(obj);
    static LiveSplitState State()
    {
        var run = new Run(new LiveSplit.Model.Comparisons.StandardComparisonGeneratorsFactory()); run.Add(new Segment("Test split"));
        var settings = new StandardLayoutSettingsFactory().Create();
        settings.TextFont = new Font("Arial",16,FontStyle.Italic,GraphicsUnit.Pixel);
        settings.DropShadows = true; settings.TextOutlineColor = Color.Black;
        return new LiveSplitState(run,null,new Layout{Settings=settings},settings,new LiveSplit.Options.Settings());
    }
    static void GraphicsSettings(Graphics g) {g.TextRenderingHint=TextRenderingHint.AntiAlias;g.SmoothingMode=SmoothingMode.AntiAlias;}
    class TestSplit : SplitComponent
    {
        public TestSplit(SplitsSettings settings) : base(settings,Array.Empty<ColumnData>(),new List<(int,float,float)>()) {NeedUpdateAll=false;}
        public SimpleLabel Name => NameLabel;
        public int IconSpace => IconWidth;
    }
    static void Splits(LiveSplitState state)
    {
        using(var settings=new SplitsSettings(state)) using(var icon=new Bitmap(24,24)) using(var b=new Bitmap(240,60)) using(var g=Graphics.FromImage(b)) {
            settings.DisplayIcons=true;settings.IconShadows=false;
            var row=new TestSplit(settings){DisplayIcon=true,Split=state.Run[0]};
            row.Name.Text="No icon";row.DrawVertical(g,state,240,null);
            Check(row.IconSpace==0 && row.Name.X==5,"Iconless split indented");
            state.Run[0].Icon=icon;row.DrawVertical(g,state,240,null);
            Check(row.IconSpace>0 && row.Name.X>5,"Icon split lost spacing");
            state.Run[0].Icon=null;row.DrawVertical(g,state,240,null);
            Check(row.IconSpace==0 && row.Name.X==5,"Reused split retained old indent");
            state.Run[0].Icon=icon;row.DisplayIcon=false;row.DrawVertical(g,state,240,null);
            Check(row.IconSpace==0 && row.Name.X==5,"Hidden icons still reserve space");
            state.Run[0].Icon=null;
        }
    }
    static void Labels(LiveSplitState state)
    {
        using(var b=new Bitmap(300,100)) using(var g=Graphics.FromImage(b)) {
            GraphicsSettings(g);g.Clear(Color.Magenta);
            var empty=new SimpleLabel("This must not be drawn",10,5,state.LayoutSettings.TextFont,Brushes.White,0,40){ClipToWidth=true};
            empty.Draw(g);empty.Width=-10;empty.Draw(g);
            Check(b.GetPixel(15,15).ToArgb()==Color.Magenta.ToArgb(),"Zero/negative width painted");
            var info=new InfoTextComponent("A very long left label","A long value which fills the whole row");
            info.ContentInsetLeft=30;info.ContentInsetRight=35;
            foreach(var width in new[]{10f,60f,100f,260f}) {
                info.ComputeVerticalLayout(g,state,width);
                Check(info.NameLabel.Width>=0 && info.ValueLabel.Width>=0,"Negative info-label width");
                if(info.NameLabel.Width>0) Check(info.NameLabel.X+info.NameLabel.Width<=info.ValueLabel.X,"Name overlaps value");
                info.DrawVerticalLabels(g);
                info.DisplayTwoRows=true;info.ComputeVerticalLayout(g,state,width);
                Check(info.NameLabel.Width>=0 && info.ValueLabel.Width>=0,"Negative two-row width");
                info.DrawVerticalLabels(g);info.DisplayTwoRows=false;
            }
            info.InformationName="Centered text";info.InformationValue="";info.ComputeVerticalLayout(g,state,260);
            Check(info.NameLabel.Width==185,"Empty value reserved unexpected space");
            using(var text=new TextComponent(state)) {
                text.Settings.Text1="Long overridden font label with many words";text.Settings.Text2="Value";
                text.Settings.OverrideFont1=true;text.Settings.Font1=new Font("Arial",32,GraphicsUnit.Pixel);
                text.Update(null,state,160,60,LayoutMode.Vertical);
                text.DrawVertical(g,state,160,null);
                var labels=Property<TextTextComponent>(text,"InternalComponent");
                Check(labels.NameLabel.Font.Size==32,"Text override font not applied before layout");
                Check(labels.NameLabel.X+labels.NameLabel.Width<=labels.ValueLabel.X,"Text columns overlap");
                Check(labels.ValueLabel.X+labels.ValueLabel.Width<=155.01f,"Text value escaped width");
                text.Settings.Dispose();
            }
            // Check actual pixels, including outline/shadow overhang, outside a label band.
            g.Clear(Color.Magenta);
            var clipped=new SimpleLabel("ffffffffffff",65,0,state.LayoutSettings.TextFont,Brushes.White,60,55){ClipToWidth=true,OutlineColor=Color.Black,HasShadow=true};
            clipped.Draw(g);
            for(int y=0;y<90;y++) for(int x=0;x<300;x++) if(x<65||x>=125)
                Check(b.GetPixel(x,y).ToArgb()==Color.Magenta.ToArgb(),"Text escaped horizontal clip");
            g.FillRectangle(Brushes.Lime,0,0,5,5);
            Check(b.GetPixel(2,2).ToArgb()==Color.Lime.ToArgb(),"Label clip was not restored");
        }
    }
    static void WorldRecord(LiveSplitState state,string proof)
    {
        using(var wr=new WorldRecordComponent(state)) using(var image=new Bitmap(900,380)) using(var proofGraphics=Graphics.FromImage(image)) {
            var settings=Property<WorldRecordSettings>(wr,"Settings");
            var inner=Property<InfoTextComponent>(wr,"InternalComponent");
            GraphicsSettings(proofGraphics);proofGraphics.Clear(Color.FromArgb(35,35,42));
            int scenarios=0;
            foreach(var shape in new[]{new Size(24,24),new Size(12,48),new Size(48,12)}) using(var icon=new Bitmap(shape.Width,shape.Height)) {
                using(var ig=Graphics.FromImage(icon)) ig.Clear(Color.Lime);
                state.Run.GameIcon=icon;
                foreach(bool twoRows in new[]{false,true}) foreach(bool centered in new[]{false,true})
                foreach(WorldRecordGameIconSide side in Enum.GetValues(typeof(WorldRecordGameIconSide)))
                foreach(WorldRecordGameIconPlacing placing in Enum.GetValues(typeof(WorldRecordGameIconPlacing)))
                foreach(int offset in new[]{-100,0,100}) foreach(float width in new[]{12f,45f,100f,220f,440f}) {
                    settings.DisplayGameIcon=true;settings.Display2Rows=twoRows;settings.CenteredText=centered;settings.GameIconSide=side;settings.GameIconPlacing=placing;
                    settings.IconHorizontalOffset=offset;settings.TextHorizontalOffset=-offset;
                    inner.InformationName="World Record is 12:34.56 by a very long runner name";inner.AlternateNameText=Array.Empty<string>();inner.InformationValue=centered&&!twoRows?"":"12:34.56 by a long name";
                    using(var bitmap=new Bitmap(500,180)) using(var g=Graphics.FromImage(bitmap)) {
                        GraphicsSettings(g);wr.DrawVertical(g,state,width,null);
                        // Bounds are computed before final label clipping; record the icon's actual pixels instead of recomputing from shortened text.
                        int minX=500,maxX=-1;
                        for(int y=0;y<180;y++)for(int x=0;x<500;x++) {var pixel=bitmap.GetPixel(x,y);if(pixel.G>240 && pixel.R<10 && pixel.B<10){minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);}}
                        Check(maxX>=minX,"Icon did not render");
                        foreach(var label in new[]{inner.NameLabel,inner.ValueLabel}) if(label.Width>0) {
                            Check(label.X>=5 && label.X+label.Width<=width-5+.01f,"Text escaped component");
                            Check(side==WorldRecordGameIconSide.Left?label.X>maxX:label.X+label.Width<=minX,"Text band overlaps icon");
                        }
                        scenarios++;
                    }
                }
            }
            state.Run.GameIcon=null;
            using (var bitmap = new Bitmap(1000,160)) using(var g=Graphics.FromImage(bitmap)) {
                foreach(bool icons in new[]{false,true}) using(var icon=new Bitmap(24,24)) {
                    state.Run.GameIcon=icons?icon:null;settings.DisplayGameIcon=icons;
                    foreach(var side in new[]{WorldRecordGameIconSide.Left,WorldRecordGameIconSide.Right}) {
                        settings.GameIconSide=side;settings.IconHorizontalOffset=25;settings.TextHorizontalOffset=-25;
                        inner.InformationName="World Record";inner.InformationValue="12:34.56 by Runner";
                        wr.DrawHorizontal(g,state,70,null);
                        foreach(var label in new[]{inner.NameLabel,inner.ValueLabel}) {
                            Check(label.Width>=0,"Horizontal negative text width");
                            if(label.Width>0) Check(label.X>=5 && label.X+label.Width<=wr.HorizontalWidth-5+.01f,"Horizontal text escaped component");
                        }
                    }
                }
                state.Run.GameIcon=null;
            }
            foreach(WorldRecordTextShortening mode in Enum.GetValues(typeof(WorldRecordTextShortening))) {
                settings.TextShortening=mode;
                Call(wr,"SetRecordText","12:34.56","Runner",1,true);
                string expected=mode switch {WorldRecordTextShortening.FullLabel=>"World Record: 12:34.56 by Runner",WorldRecordTextShortening.ShortLabel=>"WR: 12:34.56 by Runner",WorldRecordTextShortening.ShortSentence=>"WR is 12:34.56 by Runner",WorldRecordTextShortening.TimeAndRunner=>"12:34.56 by Runner",WorldRecordTextShortening.TimeOnly=>"12:34.56",_=>"World Record is 12:34.56 by Runner"};
                Check(inner.InformationName==expected,"Wrong shortening: "+mode);
                Check(mode==WorldRecordTextShortening.Automatic||inner.AlternateNameText.Count==0,"Explicit format still auto-shortens");
                var doc=new XmlDocument();var xml=settings.GetSettings(doc);
                using(var restored=new WorldRecordSettings()){restored.SetSettings(xml);Check(restored.TextShortening==mode,"Shortening setting did not round-trip");}
                int hash=settings.GetSettingsHashCode();settings.TextShortening=mode==WorldRecordTextShortening.Automatic?WorldRecordTextShortening.TimeOnly:WorldRecordTextShortening.Automatic;
                Check(hash!=settings.GetSettingsHashCode(),"Shortening omitted from settings hash");
            }
            using(var old=new WorldRecordSettings()) {
                var doc=new XmlDocument();var xml=(XmlElement)settings.GetSettings(doc);xml.RemoveChild(xml["TextShortening"]);old.SetSettings(xml);Check(old.TextShortening==WorldRecordTextShortening.Automatic,"Old layouts lost default");
                var invalid=doc.CreateElement("TextShortening");invalid.InnerText="999";xml.AppendChild(invalid);old.SetSettings(xml);Check(old.TextShortening==WorldRecordTextShortening.Automatic,"Invalid enum not handled");
            }
            // Render selected examples and the actual settings control for visual inspection.
            settings.IconHorizontalOffset=0;settings.TextHorizontalOffset=0;settings.Display2Rows=false;settings.CenteredText=true;settings.TextShortening=WorldRecordTextShortening.Automatic;
            using(var icon=new Bitmap(24,24)) using(var heading=new Font("Arial",14,GraphicsUnit.Pixel)) {
                using(var ig=Graphics.FromImage(icon))ig.Clear(Color.Lime);state.Run.GameIcon=icon;
                int row=0;
                foreach(var placement in new[]{WorldRecordGameIconPlacing.WindowEdge,WorldRecordGameIconPlacing.NextToText}) foreach(var side in new[]{WorldRecordGameIconSide.Left,WorldRecordGameIconSide.Right}) {
                    settings.GameIconPlacing=placement;settings.GameIconSide=side;
                    proofGraphics.DrawString(placement+" / "+side,heading,Brushes.White,15,15+row*90);
                    Call(wr,"SetRecordText","12:34.56","A very long runner name",1,true);
                    var saved=proofGraphics.Save();proofGraphics.TranslateTransform(320,10+row*90);wr.DrawVertical(proofGraphics,state,250,null);proofGraphics.Restore(saved);row++;
                }
                state.Run.GameIcon=null;
            }
            image.Save(proof,ImageFormat.Png);
            settings.Mode=LayoutMode.Vertical;settings.BindingContext=new BindingContext();
            Call(settings,"WorldRecordSettings_Load",null,EventArgs.Empty);
            settings.PerformLayout();
            using(var host=new Form {ShowInTaskbar=false,Opacity=0,StartPosition=FormStartPosition.Manual,Location=new Point(-32000,-32000),ClientSize=settings.Size}) {
                host.Controls.Add(settings);host.Show();Application.DoEvents();
                using(var panel=new Bitmap(settings.Width,settings.Height)){settings.DrawToBitmap(panel,new Rectangle(Point.Empty,settings.Size));panel.Save(Path.ChangeExtension(proof,"settings.png"),ImageFormat.Png);}
                host.Hide();host.Controls.Remove(settings);
            }
            var combo=(ComboBox)settings.Controls.Find("cmbTextShortening",true)[0];Check(combo.Items.Count==7,"Shortening choices missing from UI");Check(combo.SelectedIndex==(int)settings.TextShortening,"Shortening selection not visible");
            Console.WriteLine("PASS "+scenarios+" World Record icon/layout scenarios");settings.Dispose();
        }
    }
    [STAThread] static int Main(string[] args)
    {
        try {var state=State();Splits(state);Labels(state);WorldRecord(state,args[0]);Console.WriteLine("PASS "+checks+" checks");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
