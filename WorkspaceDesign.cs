using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;

namespace Inspector;
public sealed partial class Controller {
 string previewText="";
 void InitializeDesign(){
  Get<Button>("Settings").Click+=(_,_)=>Get<Popup>("SettingsPopup").IsOpen=true;
  Get<Button>("OpenSession").Click+=(_,_)=>OpenSavedSession();
  SetScriptPreview("' Capture a step to preview its script.");
 }
 static FrameworkElement TreeHeader(ControlRow row){
  var panel=new StackPanel{Orientation=Orientation.Horizontal};
  panel.Children.Add(new TextBlock{Text=row.Icon,FontFamily=new FontFamily("Segoe MDL2 Assets"),Foreground=new SolidColorBrush(Color.FromRgb(82,103,129)),Margin=new(0,0,10,0),VerticalAlignment=VerticalAlignment.Center});
  panel.Children.Add(new TextBlock{Text=string.IsNullOrEmpty(row.Name)?row.Type:row.Name,VerticalAlignment=VerticalAlignment.Center});
  return panel;
 }
 void SetScriptPreview(string script){
  previewText=script;
  var box=Get<RichTextBox>("ScriptPreview");
  var document=new FlowDocument{PagePadding=new Thickness(0),FontFamily=new FontFamily("Consolas"),FontSize=14};
  var lines=script.Split('\n').Select(l=>l.TrimEnd('\r')).Where(l=>!string.IsNullOrWhiteSpace(l)).ToArray();
  var commands=lines.Where(l=>!l.StartsWith("'",StringComparison.Ordinal)).ToArray();
  if(commands.Length>0)lines=commands;
  int number=1;
  foreach(var line in lines){
   var paragraph=new Paragraph{Margin=new Thickness(0,0,0,6)};
   paragraph.Inlines.Add(new Run($"{number++,2}   "){Foreground=Brushes.SlateGray});
   foreach(Match token in Regex.Matches(line,"\"(?:[^\"]|\"\")*\"|'[^\\r\\n]*|[^\"']+")){
    var color=token.Value.StartsWith('"')?(token.Value.Contains("wnd[")?Color.FromRgb(255,214,55):Color.FromRgb(137,222,158)):token.Value.StartsWith("'")?Color.FromRgb(148,167,190):Color.FromRgb(233,239,248);
    paragraph.Inlines.Add(new Run(token.Value){Foreground=new SolidColorBrush(color)});
   }
   document.Blocks.Add(paragraph);
  }
  box.Document=document;
 }
 internal static StepSequence ReadSequence(string json){
  var data=JsonSerializer.Deserialize<StepSequence>(json)??throw new InvalidDataException("The selected file is empty.");
  if(data.Format!="SAP-Screen-Inspector-steps"||data.Version!=1||data.Steps is null||data.Steps.Count==0||data.Steps.Count>10000)
   throw new InvalidDataException("Select a supported SAP Screen Inspector JSON step sequence.");
  foreach(var step in data.Steps){
   if(step is null||step.Screen is null||step.Screen.Session is null||step.Screen.Controls is null||step.Screen.Controls.Any(c=>c is null))throw new InvalidDataException("A saved step is incomplete.");
   step.Action??="Unspecified";step.ControlId??="";step.Argument??="";step.Notes??="";
  }
  for(int i=0;i<data.Steps.Count;i++)data.Steps[i].Number=i+1;
  return data;
 }
 void OpenSavedSession(){
  if(busy||recording){Status("Stop capture before opening a saved sequence.");return;}
  var dialog=new OpenFileDialog{Title="Open saved session",Filter="Step sequence (*.json)|*.json"};
  if(dialog.ShowDialog(w)!=true)return;
  try{
   if(new FileInfo(dialog.FileName).Length>50*1024*1024)throw new InvalidDataException("This sequence exceeds the 50 MB file limit.");
   var data=ReadSequence(File.ReadAllText(dialog.FileName));
   if(stepsDirty&&MessageBox.Show(w,"Replace the current unexported sequence?","Open saved session",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
   Get<CheckBox>("Auto").IsChecked=false;
   recordingIncomplete=data.Steps.Any(s=>s.Action=="Recording error");
   steps.Clear();steps.AddRange(data.Steps);stepsDirty=false;RefreshSteps();UpdateStepStatus();
   Status($"Opened {steps.Count} saved steps. Review or export; select the original SAP session to resume capture.");
  }catch(Exception ex){Status("Could not open sequence: "+ex.Message);}
 }
}
