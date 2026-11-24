namespace Inspector;
public static class ScriptExportTests {
 public static void Run(){
  var session=new SapSession("/app/con[2]/ses[1]","TEST","100","Demo","IE03","","","");
  ControlRow Row(string id,string type,string text="")=>new(id,type,"",text,"","",0,"","");
  var before=new Snapshot(DateTime.Now,session,[Row("wnd[0]/usr/btnGo","GuiButton"),Row("wnd[0]/usr/txtName","GuiTextField"),Row("wnd[0]/usr/pwd","GuiPasswordField"),Row("wnd[0]/usr/cmb","GuiComboBox")],false,[]);
  var after=before with {Controls=[Row("wnd[0]/usr/txtName","GuiTextField","A\"B\r\nC")],ValuesIncluded=true};
  var steps=new List<CapturedStep>{new(){Number=1,Action="Starting screen",Screen=before},new(){Number=2,Action="Click",ControlId="wnd[0]/usr/btnGo",Screen=after},new(){Number=3,Action="Enter text",ControlId="wnd[0]/usr/txtName",Screen=after}};
  void Check(bool value){if(!value)throw new Exception("SAP script export test failed");}
  var result=ScriptExport.Render(steps);
  Check(result.Contains("application.Children(2)")&&result.Contains("connection.Children(1)"));
  Check(result.Contains("session.findById(\"wnd[0]/usr/btnGo\").press"));
  Check(result.Contains(".text = \"A\"\"B\" & vbCrLf & \"C\""));
  steps.Add(new(){Number=4,Action="Enter text",ControlId="wnd[0]/usr/pwd",Argument="secret-value",Screen=before});
  steps.Add(new(){Number=5,Action="Choose item",ControlId="wnd[0]/usr/cmb",Argument="KEY",Screen=before});
  steps.Add(new(){Number=6,Action="Press key",ControlId="wnd[0]/usr/cmb",Argument="0",Screen=before});
  steps.Add(new(){Number=7,Action="Double-click",ControlId="wnd[0]/usr/cmb",Screen=before});
  steps.Add(new(){Number=8,Action="Enter text",ControlId="wnd[0]/usr/txtName",Screen=before});
  result=ScriptExport.Render(steps);
  Check(!result.Contains("secret-value")&&result.Contains("Password action omitted"));
  Check(result.Contains(".key = \"KEY\"")&&result.Contains("session.findById(\"wnd[0]\").sendVKey 0"));
  Check(result.Split("' TODO:").Length==4);
 }
}
