using System;
using System.IO;
using System.Xml;
using System.Drawing;
using System.Drawing.Imaging;
using LiveSplit.Model;
using LiveSplit.Model.Comparisons;
using LiveSplit.Model.RunFactories;
using LiveSplit.Model.RunSavers;
using LiveSplit.UI;
class SplitExternalCheck {
 static IRun Load(string p){using(var s=File.OpenRead(p))return new StandardFormatsRunFactory(s,p).Create(new StandardComparisonGeneratorsFactory());}
 static void Assert(bool b,string msg){if(!b)throw new Exception(msg);}
 static string WithoutIcons(XmlDocument d){var copy=(XmlDocument)d.CloneNode(true);foreach(XmlNode n in copy.SelectNodes("/Run/GameIcon|/Run/Segments/Segment/Icon"))n.ParentNode.RemoveChild(n);return copy.OuterXml;}
 [STAThread] static int Main(string[] args){try{
 string original=args[0], output=args[1], media=args[2];Directory.CreateDirectory(media);
 var doc=new XmlDocument();doc.PreserveWhitespace=true;doc.Load(original);string before=WithoutIcons(doc);
 int count=0;foreach(XmlElement n in doc.SelectNodes("/Run/GameIcon|/Run/Segments/Segment/Icon")){
 if(string.IsNullOrWhiteSpace(n.InnerText))continue;
 using(var image=SettingsHelper.GetImageFromElement(n)){
 string p=Path.Combine(media,(n.Name=="GameIcon"?"game":"segment-"+(++count).ToString("D2"))+".png");
 image.Save(p,ImageFormat.Png);n.RemoveAll();n.SetAttribute("path",p);
 }}
 Assert(before==WithoutIcons(doc),"Non-icon XML changed");doc.Save(output);
 var old=Load(original);var converted=Load(output);
 Assert(old.Count==converted.Count&&old.AttemptCount==converted.AttemptCount&&old.AttemptHistory.Count==converted.AttemptHistory.Count,"Run history/count changed");
 Assert(old.GameIcon.Size==converted.GameIcon.Size,"Game icon dimensions changed");
 for(int i=0;i<old.Count;i++){
 Assert(old[i].Name==converted[i].Name&&old[i].SegmentHistory.Count==converted[i].SegmentHistory.Count,"Segment history changed");
 if(old[i].Icon!=null)Assert(converted[i].Icon!=null&&old[i].Icon.Size==converted[i].Icon.Size,"Segment icon missing");
 }
 string saved=output+".roundtrip";using(var s=File.Create(saved))new XMLRunSaver().Save(converted,s);
 var result=new XmlDocument();result.Load(saved);
 foreach(XmlElement n in result.SelectNodes("/Run/GameIcon|/Run/Segments/Segment/Icon"))Assert(string.IsNullOrWhiteSpace(n.InnerText),"Icon was re-embedded");
 var again=Load(saved);Assert(again.Count==old.Count&&again.GameIcon!=null,"Reopen failed");
 string cloned=output+".clone";using(var s=File.Create(cloned))new XMLRunSaver().Save(((Run)converted).Clone(),s);
 result.Load(cloned);foreach(XmlElement n in result.SelectNodes("/Run/GameIcon|/Run/Segments/Segment/Icon"))Assert(string.IsNullOrWhiteSpace(n.InnerText),"Clone re-embedded icons");
 Console.WriteLine("PASS "+Path.GetFileName(original)+": "+old.Count+" segments, "+old.AttemptHistory.Count+" attempts, all icons loaded; non-icon XML unchanged; save/reopen/clone stay external. Bytes "+new FileInfo(original).Length+" -> "+new FileInfo(output).Length);
 return 0;
 }catch(Exception e){Console.WriteLine(e);return 1;}}
}
