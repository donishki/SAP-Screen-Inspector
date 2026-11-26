using System.Text.Json;

namespace Inspector;

internal static class ObservationTests {
 internal static async Task Run(){
  void Check(bool condition,string name){if(!condition)throw new Exception("Observation: "+name);}
  var session=new SapSession("/app/con[0]/ses[0]","TEST","100","Demo","T1","","1","0");
  var button=new ControlRow("wnd[0]/usr/btn","GuiButton","Go","","","wnd[0]",2,"",""){Bounds=new(10,10,30,20)};
  var field=new ControlRow("wnd[0]/usr/txt","GuiTextField","Name","OLD","","wnd[0]",2,"True",""){ObservedState="OLD"};
  var before=new Snapshot(DateTime.Now,session,[button,field],true,[]){Windows=[new(10,"wnd[0]",100)]};
  Check(SapInputMonitor.Accepts(before,10,100),"selected window accepted");
  Check(!SapInputMonitor.Accepts(before,11,100),"another SAP session excluded");
  Check(!SapInputMonitor.Accepts(before,10,101),"reused handle in another process excluded");
  Check(!SapInputMonitor.Accepts(before,0,0),"missing foreground excluded");
  Check(SapInputMonitor.NavigationKey(65)==null&&SapInputMonitor.NavigationKey(49)==null&&SapInputMonitor.NavigationKey(8)==null,"typed characters and editing keys not recorded");
  Check(SapInputMonitor.NavigationKey(13)=="Enter"&&SapInputMonitor.NavigationKey(119)=="F8","navigation keys recognized");
  var click=new ObservedInput(DateTime.Now,"Click","Left mouse button",15,15,10,before);
  var step=Observation.Input(click);
  Check(step.RequiresReview&&step.ControlId==button.ShortId,"click target inferred and marked");
  Check(Observation.Input(click with {X=200}).ControlId=="","unmatched position not guessed");
  Check(Observation.Input(click with {Action="Press key",Detail="Enter"}).ControlId=="","keyboard target not guessed from mouse position");
  var after=before with {Captured=DateTime.Now,Controls=[button,field with {Text="NEW",ObservedState="NEW",Modified=true}]};
  var changes=Observation.Changes(before,after).ToList();
  Check(changes.Count==1&&changes[0].Action=="Enter text"&&changes[0].Argument=="NEW"&&changes[0].RequiresReview,"field change sampled");
  Check(!Observation.Changes(after,after).Any(),"unchanged sample suppressed");
  var hiddenBefore=before with {ValuesIncluded=false,Controls=[field with {Text="",ObservedState=""}]};
  var hiddenAfter=hiddenBefore with {Controls=[field with {Text="",ObservedState="",Modified=true}]};
  Check(Observation.Changes(hiddenBefore,hiddenAfter).Single().Argument=="","modified field observed without values");
  var pwd=field with {Type="GuiPasswordField"};
  Check(!Observation.Changes(before with {Controls=[pwd]},after with {Controls=[pwd with {ObservedState="DO-NOT-EXPORT",Modified=true}]}).Any(),"password changes excluded");
  Check(Observation.Changes(before,after with {Session=session with {Transaction="T2"}}).Any(s=>s.Action=="Screen changed"),"transaction navigation captured");
  // Synthetic versions of the noisy caption changes found in a real-world export.
  var decorations=new[]{"GuiMenu","GuiMenubar","GuiLabel","GuiMainWindow","GuiStatusbar"}
   .Select((type,i)=>new ControlRow("wnd[0]/dec"+i,type,"Caption","Old","","",0,"True",""){ObservedState="Old"}).ToList();
  var decoratedBefore=before with {Controls=[..before.Controls,..decorations]};
  var decoratedAfter=after with {Controls=[..after.Controls,..decorations.Select(c=>c with {Text="New",ObservedState="New",Modified=true})]};
  Check(Observation.Changes(decoratedBefore,decoratedAfter).Count()==1,"menu/title/label changes do not create false field edits");
  Check(Observation.Changes(before,after with {Session=session with {Transaction="T2"}}).Count()==1,"new screen values not treated as edits");
  Check(!Observation.Changes(before with {Controls=[field with {Changeable="False"}]},after with {Controls=[field with {Changeable="False",ObservedState="New"}]}).Any(),"read-only field refresh is not an edit");
  var combo=field with {Type="GuiComboBox"};
  Check(Observation.Changes(before with {Controls=[combo]},after with {Controls=[combo with {ObservedState="KEY2"}]}).Single().Action=="Choose item","editable selection change retained");
  var frame=button with {Id="wnd[0]",Type="GuiMainWindow",Depth=0,Bounds=new(0,0,800,600)};
  Check(Observation.Input(click with {Screen=before with {Controls=[frame]},X=200}).ControlId=="","window is not a target fallback");
  Check(Observation.Input(click with {Time=before.Captured.AddSeconds(5)}).ControlId=="","stale target rejected");
  Check(!SapInputMonitor.Accepts(before with {Captured=DateTime.Now.AddSeconds(-31)},10,100),"stale window scope expires");
  var positioned=before with {Windows=[new(10,"wnd[0]",100){Bounds=new(0,0,800,600)}]};
  Check(Observation.Input(click with {Screen=positioned,X=115,Y=215,WindowBounds=new(100,200,800,600)}).ControlId==button.ShortId,"window movement compensated");
  Check(Observation.Input(click with {Screen=positioned,WindowBounds=new(0,0,900,600)}).ControlId=="","resize invalidates old geometry");
  var shell=button with {Id="wnd[0]/usr/grid",Type="GuiGridView",NativeHandle=55,Bounds=null};
  Check(Observation.Input(click with {Screen=before with {Controls=[frame,shell]},NativeTarget=55}).ControlId==shell.ShortId,"grid native handle identifies target without pixel bounds");
  step.Number=1;
  string script=ScriptExport.Render([step]);
  Check(script.Contains("OBSERVED / INFERRED")&&!script.Contains(".press"),"inferred click not emitted as replay command");
  Check(script.Contains("Observed summary: 1 inputs, 0 field changes, 0 screen changes, 0 reported gaps/errors."),"export distinguishes inputs from sampled changes");
  var restored=Controller.ReadSequence(JsonSerializer.Serialize(new StepSequence("SAP-Screen-Inspector-steps",1,DateTime.Now,"Observed",[step])));
  Check(restored.Steps[0].RequiresReview&&restored.Steps[0].Screen.Windows[0].Handle==10,"observation JSON roundtrip");

  var monitor=new FakeInputMonitor();int reads=0;var collected=new List<CapturedStep>();
  Task<Reply> Read(Request request,CancellationToken token){
   reads++;Check(request.Observe&&request.Session==session&&request.Values,"scoped read request");
   return Task.FromResult(new Reply(Snapshot:after));
  }
  var recorder=new ObservedRecorder(before,true,Read,monitor);recorder.Step+=collected.Add;
  recorder.Start();monitor.Pending.Add(click);
  await recorder.Stop();
  Check(monitor.Disposed&&reads==1,"Stop immediately unhooks and takes final sample");
  Check(collected.Count==2&&collected[0].Action=="Click"&&collected[1].Action=="Enter text","queued input drains before final changes");
  await recorder.Stop();Check(reads==1,"Stop is idempotent");

  var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  var monitor2=new FakeInputMonitor();int gaps=0,attempt=0;
  async Task<Reply> Slow(Request request,CancellationToken token){
   if(++attempt==1){entered.SetResult();await Task.Delay(Timeout.Infinite,token);}
   throw new InvalidOperationException("Simulated disconnected session");
  }
  var recorder2=new ObservedRecorder(before,true,Slow,monitor2);recorder2.Gap+=_=>gaps++;
  recorder2.Start();await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));await recorder2.Stop();
  Check(monitor2.Disposed&&gaps==1&&monitor2.Latest!.Windows.Count==0,"in-flight read cancelled; final failure flagged and input suspended");

  // A partial screen must never replace the complete baseline or create false navigation.
  var monitor3=new FakeInputMonitor();int calls=0,notes=0,partialGaps=0;var filtered=new List<CapturedStep>();
  Task<Reply> Partial(Request request,CancellationToken token){
   Check(request.TimeoutSeconds>=15,"sampling allows slow responses beyond old six-second limit");
   return Task.FromResult(new Reply(Snapshot:++calls==1?after with {Controls=[button],Warnings=["Synthetic changing screen"]}:after));
  }
  var recorder3=new ObservedRecorder(before,true,Partial,monitor3);
  recorder3.Step+=filtered.Add;recorder3.Notice+=_=>notes++;recorder3.Gap+=_=>partialGaps++;
  await recorder3.Sample(CancellationToken.None);
  Check(calls==2&&notes==1&&partialGaps==0&&filtered.Count==1&&filtered[0].Action=="Enter text","partial screen retried without spurious removal/navigation");

  var monitor4=new FakeInputMonitor();int busyGaps=0,busyCalls=0;
  var recorder4=new ObservedRecorder(before,true,(_,_)=>{
   busyCalls++;throw new SapReadException("Busy","Synthetic processing");
  },monitor4);recorder4.Gap+=_=>busyGaps++;
  await recorder4.Sample(CancellationToken.None);
  Check(busyCalls==2&&busyGaps==0&&monitor4.Latest!.Windows.Count==1&&monitor4.Latest.Controls.Count==0,"brief busy wait retains scoped input but clears targets");
  await recorder4.Sample(CancellationToken.None,true);
  Check(busyGaps==1,"unresolved final busy state explicitly flagged");

  var monitor5=new FakeInputMonitor();int timeouts=0;var budgets=new List<int>();
  var recorder5=new ObservedRecorder(before,true,(r,_)=>{budgets.Add(r.TimeoutSeconds);throw new SapReadException("Timeout","Synthetic timeout");},monitor5);
  recorder5.Gap+=_=>timeouts++;
  await recorder5.Sample(CancellationToken.None);await recorder5.Sample(CancellationToken.None);
  Check(budgets.SequenceEqual(new[]{15,25,15,25})&&timeouts==1,"timeouts back off and report once per failure interval");

  var monitor6=new FakeInputMonitor();int identityGaps=0;
  var recorder6=new ObservedRecorder(before,true,(_,_)=>Task.FromResult(new Reply(Snapshot:after with {Session=session with {User="Other"}})),monitor6);
  recorder6.Gap+=_=>identityGaps++;await recorder6.Sample(CancellationToken.None);
  Check(identityGaps==1&&monitor6.Latest!.Windows.Count==0,"identity change suspends immediately");
 }
 sealed class FakeInputMonitor : IInputMonitor {
  public List<ObservedInput> Pending=[];public bool Disposed;public Snapshot? Latest;
  public void Start(Snapshot initial)=>Latest=initial;
  public void Update(Snapshot next)=>Latest=next;
  public List<ObservedInput> Drain(){var result=Pending.ToList();Pending.Clear();return result;}
  public int TakeDropped()=>0;
  public void Dispose()=>Disposed=true;
 }
}
