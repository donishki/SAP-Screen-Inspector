namespace Inspector;

internal static class RecorderTests {
 public static void Run(){
  void Check(bool ok,string message){if(!ok)throw new Exception("Recorder: "+message);}
  string Render(object data)=>SapRecorder.RenderCommand("wnd[0]/usr/control",data);
  Check(Render(new object[]{"SP","text","a\"b\r\nc"}).Contains("\"a\"\"b\" & vbCr & \"\" & vbLf & \"c\""),"quoted multiline text");
  Check(Render(new object[]{"M","resize",new object[]{96,32,false}}).EndsWith(".resize 96, 32, False"),"method arguments");
  Check(Render(new object[]{"M","press",Array.Empty<object>()}).EndsWith(".press"),"parameterless method");
  var chain=Array.CreateInstance(typeof(object),[2,3],[1,1]);
  chain.SetValue("M",1,1);chain.SetValue("getAbsoluteRow",1,2);chain.SetValue(1,1,3);
  chain.SetValue("SP",2,1);chain.SetValue("selected",2,2);chain.SetValue(true,2,3);
  Check(Render(chain).EndsWith(".getAbsoluteRow(1).selected = True"),"chained nonzero-bound SAFEARRAY");
  Check(Render(new object[]{new object[]{"M","getAbsoluteRow",1},new object[]{"SP","selected",false}}).EndsWith(".getAbsoluteRow(1).selected = False"),"jagged commands");
  bool rejected=false;try{Render(new object[]{"SP","text:evil",1});}catch(InvalidOperationException){rejected=true;}Check(rejected,"invalid member rejected");

  var session=new RecordingSession();
  var engine=new RecordingEngine{Children=[new RecordingConnection{Children=[session]}]};
  var selected=new SapSession(session.Id,"TEST","100","Tester","IE03","","0","0");
  Action<object,object,object>? callback=null;
  bool detached=false;
  Action Subscribe(object source,Action<object,object,object> listener){callback=listener;return()=>{detached=true;callback=null;};}
  var recorder=new SapRecorder();var lines=new List<string>();int errors=0;
  recorder.Changed+=(_,_,script,_)=>lines.Add(script);recorder.Failed+=_=>errors++;
  recorder.Start(selected,engine,Subscribe);
  Check(session.Record&&!detached,"record starts");
  callback!(session,new FakeNode{Id="wnd[0]/usr/txt",Type="GuiTextField"},new object[]{"SP","text","FIRST"});
  callback!(session,new FakeNode{Id="wnd[0]/usr/pwd",Type="GuiPasswordField",Secret=true},new object());
  callback!(session,new FakeNode{Id="wnd[0]",Type="GuiMainWindow"},new object[]{"bad"});
  session.OnStop=()=>callback!(session,new FakeNode{Id="wnd[0]",Type="GuiMainWindow"},new object[]{"M","sendVKey",0});
  recorder.Stop();
  Check(!session.Record&&detached&&lines.Count==3,"Stop flushes before unsubscribe");
  Check(lines[0].Contains("FIRST")&&lines[1].Contains("Password action omitted")&&lines[2].EndsWith("sendVKey 0"),"ordered actions and password exclusion");
  Check(errors==1,"malformed events reported");
  recorder.Stop();
  session.OnStop=null;detached=false;
  recorder.Start(selected,engine,Subscribe);recorder.Stop();Check(detached,"resume and cleanup");
  session.Info.ScriptingModeRecordingDisabled=1;
  rejected=false;try{recorder.Start(selected,engine,Subscribe);}catch(InvalidOperationException){rejected=true;}
  Check(rejected&&!session.Record,"recording-disabled rejected");
  session.Info.ScriptingModeRecordingDisabled=0;session.Record=true;
  rejected=false;try{recorder.Start(selected,engine,Subscribe);}catch(InvalidOperationException){rejected=true;}
  Check(rejected&&session.Record,"other recorder preserved");session.Record=false;
  rejected=false;try{recorder.Start(selected with {User="Another"},engine,Subscribe);}catch(InvalidOperationException){rejected=true;}
  Check(rejected,"session identity checked");
  session.FailStart=true;detached=false;
  rejected=false;try{recorder.Start(selected,engine,Subscribe);}catch(InvalidOperationException){rejected=true;}
  Check(rejected&&detached&&!session.Record,"start failure cleanup");
  session.FailStart=false;session.Info.ScriptingModeReadOnly=1;
  rejected=false;try{recorder.Start(selected,engine,Subscribe);}catch(InvalidOperationException){rejected=true;}
  Check(rejected,"read-only rejected");session.Info.ScriptingModeReadOnly=0;
  recorder.Start(selected,engine,Subscribe);detached=false;session.FailStop=true;
  rejected=false;try{recorder.Stop();}catch(InvalidOperationException){rejected=true;}
  Check(rejected&&!detached&&session.Record,"failed Stop retains subscription for retry");
  session.FailStop=false;recorder.Stop();Check(detached,"Stop retry cleanup");
 }
}
public class RecordingEngine {public List<RecordingConnection> Children{get;set;}=[];}
public class RecordingConnection {public bool DisabledByServer=>false;public List<RecordingSession> Children{get;set;}=[];}
public class RecordingInfo {
 public string SystemName=>"TEST";public string Client=>"100";public string User=>"Tester";public string Transaction=>"IE03";
 public int ScriptingModeRecordingDisabled{get;set;}public int ScriptingModeReadOnly{get;set;}
}
public class RecordingSession {
 public string Id=>"/app/con[0]/ses[0]";public bool Busy=>false;public RecordingInfo Info{get;}=new();
 public FakeNode ActiveWindow{get;}=new();public Action? OnStop{get;set;}public bool FailStart{get;set;}public bool FailStop{get;set;}
 bool record;
 public bool Record {get=>record;set {if(value&&FailStart)throw new InvalidOperationException("Simulated start failure");if(!value&&FailStop)throw new InvalidOperationException("Simulated stop failure");if(!value&&record)OnStop?.Invoke();record=value;}}
}
