using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Xunit;
using SwitchHandover.Desktop;
[assembly:AvaloniaTestApplication(typeof(SwitchHandover.UiTests.TestAppBuilder))]
namespace SwitchHandover.UiTests;
public static class TestAppBuilder { public static AppBuilder BuildAvaloniaApp()=>AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).UseSkia().WithInterFont(); }
public class SmokeTests {
 [AvaloniaFact] public void Window_loads_in_both_themes_without_a_display_server() {
  var window=new MainWindow();try{window.Show();foreach(var theme in new[]{ThemeVariant.Light,ThemeVariant.Dark}){Application.Current!.RequestedThemeVariant=theme;Dispatcher.UIThread.RunJobs();window.UpdateLayout();Assert.True(window.IsVisible);Assert.True(window.Bounds.Width>0);Assert.True(window.TryFindResource("TextFillColorPrimaryBrush",theme,out var brush));Assert.NotNull(brush);}}finally{window.Close();}
 }
 [AvaloniaFact]public async Task Offline_analysis_populates_reports_and_shared_topology(){var window=new MainWindow();try{window.Show();var model=Assert.IsType<MainViewModel>(window.DataContext);model.Site="Lab";model.InputPaths=Path.Combine(AppContext.BaseDirectory,"Fixtures","access.txt");await model.AnalyzeAsync();Dispatcher.UIThread.RunJobs();window.UpdateLayout();Assert.Single(model.Switches);Assert.Equal(2,model.Ports.Count);Assert.Equal(2,model.Diagram!.Links.Count);Assert.Same(model.Analysis!.Layout,model.Diagram);model.Search="CORE-01";Assert.Equal(2,model.Ports.Count);}finally{window.Close();}}
}
