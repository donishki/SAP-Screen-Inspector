using System.Text.Json;

namespace Inspector;

internal static class SelectionTests {
 internal static void Run(){
  void Check(bool condition,string name){if(!condition)throw new Exception("Selection details: "+name);}
  var grid=new FakeSelectionGrid{Id="wnd[0]/usr/grid",Type="GuiShell",Name="Grid"};
  Check(SelectionReader.Kind(grid.Type,grid.SubType)=="GridView","shell subtype recognized");
  var selected=SelectionReader.Read(grid,"GridView")!;
  Check(selected.CurrentRow==2&&selected.CurrentColumn=="COL_ID"&&selected.SelectedRows=="2,4-6"&&selected.RowCount==20,"grid state read");
  Check(grid.CellReads==0,"grid contents not read");
  grid.FailColumn=true;var partial=SelectionReader.Read(grid,"GridView")!;
  Check(partial.CurrentColumn==null&&partial.CurrentRow==2&&partial.Warnings.Count==1,"unsupported getter stays explicitly unavailable");
  grid.FailColumn=false;
  var tree=new FakeSelectionTree{Id="wnd[0]/usr/tree",Type="GuiShell",Name="Tree"};
  var nodes=SelectionReader.Read(tree,"Tree")!;
  Check(nodes.SelectedNodes!.SequenceEqual(new[]{"  0001","0020"}),"tree keys preserve leading spaces and zeros");
  Check(tree.ItemReads==0&&nodes.ItemNode==null,"node mode does not probe item APIs");
  tree.Mode=2;var item=SelectionReader.Read(tree,"Tree")!;
  Check(item.ItemNode=="  0001"&&item.ItemColumn=="COL_NODE"&&item.SelectedNodes==null,"tree item node and column read");
  tree.Mode=3;int itemReads=tree.ItemReads;var unsupported=SelectionReader.Read(tree,"Tree")!;
  Check(unsupported.Warnings.Count==1&&tree.ItemReads==itemReads,"multi-item mode explicitly unsupported");
  tree.Mode=1;tree.Keys=Enumerable.Range(0,120).Select(i=>i.ToString("D4")).ToList();
  var bounded=SelectionReader.Read(tree,"Tree")!;
  Check(bounded.SelectedNodes!.Count==100&&bounded.Warnings.Count==1,"large selections bounded and flagged");
  Check(SelectionReader.Read(grid,"Picture")==null,"unsupported shell not probed");

  var window=new FakeNode{Id="wnd[0]",Type="GuiMainWindow",Children=[grid,tree]};
  var engine=new FakeEngine{Children=[new FakeConnection{Children=[new FakeSession{Children=[window]}]}]};
  var session=Reader.Read(new("sessions"),engine).Sessions![0];
  var before=Reader.Read(new("capture",session,false,true),engine).Snapshot! with {Windows=[new(10,"wnd[0]",100)]};
  var row=before.Controls.Single(c=>c.Id==grid.Id);
  Check(row.SubType=="GridView"&&row.Selection!.CurrentColumn=="COL_ID"&&row.Text==""&&grid.CellReads==0,"full reader obtains subtype/state without values");
  var oldSelection=row.Selection!;
  var changed=oldSelection with {CurrentRow=5,SelectedRows="5",SampledAt=DateTime.Now};
  var after=before with {Controls=before.Controls.Select(c=>c.Id==row.Id?c with {Selection=changed}:c).ToList()};
  var steps=Observation.Changes(before,after).ToList();
  Check(steps.Count==1&&steps[0].Action=="Grid selection changed"&&steps[0].Selection!.CurrentRow==5&&steps[0].RequiresReview,"selection change gets a structured review step");
  Check(!Observation.Changes(after,after).Any(),"unchanged selection suppressed");
  var treeAfter=before with {Controls=before.Controls.Select(c=>c.Id==tree.Id?c with {Selection=nodes}:c).ToList()};
  var treeStep=Observation.Changes(before,treeAfter).Single();
  Check(treeStep.Action=="Tree selection changed"&&treeStep.Selection!.SelectedNodes![0]=="  0001","tree state change preserves exact node key");
  var empty=nodes with {SelectedNodes=[]};
  Check(!nodes.SameState(empty)&&!empty.SameState(nodes with {SelectedNodes=null}),"empty selection and unavailable selection remain distinct");
  Check(oldSelection.SameState(oldSelection with {SampledAt=oldSelection.SampledAt.AddSeconds(1)}),"sample timestamps do not create duplicate selection changes");
  var clicked=Observation.Input(new(DateTime.Now,"Click","Left mouse button",5,5,10,before with {
   Controls=[row with {Bounds=new(0,0,20,20)}]}));
  Check(clicked.Selection!.CurrentRow==2&&clicked.SelectionContext.Contains("BEFORE")&&clicked.Input!.ClickCount==1,"pre-click state not replaced by later selection");
  var unknown=Observation.Input(new(DateTime.Now,"Click","Left mouse button",500,500,10,before));
  Check(unknown.Selection==null,"unresolved click does not borrow another control's selection");
  steps.Insert(0,clicked);for(int i=0;i<steps.Count;i++)steps[i].Number=i+1;
  string script=ScriptExport.Render(steps);
  Check(script.Contains("COL_ID")&&script.Contains("BEFORE")&&script.Contains("Current row")&&!script.Split('\n').Any(l=>l.StartsWith("session.findById")),"selection export describes evidence without replay commands");
  var restored=Controller.ReadSequence(JsonSerializer.Serialize(new StepSequence("SAP-Screen-Inspector-steps",1,DateTime.Now,"Observed",steps)));
  Check(restored.Steps[0].Input!.ClickCount==1&&restored.Steps[1].Selection!.CurrentRow==5&&restored.Steps[0].SelectionContext.Contains("BEFORE"),"structured evidence roundtrips");
  restored.Steps[1].ControlId="wnd[0]/usr/other";
  Check(ScriptExport.Render(restored.Steps).Contains("Selection control at sampling: "+row.ShortId),"editing target does not reassign selection evidence");
  var legacy=JsonSerializer.Serialize(new StepSequence("SAP-Screen-Inspector-steps",1,DateTime.Now,"Legacy",[new(){Number=1,Action="Starting screen",Screen=before}]));
  Check(Controller.ReadSequence(legacy).Steps[0].Selection==null,"legacy steps need no selection details");

  var click=new ObservedInput(DateTime.Now,"Click","Left mouse button",10,10,10,before,55);
  var classifier=new DoubleClickTracker(500,4,4);
  Check(classifier.Classify(click).Action=="Click","first click retained");
  var second=classifier.Classify(click with {Time=click.Time.AddMilliseconds(200),X=11});
  Check(second.Action=="Double-click"&&Observation.Input(second).Input!.ClickCount==2,"double-click timing classified and exported as evidence");
  Check(classifier.Classify(click with {Time=click.Time.AddMilliseconds(300)}).Action=="Click","triple click does not reuse pair");
  classifier.Reset();classifier.Classify(click);
  Check(classifier.Classify(click with {Window=11,Time=click.Time.AddMilliseconds(200)}).Action=="Click","other window cannot form pair");
  classifier.Reset();classifier.Classify(click);
  Check(classifier.Classify(click with {Time=click.Time.AddMilliseconds(600)}).Action=="Click","slow clicks kept separate");
  classifier.Reset();classifier.Classify(click);
  Check(classifier.Classify(click with {X=20,Time=click.Time.AddMilliseconds(200)}).Action=="Click","distant clicks kept separate");
 }
}

public class FakeSelectionGrid : FakeNode {
 public string SubType=>"GridView";
 public int CurrentCellRow=>2;
 public string CurrentCellColumn=>FailColumn?throw new InvalidOperationException("Unavailable"):"COL_ID";
 public string SelectedRows=>"2,4-6";
 public int RowCount=>20;
 public bool FailColumn{get;set;}
 public int CellReads{get;private set;}
 public string GetCellValue(int row,string column){CellReads++;throw new Exception("Cell content must not be read");}
}
public class FakeSelectionTree : FakeNode {
 public string SubType=>"Tree";
 public int Mode{get;set;}
 public List<string> Keys{get;set;}=["  0001","0020"];
 public int ItemReads{get;private set;}
 public int GetSelectionMode()=>Mode;
 public List<string> GetSelectedNodes()=>Keys;
 public string SelectedItemNode(){ItemReads++;return "  0001";}
 public string SelectedItemColumn(){ItemReads++;return "COL_NODE";}
}
