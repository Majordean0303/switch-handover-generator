using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SwitchHandover.Core.Models;
using SwitchHandover.Core.Services;
using VisioCreator.Core.Graph;
using VisioCreator.Core.Layout;
namespace SwitchHandover.Desktop;
public partial class MainViewModel:UiOperation{
 [ObservableProperty]private string inputPaths="";
 [ObservableProperty]private string site="";
 [ObservableProperty]private string locationType="Branch";
 [ObservableProperty]private string managementVlan="";
 [ObservableProperty]private bool includeSfps=true;
 [ObservableProperty]private bool includeAllPorts=true;
 [ObservableProperty]private string seed="";
 [ObservableProperty]private string username="";
 [ObservableProperty]private string password="";
 [ObservableProperty]private string enableSecret="";
 [ObservableProperty]private bool traverse=true;
 [ObservableProperty]private bool includeAps=true;
 [ObservableProperty]private string subnetFilter="";
 [ObservableProperty]private decimal workers=5;
 [ObservableProperty]private decimal maxHops=8;
 [ObservableProperty]private string captureDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SwitchHandover","Captures",DateTime.Now.ToString("yyyyMMdd_HHmmss"));
 [ObservableProperty]private string logs="";
 [ObservableProperty]private string search="";
 [ObservableProperty]private DiagramLayout? diagram;
 [ObservableProperty]private TopologyGraph? graph;
 [ObservableProperty]private double diagramScale=70;
 [ObservableProperty]private double zoom=1;
 public string[] LocationTypes{get;}=["Branch","DC","DR","Vendor"];
 public ObservableCollection<SwitchDetails> Switches{get;}=[];
 public ObservableCollection<PortMapping> Ports{get;}=[];
 public ObservableCollection<SfpInventory> Sfps{get;}=[];
 public ObservableCollection<PortMapping> AllPorts{get;}=[];
 public HandoverAnalysis? Analysis{get;private set;}
 AnalysisOptions Options=>new(Site,LocationType,string.IsNullOrWhiteSpace(ManagementVlan)?null:ManagementVlan,IncludeSfps,IncludeAllPorts);
 void AddLog(string message)=>Logs+=message+Environment.NewLine;
 void SetAnalysis(HandoverAnalysis result){Analysis=result;Diagram=result.Layout;Graph=result.Graph;Refresh();foreach(var note in result.Notes)AddLog(note);Status=$"{result.Switches.Count} inventory rows · {result.Ports.Count} neighbor ports · {result.Sfps.Count} SFPs · {result.Layout.Links.Count} topology wires";}
 partial void OnSearchChanged(string value)=>Refresh();
 bool Matches(string value)=>string.IsNullOrWhiteSpace(Search)||value.Contains(Search,StringComparison.OrdinalIgnoreCase);
 void Refresh(){Switches.Clear();Ports.Clear();Sfps.Clear();AllPorts.Clear();if(Analysis is not {} a)return;foreach(var s in a.Switches.Where(s=>Matches($"{s.Hostname} {s.IpAddress} {s.Model} {s.Location}")))Switches.Add(s);foreach(var p in a.Ports.Where(p=>Matches($"{p.Hostname} {p.PortNo} {p.NeighbourHostname} {p.Description}")))Ports.Add(p);foreach(var p in a.AllPorts.Where(p=>Matches($"{p.Hostname} {p.PortNo} {p.NeighbourHostname} {p.Description}")))AllPorts.Add(p);foreach(var s in a.Sfps.Where(s=>Matches($"{s.Hostname} {s.Interface} {s.Pid} {s.SerialNumber}")))Sfps.Add(s);}
 public Task AnalyzeAsync()=>RunAsync(async token=>{Logs="";var inputs=InputPaths.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);var options=Options;var messages=new List<string>();var result=await Task.Run(()=>HandoverAnalysis.FromFiles(inputs,options,messages.Add,token),token);foreach(var message in messages)AddLog(message);SetAnalysis(result);});
 public Task CaptureAsync()=>RunAsync(async token=>{if(string.IsNullOrWhiteSpace(Site))throw new ArgumentException("Enter a site location first.");var options=Options;var seeds=Seed.Split(['\r','\n',',',';'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);var service=new BoundedLiveCapture(new SshTelnetSessionFactory());var result=await service.RunAsync(seeds,new(){Username=Username,Password=Password,Secret=EnableSecret},CaptureDirectory,new((int)Workers,(int)MaxHops,Traverse:Traverse,IncludeAps:IncludeAps,SubnetFilter:SubnetFilter),message=>Dispatcher.UIThread.Post(()=>Status=message),token);foreach(var e in result.Errors)AddLog(e);InputPaths=string.Join(Environment.NewLine,result.Files);if(result.Files.Count==0)throw new IOException("No complete captures were collected. Inspect connection errors.");SetAnalysis(await Task.Run(()=>HandoverAnalysis.FromFiles(result.Files,options,token:token),token));});
 public Task ExportAsync(string path,string kind)=>RunAsync(async token=>{if(kind=="captures"){await Task.Run(()=>{token.ThrowIfCancellationRequested();ZipArchiveService.CreateBackupsZip(CaptureDirectory,path);},token);Status="Captured logs exported.";return;}var analysis=Analysis??throw new InvalidOperationException("Analyze captures first.");var snapshot=analysis with{Layout=analysis.Layout.CreateSnapshot()};await Task.Run(()=>{token.ThrowIfCancellationRequested();switch(kind){case "xlsx":snapshot.ExportExcel(path);break;case "csv":snapshot.ExportCsv(path,"ports");break;case "zip":snapshot.ExportBundle(path);break;case "vsdx":snapshot.ExportVisio(path);break;case "captures":ZipArchiveService.CreateBackupsZip(CaptureDirectory,path);break;}},token);Status="Export saved: "+Path.GetFileName(path);});
 public void ResetLayout(){if(Analysis is {} a&&!IsBusy){Diagram=DiagramLayoutEngine.Build(a.Layout.Nodes.Values.Select(n=>new DiagramNode(n.Id,n.Role,n.Location)),a.Layout.Links.Select(l=>new DiagramLink(l.Id,l.A,l.B,l.APort,l.BPort,l.Kind,l.PortChannelId,l.IsFiber,l.MediaType)));Analysis=a with{Layout=Diagram};}}
}
