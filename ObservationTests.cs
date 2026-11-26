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
  step.Number=1;
  string script=ScriptExport.Render([step]);
  Check(script.Contains("OBSERVED / INFERRED")&&!script.Contains(".press"),"inferred click not emitted as replay command");
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
