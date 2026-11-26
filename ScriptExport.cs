using System.Text;
using System.Text.RegularExpressions;

namespace Inspector;
public static class ScriptExport {
 static string Quote(string value)=>"\""+value.Replace("\"","\"\"").Replace("\r\n","\" & vbCrLf & \"").Replace("\r","\" & vbCr & \"").Replace("\n","\" & vbLf & \"")+"\"";
 static string Line(string value)=>value.Replace('\r',' ').Replace('\n',' ');
 public static string Render(IReadOnlyList<CapturedStep> steps){
  var b=new StringBuilder();
  b.AppendLine("' SAP Screen Inspector - SAP-style VBScript draft");
  b.AppendLine("' Native SAP commands where recorded; manually annotated steps may contain TODOs.");
  b.AppendLine("' Review all commands and TODOs before use. Connection/session indices may change.");
  var match=Regex.Match(steps.FirstOrDefault()?.Screen.Session.Id??"",@"con\[(\d+)\]/ses\[(\d+)\]");
  string connection=match.Success?match.Groups[1].Value:"0",session=match.Success?match.Groups[2].Value:"0";
  b.AppendLine("If Not IsObject(application) Then\r\n   Set SapGuiAuto = GetObject(\"SAPGUI\")\r\n   Set application = SapGuiAuto.GetScriptingEngine\r\nEnd If");
  b.AppendLine($"If Not IsObject(connection) Then\r\n   Set connection = application.Children({connection})\r\nEnd If");
  b.AppendLine($"If Not IsObject(session) Then\r\n   Set session = connection.Children({session})\r\nEnd If");
  b.AppendLine("If IsObject(WScript) Then\r\n   WScript.ConnectObject session, \"on\"\r\n   WScript.ConnectObject application, \"on\"\r\nEnd If");
  for(int i=0;i<steps.Count;i++){
   var step=steps[i];
   b.AppendLine();b.AppendLine($"' Step {step.Number}: {Line(step.Action)}");
   if(step.Notes.Length>0)b.AppendLine("' "+Line(step.Notes));
   if(step.RecordedScript!=null){b.AppendLine(step.RecordedScript);continue;}
   if(step.Action=="Starting screen")continue;
   var current=step.Screen.Controls.FirstOrDefault(c=>c.ShortId==step.ControlId);
   var previous=i>0?steps[i-1].Screen.Controls.FirstOrDefault(c=>c.ShortId==step.ControlId):null;
   var control=previous??current;
   string target="session.findById("+Quote(step.ControlId)+")",argument=step.Argument;
   string? command=null;
   if(control?.Type=="GuiPasswordField"){
    b.AppendLine("' TODO: Password action omitted.");continue;
   }
   if(control!=null&&Regex.IsMatch(step.ControlId,@"^wnd\[\d+\](?:/|$)")){
    command=step.Action switch {
     "Click" when control.Type=="GuiButton"=>target+".press",
     "Click" when control.Type is "GuiRadioButton" or "GuiTab" or "GuiMenu"=>target+".select",
     "Select tab" when control.Type=="GuiTab"=>target+".select",
     "Choose item" when control.Type=="GuiComboBox"&&argument.Length>0=>target+".key = "+Quote(argument),
     "Choose item" or "Click" when control.Type=="GuiCheckBox"&&bool.TryParse(argument,out var selected)=>target+".selected = "+(selected?"True":"False"),
     "Enter text" when control.Type is "GuiTextField" or "GuiCTextField" && (argument.Length>0||(current!=null&&step.Screen.ValuesIncluded))=>target+".text = "+Quote(argument.Length>0?argument:current!.Text),
     "Press key" when int.TryParse(argument,out var key)&&key>=0&&key<=255=>"session.findById("+Quote(Regex.Match(step.ControlId,@"^wnd\[\d+\]").Value)+").sendVKey "+key,
     _=>null
    };
   }
   b.AppendLine(command??"' TODO: Specify a supported action, control and required action value. Control: "+Line(step.ControlId));
  }
  return b.ToString();
 }
}
