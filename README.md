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