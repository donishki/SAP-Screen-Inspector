using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;


namespace Inspector;
public record SapSession(string Id,string System,string Client,string User,string Transaction,string Title,string Recording,string ReadOnly) {
 public string Label => $"{System} / {Client}  ·  {User}  ·  {Transaction}  —  {Title}";
}
public record ControlRow(string Id,string Type,string Name,string Text,string Tooltip,string Parent,int Depth,string Changeable,string Columns) {
 public ScreenBounds? Bounds {get;init;}
 public string ObservedState {get;init;}="";
 public bool Modified {get;init;}
 public long NativeHandle {get;init;}
 public string SubType {get;init;}="";
 public SelectionDetails? Selection {get;init;}
 public DateTime SampledAt {get;init;}
 public bool StateAvailable {get;init;}=true;
 public bool ModifiedAvailable {get;init;}=true;
 public string Change {get;set;}="";
 [System.Text.Json.Serialization.JsonIgnore] public string StatusLabel=>string.IsNullOrEmpty(Change)?"Captured":Change;
 [System.Text.Json.Serialization.JsonIgnore] public string Icon=>Type switch {"GuiButton"=>"\uE768","GuiTab"=>"\uE8B7","GuiCheckBox"=>"\uE73A","GuiComboBox"=>"\uE70D","GuiMainWindow"=>"\uE737","GuiUserArea"=>"\uE8B7",_=>"\uE8A5"};
 public string ShortId => Id.Contains("/wnd[")?Id[Id.IndexOf("/wnd[")..].TrimStart('/'):Id;
}
public record ScreenBounds(int Left,int Top,int Width,int Height);
public record ObservedWindow(long Handle,string Id,uint ProcessId=0) {
 public ScreenBounds? Bounds {get;init;}
}
public record Snapshot(DateTime Captured,SapSession Session,List<ControlRow> Controls,bool ValuesIncluded,List<string> Warnings) {
 public List<ObservedWindow> Windows {get;init;}=[];
 public DateTime CompletedAt {get;init;}
 public bool ScopeVerified {get;init;}
}
public record Request(string Action,SapSession? Session=null,bool Values=false,bool Observe=false,int TimeoutSeconds=25);
public record Reply(List<SapSession>? Sessions=null,Snapshot? Snapshot=null,string? Error=null,string? ErrorKind=null);
public sealed class SapReadException(string kind,string message) : InvalidOperationException(message) {
 public string Kind {get;}=kind;
}
public static class Reader {
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out NativeRect rect);
 [StructLayout(LayoutKind.Sequential)]struct NativeRect {public int Left,Top,Right,Bottom;}
 static string Get(Func<object?> f){try{return Convert.ToString(f())??"";}catch{return "";}}
 internal static dynamic Engine(){
  dynamic? gui=null;
  try{gui=SapDispatch.Wrap(Marshal.BindToMoniker("SAPGUI"));}
  catch(COMException){var t=Type.GetTypeFromProgID("SapROTWr.SapROTWrapper");if(t!=null){dynamic rot=SapDispatch.Wrap(Activator.CreateInstance(t))!;gui=rot.GetROTEntry("SAPGUI");}}
  if(gui==null)throw new InvalidOperationException("Open SAP GUI for Windows and log in first. Use the inspector in the same Windows session as SAP.");
  return gui.GetScriptingEngine;
 }
 internal static SapSession Describe(dynamic s){
  if((bool)s.Busy) return new(Get(()=>s.Id),"","","","Busy","SAP is processing","","");
  dynamic i=s.Info;
  return new(Get(()=>s.Id),Get(()=>i.SystemName),Get(()=>i.Client),Get(()=>i.User),Get(()=>i.Transaction),Get(()=>s.ActiveWindow.Text),Get(()=>i.ScriptingModeRecordingDisabled),Get(()=>i.ScriptingModeReadOnly));
 }
 public static Reply Read(Request r, object? testEngine=null){
  dynamic app=testEngine??Engine();var sessions=new List<SapSession>();var warnings=new List<string>();
  dynamic connections=app.Children;int connectionCount=(int)connections.Count;
  for(int c=0;c<connectionCount;c++){
   dynamic connection=connections[c];
   if(Get(()=>connection.DisabledByServer).Equals("True",StringComparison.OrdinalIgnoreCase)){warnings.Add("Scripting is disabled by the SAP server.");continue;}
   dynamic children=connection.Children;int sessionCount=(int)children.Count;
   for(int n=0;n<sessionCount;n++){
    dynamic s=children[n];
    if(r.Action=="capture"&&r.Session!=null&&Get(()=>s.Id)!=r.Session.Id)continue;
    if(r.Action=="capture"&&r.Session!=null&&Get(()=>s.Id)==r.Session.Id&&Get(()=>s.Busy)=="True")
     throw new SapReadException("Busy","SAP is processing. Waiting for a stable screen.");
    var info=Describe(s);sessions.Add(info);
    if(r.Action!="capture"||r.Session==null||info.Id!=r.Session.Id)continue;
    if(info.System!=r.Session!.System||info.Client!=r.Session.Client||info.User!=r.Session.User)throw new InvalidOperationException("This SAP session changed. Reload sessions and select it again.");
    if(Get(()=>s.Busy)=="True")throw new SapReadException("Busy","SAP is processing. Waiting for a stable screen.");
    var sampleStarted=DateTime.Now;var rows=new List<ControlRow>();var watch=Stopwatch.StartNew();
    void Walk(dynamic node,string parent,int depth){
     if(rows.Count>=12000||depth>40||watch.Elapsed.TotalSeconds>15){if(!warnings.Contains("Capture limited: screen is very large."))warnings.Add("Capture limited: screen is very large.");return;}
     var sampledAt=DateTime.Now;
     string id=Get(()=>node.Id),type=Get(()=>node.Type);
     if(id.Length==0||type.Length==0){warnings.Add("A control identity was unavailable.");return;}
     // Hidden menu trees account for many expensive COM reads but no screen input state.
     // Full manual inspection still includes them.
     if(r.Observe&&type is "GuiMenubar" or "GuiMenu" or "GuiLabel" or "GuiTitlebar")return;
     bool target=Observation.IsTarget(type),field=Observation.IsValueControl(type);
     bool password=type.Contains("Password",StringComparison.OrdinalIgnoreCase);
     string changeable=!r.Observe||field?Get(()=>node.Changeable):"";
     string subtype=type=="GuiShell"?Get(()=>node.SubType):"";
     bool stateAvailable=true,modifiedAvailable=true;
     string ReadState(Func<object?> read){try{return Convert.ToString(read())??"";}catch{stateAvailable=false;return "";}}
     string value=password?"[password excluded]":r.Values&&(!r.Observe||field)?ReadState(()=>node.Text):"";
     string columns="";
     if(type=="GuiGridView"&&!r.Observe){
      var cols=new List<string>();try{foreach(var col in node.ColumnOrder){string key=Convert.ToString(col)??"";cols.Add(key+" — "+Get(()=>node.GetDisplayedColumnTitle(key)));}}catch{warnings.Add("Some grid column metadata could not be read.");}
      columns=string.Join(Environment.NewLine,cols);
     }
     ScreenBounds? bounds=null;
     if(r.Observe&&target&&int.TryParse(Get(()=>node.ScreenLeft),out int left)&&int.TryParse(Get(()=>node.ScreenTop),out int top)&&
        int.TryParse(Get(()=>node.Width),out int width)&&int.TryParse(Get(()=>node.Height),out int height)&&width>0&&height>0)
      bounds=new(left,top,width,height);
     string state="";
     if(r.Observe&&r.Values&&!password&&Observation.IsValueControl(type))state=type switch {
      "GuiComboBox"=>ReadState(()=>node.Key),"GuiCheckBox" or "GuiRadioButton"=>ReadState(()=>node.Selected),_=>value
     };
     long nativeHandle=0;
     if(r.Observe&&type is "GuiShell" or "GuiGridView" or "GuiTree" or "GuiTextedit")long.TryParse(Get(()=>node.Handle),out nativeHandle);
     string modified="";
     if(r.Observe&&!password&&field&&changeable is "True" or "1" or "-1"){try{modified=Convert.ToString(node.Modified)??"";}catch{modifiedAvailable=false;}}
     if(r.Observe&&!stateAvailable)warnings.Add("A field value was unavailable during capture.");
     rows.Add(new(id,type,!r.Observe||target||password?Get(()=>node.Name):"",value,r.Observe?"":Get(()=>node.Tooltip),parent,depth,changeable,columns){
      Bounds=bounds,NativeHandle=unchecked((uint)nativeHandle),ObservedState=state,SubType=subtype,
      Selection=password?null:SelectionReader.Read(node,SelectionReader.Kind(type,subtype)),
      SampledAt=sampledAt,StateAvailable=stateAvailable,ModifiedAvailable=modifiedAvailable,
      Modified=modified is "True" or "1" or "-1"
     });
     // Do not read grid cells or change scroll position; only enumerate child controls.
     if(Get(()=>node.ContainerType)!="True")return;
     try{dynamic descendants=node.Children;int count=(int)descendants.Count;for(int k=0;k<count;k++){
      try{Walk(descendants[k],id,depth+1);}catch{warnings.Add("Some controls changed or were unavailable during capture.");}
     }}
     catch{warnings.Add("Some controls changed or were unavailable during capture.");}
    }
    var windows=new List<ObservedWindow>();
    dynamic sessionWindows=s.Children;int windowCount=(int)sessionWindows.Count;
    for(int w=0;w<windowCount;w++){
     dynamic window=sessionWindows[w];
     if(r.Observe&&long.TryParse(Get(()=>window.Handle),out long handle)&&handle!=0){
      uint normalized=unchecked((uint)handle);
      GetWindowThreadProcessId(new IntPtr(unchecked((int)normalized)),out uint processId);
      var bounds=GetWindowRect(new IntPtr(unchecked((int)normalized)),out var rect)?new ScreenBounds(rect.Left,rect.Top,rect.Right-rect.Left,rect.Bottom-rect.Top):null;
      windows.Add(new(normalized,Get(()=>window.Id),processId){Bounds=bounds});
     }
     Walk(window,"",0);
    }
    bool verified=false;
    if(r.Observe){
     try{
      if(Get(()=>s.Busy)=="True")throw new SapReadException("Busy","SAP started processing during sampling.");
      var end=Describe(s);
      if(end.Id!=info.Id||end.System!=info.System||end.Client!=info.Client||end.User!=info.User)
       throw new SapReadException("Unavailable","SAP session identity changed during sampling.");
      verified=end.Transaction==info.Transaction&&end.Title==info.Title&&(int)s.Children.Count==windowCount&&windows.Count==windowCount;
      for(int w=0;verified&&w<windowCount;w++){
       dynamic window=s.Children[w];var saved=windows[w];
       uint handle=unchecked((uint)Convert.ToInt64(window.Handle));
       GetWindowThreadProcessId(new IntPtr(unchecked((int)handle)),out uint pid);
       var bounds=GetWindowRect(new IntPtr(unchecked((int)handle)),out var rect)?new ScreenBounds(rect.Left,rect.Top,rect.Right-rect.Left,rect.Bottom-rect.Top):null;
       verified=saved.Id==Get(()=>window.Id)&&saved.Handle==handle&&saved.ProcessId==pid&&pid!=0&&saved.Bounds==bounds;
      }
     }catch(SapReadException){throw;}
     catch{verified=false;}
     // Without HWNDs the result is inspection-only and cannot start an input monitor.
     if(!verified&&windows.Count>0){warnings.Add("Screen or window scope changed during sampling; control targets were discarded.");rows.Clear();}
    }
    return new(Snapshot:new(sampleStarted,info,rows,r.Values,warnings.Distinct().ToList()){Windows=windows,CompletedAt=DateTime.Now,ScopeVerified=verified});
   }
  }
  if(r.Action=="capture")throw new InvalidOperationException("The selected SAP session is no longer available. Reload sessions.");
  if(sessions.Count==0)throw new InvalidOperationException(warnings.Count>0?string.Join(" ",warnings):"No SAP sessions found. Log in and ensure SAP GUI scripting is enabled.");
  return new(Sessions:sessions);
 }
 public static async Task<Reply> Run(Request request,CancellationToken cancellationToken=default){
  string dir=Path.Combine(Path.GetTempPath(),"SAP-Screen-Inspector",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
  string input=Path.Combine(dir,"request.json"),output=Path.Combine(dir,"reply.json");
  try{
   await File.WriteAllTextAsync(input,JsonSerializer.Serialize(request));
   var start=WorkerStart("--worker",input,output);
   using var process=Process.Start(start)!;
   int seconds=Math.Clamp(request.TimeoutSeconds,1,25);
   using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
   try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();cancellationToken.ThrowIfCancellationRequested();throw new SapReadException("Timeout",$"SAP did not respond within {seconds} seconds.");}
   if(!File.Exists(output))throw new InvalidOperationException("SAP reader could not start. Try the other x86/x64 build if SAP scripting components are unavailable.");
   var result=JsonSerializer.Deserialize<Reply>(await File.ReadAllTextAsync(output))!;
   if(result.Error!=null)throw new SapReadException(result.ErrorKind??"Unavailable",result.Error);
   return result;
  }finally{try{Directory.Delete(dir,true);}catch{}}
 }
 internal static ProcessStartInfo WorkerStart(params string[] arguments){
  var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};
  if(Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(typeof(Reader).Assembly.Location);
  foreach(var argument in arguments)start.ArgumentList.Add(argument);
  return start;
 }
}

