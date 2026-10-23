using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
namespace SwitchHandover.Desktop;
public partial class MainWindow:Window{
 readonly MainViewModel _model=new();
 public MainWindow(){InitializeComponent();DataContext=_model;Closing+=(_,_)=>_model.CancelCommand.Execute(null);KeyDown+=(_,e)=>{if(!e.KeyModifiers.HasFlag(KeyModifiers.Control))return;if(e.Key is Key.OemPlus or Key.Add)_model.Zoom=Math.Clamp(_model.Zoom+.1,.75,1.5);if(e.Key is Key.OemMinus or Key.Subtract)_model.Zoom=Math.Clamp(_model.Zoom-.1,.75,1.5);if(e.Key==Key.D0)_model.Zoom=1;};}
 async Task Guard(Func<Task> task){try{await task();}catch(Exception ex){_model.Status=ex.Message;}}
 async void Files(object? s,RoutedEventArgs e)=>await Guard(async()=>{var files=await StorageProvider.OpenFilePickerAsync(new(){Title="Cisco command captures and ZIPs",AllowMultiple=true,FileTypeFilter=[new("Captures"){Patterns=["*.txt","*.log","*.cfg","*.conf","*.zip"]}]});var paths=files.Select(f=>f.TryGetLocalPath()).Where(p=>p is not null);_model.InputPaths=string.Join(Environment.NewLine,_model.InputPaths.Split('\n').Concat(paths).Where(p=>!string.IsNullOrWhiteSpace(p)));});
 async void Folder(object? s,RoutedEventArgs e)=>await Guard(async()=>{var dirs=await StorageProvider.OpenFolderPickerAsync(new(){Title="Capture folder",AllowMultiple=false});if(dirs.Count>0)_model.InputPaths+=Environment.NewLine+dirs[0].TryGetLocalPath();});
 async void Analyze(object? s,RoutedEventArgs e)=>await _model.AnalyzeAsync();
 async void Capture(object? s,RoutedEventArgs e)=>await _model.CaptureAsync();
 async Task Export(string kind,string filename,string? ext=null)=>await Guard(async()=>{var p=await FileDialogs.SaveAsync(this,filename,ext??kind);if(p is not null)await _model.ExportAsync(p,kind);});
 async void Excel(object? s,RoutedEventArgs e)=>await Export("xlsx","Complete_Handover_Report.xlsx");
 async void Csv(object? s,RoutedEventArgs e)=>await Export("csv","Port_Mapping.csv");
 async void Bundle(object? s,RoutedEventArgs e)=>await Export("zip","Handover_Reports.zip");
 async void Visio(object? s,RoutedEventArgs e)=>await Export("vsdx","Handover_Topology.vsdx");
 async void CapturesZip(object? s,RoutedEventArgs e)=>await Export("captures","Switch_Backups.zip","zip");
 void Reset(object? s,RoutedEventArgs e){try{_model.ResetLayout();}catch(Exception ex){_model.Status=ex.Message;}}
 void ToggleTheme(object? s,RoutedEventArgs e){if(Application.Current is {} app)app.RequestedThemeVariant=app.ActualThemeVariant==ThemeVariant.Dark?ThemeVariant.Light:ThemeVariant.Dark;}
}
