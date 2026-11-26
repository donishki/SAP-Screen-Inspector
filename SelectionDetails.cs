using System.Globalization;
using System.Text.Json;

namespace Inspector;

// Null means unavailable; an empty string/list means SAP reported no selection.
public sealed record SelectionDetails {
 public string Kind {get;init;}="";
 public DateTime SampledAt {get;init;}
 public int? CurrentRow {get;init;}
 public string? CurrentColumn {get;init;}
 public string? SelectedRows {get;init;}
 public int? RowCount {get;init;}
 public int? SelectionMode {get;init;}
 public List<string>? SelectedNodes {get;init;}
 public string? ItemNode {get;init;}
 public string? ItemColumn {get;init;}
 public List<string> Warnings {get;init;}=[];

 public IEnumerable<string> Describe(){
  static string Text(string? value)=>value==null?"unavailable":JsonSerializer.Serialize(value);
  yield return $"{Kind} state sampled at {SampledAt:HH:mm:ss.fff} (not a confirmed action).";
  if(Kind=="GridView"){
   yield return $"Current row (zero-based; -1 = header): {CurrentRow?.ToString(CultureInfo.InvariantCulture)??"unavailable"}; column ID: {Text(CurrentColumn)}.";
   yield return $"Selected row indices/ranges: {Text(SelectedRows)}; row count: {RowCount?.ToString(CultureInfo.InvariantCulture)??"unavailable"}.";
   yield return "Row indices can change after sorting/filtering; verify the intended business record before replay.";
  }else{
   yield return $"Tree selection mode: {SelectionMode?.ToString(CultureInfo.InvariantCulture)??"unavailable"}; selected node keys: {(SelectedNodes==null?"unavailable":JsonSerializer.Serialize(SelectedNodes))}.";
   yield return $"Selected item node key: {Text(ItemNode)}; item column ID: {Text(ItemColumn)}.";
  }
  foreach(string warning in Warnings)yield return "Selection detail: "+warning;
 }
 internal bool SameState(SelectionDetails other)=>Kind==other.Kind&&CurrentRow==other.CurrentRow&&CurrentColumn==other.CurrentColumn&&
  SelectedRows==other.SelectedRows&&RowCount==other.RowCount&&SelectionMode==other.SelectionMode&&ItemNode==other.ItemNode&&ItemColumn==other.ItemColumn&&
  (SelectedNodes==null?other.SelectedNodes==null:other.SelectedNodes!=null&&SelectedNodes.SequenceEqual(other.SelectedNodes))&&Warnings.SequenceEqual(other.Warnings);
}

internal static class SelectionReader {
 internal static string Kind(string type,string subtype)=>type=="GuiGridView"||subtype.Equals("GridView",StringComparison.OrdinalIgnoreCase)?"GridView":
  type=="GuiTree"||subtype.Equals("Tree",StringComparison.OrdinalIgnoreCase)?"Tree":"";
 internal static SelectionDetails? Read(object target,string kind){
  if(kind is not ("GridView" or "Tree"))return null;
  dynamic control=target;
  var warnings=new List<string>();
  T? Try<T>(string name,Func<T> read) where T:class {try{return read();}catch{warnings.Add(name+" unavailable.");return null;}}
  string? Text(string name,Func<object?> read)=>Try(name,()=>{
   string value=Convert.ToString(read(),CultureInfo.InvariantCulture)??"";
   if(value.Length>2048){warnings.Add(name+" truncated at 2048 characters.");return value[..2048];}return value;
  });
  int? Number(string name,Func<object?> read){
   try{var value=read();if(value==null)throw new InvalidOperationException();return Convert.ToInt32(value,CultureInfo.InvariantCulture);}catch{warnings.Add(name+" unavailable.");return null;}
  }
  var time=DateTime.Now;
  if(kind=="GridView")return new(){Kind=kind,SampledAt=time,
   CurrentRow=Number("Current row",()=>control.CurrentCellRow),CurrentColumn=Text("Column ID",()=>control.CurrentCellColumn),
   SelectedRows=Text("Selected rows",()=>control.SelectedRows),RowCount=Number("Row count",()=>control.RowCount),Warnings=warnings};
  int? mode=Number("Selection mode",()=>control.GetSelectionMode());
  List<string>? nodes=null;string? node=null,column=null;
  if(mode is 0 or 1){
   nodes=Try("Selected nodes",()=>{
    dynamic selected=control.GetSelectedNodes();int count=Convert.ToInt32(selected.Count);
    var keys=new List<string>();
    if(count>100)warnings.Add("Selected nodes truncated at 100 keys.");
    for(int i=0;i<Math.Min(count,100);i++){
     string key=Convert.ToString(selected[i],CultureInfo.InvariantCulture)??"";
     if(key.Length>2048){warnings.Add("Node key truncated at 2048 characters.");key=key[..2048];}
     keys.Add(key);
    }
    return keys;
   });
  }else if(mode is 2){
   node=Text("Item node key",()=>control.SelectedItemNode());column=Text("Item column ID",()=>control.SelectedItemColumn());
  }else if(mode is 3)warnings.Add("Multiple-item tree selection is not supported; individual items were not inferred.");
  else if(mode!=null)warnings.Add("Unknown tree selection mode; individual items were not inferred.");
  return new(){Kind=kind,SampledAt=time,SelectionMode=mode,SelectedNodes=nodes,ItemNode=node,ItemColumn=column,Warnings=warnings};
 }
}

public sealed record InputDetails(string DeviceAction,string WindowId,int? X,int? Y,int ClickCount,string Evidence);

internal sealed class DoubleClickTracker(int intervalMs,int width,int height) {
 ObservedInput? previous;
 public void Reset()=>previous=null;
 public ObservedInput Classify(ObservedInput input){
  if(input.Action!="Click"||input.Detail!="Left mouse button"){Reset();return input;}
  var first=previous;previous=input;
  if(first!=null&&input.Window==first.Window&&input.NativeTarget==first.NativeTarget&&
     input.Screen.Session.Transaction==first.Screen.Session.Transaction&&input.Screen.Session.Title==first.Screen.Session.Title&&
     (input.Time-first.Time).TotalMilliseconds is >=0&&(input.Time-first.Time).TotalMilliseconds<=intervalMs&&
     Math.Abs((long)input.X-first.X)<=width/2&&Math.Abs((long)input.Y-first.Y)<=height/2){
   Reset();return input with {Action="Double-click",Detail="Left mouse button: second click within Windows double-click limits"};
  }
  return input;
 }
}
