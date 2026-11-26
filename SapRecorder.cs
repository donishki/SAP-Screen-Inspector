using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text.RegularExpressions;

namespace Inspector;

// Keep this object on the WPF STA: COM delivers recording events through its message pump.
internal sealed class SapRecorder {
 object? source;
 dynamic? session;
 Guid eventId;
 int changeId;
 Action<object,object,object>? handler;
 Action? unsubscribe;
 public event Action<string,string,string,string>? Changed;
 public event Action<string>? Failed;

 public void Start(SapSession selected,object? testEngine=null,Func<object,Action<object,object,object>,Action>? subscribe=null) {
  dynamic app=testEngine??Reader.Engine();
  for(int c=0;c<(int)app.Children.Count;c++) {
   dynamic connection=app.Children[c];
   if(Convert.ToBoolean(connection.DisabledByServer))continue;
   for(int n=0;n<(int)connection.Children.Count;n++) {
    dynamic candidate=connection.Children[n];
    if((string)candidate.Id!=selected.Id)continue;
    if(Convert.ToBoolean(candidate.Busy))throw new InvalidOperationException("SAP is busy. Wait for SAP to finish, then press Record.");
    SapSession info=Reader.Describe(candidate);
    if(info.System!=selected.System||info.Client!=selected.Client||info.User!=selected.User)
     throw new InvalidOperationException("The selected session changed. Reload sessions first.");
    if(info.Recording is "1" or "True" || info.ReadOnly is "1" or "True")
     throw new InvalidOperationException("SAP recording is disabled or scripting is read-only. Ask your SAP administrator to permit recording. Screen inspection remains available.");
    if(Convert.ToBoolean(candidate.Record))throw new InvalidOperationException("This SAP session is already recording. Stop its other recorder first.");
    source=SapDispatch.Unwrap(candidate);
    handler=OnChange;
    if(subscribe!=null)unsubscribe=subscribe(source!,handler);
    else {
     (eventId,changeId)=FindChangeEvent(source!);
     ComEventsHelper.Combine(source!,eventId,changeId,handler);
     unsubscribe=()=>ComEventsHelper.Remove(source!,eventId,changeId,handler);
    }
    session=candidate;
    try { session.Record=true; }
    catch { try { session.Record=false; } finally { Detach(); } throw; }
    return;
   }
  }
  throw new InvalidOperationException("The selected SAP session is no longer available. Reload sessions.");
 }

 public void Stop() {
  // SAP flushes the final pending changes synchronously here; detach only afterwards.
  try {if(session!=null)session.Record=false;}
  catch(COMException ex) when(ex.HResult is unchecked((int)0x80010108) or unchecked((int)0x800706BA) or unchecked((int)0x800401FD)) {
   Failed?.Invoke("SAP disconnected before Stop could flush pending actions. Collected steps are retained, but the sequence may be incomplete.");
   try {Detach();} catch(COMException) {unsubscribe=null;handler=null;source=null;session=null;}
   return;
  }
  Detach();
 }
 void Detach() {
  unsubscribe?.Invoke();unsubscribe=null;
  handler=null;source=null;session=null;
 }
 void OnChange(object sender,object component,object commands) {
  try {
   dynamic control=SapDispatch.Wrap(component)!;
   string id=control.Id,type=control.Type;
   string shortId=id.Contains("/wnd[")?id[(id.IndexOf("/wnd[",StringComparison.Ordinal)+1)..]:id;
   // Never inspect password command arguments or read the control's Text property.
   string script=type.Contains("Password",StringComparison.OrdinalIgnoreCase)
    ? "' TODO: Password action omitted." : RenderCommand(shortId,commands);
   Changed?.Invoke(shortId,type,script,type.Contains("Password",StringComparison.OrdinalIgnoreCase)?"Password omitted":"SAP action");
  } catch { Failed?.Invoke("A SAP recording event could not be decoded. The sequence is incomplete; review it before use."); }
 }

 internal static string RenderCommand(string id,object commands) {
  if(!Regex.IsMatch(id,@"^wnd\[\d+\](?:/|$)"))throw new InvalidOperationException("Invalid SAP control ID.");
  var rows=new List<object?[]>();
  if(commands is not Array array)throw new InvalidOperationException("Invalid SAP command array.");
  if(array.Rank==2) {
   if(array.GetLength(1)!=3)throw new InvalidOperationException("Invalid SAP command columns.");
   for(int r=array.GetLowerBound(0);r<=array.GetUpperBound(0);r++)rows.Add(Enumerable.Range(array.GetLowerBound(1),3).Select(c=>array.GetValue(r,c)).ToArray());
  } else if(array.Rank==1) {
   var items=array.Cast<object?>().ToArray();
   if(items.Length>=2&&items[0] is string)rows.Add(items);
   else foreach(var row in items)rows.Add(row is Array a?a.Cast<object?>().ToArray():throw new InvalidOperationException("Invalid SAP command row."));
  } else throw new InvalidOperationException("Invalid SAP command dimensions.");
  if(rows.Count==0)throw new InvalidOperationException("Empty SAP command.");
  string result="session.findById("+Literal(id)+")";
  for(int i=0;i<rows.Count;i++) {
   var row=rows[i];
   if(row.Length<2||row.Length>3||row[1] is not string member||!Regex.IsMatch(member,@"^[A-Za-z_][A-Za-z0-9_]*$"))throw new InvalidOperationException("Invalid SAP member.");
   object? value=row.Length==3?row[2]:null;
   var args=value is Array a?a.Cast<object?>().ToArray():value==null?[]:new[]{value};
   result+="."+member;
   if(Equals(row[0],"SP")&&i==rows.Count-1&&args.Length==1)result+=" = "+Literal(args[0]);
   else if(Equals(row[0],"M"))result+=i<rows.Count-1?"("+string.Join(", ",args.Select(Literal))+")":args.Length==0?"":" "+string.Join(", ",args.Select(Literal));
   else throw new InvalidOperationException("Unsupported SAP command type.");
  }
  return result;
 }
 static string Literal(object? value)=>value switch {
  string s=>"\""+s.Replace("\"","\"\"").Replace("\r","\" & vbCr & \"").Replace("\n","\" & vbLf & \"")+"\"",
  bool b=>b?"True":"False",
  byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal=>Convert.ToString(value,CultureInfo.InvariantCulture)!,
  _=>throw new InvalidOperationException("Unsupported SAP argument type.")
 };

 static (Guid,int) FindChangeEvent(object target) {
  ((IDispatchInfo)target).GetTypeInfo(0,0,out var type);
  type.GetContainingTypeLib(out var library,out _);
  ((IConnectionPointContainer)target).EnumConnectionPoints(out var points);
  var buffer=new IConnectionPoint[1];var fetched=Marshal.AllocCoTaskMem(sizeof(int));
  try {
   while(true) {
    Marshal.WriteInt32(fetched,0);points.Next(1,buffer,fetched);if(Marshal.ReadInt32(fetched)==0)break;
    var point=buffer[0];
    try {
     point.GetConnectionInterface(out var guid);
     library.GetTypeInfoOfGuid(ref guid,out var events);
     try {
      events.GetTypeAttr(out var attr);
      try {
       var description=Marshal.PtrToStructure<TYPEATTR>(attr);
       for(int i=0;i<description.cFuncs;i++) {
        events.GetFuncDesc(i,out var pointer);
        try {
         var function=Marshal.PtrToStructure<FUNCDESC>(pointer);
         events.GetDocumentation(function.memid,out var name,out _,out _,out _);
         if(name=="Change"&&function.cParams==3)return(guid,function.memid);
        } finally {events.ReleaseFuncDesc(pointer);}
       }
      } finally {events.ReleaseTypeAttr(attr);}
     } finally {Marshal.ReleaseComObject(events);}
    } finally {Marshal.ReleaseComObject(point);}
   }
  } finally {Marshal.FreeCoTaskMem(fetched);Marshal.ReleaseComObject(points);Marshal.ReleaseComObject(library);Marshal.ReleaseComObject(type);}
  throw new InvalidOperationException("This SAP GUI does not expose recording events. Try the other x86/x64 package.");
 }
 [ComImport,Guid("00020400-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface IDispatchInfo {
  void GetTypeInfoCount(out uint count);
  void GetTypeInfo(uint index,uint locale,out ITypeInfo info);
 }
}
