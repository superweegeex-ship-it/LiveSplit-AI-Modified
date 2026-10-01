using System;using System.Drawing;using System.Xml;using LiveSplit.Model;using LiveSplit.UI;using LiveSplit.UI.Components;using LiveSplit.Options.SettingsFactories;
class TimerSwapCheck {
 static void Check(bool b,string m){if(!b)throw new Exception(m);}
 [STAThread]static int Main(){try{
 var run=new Run(new LiveSplit.Model.Comparisons.StandardComparisonGeneratorsFactory());run.Add(new Segment("Test"));
 var ls=new StandardLayoutSettingsFactory().Create();ls.DropShadows=false;
 var state=new LiveSplitState(run,null,new Layout{Settings=ls},ls,new LiveSplit.Options.Settings());
 state.CurrentSplitIndex=0;
 using(var timer=new DetailedTimer(state))using(var b=new Bitmap(400,160))using(var g=Graphics.FromImage(b)){
 var s=timer.Settings;s.Height=150;s.Width=400;s.DisplayIcon=false;s.ShowSplitName=false;s.OverrideTimerColors=true;s.TimerColor=Color.Red;s.SegmentTimerColor=Color.Lime;s.TimerShowGradient=false;s.SegmentTimerShowGradient=false;
 Check(!s.SegmentTimerOnTop,"Legacy default");s.SegmentTimerOnTop=true;var xml=s.GetSettings(new XmlDocument());s.SegmentTimerOnTop=false;s.SetSettings(xml);Check(s.SegmentTimerOnTop,"Save/load");xml.RemoveChild(xml.SelectSingleNode("SegmentTimerOnTop"));s.SetSettings(xml);Check(!s.SegmentTimerOnTop,"Missing setting default");
 s.KeepLeftSideOrder=true;xml=s.GetSettings(new XmlDocument());s.KeepLeftSideOrder=false;s.SetSettings(xml);Check(s.KeepLeftSideOrder,"Left order persistence");xml.RemoveChild(xml.SelectSingleNode("KeepLeftSideOrder"));s.SetSettings(xml);Check(!s.KeepLeftSideOrder,"Left order legacy default");
 foreach(var mode in new[]{LayoutMode.Vertical,LayoutMode.Horizontal})foreach(int ratio in new[]{25,40,70})foreach(bool swap in new[]{false,true})foreach(bool keep in new[]{false,true}){
 s.KeepLeftSideOrder=keep;
 s.SegmentTimerSizeRatio=ratio;s.SegmentTimerOnTop=swap;timer.Update(null,state,400,150,mode);timer.DrawVertical(g,state,400,null);timer.Update(null,state,400,150,mode);g.Clear(Color.Black);
 if(mode==LayoutMode.Vertical)timer.DrawVertical(g,state,400,null);else timer.DrawHorizontal(g,state,150,null);
 double red=0,green=0;int nr=0,ng=0;for(int y=0;y<160;y++)for(int x=0;x<400;x++){var c=b.GetPixel(x,y);if(c.R>120&&c.G<60){red+=y;nr++;}if(c.G>120&&c.R<60){green+=y;ng++;}}
 Check(nr>0&&ng>0,"Timers rendered red="+nr+" green="+ng);Check(swap?green/ng<red/nr:red/nr<green/ng,"Timer order");Check(Math.Abs(g.Transform.OffsetY)<.01,"Transform restored");
 Check(Math.Abs(timer.LabelSegment.Y-(swap&&!keep?0:150*(100-ratio)/100f))<.01,"Comparison row position");
 Check(Math.Abs(timer.SplitName.Y-(swap&&!keep?150*ratio/100f:0))<.01,"Split name position");
 }
 }
 Console.WriteLine("PASS: both timer orders at 3 ratios in both orientations; persistence and old-layout default.");return 0;
 }catch(Exception e){Console.WriteLine(e);return 1;}}
}
