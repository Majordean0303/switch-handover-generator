# Switch Handover Generator 🚀

[![.NET 10](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![Platform](https://img.shields.io/badge/Platform-Windows%20|%20macOS%20|%20Linux-lightgrey.svg)](https://dotnet.microsoft.com/)
[![UI](https://img.shields.io/badge/UI-Avalonia%2011%20Fluent-purple.svg)](https://avaloniaui.net/)
[![Format](https://img.shields.io/badge/Export-Excel%20|%20CSV%20|%20Visio%20VSDX%20|%20ZIP-green.svg)](https://github.com/Majordean0303/switch-handover-generator)

An enterprise-grade network engineering and documentation application built in **C# (.NET 10)** with **Avalonia UI (Cross-Platform Fluent Design)** and **System.CommandLine**. **Switch Handover Generator** automates parsing Cisco switch configurations, Cisco Catalyst 9100 AP operational logs, and live SSH network topologies to produce client-ready handover reports in formatted Excel (`.xlsx`), granular CSVs, reports bundle ZIPs, and interactive Microsoft Visio (`.vsdx`) topology diagrams.

---

## ✨ Key Capabilities

* **⚡ Bounded Live Network Discovery (SSH & CDP Traversal):**
  * Explicit, safe SSH session management across Cisco Catalyst switches and access points.
  * Configurable concurrency (1–32 bounded workers), hop depth limit, and 512-device discovery ceiling.
  * Subnet CIDR boundary filtering (e.g. `10.1.0.0/16`) to strictly keep crawling within designated site limits.
  * Automatically filters out VoIP phones (`CP-`, `SEP...`) and discovers Cisco Catalyst 9100 series Access Points.
  * In-memory session credential handling without disk caching; supports graceful cancellation while keeping completed captures intact.
* **📁 Offline Configuration Logs Parser:**
  * High-speed parsing of PuTTY, SecureCRT, and raw CLI text logs.
  * **Smart Stack Detection:** Extracts stack member IDs, roles, individual MAC addresses, serial numbers, and models (Catalyst 9200, 9300, 3850 series, etc.).
  * **Layer 2 vs. Layer 3 Context:** Automatically detects Core/Distribution vs. Access roles based on active IP routing status.
  * **CDP Neighbor & Interface Mapping:** Parses both summary and detailed CDP blocks to map local interfaces, remote hostnames, remote ports, and neighbor management IPs.
  * **Environmental & Power Supplies:** Scans operational logs for active/redundant power supply status and hardware health.
* **🗺️ Interactive Topology Visualizer & Visio (`.vsdx`) Export:**
  * Interactive canvas with draggable device nodes organized into hierarchical tiers (Core, Access, APs, External).
  * Direct export to Microsoft Visio (`.vsdx`) based on the canvas layout snapshot using validated OpenXML packaging.
  * Physical-wire links preserving port labels, Port-Channel groupings, and media types without fabricating unobserved connections.
* **💎 SFP Optics & Wireless AP Inventory:**
  * Optical transceiver inventory (100G, 40G, 25G, 10G SFP+, 1G SFP) with PID, serial numbers, and In-Use vs. Plugged operational status.
  * Dedicated Cisco Catalyst 9100 series Access Point configuration parser for standalone and controller-managed APs.
* **🗜️ Complete ZIP Archive Support (Input & Export):**
  * Direct ingestion of `.zip` archives (e.g. from *Bulk Switch Backup*) without manual file extraction.
  * Built-in safety limits (1,000 files, 128 MiB per file / 512 MiB total) to guard against decompression bombs.
  * **One-Click Reports Bundle (.zip):** Exports the Complete Handover Excel workbook and all four CSV reports in a single package.
  * **Captured Logs Backup (.zip):** Instantly bundles raw show command outputs from live discovery as a timestamped ZIP archive.
* **📊 Multi-Sheet Excel & Granular CSV Export:**
  * Generates auto-styled Excel (`.xlsx`) workbooks with corporate headers, zebra striping, and auto-fitted columns.
  * Instant CSV exports for *Switch Details*, *Port Mapping*, *SFP Inventory*, and *Complete Physical Port Configurations*.
* **💻 Headless CLI (`SwitchHandover.Cli`):**
  * Fully automatable command-line interface for CI/CD, batch processing, and scheduled network audits.
  * Accepts single files, directories, and ZIP archives with flags for location metadata, VLAN filtering, and export formats.
* **🎨 Modern Cross-Platform Fluent UI (`SwitchHandover.Desktop`):**
  * Native rendering across Windows, macOS, and Linux using Avalonia 11 Fluent Theme.
  * Instant Light and Dark theme switching, responsive layouts, and UI display scaling options (100%, 125%, 150%).

---

## 🛠️ Technology Stack

* **Runtime & Framework:** .NET 10.0 (C# 13)
* **Desktop UI:** [Avalonia UI 11](https://avaloniaui.net/) — Cross-platform Fluent Design
* **CLI:** Portable C# .NET 10 console application
* **Topology Engine & Visio Export:** OpenXml Visio generator (`Topology/VisioCreator.*`)
* **Excel Reporting:** [ClosedXML](https://github.com/ClosedXML/ClosedXML)
* **SSH Networking:** [SSH.NET](https://github.com/sshnet/SSH.NET)
* **Testing:** xUnit (`SwitchHandover.Tests`) & Skia Headless UI Testing (`SwitchHandover.UiTests`)
* **Legacy Reference:** WPF-UI (.NET 8) preserved in `SwitchHandover.App` / `SwitchHandoverGenerator.LegacyWpf.sln`

---

## 💻 Recommended Cisco CLI Commands

When capturing configuration logs via PuTTY or SecureCRT for offline analysis, ensure the session includes the following commands:

```text
terminal length 0
show version
show inventory
show running-config
show ip interface brief
show interfaces status
show cdp neighbors detail
show interfaces transceiver
show environment all       (or 'show env power')
```

---

## 🚀 Building & Running

### Prerequisites
* [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
* Windows 10/11, macOS, or Linux

### 1. Build & Test via CLI
```bash
# Clone the repository
git clone https://github.com/Majordean0303/switch-handover-generator.git
cd switch-handover-generator

# Run full test suite (45 core unit tests + 2 UI smoke tests)
dotnet test SwitchHandover.sln

# Or execute the complete test pipeline via PowerShell
powershell -ExecutionPolicy Bypass -File scripts/test-all.ps1
```

### 2. Launch the Desktop Application
```bash
dotnet run --project SwitchHandover.Desktop
```

### 3. Run the CLI
```bash
# Generate Excel report and Visio topology diagram from log files or ZIP archive
dotnet run --project SwitchHandover.Cli -- \
  --input SwitchHandover.Tests/Fixtures/access.txt \
  --location "HQ-DataCenter" \
  --location-type "DC" \
  --mgmt-vlan 10 \
  --excel artifacts/handover.xlsx \
  --vsdx artifacts/topology.vsdx
```

### 4. Publish Standalone Single-File Executable
```powershell
# Windows x64
powershell -ExecutionPolicy Bypass -File scripts/package.ps1 -Runtime win-x64

# Linux x64
powershell -ExecutionPolicy Bypass -File scripts/package.ps1 -Runtime linux-x64

# macOS ARM64 (Apple Silicon)
powershell -ExecutionPolicy Bypass -File scripts/package.ps1 -Runtime osx-arm64
```
The published standalone executable will be generated at `artifacts/packages/<runtime>/`.

---

## 📂 Project Structure

```text
switch-handover-generator/
├── SwitchHandover.sln                         # Primary .NET 10 Solution
├── SwitchHandover.Desktop/                    # Avalonia 11 Cross-Platform UI Application
│   ├── Assets/                                # Fluent icons and graphics
│   ├── ViewModels/                            # Reactive ViewModels
│   └── Views/                                 # Fluent AXAML views and visual layout canvas
├── SwitchHandover.Cli/                        # Headless Command-Line Interface
├── SwitchHandover.Core/                       # Core Parsing and Reporting Engine Library
│   ├── Models/                                # SwitchDetails, PortMapping, SfpInventory, etc.
│   ├── Parsers/                               # SwitchHandoverParser, APHandoverParser, ConfigParser
│   └── Services/                              # BoundedLiveCapture, HandoverAnalysis, ZipArchiveService, Exporters
├── Topology/                                  # Visio Diagram & Graph Engine
│   ├── VisioCreator.Core/                     # Graph layout engine, tier assignment, node models
│   └── VisioCreator.Export/                   # OpenXML VSDX generator with standard stencils
├── SwitchHandover.Tests/                      # xUnit Core Unit Tests
├── SwitchHandover.UiTests/                    # Skia Headless UI Integration Tests
├── SwitchHandoverGenerator.LegacyWpf.sln      # Legacy WPF .NET 8 Reference Solution
├── SwitchHandover.App/                        # Legacy WPF Windows-only Application
├── docs/                                      # Topology provenance and architecture notes
├── scripts/                                   # Build, test, and packaging PowerShell scripts
└── README.md                                  # Application documentation
```

---

## 👤 Author & Credits

* **Author:** Yash Nalawde ([@Majordean0303](https://github.com/Majordean0303))
