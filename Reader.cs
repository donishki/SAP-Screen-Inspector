using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;


namespace Inspector;
public record SapSession(string Id,string System,string Client,string User,string Transaction,string Title,string Recording,string ReadOnly) {
 public string Label => $"{System} / {Client}  ·  {User}  ·  {Transaction}  —  {Title}";
}
public record ControlRow(string Id,string Type,string Name,string Text,string Tooltip,string Parent,int Depth,string Changeable,string Columns) {
 public string Change {get;set;}="";
 public string ShortId => Id.Contains("/wnd[")?Id[Id.IndexOf("/wnd[")..].TrimStart('/'):Id;
}
public record Snapshot(DateTime Captured,SapSession Session,List<ControlRow> Controls,bool ValuesIncluded,List<string> Warnings);
public record Request(string Action,SapSession? Session=null,bool Values=false);
public record Reply(List<SapSession>? Sessions=null,Snapshot? Snapshot=null,string? Error=null);
public static class Reader {
 static string Get(Func<object?> f){try{return Convert.ToString(f())??"";}catch{return "";}}
 static dynamic Engine(){
  dynamic? gui=null;
  try{gui=SapDispatch.Wrap(Marshal.BindToMoniker("SAPGUI"));}
  catch(COMException){var t=Type.GetTypeFromProgID("SapROTWr.SapROTWrapper");if(t!=null){dynamic rot=SapDispatch.Wrap(Activator.CreateInstance(t))!;gui=rot.GetROTEntry("SAPGUI");}}
  if(gui==null)throw new InvalidOperationException("Open SAP GUI for Windows and log in first. Use the inspector in the same Windows session as SAP.");
  return gui.GetScriptingEngine;
 }
 static SapSession Describe(dynamic s){
  if((bool)s.Busy) return new(Get(()=>s.Id),"","","","Busy","SAP is processing","","");
  dynamic i=s.Info;
  return new(Get(()=>s.Id),Get(()=>i.SystemName),Get(()=>i.Client),Get(()=>i.User),Get(()=>i.Transaction),Get(()=>s.ActiveWindow.Text),Get(()=>i.ScriptingModeRecordingDisabled),Get(()=>i.ScriptingModeReadOnly));
 }
 public static Reply Read(Request r, object? testEngine=null){
  dynamic app=testEngine??Engine();var sessions=new List<SapSession>();var warnings=new List<string>();
  for(int c=0;c<(int)app.Children.Count;c++){
   dynamic connection=app.Children[c];
   if(Get(()=>connection.DisabledByServer).Equals("True",StringComparison.OrdinalIgnoreCase)){warnings.Add("Scripting is disabled by the SAP server.");continue;}
   for(int n=0;n<(int)connection.Children.Count;n++){
    dynamic s=connection.Children[n];var info=Describe(s);sessions.Add(info);
    if(r.Action!="capture"||r.Session==null||info.Id!=r.Session.Id)continue;
    if(info.System!=r.Session.System||info.Client!=r.Session.Client||info.User!=r.Session.User)throw new InvalidOperationException("This SAP session changed. Reload sessions and select it again.");
    if(Get(()=>s.Busy)=="True")throw new InvalidOperationException("SAP is busy. Wait for the screen to finish loading, then capture again.");
    var rows=new List<ControlRow>();var watch=Stopwatch.StartNew();
    void Walk(dynamic node,string parent,int depth){
     if(rows.Count>=12000||depth>40||watch.Elapsed.TotalSeconds>15){if(!warnings.Contains("Capture limited: screen is very large."))warnings.Add("Capture limited: screen is very large.");return;}
     string id=Get(()=>node.Id),type=Get(()=>node.Type);
     bool password=type.Contains("Password",StringComparison.OrdinalIgnoreCase);
     string value=password?"[password excluded]":r.Values?Get(()=>node.Text):"";
     string columns="";
     if(type=="GuiGridView"){
      var cols=new List<string>();try{foreach(var col in node.ColumnOrder){string key=Convert.ToString(col)??"";cols.Add(key+" — "+Get(()=>node.GetDisplayedColumnTitle(key)));}}catch{warnings.Add("Some grid column metadata could not be read.");}
      columns=string.Join(Environment.NewLine,cols);
     }
     rows.Add(new(id,type,Get(()=>node.Name),value,Get(()=>node.Tooltip),parent,depth,Get(()=>node.Changeable),columns));
     // Do not read grid cells or change scroll position; only enumerate child controls.
     if(Get(()=>node.ContainerType)!="True")return;
     try{int count=(int)node.Children.Count;for(int k=0;k<count;k++)Walk(node.Children[k],id,depth+1);}
     catch{warnings.Add("Some controls changed or were unavailable during capture.");}
    }
    for(int w=0;w<(int)s.Children.Count;w++)Walk(s.Children[w],"",0);
    return new(Snapshot:new(DateTime.Now,info,rows,r.Values,warnings.Distinct().ToList()));
   }
  }
  if(r.Action=="capture")throw new InvalidOperationException("The selected SAP session is no longer available. Reload sessions.");
  if(sessions.Count==0)throw new InvalidOperationException(warnings.Count>0?string.Join(" ",warnings):"No SAP sessions found. Log in and ensure SAP GUI scripting is enabled.");
  return new(Sessions:sessions);
 }
 public static async Task<Reply> Run(Request request){
  string dir=Path.Combine(Path.GetTempPath(),"SAP-Screen-Inspector",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
  string input=Path.Combine(dir,"request.json"),output=Path.Combine(dir,"reply.json");
  try{
   await File.WriteAllTextAsync(input,JsonSerializer.Serialize(request));
   var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};
   if(Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(typeof(Reader).Assembly.Location);
   foreach(var arg in new[]{"--worker",input,output})start.ArgumentList.Add(arg);
   using var process=Process.Start(start)!;
   using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25));
   try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){process.Kill(true);await process.WaitForExitAsync();throw new InvalidOperationException("SAP did not respond within 25 seconds. Close any SAP prompts and try again.");}
   if(!File.Exists(output))throw new InvalidOperationException("SAP reader could not start. Try the other x86/x64 build if SAP scripting components are unavailable.");
   var result=JsonSerializer.Deserialize<Reply>(await File.ReadAllTextAsync(output))!;
   if(result.Error!=null)throw new InvalidOperationException(result.Error);
   return result;
  }finally{try{Directory.Delete(dir,true);}catch{}}
 }
}

