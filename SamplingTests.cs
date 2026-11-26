using System.Diagnostics;
using System.Text.Json;

namespace Inspector;

internal static class SamplingTests {
 internal static async Task Run(){
  void Check(bool condition,string name){if(!condition)throw new Exception("Sampling: "+name);}
  var session=new SapSession("/app/con[0]/ses[0]","TEST","100","Demo","T1","Screen","1","0");
  var now=DateTime.Now;
  var button=new ControlRow("wnd[0]/usr/btn","GuiButton","Go","","","wnd[0]",2,"",""){Bounds=new(10,10,30,20),SampledAt=now.AddMilliseconds(-100)};
  var field=new ControlRow("wnd[0]/usr/txt","GuiTextField","Input","Old","","wnd[0]",2,"True",""){ObservedState="Old",SampledAt=now};
  var before=new Snapshot(now.AddSeconds(-8),session,[button,field],true,[]){Windows=[new(10,"wnd[0]",100)],CompletedAt=now,ScopeVerified=true};
  var click=new ObservedInput(now,"Click","Left mouse button",15,15,10,before);
  Check(Observation.Input(click).ControlId==button.ShortId,"late-read control uses its own timestamp, not the start of a slow screen read");
  Check(Observation.Input(click with {Screen=before with {Controls=[button with {SampledAt=now.AddSeconds(-5)}]}}).ControlId=="","stale individual control rejected even on a fresh screen");
  Check(Observation.Input(click with {Screen=before with {Controls=[button with {SampledAt=now.AddSeconds(1)}]}}).ControlId=="","future samples cannot infer an earlier click");
  var partial=before with {Controls=[button],Warnings=["Synthetic unavailable sibling"]};
  var partialInput=Observation.Input(click with {Screen=partial});
  Check(partialInput.ControlId==button.ShortId&&partialInput.Notes.Contains("Partial screen"),"verified partial preserves fresh input target and disclosure");
  Check(Observation.Input(click with {Screen=partial with {ScopeVerified=false}}).ControlId=="","unverified partial rejects targets");
  Check(SapInputMonitor.Accepts(before with {Captured=now.AddSeconds(-40)},10,100),"scope age uses successful end verification");
  Check(!SapInputMonitor.Accepts(before with {CompletedAt=now.AddSeconds(-31)},10,100),"verified scope still expires after 30 seconds");
  var unavailable=before with {Controls=[button,field with {Text="",ObservedState="",StateAvailable=false,ModifiedAvailable=false}]};
  Check(!Observation.Changes(before,unavailable).Any(),"failed field getter is not reported as clearing text");
  Check(!Observation.Changes(unavailable,before).Any(),"recovery from missing field value is not reported as a user edit");

  var monitor=new SamplingMonitor();int calls=0,gaps=0,notes=0;var steps=new List<CapturedStep>();
  var after=before with {Controls=[button,field with {Text="New",ObservedState="New"}]};
  Task<Reply> Read(Request request,CancellationToken token){
   calls++;
   if(calls==2){
    Check(monitor.Latest==partial,"partial controls published before retry completes");
    monitor.Pending.Add(click with {Screen=monitor.Latest!});
   }
   return Task.FromResult(new Reply(Snapshot:calls==1?partial:after));
  }
  var recorder=new ObservedRecorder(before,true,Read,monitor);
  recorder.Step+=steps.Add;recorder.Gap+=_=>gaps++;recorder.Notice+=_=>notes++;
  await recorder.Sample(CancellationToken.None);
  Check(calls==2&&gaps==0&&notes==1,"partial retry recovers without a persistent error");
  Check(steps.Count(s=>s.Action=="Sampling note")==1&&steps.Count(s=>s.Action=="Click")==1&&steps.Count(s=>s.Action=="Enter text")==1&&!steps.Any(s=>s.Action=="Screen changed"),"partial sample retains input without inventing removals or replacing complete baseline");
  var roundtrip=Controller.ReadSequence(JsonSerializer.Serialize(new StepSequence("SAP-Screen-Inspector-steps",1,now,"Observed",steps)));
  Check(roundtrip.Steps[0].Screen.ScopeVerified&&roundtrip.Steps[1].Screen.Controls[0].SampledAt==button.SampledAt,"partial evidence and control times roundtrip");

  var persistentMonitor=new SamplingMonitor();int persistentGaps=0;var partialSteps=new List<CapturedStep>();
  var persistent=new ObservedRecorder(before,true,(_,_)=>Task.FromResult(new Reply(Snapshot:partial)),persistentMonitor);
  persistent.Step+=partialSteps.Add;persistent.Gap+=_=>persistentGaps++;
  await persistent.Sample(CancellationToken.None);await persistent.Sample(CancellationToken.None);
  Check(persistentGaps==1&&partialSteps.Count==1&&persistentMonitor.Latest==partial,"persistent partial data remains useful and reports one gap per interval");

  var label=new FakeNode{Id="wnd[0]/usr/lbl",Type="GuiLabel",Name="Caption",Text="Decoration"};
  var brokenField=new FakeNode{Id="wnd[0]/usr/txt",Type="GuiTextField",Name="Unavailable",Secret=true};
  var window=new CountingChildrenNode{Id="wnd[0]",Type="GuiMainWindow"};
  ((FakeNode)window).Children=[label,brokenField];
  var engine=new FakeEngine{Children=[new FakeConnection{Children=[new FakeSession{Children=[window]}]}]};
  var selected=Reader.Read(new("sessions"),engine).Sessions![0];
  var sample=Reader.Read(new("capture",selected,true,true),engine).Snapshot!;
  Check(window.ChildrenReads==1,"child collection fetched once per container");
  Check(label.TextReads==0&&!sample.Controls.Any(c=>c.Type=="GuiLabel"),"decorative control reads skipped");
  Check(sample.Controls.Single(c=>c.Name=="Unavailable").StateAvailable==false,"reader preserves field read failures");

  // Exercise the real process/pipe transport with synthetic objects, never live SAP.
  var worker=new ReaderSession(true);
  try{
   var first=await worker.Read(new("sessions"),CancellationToken.None);int firstId=worker.WorkerId!.Value;
   var second=await worker.Read(new("sessions"),CancellationToken.None);
   Check(first.Sessions!.Count==1&&second.Sessions!.Count==1&&worker.WorkerId==firstId,"successive samples reuse one worker");
   using(var cancel=new CancellationTokenSource(150)){
    bool cancelled=false;try{await worker.Read(new("test-wait"),cancel.Token);}catch(OperationCanceledException){cancelled=true;}
    Check(cancelled&&worker.WorkerId==null&&!Running(firstId),"cancellation kills blocked worker");
   }
   await worker.Read(new("sessions"),CancellationToken.None);int replacement=worker.WorkerId!.Value;
   bool timedOut=false;try{await worker.Read(new("test-wait",TimeoutSeconds:1),CancellationToken.None);}catch(SapReadException ex) when(ex.Kind=="Timeout"){timedOut=true;}
   Check(timedOut&&worker.WorkerId==null&&!Running(replacement),"timeout closes worker and preserves timeout classification");
   await worker.Read(new("sessions"),CancellationToken.None);int last=worker.WorkerId!.Value;
   await worker.DisposeAsync();Check(!Running(last),"disposal removes worker");
   bool rejected=false;try{await worker.Read(new("sessions"),CancellationToken.None);}catch(ObjectDisposedException){rejected=true;}
   Check(rejected,"disposed reader cannot restart");
  }finally{await worker.DisposeAsync();}
 }
 static bool Running(int id){try{using var process=Process.GetProcessById(id);return !process.HasExited;}catch(ArgumentException){return false;}}
 internal static void VerifyWindowScope(long handle){
  var window=new SamplingWindow{Id="wnd[0]",Type="GuiMainWindow",NativeWindow=handle,Children=[new FakeNode{Id="wnd[0]/usr/btn",Type="GuiButton"},new MissingControl()]};
  var engine=new FakeEngine{Children=[new FakeConnection{Children=[new FakeSession{Children=[window]}]}]};
  var session=Reader.Read(new("sessions"),engine).Sessions![0];
  var partial=Reader.Read(new("capture",session,false,true),engine).Snapshot!;
  if(!partial.ScopeVerified||partial.Warnings.Count==0||!partial.Controls.Any(c=>c.Type=="GuiButton"))throw new Exception("Partial reader did not retain controls in a verified native window");
  window.HandleReads=0;window.ChangeHandle=true;
  var changed=Reader.Read(new("capture",session,false,true),engine).Snapshot!;
  if(changed.ScopeVerified||changed.Controls.Count!=0||changed.Warnings.Count==0)throw new Exception("Mixed window sample retained unsafe targets");
 }
 sealed class SamplingMonitor : IInputMonitor {
  public Snapshot? Latest;public List<ObservedInput> Pending=[];
  public void Start(Snapshot initial)=>Latest=initial;
  public void Update(Snapshot next)=>Latest=next;
  public List<ObservedInput> Drain(){var result=Pending.ToList();Pending.Clear();return result;}
  public int TakeDropped()=>0;
  public void Dispose(){}
 }
}

public class CountingChildrenNode : FakeNode {
 public int ChildrenReads {get;private set;}
 public new List<FakeNode> Children {get{ChildrenReads++;return base.Children;}}
}
public class SamplingWindow : FakeNode {
 public long NativeWindow {get;set;}
 public int HandleReads {get;set;}
 public bool ChangeHandle {get;set;}
 public long Handle=>++HandleReads>1&&ChangeHandle?0:NativeWindow;
}
public class MissingControl : FakeNode {
 public new string Id=>throw new InvalidOperationException("Synthetic unavailable control");
}
