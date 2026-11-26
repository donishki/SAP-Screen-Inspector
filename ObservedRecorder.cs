using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Inspector;

internal record ObservedInput(DateTime Time,string Action,string Detail,int X,int Y,long Window,Snapshot Screen,
 long NativeTarget=0,ScreenBounds? WindowBounds=null);
internal interface IInputMonitor : IDisposable {
 void Start(Snapshot initial);
 void Update(Snapshot next);
 List<ObservedInput> Drain();
 int TakeDropped();
}

// This monitor never translates or stores printable keys. Hooks do no COM or UI work.
internal sealed class SapInputMonitor : IInputMonitor {
 readonly ConcurrentQueue<ObservedInput> pending=new();
 readonly ManualResetEventSlim ready=new();
 readonly Hook mouseCallback,keyboardCallback;
 Thread? thread;Dispatcher? dispatcher;Exception? startupError;
 Snapshot? screen;IntPtr mouse,keyboard;int count,dropped;
 public SapInputMonitor(){mouseCallback=Mouse;keyboardCallback=Keyboard;}
 public void Start(Snapshot initial){
  if(initial.Windows.Count==0||initial.Windows.Any(w=>w.ProcessId==0))throw new InvalidOperationException("SAP did not expose valid window handles. Cannot restrict observation to the selected session.");
  screen=initial;
  thread=new Thread(()=>{
   try {
    dispatcher=Dispatcher.CurrentDispatcher;
    mouse=SetWindowsHookEx(14,mouseCallback,GetModuleHandle(null),0);
    keyboard=SetWindowsHookEx(13,keyboardCallback,GetModuleHandle(null),0);
    if(mouse==IntPtr.Zero||keyboard==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot observe Windows input.");
   }catch(Exception ex){startupError=ex;}finally{ready.Set();}
   try {if(startupError==null)Dispatcher.Run();}
   finally {if(mouse!=IntPtr.Zero)UnhookWindowsHookEx(mouse);if(keyboard!=IntPtr.Zero)UnhookWindowsHookEx(keyboard);}
  }){IsBackground=true,Name="SAP input observer"};
  thread.SetApartmentState(ApartmentState.STA);thread.Start();ready.Wait();
  if(startupError!=null){Dispose();throw new InvalidOperationException(startupError.Message,startupError);}
 }
 public void Update(Snapshot next)=>Volatile.Write(ref screen,next);
 public List<ObservedInput> Drain(){var result=new List<ObservedInput>();while(pending.TryDequeue(out var input)){Interlocked.Decrement(ref count);result.Add(input);}return result;}
 public int TakeDropped()=>Interlocked.Exchange(ref dropped,0);
 public void Dispose(){
  Volatile.Write(ref screen,null);
  dispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
  thread?.Join(2000);
 }
 static long Handle(IntPtr value)=>unchecked((uint)value.ToInt64());
 internal static bool Accepts(Snapshot snapshot,long root,uint processId)=>root!=0&&processId!=0&&(DateTime.Now-snapshot.Captured).TotalSeconds<=30&&snapshot.Windows.Any(w=>w.Handle==root&&w.ProcessId==processId);
 void Enqueue(Snapshot current,string action,string detail,int x,int y,long root,long nativeTarget=0,ScreenBounds? bounds=null){
  if(Interlocked.Increment(ref count)>512){Interlocked.Decrement(ref count);Interlocked.Increment(ref dropped);return;}
  pending.Enqueue(new(DateTime.Now,action,detail,x,y,root,current,nativeTarget,bounds));
 }
 IntPtr Mouse(int code,IntPtr message,IntPtr data){
  try {
   int m=message.ToInt32();
   var current=Volatile.Read(ref screen);
   if(code>=0&&current!=null&&m is 0x201 or 0x204 or 0x207 or 0x20A or 0x20E){
    var input=Marshal.PtrToStructure<MouseData>(data);
    IntPtr target=WindowFromPoint(input.Point);
    long root=Handle(GetAncestor(target,2));
    GetWindowThreadProcessId(new IntPtr(unchecked((int)root)),out uint pid);
    var bounds=GetWindowRect(new IntPtr(unchecked((int)root)),out var rect)?new ScreenBounds(rect.Left,rect.Top,rect.Right-rect.Left,rect.Bottom-rect.Top):null;
    if((input.Flags&1)==0&&Accepts(current,root,pid))
     Enqueue(current,m is 0x20A or 0x20E?"Scroll":"Click",m switch{0x204=>"Right mouse button",0x207=>"Middle mouse button",0x20A=>"Vertical mouse wheel",0x20E=>"Horizontal mouse wheel",_=>"Left mouse button"},input.Point.X,input.Point.Y,root,Handle(target),bounds);
   }
  }catch{Interlocked.Increment(ref dropped);}
  return CallNextHookEx(IntPtr.Zero,code,message,data);
 }
 IntPtr Keyboard(int code,IntPtr message,IntPtr data){
  try {
   var current=Volatile.Read(ref screen);
   if(code>=0&&current!=null&&message.ToInt32() is 0x100 or 0x104){
    long root=Handle(GetAncestor(GetForegroundWindow(),2));
    GetWindowThreadProcessId(new IntPtr(unchecked((int)root)),out uint pid);
    if(Accepts(current,root,pid)){
     var input=Marshal.PtrToStructure<KeyData>(data);
     string? key=NavigationKey(input.Key);
     // Exclude injected keys and our own Ctrl+Alt+F8 shortcut.
     bool ctrl=(GetAsyncKeyState(0x11)&0x8000)!=0,alt=(GetAsyncKeyState(0x12)&0x8000)!=0,shift=(GetAsyncKeyState(0x10)&0x8000)!=0;
     if(key!=null&&(input.Flags&0x10)==0&&!(ctrl&&alt&&input.Key==0x77))
      Enqueue(current,"Press key",(ctrl?"Ctrl+":"")+(alt?"Alt+":"")+(shift?"Shift+":"")+key,0,0,root);
    }
   }
  }catch{Interlocked.Increment(ref dropped);}
  return CallNextHookEx(IntPtr.Zero,code,message,data);
 }
 internal static string? NavigationKey(uint key)=>key switch {
  9=>"Tab",13=>"Enter",27=>"Escape",33=>"Page Up",34=>"Page Down",35=>"End",36=>"Home",
  37=>"Left",38=>"Up",39=>"Right",40=>"Down",>=112 and <=123=>"F"+(key-111),_=>null
 };
 delegate IntPtr Hook(int code,IntPtr message,IntPtr data);
 [StructLayout(LayoutKind.Sequential)] struct Point {public int X,Y;}
 [StructLayout(LayoutKind.Sequential)] struct NativeRect {public int Left,Top,Right,Bottom;}
 [StructLayout(LayoutKind.Sequential)] struct MouseData {public Point Point;public uint MouseInfo,Flags,Time;public UIntPtr Extra;}
 [StructLayout(LayoutKind.Sequential)] struct KeyData {public uint Key,Scan,Flags,Time;public UIntPtr Extra;}
 [DllImport("user32.dll",SetLastError=true)]static extern IntPtr SetWindowsHookEx(int id,Hook callback,IntPtr module,uint thread);
 [DllImport("user32.dll")]static extern bool UnhookWindowsHookEx(IntPtr hook);
 [DllImport("user32.dll")]static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr GetModuleHandle(string? name);
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr window,uint flags);
 [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(Point point);
 [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out NativeRect rect);
}

internal static class Observation {
 internal static bool IsValueControl(string type)=>type is "GuiTextField" or "GuiCTextField" or "GuiOkCodeField" or "GuiComboBox" or "GuiCheckBox" or "GuiRadioButton";
 static bool Editable(ControlRow row)=>IsValueControl(row.Type)&&row.Changeable is "True" or "1" or "-1";
 static bool IsTarget(ControlRow row)=>IsValueControl(row.Type)||row.Type is "GuiButton" or "GuiTab" or "GuiShell" or "GuiGridView" or "GuiTree" or "GuiTableControl" or "GuiTextedit";
 static bool IsStructure(ControlRow row)=>row.Type is not ("GuiMenu" or "GuiMenubar" or "GuiLabel" or "GuiStatusbar" or "GuiTitlebar");
 internal static CapturedStep Input(ObservedInput input){
  var window=input.Screen.Windows.First(w=>w.Handle==input.Window);
  var candidates=input.Screen.Controls.Where(c=>c.Id.StartsWith(window.Id+"/",StringComparison.Ordinal)&&IsTarget(c)).ToList();
  double age=Math.Max(0,(input.Time-input.Screen.Captured).TotalSeconds);
  int x=input.X,y=input.Y;
  bool resized=false;
  if(window.Bounds is {} old&&input.WindowBounds is {} now){
   resized=old.Width!=now.Width||old.Height!=now.Height;
   x-=now.Left-old.Left;y-=now.Top-old.Top;
  }
  bool reliable=age<=4&&!resized&&input.Screen.Warnings.Count==0;
  ControlRow? control=null;
  string match="";
  if(input.Action!="Press key"&&reliable){
   if(input.NativeTarget!=0&&input.NativeTarget!=input.Window)
    control=candidates.FirstOrDefault(c=>c.NativeHandle==input.NativeTarget);
   if(control!=null)match="Matched a sampled native control handle.";
   else {
    control=candidates.Where(c=>c.Bounds is {Width:>0,Height:>0} b&&x>=b.Left&&y>=b.Top&&(long)x<b.Left+(long)b.Width&&(long)y<b.Top+(long)b.Height)
     .OrderBy(c=>(long)c.Bounds!.Width*c.Bounds.Height).ThenByDescending(c=>c.Depth).FirstOrDefault();
    if(control!=null)match="Target inferred from sampled bounds, adjusted for window movement.";
   }
  }
  string context=input.Action=="Press key"?$"Window: {window.Id}.":$"Position: ({input.X}, {input.Y}); window: {window.Id}.";
  return new(){Captured=input.Time,Action=input.Action,ControlId=control?.ShortId??"",RequiresReview=true,
   Screen=input.Screen,Notes=$"Observed: {input.Detail}. {context} "+(control==null?"Target control unknown. ":$"Target: {control.Name} ({control.Type}). {match} ")+
    (resized?"Window resized since sample. ":age>4?$"Screen sample is {age:0.0}s old. ":"")+"Review required."};
 }
 internal static IEnumerable<CapturedStep> Changes(Snapshot before,Snapshot after){
  var old=before.Controls.GroupBy(c=>c.Id).ToDictionary(g=>g.Key,g=>g.First());
  bool structure=before.Session.Transaction!=after.Session.Transaction||before.Session.Title!=after.Session.Title||
   !before.Controls.Where(IsStructure).Select(c=>(c.Id,c.Type,c.Name)).ToHashSet().SetEquals(after.Controls.Where(IsStructure).Select(c=>(c.Id,c.Type,c.Name)));
  if(structure){
   yield return new(){Captured=after.Captured,Action="Screen changed",RequiresReview=true,Screen=after,
    Notes=$"Screen changed: {before.Session.Transaction} -> {after.Session.Transaction}. {after.Session.Title}. Initial values on the new screen are not counted as edits. Review required."};
   yield break;
  }
  foreach(var row in after.Controls){
   if(!Editable(row)||!old.TryGetValue(row.Id,out var previous)||!Editable(previous))continue;
   bool values=before.ValuesIncluded&&after.ValuesIncluded;
   if((values&&row.ObservedState!=previous.ObservedState)||(!previous.Modified&&row.Modified)){
    bool text=row.Type is "GuiTextField" or "GuiCTextField" or "GuiOkCodeField";
    yield return new(){Captured=after.Captured,Action=text?"Enter text":"Choose item",ControlId=row.ShortId,RequiresReview=true,
     Argument=values?row.ObservedState:"",Screen=after,
     Notes="Observed field change; it may be caused by typing or by SAP itself. "+(values?"Sampled value included.":"Value excluded; enable Screen values before recording to include it.")+" Review required."};
   }
  }
 }
}

internal sealed class ObservedRecorder {
 readonly SapSession session;readonly bool values;
 readonly Func<Request,CancellationToken,Task<Reply>> read;
 readonly IInputMonitor monitor;
 readonly CancellationTokenSource cancel=new();
 readonly DispatcherTimer drainTimer=new(){Interval=TimeSpan.FromMilliseconds(150)};
 Snapshot current;Task? loop;bool stopped,gapReported;DateTime? failureSince;
 public event Action<CapturedStep>? Step;
 public event Action<string>? Gap;
 public event Action<string>? Notice;
 public ObservedRecorder(Snapshot initial,bool includeValues,Func<Request,CancellationToken,Task<Reply>>? reader=null,IInputMonitor? inputMonitor=null){
  current=initial;session=initial.Session;values=includeValues;read=reader??Reader.Run;monitor=inputMonitor??new SapInputMonitor();
 }
 public void Start(){
  monitor.Start(current);
  drainTimer.Tick+=(_,_)=>Drain();drainTimer.Start();loop=Poll();
 }
 void Drain(){foreach(var input in monitor.Drain())Step?.Invoke(Observation.Input(input));if(monitor.TakeDropped()>0)Gap?.Invoke("Input observations were lost because the input queue filled or the monitor encountered an error.");}
 async Task Poll(){
  while(!cancel.IsCancellationRequested){
   try{await Task.Delay(750,cancel.Token);await Sample(cancel.Token);}
   catch(OperationCanceledException) when(cancel.IsCancellationRequested){break;}
  }
 }
 internal async Task Sample(CancellationToken token,bool final=false){
  Exception? failure=null;
  for(int attempt=0;attempt<2;attempt++){
   try{
    // Slow SAP screens get time to settle. Stop cancels this read immediately.
    int timeout=final?10:attempt==0?15:25;
    var result=await read(new("capture",session,values,true,timeout),token);
    if(result.Error!=null)throw new SapReadException(result.ErrorKind??"Unavailable",result.Error);
    var next=result.Snapshot??throw new InvalidOperationException("SAP returned no screen sample.");
    if(next.Session.Id!=session.Id||next.Session.System!=session.System||next.Session.Client!=session.Client||next.Session.User!=session.User)
     throw new InvalidOperationException("SAP session identity changed.");
    if(next.Controls.Count==0||next.Windows.Count==0||next.Windows.Any(w=>w.ProcessId==0)||next.Warnings.Count>0)
     throw new SapReadException("Partial","SAP returned an incomplete screen sample; it was not used for change detection.");
    Drain();
    if(failureSince!=null){
     Notice?.Invoke("Screen sampling resumed after SAP was busy or a sample was unavailable. Intermediate screens may be missing; retained input observations still require review.");
    }
    foreach(var step in Observation.Changes(current,next))Step?.Invoke(step);
    current=next;monitor.Update(next);failureSince=null;gapReported=false;
    return;
   }catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
   catch(Exception ex){
    failure=ex;failureSince??=DateTime.UtcNow;Drain();
    bool transient=ex is SapReadException {Kind:"Busy" or "Timeout" or "Partial"};
    // Keep only recently verified HWND/process scope through transient waits. Never
    // use stale or partial controls for targeting, and never keep scope after identity loss.
    bool keepScope=transient&&(DateTime.Now-current.Captured).TotalSeconds<=30;
    monitor.Update(current with {Controls=[],Windows=keepScope?current.Windows:[]});
    if(!transient)break;
    if(attempt==0)await Task.Delay(300,token);
   }
  }
  bool briefBusy=failure is SapReadException {Kind:"Busy"}&&(DateTime.UtcNow-failureSince!.Value).TotalSeconds<8;
  if(!gapReported&&(final||!briefBusy)){
   Gap?.Invoke("Screen sampling gap: "+failure!.Message+" Recent selected-window inputs may be retained without a control target. Review this interval.");
   gapReported=true;
  }
 }

 public async Task Stop(){
  if(stopped)return;stopped=true;
  // Stop collecting Windows input immediately. Then drain and obtain a final SAP sample.
  monitor.Dispose();drainTimer.Stop();cancel.Cancel();
  if(loop!=null)await loop;
  Drain();await Sample(CancellationToken.None,true);cancel.Dispose();
 }
}
