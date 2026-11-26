using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Inspector;

// One STA worker per recording, with sequential requests over private redirected pipes.
// Killing a blocked worker cancels COM reads without blocking the UI thread.
internal sealed class ReaderSession(bool synthetic=false) : IAsyncDisposable {
 readonly SemaphoreSlim gate=new(1,1);
 Process? process;bool disposed;
 internal int? WorkerId=>process?.Id;
 public async Task<Reply> Read(Request request,CancellationToken token){
  await gate.WaitAsync(token);
  try{
   ObjectDisposedException.ThrowIf(disposed,this);
   using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);
   int seconds=Math.Clamp(request.TimeoutSeconds,1,25);timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
   try{
    if(process==null||process.HasExited){
     await CloseWorker();
     var start=Reader.WorkerStart(synthetic?"--observe-worker-demo":"--observe-worker");
     start.RedirectStandardInput=true;start.RedirectStandardOutput=true;
     start.StandardInputEncoding=new UTF8Encoding(false);start.StandardOutputEncoding=new UTF8Encoding(false);
     process=Process.Start(start)??throw new InvalidOperationException("SAP reader could not start.");
    }
    await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(),timeout.Token);
    await process.StandardInput.FlushAsync(timeout.Token);
    string line=await process.StandardOutput.ReadLineAsync(timeout.Token)??throw new InvalidOperationException("SAP reader closed before returning a sample.");
    var reply=JsonSerializer.Deserialize<Reply>(line)??throw new InvalidDataException("SAP reader returned no sample.");
    if(reply.Error!=null)throw new SapReadException(reply.ErrorKind??"Unavailable",reply.Error);
    return reply;
   }catch(OperationCanceledException){
    await CloseWorker();token.ThrowIfCancellationRequested();
    throw new SapReadException("Timeout",$"SAP did not respond within {seconds} seconds.");
   }catch(SapReadException){throw;}
   catch{await CloseWorker();throw;}
  }finally{gate.Release();}
 }
 async Task CloseWorker(){
  var child=process;process=null;if(child==null)return;
  try{
   try{if(!child.HasExited)child.Kill(true);}catch(InvalidOperationException) when(child.HasExited){}
   await child.WaitForExitAsync();
  }finally{child.Dispose();}
 }
 public async ValueTask DisposeAsync(){
  await gate.WaitAsync();
  try{if(disposed)return;disposed=true;await CloseWorker();}finally{gate.Release();}
 }
 internal static void RunWorker(bool synthetic){
  using var input=new StreamReader(Console.OpenStandardInput(),new UTF8Encoding(false));
  using var output=new StreamWriter(Console.OpenStandardOutput(),new UTF8Encoding(false)){AutoFlush=true};
  object? engine=null;
  while(input.ReadLine() is {} line){
   Reply reply;
   try{
    var request=JsonSerializer.Deserialize<Request>(line)??throw new InvalidDataException("Missing request.");
    if(synthetic&&request.Action=="test-wait")Thread.Sleep(5000);
    engine??=synthetic?new FakeEngine{Children=[new FakeConnection{Children=[new FakeSession{Children=[new FakeNode{Id="wnd[0]",Type="GuiMainWindow"}]}]}]}:Reader.Engine();
    reply=Reader.Read(request,engine);
   }catch(Exception ex){engine=null;reply=new(Error:ex.Message,ErrorKind:(ex as SapReadException)?.Kind??"Unavailable");}
   output.WriteLine(JsonSerializer.Serialize(reply));
  }
 }
}
