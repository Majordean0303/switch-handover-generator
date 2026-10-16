using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SwitchHandover.Core.Models;
using SwitchHandover.Core.Parsers;
using SwitchHandover.Core.Services;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace SwitchHandover.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ISnackbarService _snackbarService;
    private readonly List<string> _selectedFiles = [];

    public MainViewModel(ISnackbarService snackbarService)
    {
        _snackbarService = snackbarService;
        LocationType = LocationTypes.FirstOrDefault() ?? "Branch";
    }

    [ObservableProperty] private string _applicationTitle = "Switch Handover Generator";
    [ObservableProperty] private string _siteLocation = "";
    [ObservableProperty] private string _mgmtVlan = "";
    [ObservableProperty] private string _locationType = "Branch";
    [ObservableProperty] private ObservableCollection<string> _locationTypes = ["Branch", "DC", "DR", "Vendor"];

    // Input mode selector: 0 = Offline Logs / Files, 1 = Live Network Discovery (SSH & CDP)
    [ObservableProperty] private int _inputModeIndex;

    // Live Discovery Settings
    [ObservableProperty] private string _targetIps = "";
    [ObservableProperty] private string _sshUsername = "";
    [ObservableProperty] private string _sshPassword = "";
    [ObservableProperty] private string _sshSecret = "";
    [ObservableProperty] private bool _enableCdp = true;
    [ObservableProperty] private bool _includeAps = true;
    [ObservableProperty] private string _subnetFilter = "";
    [ObservableProperty] private int _maxConcurrency = 5;

    [ObservableProperty] private bool _getSfps;
    [ObservableProperty] private bool _getCompletePortConfig;

    [ObservableProperty] private string _selectedFilesSummary = "No files selected";
    [ObservableProperty] private bool _hasSelectedFiles;

    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private string _logText = "";

    [ObservableProperty] private ObservableCollection<SwitchDetails> _switchDetails = [];
    [ObservableProperty] private ObservableCollection<PortMapping> _portMappings = [];
    [ObservableProperty] private ObservableCollection<SfpInventory> _sfpInventories = [];
    [ObservableProperty] private ObservableCollection<PortMapping> _completePortConfigs = [];

    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private bool _hasSfps;
    [ObservableProperty] private bool _hasCompletePorts;

    // View selector: 0=Summary/Log, 1=Switch Details, 2=Port Mappings, 3=SFP Inventory, 4=Complete Port Config
    [ObservableProperty] private int _selectedViewIndex;

    // Theme toggle
    [ObservableProperty] private SymbolRegular _themeSymbol = SymbolRegular.WeatherMoon24;
    [ObservableProperty] private string _themeButtonText = "Dark Mode";
    private bool _isDark = true;

    [RelayCommand]
    private void ToggleTheme()
    {
        if (_isDark)
        {
            ApplicationThemeManager.Apply(ApplicationTheme.Light);
            ThemeSymbol = SymbolRegular.WeatherSunny24;
            ThemeButtonText = "Light Mode";
            _isDark = false;
        }
        else
        {
            ApplicationThemeManager.Apply(ApplicationTheme.Dark);
            ThemeSymbol = SymbolRegular.WeatherMoon24;
            ThemeButtonText = "Dark Mode";
            _isDark = true;
        }
    }

    [RelayCommand]
    private void BrowseFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Cisco Config / Backup Logs",
            Filter = "Text / Log Files (*.txt;*.log)|*.txt;*.log|All Files (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            _selectedFiles.Clear();
            _selectedFiles.AddRange(dialog.FileNames);

            SelectedFilesSummary = $"{_selectedFiles.Count} file(s) selected";
            HasSelectedFiles = _selectedFiles.Count > 0;
            AppendLog($"Selected {_selectedFiles.Count} configuration file(s).");
        }
    }

    [RelayCommand]
    private void ImportSeedIps()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Target Seed IPs",
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                string content = File.ReadAllText(dialog.FileName);
                TargetIps = content;
                AppendLog($"Imported seed IP list from {Path.GetFileName(dialog.FileName)}.");
            }
            catch (Exception ex)
            {
                _snackbarService.Show("Import Failed", ex.Message, ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(3));
            }
        }
    }

    private void AppendLog(string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        LogText += $"[{timestamp}] {message}\n";
    }

    [RelayCommand]
    private async Task GenerateAsync()
    {
        if (string.IsNullOrWhiteSpace(SiteLocation))
        {
            _snackbarService.Show("Location Required", "Please specify a Site Location (e.g. Mumbai, DC-1).", ControlAppearance.Caution, new SymbolIcon(SymbolRegular.Warning24), TimeSpan.FromSeconds(3));
            return;
        }

        List<string> filesToProcess = [];

        if (InputModeIndex == 1)
        {
            // LIVE DISCOVERY MODE
            var rawIps = TargetIps.Split(new[] { '\r', '\n', ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Select(ip => ip.Trim())
                                  .Where(ip => !string.IsNullOrWhiteSpace(ip))
                                  .Distinct(StringComparer.OrdinalIgnoreCase)
                                  .ToList();

            if (rawIps.Count == 0)
            {
                _snackbarService.Show("Target IP Required", "Please provide at least one seed switch IP address.", ControlAppearance.Caution, new SymbolIcon(SymbolRegular.Warning24), TimeSpan.FromSeconds(3));
                return;
            }

            if (string.IsNullOrWhiteSpace(SshUsername))
            {
                _snackbarService.Show("SSH Username Required", "Please enter the SSH username.", ControlAppearance.Caution, new SymbolIcon(SymbolRegular.Warning24), TimeSpan.FromSeconds(3));
                return;
            }

            if (string.IsNullOrWhiteSpace(SshPassword))
            {
                _snackbarService.Show("SSH Password Required", "Please enter the SSH password.", ControlAppearance.Caution, new SymbolIcon(SymbolRegular.Warning24), TimeSpan.FromSeconds(3));
                return;
            }

            IsProcessing = true;
            StatusMessage = "Starting live network discovery...";
            AppendLog($"Initiating Live Network Discovery on {rawIps.Count} seed IP(s) (CDP Crawl: {EnableCdp}, AP Discovery: {IncludeAps})...");

            string tempCaptureDir = Path.Combine(Path.GetTempPath(), "SwitchHandover", $"LiveCapture_{DateTime.Now:yyyyMMdd_HHmmss}");
            var liveEngine = new LiveDiscoveryEngine();

            try
            {
                var capturedFiles = await liveEngine.RunLiveCaptureAsync(
                    rawIps,
                    new LiveCredential { Username = SshUsername.Trim(), Password = SshPassword, Secret = SshSecret?.Trim() ?? "" },
                    EnableCdp,
                    IncludeAps,
                    SubnetFilter?.Trim() ?? "",
                    MaxConcurrency,
                    tempCaptureDir,
                    (cat, msg) => AppendLog($"[{cat}] {msg}"),
                    (ip, pct, status) => StatusMessage = $"{ip}: {status} ({pct}%)",
                    CancellationToken.None);

                filesToProcess = capturedFiles;

                if (filesToProcess.Count == 0)
                {
                    IsProcessing = false;
                    StatusMessage = "No devices could be captured.";
                    _snackbarService.Show("Live Discovery Incomplete", "No devices could be successfully reached or authenticated via SSH.", ControlAppearance.Caution, new SymbolIcon(SymbolRegular.Warning24), TimeSpan.FromSeconds(4));
                    return;
                }

                AppendLog($"Live capture succeeded! Processing {filesToProcess.Count} live device output(s)...");
            }
            catch (Exception ex)
            {
                IsProcessing = false;
                StatusMessage = $"Live discovery failed: {ex.Message}";
                AppendLog($"[Error] Live discovery failed: {ex.Message}");
                _snackbarService.Show("Discovery Error", ex.Message, ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(4));
                return;
            }
        }
        else
        {
            // OFFLINE FILE MODE
            if (_selectedFiles.Count == 0)
            {
                _snackbarService.Show("No Files Selected", "Please select at least one configuration file.", ControlAppearance.Caution, new SymbolIcon(SymbolRegular.Warning24), TimeSpan.FromSeconds(3));
                return;
            }
            filesToProcess = _selectedFiles.ToList();
        }

        IsProcessing = true;
        StatusMessage = "Analyzing files...";
        AppendLog($"Starting handover analysis for location '{SiteLocation}' across {filesToProcess.Count} device log(s)...");

        string loc = SiteLocation.Trim();
        string locType = LocationType;
        string? vlan = string.IsNullOrWhiteSpace(MgmtVlan) ? null : MgmtVlan.Trim();
        bool sfps = GetSfps;
        bool allPorts = GetCompletePortConfig;

        try
        {
            var (switches, ports, sfpList, completePorts) = await Task.Run(() =>
            {
                var swList = new List<SwitchDetails>();
                var pmList = new List<PortMapping>();
                var sfpRes = new List<SfpInventory>();
                var cpList = new List<PortMapping>();

                foreach (var file in filesToProcess)
                {
                    string filename = Path.GetFileName(file);
                    string text = File.ReadAllText(file);

                    if (text.Contains("Device Type: AP") || text.Contains("AP Name"))
                    {
                        var apParser = new APHandoverParser(text, location: loc, locationType: locType);
                        swList.AddRange(apParser.ParseDeviceDetails());
                        pmList.AddRange(apParser.ParsePortMapping());
                        if (sfps) sfpRes.AddRange(apParser.ParseSfpInventory());
                        if (allPorts) cpList.AddRange(apParser.ParsePortMapping(returnAll: true));
                    }
                    else
                    {
                        var swParser = new SwitchHandoverParser(text, location: loc, locationType: locType, mgmtVlan: vlan);
                        swList.AddRange(swParser.ParseSwitchDetails());
                        pmList.AddRange(swParser.ParsePortMapping());
                        if (sfps) sfpRes.AddRange(swParser.ParseSfpInventory());
                        if (allPorts) cpList.AddRange(swParser.ParsePortMapping(returnAll: true));
                    }
                }

                // Cross-resolution of neighbor IPs
                var ipDir = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var s in swList)
                {
                    if (!string.IsNullOrWhiteSpace(s.IpAddress) && s.IpAddress != "N/A")
                    {
                        ipDir[s.Hostname] = s.IpAddress;
                        string cleanH = s.Hostname.Split('.')[0];
                        ipDir[cleanH] = s.IpAddress;
                    }
                }

                foreach (var list in new[] { pmList, cpList })
                {
                    foreach (var port in list)
                    {
                        if (port.NeighbourDeviceIp != "-") continue;
                        string nh = port.NeighbourHostname;
                        if (nh != "-")
                        {
                            string cleanNh = nh.Split('.')[0];
                            if (ipDir.TryGetValue(cleanNh, out var resolvedIp))
                            {
                                port.NeighbourDeviceIp = resolvedIp;
                            }
                            else
                            {
                                port.NeighbourDeviceIp = "Unknown (Upload config)";
                            }
                        }
                    }
                }

                return (swList, pmList, sfpRes, cpList);
            });

            SwitchDetails = new ObservableCollection<SwitchDetails>(switches);
            PortMappings = new ObservableCollection<PortMapping>(ports);
            SfpInventories = new ObservableCollection<SfpInventory>(sfpList);
            CompletePortConfigs = new ObservableCollection<PortMapping>(completePorts);

            HasResults = switches.Count > 0 || ports.Count > 0;
            HasSfps = sfpList.Count > 0;
            HasCompletePorts = completePorts.Count > 0;

            AppendLog($"✔ Analysis complete! Found {switches.Count} switch/device(s), {ports.Count} connected port mapping(s), {sfpList.Count} SFP(s), {completePorts.Count} total port config(s).");
            StatusMessage = "Analysis completed successfully";

            // Automatically switch to Switch Details view if available
            SelectedViewIndex = 1;

            _snackbarService.Show(
                "Analysis Complete",
                $"Processed {filesToProcess.Count} file(s) successfully.",
                ControlAppearance.Success,
                new SymbolIcon(SymbolRegular.CheckmarkCircle24),
                TimeSpan.FromSeconds(4));
        }
        catch (Exception ex)
        {
            AppendLog($"✘ Error during analysis: {ex.Message}");
            StatusMessage = "Error occurred";
            _snackbarService.Show(
                "Analysis Failed",
                ex.Message,
                ControlAppearance.Danger,
                new SymbolIcon(SymbolRegular.ErrorCircle24),
                TimeSpan.FromSeconds(5));
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private void ExportExcel(string exportType)
    {
        string defaultName = exportType switch
        {
            "switches" => "Switch_Details_Report.xlsx",
            "ports" => "Port_Mapping_Report.xlsx",
            _ => "Complete_Handover_Report.xlsx"
        };

        var dialog = new SaveFileDialog
        {
            Title = "Save Handover Report",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            FileName = defaultName
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                HandoverExcelExporter.ExportExcel(
                    dialog.FileName,
                    exportType,
                    SwitchDetails,
                    PortMappings,
                    SfpInventories,
                    CompletePortConfigs);

                AppendLog($"✔ Exported Excel file to: {dialog.FileName}");
                _snackbarService.Show(
                    "Export Successful",
                    $"Saved report to {Path.GetFileName(dialog.FileName)}",
                    ControlAppearance.Success,
                    new SymbolIcon(SymbolRegular.Checkmark24),
                    TimeSpan.FromSeconds(4));
            }
            catch (Exception ex)
            {
                AppendLog($"✘ Export failed: {ex.Message}");
                _snackbarService.Show(
                    "Export Error",
                    ex.Message,
                    ControlAppearance.Danger,
                    new SymbolIcon(SymbolRegular.ErrorCircle24),
                    TimeSpan.FromSeconds(5));
            }
        }
    }

    [RelayCommand]
    private void ExportCsv(string csvType)
    {
        string defaultName = csvType switch
        {
            "switches" => "Switch_Details.csv",
            "ports" => "Port_Mapping.csv",
            "sfp" => "SFP_Inventory.csv",
            "allports" => "Complete_Port_Config.csv",
            _ => "Handover_Report.csv"
        };

        var dialog = new SaveFileDialog
        {
            Title = "Save CSV Report",
            Filter = "CSV File (*.csv)|*.csv",
            FileName = defaultName
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                HandoverExcelExporter.ExportCsv(
                    dialog.FileName,
                    csvType,
                    SwitchDetails,
                    PortMappings,
                    SfpInventories,
                    CompletePortConfigs);

                AppendLog($"✔ Exported CSV file to: {dialog.FileName}");
                _snackbarService.Show(
                    "CSV Export Successful",
                    $"Saved CSV to {Path.GetFileName(dialog.FileName)}",
                    ControlAppearance.Success,
                    new SymbolIcon(SymbolRegular.Checkmark24),
                    TimeSpan.FromSeconds(4));
            }
            catch (Exception ex)
            {
                AppendLog($"✘ CSV Export failed: {ex.Message}");
                _snackbarService.Show(
                    "Export Error",
                    ex.Message,
                    ControlAppearance.Danger,
                    new SymbolIcon(SymbolRegular.ErrorCircle24),
                    TimeSpan.FromSeconds(5));
            }
        }
    }
}

