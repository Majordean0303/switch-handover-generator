# Switch Handover Generator — Avalonia migration

The default `SwitchHandover.sln` runs .NET 10 Core/CLI/Avalonia projects. Prior WPF source and solution remain references. Desktop version remains 2.0.0; no release is published.

```powershell
pwsh scripts/test-all.ps1
dotnet run --project SwitchHandover.Desktop
dotnet run --project SwitchHandover.Cli -- --input SwitchHandover.Tests/Fixtures/access.txt --location Lab --excel artifacts/handover.xlsx --vsdx artifacts/topology.vsdx
pwsh scripts/package.ps1 -Runtime win-x64
```

Import Cisco switch/AP logs, ZIP archives or folders; specify site/location type and optional management VLAN; review switch details, neighbor ports, SFP inventory and complete port configurations. Excel keeps its original sheet names/columns. Individual CSVs are available via CLI and all four report types in the report ZIP. The native UI also exports port CSV, workbook, report ZIP and Visio topology. Search filters the tables; exports contain the full analyzed evidence.

Live capture requires an explicit action. It uses SSH-only sessions, 1–32 bounded workers, an explicit hop cap and a 512-device ceiling, with optional CDP traversal, AP inclusion and subnet filter. Phones/peripheral hosts are excluded. No automatic connections. Session credentials are not persisted. Completed captures remain in the selected output directory after cancellation and can be analyzed/exported separately. Raw configurations can contain device secrets; choose the output directory accordingly.

The new topology tab uses the validated local Visio subset documented in [docs/TOPOLOGY_PROVENANCE.md](docs/TOPOLOGY_PROVENANCE.md). Drag nodes and export from that layout snapshot. Unobserved neighbors, ports and IPs are not fabricated; conflicting addresses produce notes. Stack rows remain separate in Excel but form one topology device. Existing core parsers are retained, with intentional fixes for detailed CDP output and standard interface-status states.

Portable validation: 45 core checks, two Skia headless UI checks, CLI synthetic capture → XLSX/VSDX and Release builds. Tests use recorded/simulated evidence and do not contact equipment. Native OS visuals/local packages, actual Visio rendering and live Cisco SSH/AP command support remain manual review items.

---

# Switch Handover Generator 🚀

[![.NET 8](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%2F11-lightgrey.svg)](https://microsoft.com/windows)
[![UI](https://img.shields.io/badge/UI-WPF%20Mica%20Fluent-cyan.svg)](https://github.com/lepoco/wpfui)

An enterprise-grade network engineering application built in **C# (.NET 8)** with **WPF-UI (Windows 11 Mica Fluent Design)**. **Switch Handover Generator** automates the tedious process of parsing Cisco switch configurations and live network topologies to produce client-ready handover reports in Excel (`.xlsx`) and CSV formats.

---

## ✨ Key Capabilities

* **⚡ Live Network Discovery (SSH & CDP Crawl):**
  * Connects to Cisco switches over SSH.
  * Recursively crawls CDP / LLDP neighbors across multi-tier topologies.
  * Automatically filters out VoIP phones and discovers Cisco Catalyst 9100 series Access Points.
  * Supports optional subnet CIDR boundary filtering (e.g. `10.1.0.0/16`) to keep discovery strictly within site limits.
* **📁 Offline Configuration Logs Parser:**
  * Fast, parallelized offline processing of SecureCRT / PuTTY log files.
  * Smart Stack Detection: Extracts stack member IDs, roles, individual MAC addresses, serial numbers, and models (Catalyst 9200, 9300, 3850 series).
  * Layer 2 vs. Layer 3 Context: Identifies Core/Distribution vs. Access switches based on active IP routing configurations.
  * Environmental & PSU Status: Scans operational logs to track healthy power supplies and hardware redundancy.
* **🔍 Port & Topology Intelligence:**
  * True physical port counting (excluding VLANs, Port-channels, and virtual interfaces).
  * Complete CDP neighbor interface mapping (remote hostnames, remote ports, neighbor management IPs).
  * Port operational states, VLAN assignments, access vs. 802.1Q trunking, speed, and duplex modes.
* **💎 SFP Optics & Wireless AP Inventory:**
  * Optical transceiver inventory (10G SFP+, 1G SFP) with PID, serial numbers, and In-Use vs. Plugged tracking.
  * Dedicated Cisco Catalyst 9100 series Access Point configuration parser.
* **🗜️ Complete ZIP Archive Support (Input & Export):**
  * Direct selection and parsing of `.zip` archives (e.g. from Bulk Switch Backup) without manual extraction.
  * Real-time ZIP inspection: Displays internal configuration log counts before starting analysis.
  * Automatic recursive extraction: Handles nested directory hierarchies inside archives seamlessly.
  * Drag & Drop: Drop `.zip` archives, loose log files, or folders directly into the application window.
  * One-Click Reports Bundle (.zip): Export Complete Handover Excel and all CSV reports packaged in a single ZIP.
  * Captured Logs Backup (.zip): Instantly save raw show command logs from Live Discovery as a `.zip` archive.
* **📊 Multi-Sheet Excel & Granular CSV Export:**
  * Generates formatted Excel (`.xlsx`) workbooks with auto-styled corporate headers, alternating row striping, and column auto-fitting.
  * Instant one-click CSV exports for Switch Details, Port Mappings, SFP Inventory, and All Physical Ports configurations.
* **🎨 Modern Windows 11 Fluent UI:**
  * Native Mica backdrop support with instantaneous Light and Dark theme switching.
  * Segmented pill navigation bar, interactive DataGrids, and progress dialog overlays.

---

## 🛠️ Technology Stack

* **Framework:** .NET 8 (Windows Desktop / WPF)
* **UI & Styling:** [WPF-UI (lepoco)](https://github.com/lepoco/wpfui) — Modern Windows 11 Fluent Design & Mica backdrop
* **Architecture:** MVVM pattern using [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)
* **SSH Networking:** [SSH.NET](https://github.com/sshnet/SSH.NET)
* **Excel Reporting:** [ClosedXML](https://github.com/ClosedXML/ClosedXML)
* **Testing:** xUnit testing suite (`SwitchHandover.Tests`)

---

## 💻 Recommended Cisco CLI Commands

When capturing configuration logs via PuTTY or SecureCRT for offline analysis, ensure the session includes the following show commands:

```text
terminal length 0
show running-config
show version
show cdp neighbors
show cdp neighbors detail
show ip interface brief
show environment all       (or 'show inventory')
show interfaces status
show interfaces transceiver
```

---

## 🚀 Building & Running

### Prerequisites
* **Windows 10 / 11 (x64)**
* [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### 1. Build & Test via CLI
```bash
# Clone the repository
git clone https://github.com/Majordean0303/switch-handover-generator.git
cd switch-handover-generator

# Run test suite
dotnet test SwitchHandover.Tests/SwitchHandover.Tests.csproj

# Build solution
dotnet build SwitchHandoverGenerator.sln
```

### 2. Publish Standalone Single-File Executable
```bash
dotnet publish SwitchHandover.App/SwitchHandover.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist-csharp
```
The standalone executable will be generated at `dist-csharp\SwitchHandover.App.exe` (no separate .NET runtime installation required).

---

## 📂 Project Structure

```text
switch-handover-generator/
├── SwitchHandoverGenerator.sln        # Main Visual Studio Solution
├── SwitchHandover.App/                # WPF-UI Application
│   ├── Assets/                        # Modern minimalist Windows 11 application icons
│   ├── Converters/                    # WPF Value Converters
│   ├── ViewModels/                    # MVVM ViewModels (MainViewModel)
│   └── Views/                         # Fluent XAML views (MainWindow)
├── SwitchHandover.Core/               # Core Engine Library
│   ├── Models/                        # Data models (SwitchDetails, PortMapping, SfpInventory, etc.)
│   ├── Parsers/                       # Regex log parsing engines (SwitchHandoverParser, APHandoverParser)
│   └── Services/                      # ClosedXML Excel exporter & SSH.NET Live Discovery Engine
├── SwitchHandover.Tests/              # xUnit Test Project
├── dist-csharp/                       # Output folder for published standalone executable
├── .gitignore                         # Git exclusion rules
└── README.md                          # Project documentation
```

---

## 👤 Author & Credits

* **Author:** Yash Nalawde ([@Majordean0303](https://github.com/Majordean0303))
* **Organization:** M s Conventus Technologies Pvt. Ltd