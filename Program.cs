using System.IO;



using System.Text.Json;



using System.Windows;



using System.Windows.Controls;



using System.Windows.Markup;



using System.Windows.Media;



using System.Windows.Media.Imaging;



using System.Windows.Threading;



using Microsoft.Win32;



namespace Inspector;



public static class Program {



 [STAThread] public static void Main(string[] args){



  if(args.Contains("--self-test")){SelfTests.Run();return;}

  if(args.Length==3&&args[0]=="--worker"){Reply reply;try{reply=Reader.Read(JsonSerializer.Deserialize<Request>(File.ReadAllText(args[1]))!);}catch(Exception ex){reply=new(Error:ex.Message+" (0x"+ex.HResult.ToString("X8")+")");}File.WriteAllText(args[2],JsonSerializer.Serialize(reply));return;}



  var app=new Application();



  using var stream=typeof(Program).Assembly.GetManifestResourceStream("Inspector.Main.xaml")!;



  var window=(Window)XamlReader.Load(stream);var controller=new Controller(window,args.Contains("--demo")||args.Contains("--smoke"));



  app.DispatcherUnhandledException+=(_,e)=>{MessageBox.Show(window,e.Exception.Message,"SAP Screen Inspector");e.Handled=true;};



  if(args.Contains("--smoke"))window.Loaded+=(_,_)=>window.Dispatcher.InvokeAsync(async()=>{await controller.StepSmoke();window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(AppContext.BaseDirectory,"preview.png"));encoder.Save(file);window.Close();},DispatcherPriority.ApplicationIdle);



  app.Run(window);



 }



}



public sealed partial class Controller {



 readonly Window w;readonly bool demo;readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(3)};



 Snapshot? snapshot;bool busy,closed;List<ControlRow> rows=[];ControlRow? selected;



 T Get<T>(string name) where T:FrameworkElement=>(T)w.FindName(name);



 void Status(string message)=>Get<TextBlock>("Status").Text=message;



 public Controller(Window window,bool demoMode){



  w=window;demo=demoMode;InitializeSteps();InitializeDesign();



  Get<Button>("Reload").Click+=async(_,_)=>await Reload();



  Get<Button>("Capture").Click+=async(_,_)=>await Capture(recording);



  Get<Button>("Export").Click+=(_,_)=>Export();



  Get<Button>("CopyId").Click+=(_,_)=>Copy(selected?.ShortId);



  Get<Button>("CopyCode").Click+=(_,_)=>Copy(selected==null?null:"Set control = session.FindById("+("\"" + selected.ShortId.Replace("\"", "\"\"") + "\"")+")\r\n' Read-only: inspect control type\r\nWScript.Echo control.Type");



  Get<TextBox>("Search").TextChanged+=(_,_)=>Filter();



  Get<ComboBox>("Types").SelectionChanged+=(_,_)=>Filter();



  Get<ComboBox>("Sessions").SelectionChanged+=(_,_)=>{snapshot=null;rows=[];Filter();Get<TreeView>("Tree").Items.Clear();Show(null);};



  Get<DataGrid>("Table").SelectionChanged+=(_,_)=>Show(Get<DataGrid>("Table").SelectedItem as ControlRow);



  Get<TreeView>("Tree").SelectedItemChanged+=(_,e)=>{if(e.NewValue is TreeViewItem item&&item.Tag is ControlRow row){Get<DataGrid>("Table").SelectedItem=row;Get<DataGrid>("Table").ScrollIntoView(row);Show(row);}};



  Get<CheckBox>("Values").Unchecked+=(_,_)=>{if(snapshot!=null){rows=rows.Select(r=>r with{Text=r.Type.Contains("Password")?"[password excluded]":""}).ToList();snapshot=snapshot with{Controls=rows,ValuesIncluded=false};Filter();Show(null);}};



  timer.Tick+=async(_,_)=>{if(!busy&&Get<CheckBox>("Auto").IsChecked==true)await Capture();};timer.Start();



  w.Closed+=(_,_)=>{closed=true;timer.Stop();};



  w.Loaded+=async(_,_)=>{await Reload();if(demo)await Capture();};Enable();



 }



 void Enable(){foreach(string name in new[]{"Reload","Capture","Export"})Get<Button>(name).IsEnabled=!busy&&(name=="Reload"|| (name=="Export"?snapshot!=null:Get<ComboBox>("Sessions").SelectedItem!=null));Get<ComboBox>("Sessions").IsEnabled=!busy&&!recording;Get<CheckBox>("Values").IsEnabled=!busy;Get<Button>("CopyId").IsEnabled=selected!=null;Get<Button>("CopyCode").IsEnabled=selected!=null;EnableSteps();}



 async Task Reload(){



  if(busy)return;busy=true;Enable();Status("Looking for SAP sessions…");



  try{var data=demo?new Reply(Sessions:[Demo().Session]):await Reader.Run(new("sessions"));if(closed)return;var box=Get<ComboBox>("Sessions");box.ItemsSource=data.Sessions;box.SelectedIndex=data.Sessions?.Count>0?0:-1;Get<TextBlock>("ConnectionStatus").Text=demo?"Demo session":data.Sessions?.Count>0?"SAP session available":"No SAP session";Status(demo?"DEMO — sample controls only; no SAP connection.":"Select a session, navigate in SAP, then capture the screen.");}



  catch(Exception ex){Status(ex.Message);}finally{busy=false;if(!closed)Enable();}



 }



 async Task Capture(bool saveStep=false){



  if(busy||Get<ComboBox>("Sessions").SelectedItem is not SapSession session)return;busy=true;Enable();Status("Reading screen controls…");



  try{



   var result=demo?new Reply(Snapshot:Demo()):await Reader.Run(new("capture",session,Get<CheckBox>("Values").IsChecked==true));



   if(closed)return;



   var next=result.Snapshot??throw new InvalidOperationException("SAP returned no screen. Reload sessions and retry.");if(next.Controls.Count==0)throw new InvalidOperationException("SAP returned no controls. Ensure the selected session has a visible, fully loaded screen.");var old=liveSnapshot?.Session.Id==next.Session.Id?liveSnapshot.Controls.GroupBy(r=>r.Id).ToDictionary(g=>g.Key,g=>g.First()):null;



   foreach(var row in next.Controls)if(old!=null)row.Change=!old.TryGetValue(row.Id,out var before)?"New":(row.Type!=before.Type||row.Name!=before.Name||row.Tooltip!=before.Tooltip||row.Changeable!=before.Changeable||(next.ValuesIncluded&&liveSnapshot!.ValuesIncluded&&row.Text!=before.Text))?"Changed":"Unchanged";



   int removed=old?.Keys.Except(next.Controls.Select(r=>r.Id)).Count()??0;



   liveSnapshot=next;snapshot=next;rows=next.Controls;string? type=Get<ComboBox>("Types").SelectedItem as string;



   Get<ComboBox>("Types").ItemsSource=new[]{"All control types"}.Concat(rows.Select(r=>r.Type).Distinct().Order()).ToArray();Get<ComboBox>("Types").SelectedItem=type;if(Get<ComboBox>("Types").SelectedIndex<0)Get<ComboBox>("Types").SelectedIndex=0;



   Filter();BuildTree();if(demo)Get<DataGrid>("Table").SelectedItem=rows.FirstOrDefault(r=>r.Name=="P_MATNR");Get<TextBlock>("Mode").Text=$"{next.Session.Transaction}  ·  SAP recorder disabled: {Flag(next.Session.Recording)}  ·  Read-only mode: {Flag(next.Session.ReadOnly)}";



   Status($"{(demo?"DEMO · ":"")}Captured {next.Captured:HH:mm:ss} · {rows.Count} controls · {removed} removed since last capture. "+string.Join(" ",next.Warnings));



   if(saveStep)AddStep(next);

  }catch(Exception ex){Get<CheckBox>("Auto").IsChecked=false;if(saveStep){recording=false;UpdateStepStatus();}Status(ex.Message+" Capture paused; no step was added.");if(saveStep)MessageBox.Show(w,ex.Message+"\n\nNo step was saved. Wait for SAP to finish loading, then click Resume capture.","Capture failed",MessageBoxButton.OK,MessageBoxImage.Warning);}finally{busy=false;if(!closed)Enable();}



 }



 static string Flag(string value)=>value is "True" or "1"?"Yes":value is "False" or "0"?"No":"Unknown";



 void Filter(){



  string query=Get<TextBox>("Search").Text.Trim(),type=Get<ComboBox>("Types").SelectedItem as string??"All control types";



  Get<TextBlock>("SearchHint").Visibility=query.Length==0?Visibility.Visible:Visibility.Collapsed;



  var filtered=rows.Where(r=>(type=="All control types"||r.Type==type)&&(query.Length==0||string.Join(" ",r.Id,r.Name,r.Type,r.Tooltip,r.Text).Contains(query,StringComparison.OrdinalIgnoreCase))).ToList();



  string? id=selected?.Id;Get<DataGrid>("Table").ItemsSource=filtered;Get<DataGrid>("Table").SelectedItem=filtered.FirstOrDefault(r=>r.Id==id);



  Get<TextBlock>("Count").Text=$"{filtered.Count} / {rows.Count}";



  Get<TextBlock>("Empty").Visibility=filtered.Count==0?Visibility.Visible:Visibility.Collapsed;



  Get<TextBlock>("Empty").Text=snapshot==null?"Your SAP screen, mapped.\n\nOpen the screen in SAP, then choose\nCapture screen to inspect its controls.":"No matching controls.\nTry a different search or type.";



 }



 void BuildTree(){



  var tree=Get<TreeView>("Tree");tree.Items.Clear();var nodes=new Dictionary<string,TreeViewItem>();



  foreach(var row in rows){var item=new TreeViewItem{Header=TreeHeader(row),Tag=row,ToolTip=row.ShortId,IsExpanded=row.Depth<2};nodes[row.Id]=item;if(nodes.TryGetValue(row.Parent,out var parent))parent.Items.Add(item);else tree.Items.Add(item);}



 }



 void Show(ControlRow? row){



  selected=row;var panel=Get<StackPanel>("Details");panel.Children.Clear();panel.Children.Add(new TextBlock{Text=row?.Name??"Select a control",FontSize=19,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,18)});



  void Field(string name,string value){

   var line=new Grid{Margin=new(0,0,0,18)};line.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(66)});line.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});

   line.Children.Add(new TextBlock{Text=name,VerticalAlignment=VerticalAlignment.Center,Foreground=new SolidColorBrush(Color.FromRgb(96,116,138))});

   var field=new TextBox{Text=string.IsNullOrEmpty(value)?"—":value,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Background=new SolidColorBrush(Color.FromRgb(247,249,252)),Padding=new(8,6,8,6)};Grid.SetColumn(field,1);line.Children.Add(field);panel.Children.Add(line);

  }

  if(row!=null){Field("Type",row.Type);Field("ID",row.ShortId);Field("Name",row.Name);Field("Value",snapshot?.ValuesIncluded==true?row.Text:"Hidden");if(row.Tooltip.Length>0)Field("Tooltip",row.Tooltip);if(row.Columns.Length>0)Field("Columns",row.Columns);}





  else panel.Children.Add(new TextBlock{Text="Select a control to see its ID and properties.",TextWrapping=TextWrapping.Wrap});



  Enable();



 }



 void Copy(string? value){if(value==null)return;try{Clipboard.SetText(value);Status("Copied to clipboard.");}catch(Exception ex){Status("Could not copy: "+ex.Message);}}



 void Export(){



  if(snapshot==null)return;Get<CheckBox>("Auto").IsChecked=false;



  var dialog=new SaveFileDialog{Filter="JSON capture (*.json)|*.json",FileName=$"SAP-{snapshot.Session.Transaction}-{DateTime.Now:yyyyMMdd-HHmmss}.json"};



  if(dialog.ShowDialog(w)!=true)return;



  try{File.WriteAllText(dialog.FileName,JsonSerializer.Serialize(snapshot,new JsonSerializerOptions{WriteIndented=true}));Status("Capture exported. Includes session username, control metadata"+(snapshot.ValuesIncluded?" and screen values.":". Screen values excluded."));}catch(Exception ex){Status("Export failed: "+ex.Message);}



 }



 static Snapshot Demo(){



  var session=new SapSession("/app/con[0]/ses[0]","DEMO","100","Sample user","ZLIFECYCLE","Tool lifecycle","1","0");



  var rows=new List<ControlRow>();



  rows.Add(new("wnd[0]","GuiMainWindow","Tool lifecycle","","","",0,"",""));



  rows.Add(new("wnd[0]/usr","GuiUserArea","Selection","","","wnd[0]",1,"",""));



  foreach(var (id,name,type,tip) in new[]{("ctxtP_MATNR","P_MATNR","GuiCTextField","Material / 12NC"),("ctxtP_SERNR","P_SERNR","GuiCTextField","Serial number"),("txtP_BARC","P_BARC","GuiTextField","Barcode ID"),("ctxtP_EQUNR","P_EQUNR","GuiCTextField","Equipment")})rows.Add(new("wnd[0]/usr/"+id,type,name,"",tip,"wnd[0]/usr",2,"True",""));



  rows.Add(new("wnd[0]/tbar[1]","GuiToolbar","Actions","","","wnd[0]",1,"",""));rows.Add(new("wnd[0]/tbar[1]/btn[8]","GuiButton","Execute","","Execute (F8)","wnd[0]/tbar[1]",2,"",""));



  return new(DateTime.Now,session,rows,false,[]);



 }



}



