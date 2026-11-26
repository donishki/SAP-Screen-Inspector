using System.IO;
namespace Inspector;
public static class SelfTests {
 public static void Run(){
  void Check(bool ok,string message){if(!ok)throw new Exception(message);}
  ScriptExportTests.Run();
  RecorderTests.Run();
  var password=new FakeNode{Id="wnd[0]/usr/pwd",Type="GuiPasswordField",Name="Secret",Secret=true};
  var field=new FakeNode{Id="wnd[0]/usr/txt",Type="GuiTextField",Name="Serial",Text="DEMO-SERIAL-001"};
  var window=new FakeNode{Id="wnd[0]",Type="GuiMainWindow",Name="Window",Children=[field,password]};
  var session=new FakeSession{Children=[window]};
  var engine=new FakeEngine{Children=[new FakeConnection{Children=[session]}]};
  var sessions=Reader.Read(new("sessions"),engine).Sessions!;
  Check(sessions.Count==1,"Session discovery");
  var hidden=Reader.Read(new("capture",sessions[0]),engine).Snapshot!;
  Check(hidden.Controls.Count==3,"Control traversal");
  Check(hidden.Controls.Single(r=>r.Name=="Serial").Text=="","Values hidden");
  Check(password.TextReads==0,"Password was read");
  var visible=Reader.Read(new("capture",sessions[0],true),engine).Snapshot!;
  Check(visible.Controls.Single(r=>r.Name=="Serial").Text=="DEMO-SERIAL-001","Values enabled");
  Check(password.TextReads==0,"Password read with values enabled");
  Check(visible.Controls.Single(r=>r.Name=="Secret").Text=="[password excluded]","Password marker");
  session.Info.User="Different";
  bool rejected=false;try{Reader.Read(new("capture",sessions[0]),engine);}catch(InvalidOperationException){rejected=true;}
  Check(rejected,"Changed session rejected");
  engine.Children[0].DisabledByServer=true;rejected=false;
  try{Reader.Read(new("sessions"),engine);}catch(InvalidOperationException){rejected=true;}Check(rejected,"Server-disabled scripting handled");
  var roundtrip=System.Text.Json.JsonSerializer.Deserialize<Snapshot>(System.Text.Json.JsonSerializer.Serialize(visible))!;
  Check(roundtrip.Controls.Count==3,"Export roundtrip");
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"self-test.txt"),"PASS: discovery, traversal, hidden values, password exclusion, changed-session protection, disabled scripting, JSON export.");
 }
}
public class FakeEngine {public List<FakeConnection> Children{get;set;}=[];}
public class FakeConnection {public bool DisabledByServer{get;set;} public List<FakeSession> Children{get;set;}=[];}
public class FakeInfo {public string SystemName=>"TEST";public string Client=>"100";public string User{get;set;}="Tester";public string Transaction=>"IE03";public int ScriptingModeRecordingDisabled=>1;public int ScriptingModeReadOnly=>0;}
public class FakeSession {public string Id=>"/app/con[0]/ses[0]";public bool Busy=>false;public FakeInfo Info{get;}=new();public FakeNode ActiveWindow=>Children[0];public List<FakeNode> Children{get;set;}=[];}
public class FakeNode {
 public string Id{get;set;}="";public string Type{get;set;}="";public string Name{get;set;}="";
 public bool Secret{get;set;}public int TextReads{get;private set;}string text="";
 public string Text{get{TextReads++;if(Secret)throw new Exception("Password read");return text;}set{text=value;}}
 public string Tooltip=>"";public bool ContainerType=>Children.Count>0;public bool Changeable=>false;
 public List<FakeNode> Children{get;set;}=[];
}
