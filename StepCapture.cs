using System.IO;

using System.Runtime.InteropServices;

using System.Text.Json;

using System.Windows;

using System.Windows.Controls;

using System.Windows.Interop;

using Microsoft.Win32;



namespace Inspector;

public sealed class CapturedStep {

 public string? RecordedScript{get;set;}

 public int Number{get;set;}

 public DateTime Captured{get;set;}

 public string Action{get;set;}="Unspecified";

 public string ControlId{get;set;}="";

 public string Argument{get;set;}="";

 public string Notes{get;set;}="";

 public Snapshot Screen{get;set;}=null!;

 [System.Text.Json.Serialization.JsonIgnore]

 public string Label=>$"{Number:00}  ·  {Captured:HH:mm:ss}  ·  {Screen.Session.Transaction}  ·  {Action}";

}

public record StepSequence(string Format,int Version,DateTime Exported,string CaptureMethod,List<CapturedStep> Steps);

public record ControlChoice(string Id,string Label);

public sealed partial class Controller {

 readonly List<CapturedStep> steps=[];

 bool recording,editingStep,stepsDirty;
 SapRecorder? recorder;
 bool recordingIncomplete;

 Snapshot? liveSnapshot;

 HwndSource? source;

 bool hotkeyRegistered;

 const int HotkeyId=0x5349;

 [DllImport("user32.dll",SetLastError=true)]static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);

 [DllImport("user32.dll")]static extern bool UnregisterHotKey(IntPtr window,int id);

 void InitializeSteps(){

  Get<ComboBox>("StepAction").ItemsSource=new[]{"Unspecified","Starting screen","Click","Double-click","Enter text","Press key","Select tab","Choose item","Other","SAP action","Password omitted","Recording error"};

  Get<Button>("StartSteps").Click+=async(_,_)=>{

   if(busy||recording||Get<ComboBox>("Sessions").SelectedItem is not SapSession)return;

   if(steps.Count>0&&Get<ComboBox>("Sessions").SelectedItem is SapSession selectedSession&&

      (steps[0].Screen.Session.Id!=selectedSession.Id||steps[0].Screen.Session.System!=selectedSession.System||steps[0].Screen.Session.Client!=selectedSession.Client||steps[0].Screen.Session.User!=selectedSession.User)){

    Status("Select the original session to resume, or choose New to start another sequence.");return;

   }

   recording=true;Get<CheckBox>("Auto").IsChecked=false;Get<Expander>("StepExpander").IsExpanded=true;Enable();

   await Capture(true);
   if(closed)return;
   if(recording&&!demo){
    try {
     recorder=new SapRecorder();
     recorder.Changed+=AddRecordedStep;
     recorder.Failed+=message=>{
      recordingIncomplete=true;
      AddRecordedStep("","","' TODO: Recording event missing; sequence incomplete.","Recording error");
      Status(message);
     };
     recorder.Start((SapSession)Get<ComboBox>("Sessions").SelectedItem);
     Status("Recording SAP actions. Perform all your steps in SAP, then return and press Stop. Action values are included; passwords are omitted.");
    } catch(Exception ex){
     recorder=null;recording=false;
     Status("Recording could not start: "+ex.Message);
     MessageBox.Show(w,ex.Message,"Recording unavailable",MessageBoxButton.OK,MessageBoxImage.Warning);
    }
   }
   Enable();UpdateStepStatus();

  };

  Get<Button>("TakeStep").Click+=async(_,_)=>{

   if(!recording){Get<Button>("StartSteps").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));return;}

   if(!demo){StopRecording();return;}
   await Capture(true);

  };

  Get<Button>("StopSteps").Click+=(_,_)=>StopRecording();

  Get<Button>("NewSteps").Click+=(_,_)=>{

   if(stepsDirty&&MessageBox.Show(w,"Discard this unexported sequence?","New sequence",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;

   recordingIncomplete=false;recording=false;steps.Clear();stepsDirty=false;RefreshSteps();Enable();UpdateStepStatus();

  };

  Get<Button>("ExportSteps").Click+=(_,_)=>ExportSequence();

  Get<Button>("ExportJson").Click+=(_,_)=>ExportSequence(true);

  Get<ListBox>("StepList").SelectionChanged+=(_,_)=>SelectStep();

  Get<ComboBox>("StepAction").SelectionChanged+=(_,_)=>SaveStepDetails();

  Get<ComboBox>("StepControl").SelectionChanged+=(_,_)=>SaveStepDetails();

  Get<TextBox>("StepArgument").TextChanged+=(_,_)=>SaveStepDetails();

  Get<TextBox>("StepNotes").TextChanged+=(_,_)=>SaveStepDetails();

  w.SourceInitialized+=(_,_)=>{

   var handle=new WindowInteropHelper(w).Handle;source=HwndSource.FromHwnd(handle);source.AddHook(Hotkey);

   hotkeyRegistered=RegisterHotKey(handle,HotkeyId,0x4000|0x0002|0x0001,0x77);UpdateStepStatus();

  };

  w.Closing+=(_,e)=>{
   if(recording){
    StopRecording();
    if(recording){e.Cancel=true;return;}
   }

   if(stepsDirty&&MessageBox.Show(w,"This sequence has unexported changes. Close without exporting?","Step capture",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)e.Cancel=true;

  };

  w.Closed+=(_,_)=>{if(hotkeyRegistered)UnregisterHotKey(new WindowInteropHelper(w).Handle,HotkeyId);source?.RemoveHook(Hotkey);};

 }

 IntPtr Hotkey(IntPtr hwnd,int message,IntPtr wp,IntPtr lp,ref bool handled){

  if(message==0x0312&&wp.ToInt32()==HotkeyId){handled=true;if(!busy)w.Dispatcher.InvokeAsync(()=>Get<Button>("TakeStep").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));}

  return IntPtr.Zero;

 }

 void EnableSteps(){

  Get<Button>("StartSteps").IsEnabled=!busy&&!recording&&Get<ComboBox>("Sessions").SelectedItem!=null;

  Get<Button>("StartSteps").Content=steps.Count>0?"Resume recording":"Record";

  Get<Button>("TakeStep").IsEnabled=recording&&!busy&&Get<ComboBox>("Sessions").SelectedItem!=null;

  Get<Button>("StopSteps").IsEnabled=recording&&!busy;

  Get<Button>("NewSteps").IsEnabled=!busy&&!recording&&steps.Count>0;

  Get<Button>("ExportSteps").IsEnabled=!busy&&!recording&&steps.Count>0;

  Get<Button>("ExportJson").IsEnabled=!busy&&!recording&&steps.Count>0;

  Get<Button>("Reload").IsEnabled=!busy&&!recording;
  Get<Button>("Capture").IsEnabled=!busy&&!recording&&Get<ComboBox>("Sessions").SelectedItem!=null;

  Get<CheckBox>("Auto").IsEnabled=!recording&&!busy;

  bool hasStep=Get<ListBox>("StepList").SelectedItem is CapturedStep;

  foreach(string name in new[]{"StepAction","StepControl","StepArgument","StepNotes"})Get<FrameworkElement>(name).IsEnabled=hasStep&&!busy&&!recording&&((CapturedStep)Get<ListBox>("StepList").SelectedItem).RecordedScript==null;

 }

 void UpdateStepStatus(){

  Get<Button>("TakeStep").Visibility=Visibility.Collapsed;
  Get<Button>("StartSteps").Visibility=recording?Visibility.Collapsed:Visibility.Visible;
  Get<TextBlock>("StepStatus").Text=$"{(recording?"Recording - work in SAP, then press Stop":"Stopped")} | {steps.Count} steps"+(recordingIncomplete?" | INCOMPLETE: recording error":"");

 }

 internal static CapturedStep FreezeStep(Snapshot screen,int number)=>new(){

  Number=number,Captured=screen.Captured,Action=number==1?"Starting screen":"Unspecified",

  Screen=JsonSerializer.Deserialize<Snapshot>(JsonSerializer.Serialize(screen))!

 };

 void AddStep(Snapshot screen){

  steps.Add(FreezeStep(screen,steps.Count+1));stepsDirty=true;

  RefreshSteps(steps[^1]);UpdateStepStatus();Status($"Step {steps.Count} saved. Starting screen saved.");

 }

 void AddRecordedStep(string id,string type,string script,string action){
  var baseline=steps.LastOrDefault()?.Screen;
  if(baseline==null)return;
  var now=DateTime.Now;
  // Events describe the affected control, not a full historical screen snapshot.
  var screen=new Snapshot(now,baseline.Session,[new(id,type,"","","","",0,"","")],true,
   ["SAP recording event: affected control only; action values are included in the recorded command."]);
  steps.Add(new(){Number=steps.Count+1,Captured=now,Action=action,ControlId=id,RecordedScript=script,Screen=screen});
  stepsDirty=true;RefreshSteps(steps[^1]);UpdateStepStatus();
 }
 void StopRecording(){
  try {
   recorder?.Stop();recorder=null;recording=false;
   Enable();UpdateStepStatus();
   Status(recordingIncomplete?"Stopped. Some events could not be recorded; review the error steps.":$"Stopped. {steps.Count} steps collected. Review and save your script or JSON sequence.");
  }catch(Exception ex){
   Status("Could not stop SAP recording: "+ex.Message+". Retry Stop; collected steps are retained.");
   MessageBox.Show(w,"Could not stop SAP recording. Wait for SAP to finish and retry Stop.\n\n"+ex.Message,"Stop recording",MessageBoxButton.OK,MessageBoxImage.Warning);
  }
 }

 void RefreshSteps(CapturedStep? selectedStep=null){

  var list=Get<ListBox>("StepList");editingStep=true;

  list.ItemsSource=null;list.ItemsSource=steps.ToArray();list.SelectedItem=selectedStep??steps.LastOrDefault();

  editingStep=false;SelectStep();EnableSteps();

 }

 void SelectStep(){

  if(editingStep)return;editingStep=true;

  try{

   var step=Get<ListBox>("StepList").SelectedItem as CapturedStep;

   Get<ComboBox>("StepAction").SelectedItem=step?.Action;

   Get<TextBox>("StepNotes").Text=step?.Notes??"";

   Get<TextBox>("StepArgument").Text=step?.Argument??"";

   var controls=new List<ControlChoice>{new("","Not specified")};

   if(step!=null){

    var previous=steps.TakeWhile(s=>s!=step).LastOrDefault();

    if(previous!=null)controls.AddRange(previous.Screen.Controls.Select(r=>new ControlChoice(r.ShortId,"Previous · "+r.Name+" · "+r.ShortId)));

    controls.AddRange(step.Screen.Controls.Select(r=>new ControlChoice(r.ShortId,"Current · "+r.Name+" · "+r.ShortId)));

    // Prefer previous-screen IDs: the action often removes the clicked control.

    controls=controls.DistinctBy(c=>c.Id).ToList();

    snapshot=step.Screen;rows=step.Screen.Controls;

    Get<ComboBox>("Types").ItemsSource=new[]{"All control types"}.Concat(rows.Select(r=>r.Type).Distinct().Order()).ToArray();Get<ComboBox>("Types").SelectedIndex=0;

    Get<TextBox>("Search").Text="";Filter();BuildTree();Show(null);

   }

   Get<ComboBox>("StepControl").ItemsSource=controls;Get<ComboBox>("StepControl").SelectedItem=controls.FirstOrDefault(c=>c.Id==step?.ControlId)??controls[0];

  }finally{editingStep=false;}

  UpdateScriptPreview();EnableSteps();

 }

 void UpdateScriptPreview(){

  var step=Get<ListBox>("StepList").SelectedItem as CapturedStep;

  if(step==null){SetScriptPreview("' Capture a step to preview its script.");return;}

  string script=ScriptExport.Render(steps);

  string marker="' Step "+step.Number+":";int start=script.IndexOf(marker,StringComparison.Ordinal);

  int end=script.IndexOf("' Step ",start+marker.Length,StringComparison.Ordinal);

  SetScriptPreview(start>=0?script[start..(end<0?script.Length:end)].Trim():script);

 }

 void SaveStepDetails(){

  if(editingStep||Get<ListBox>("StepList").SelectedItem is not CapturedStep step||step.RecordedScript!=null)return;

  step.Action=Get<ComboBox>("StepAction").SelectedItem as string??"Unspecified";

  step.ControlId=(Get<ComboBox>("StepControl").SelectedItem as ControlChoice)?.Id??"";

  step.Argument=Get<TextBox>("StepArgument").Text;

  step.Notes=Get<TextBox>("StepNotes").Text;stepsDirty=true;

  // Refresh labels without resetting the selection or caret in the notes editor.

  editingStep=true;Get<ListBox>("StepList").Items.Refresh();editingStep=false;UpdateScriptPreview();

 }

 internal async Task StepSmoke(){

  if(!demo)return;

  bool handled=false;Hotkey(IntPtr.Zero,0x0312,new IntPtr(HotkeyId),IntPtr.Zero,ref handled);
  await w.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
  if(!handled||!recording||steps.Count!=1)throw new Exception("Hotkey must start the first capture");

  var first=steps[0];

  await Capture(true);

  if(steps.Count!=2||ReferenceEquals(first.Screen.Controls,steps[1].Screen.Controls))throw new Exception("Step snapshot isolation failed");

  Get<ComboBox>("StepAction").SelectedItem="Click";

  Get<ComboBox>("StepControl").SelectedIndex=1;

  Get<TextBox>("StepNotes").Text="Opened the tool lifecycle. Sample sequence only.";

  if(steps[1].Action!="Click"||steps[1].ControlId==""||steps[1].Notes=="")throw new Exception("Step annotations were not saved");

  Get<ListBox>("StepList").SelectedIndex=0;

  if(steps[1].Action!="Click"||steps[0].Action!="Starting screen")throw new Exception("Selecting a step changed another step");

  Get<ListBox>("StepList").SelectedIndex=1;

  var serialized=JsonSerializer.Serialize(new StepSequence("SAP-Screen-Inspector-steps",1,DateTime.Now,"Manual capture",steps));

  var restored=ReadSequence(serialized); bool invalidRejected=false; try { ReadSequence("{}"); } catch(InvalidDataException) { invalidRejected=true; } if(!invalidRejected)throw new Exception("Invalid sequence accepted");

  if(restored.Steps.Count!=2||restored.Steps[1].ControlId=="")throw new Exception("Sequence export failed");

  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"step-test.txt"),"PASS: ordered snapshots, isolated captures, action/control/notes editing, timeline navigation and full sequence JSON roundtrip.");

  Get<ComboBox>("StepAction").SelectedItem="Enter text";

  Get<ComboBox>("StepControl").SelectedItem=((IEnumerable<ControlChoice>)Get<ComboBox>("StepControl").ItemsSource).First(c=>c.Id.Contains("ctxtP_MATNR"));

  Get<TextBox>("StepArgument").Text="DEMO-001";

  if(!previewText.Contains(".text = "))throw new Exception("Live script preview failed");

  Get<DataGrid>("Table").SelectedItem=rows.First(r=>r.Name=="P_MATNR");

  AddRecordedStep("wnd[0]/usr/txtName","GuiTextField",SapRecorder.RenderCommand("wnd[0]/usr/txtName",new object[]{"SP","text","DEMO-RECORDED"}),"SAP action");
  AddRecordedStep("wnd[0]","GuiMainWindow",SapRecorder.RenderCommand("wnd[0]",new object[]{"M","sendVKey",0}),"SAP action");
  Get<Button>("StopSteps").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
  if(recording||steps.Count!=4||!ScriptExport.Render(steps).Contains("DEMO-RECORDED"))throw new Exception("Record/Stop timeline failed");
  var eventRoundtrip=ReadSequence(JsonSerializer.Serialize(new StepSequence("SAP-Screen-Inspector-steps",1,DateTime.Now,"SAP recording",steps)));
  if(eventRoundtrip.Steps[2].RecordedScript!=steps[2].RecordedScript)throw new Exception("Recorded commands lost in JSON");
  File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"step-test.txt")," PASS: recorded command timeline, Stop, script export and recorded JSON roundtrip.");
  stepsDirty=false;Enable();UpdateStepStatus();

 }

 void ExportSequence(bool json=false){

  if(steps.Count==0)return;

  var dialog=new SaveFileDialog{Filter="SAP script text (*.txt)|*.txt|Step sequence (*.json)|*.json",FilterIndex=json?2:1,FileName=$"SAP-steps-{DateTime.Now:yyyyMMdd-HHmmss}."+(json?"json":"txt")};

  if(dialog.ShowDialog(w)!=true)return;

  try{

   var data=new StepSequence("SAP-Screen-Inspector-steps",1,DateTime.Now,"SAP native recording events with an initial screen snapshot. Recorded commands include action values; passwords omitted. Legacy steps may be manually annotated.",steps);

   bool script=dialog.FilterIndex==1;

   File.WriteAllText(dialog.FileName,script?ScriptExport.Render(steps):JsonSerializer.Serialize(data,new JsonSerializerOptions{WriteIndented=true}),System.Text.Encoding.UTF8);

   if(!script)stepsDirty=false;

   Status(script?"Saved SAP script. Review before running; export JSON too to preserve the sequence.":$"Exported {steps.Count} steps. Earlier captures retain the value-inclusion setting used when captured.");

  }catch(Exception ex){Status("Export failed: "+ex.Message);}

 }

}

