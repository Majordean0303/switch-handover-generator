# Switch Handover Generator 🚀
**Author:** Yash Nalawde. **Programmer Name:** MajorDean0303

An automated Python Desktop application designed to eliminate the manual, tedious process of creating network handover documentation. 

This tool parses raw Cisco CLI logs (specifically Catalyst 9200/9300 series) and intelligently extracts hardware inventory, stack member details, physical port counts, and CDP-based topology mappings to generate a clean, client-ready Excel Handover Matrix.

## ✨ Key Features

* **Smart Stack Detection:** Automatically navigates Cisco's interleaved `show version` logs to extract correct MAC addresses, Serial Numbers, and Models for *every individual switch* within a stack (dynamically handling model-specific CLI inconsistencies).
* **Automated Topology Mapping:** Parses `show cdp neighbors` to map local interfaces to neighbor hostnames, IP addresses, and remote ports.
* **Physical Port Counting:** Uses strict regex to count true physical hardware interfaces, filtering out virtual interfaces (VLANs, Port-channels, App-hosting).
* **L2 / L3 Context:** Automatically identifies Core (L3) vs. Access (L2) switches based on routing configurations.
* **Deep Interface Context:** Extracts port descriptions, VLAN assignments, and Trunk/Access modes directly from the running config.
* **Power Supply Health:** Scans environmental logs to report the exact number of *active/healthy* power supplies per stack member.
* **Modern Desktop GUI:** Built with CustomTkinter featuring a sleek dark mode, native OS file selection, and an instant dashboard preview before downloading to Excel.

## 🛠️ Tech Stack

* **Language:** Python 3.x
* **Data Processing:** Python `re` (Regular Expressions), `collections.Counter`
* **Frontend:** CustomTkinter (Dark Mode GUI)
* **Export:** `xlsxwriter` (for lightweight Excel `.xlsx` generation)
* **Build Tool:** PyInstaller

## 📦 Installation & Setup

1. **Download the Executable:**
   Navigate to the `dist/` folder and download `SwitchHandoverGenerator.exe`.
2. **Run the Application:**
   Simply double-click the `.exe` to launch the application. No installation is required!

*(If you wish to build from source, you can clone the repository, install `customtkinter` and `xlsxwriter`, and run `python build_executable.py`)*

## 💻 Usage & Required CLI Commands

To generate a **100% complete** handover matrix, the tool requires specific output from the switch.

When pulling logs from SecureCRT or PuTTY, ensure you execute the following commands in order and save the output as a `.txt` or `.log` file:

```
terminal length 0
show running-config
show version
show cdp neighbors
show cdp neighbors det
show ip int bri
show env all      (or 'show inventory')

```

### Workflow:

1. Open the application.
2. Fill in the generic site details (Location, Location Type, Management VLAN).
3. Click **"Browse Config Files"** to select your `.txt` or `.log` config files.
4. Click **"ANALYZE & GENERATE"**.
5. Review the parsed data on the dashboard grid.
6. Click **"Complete Handover"** to export your Excel workbook.

## 🗂️ Project Structure

* `main.py`: The main GUI application built with CustomTkinter and xlsxwriter logic.
* `parser.py`: The core regex parsing engine (`SwitchHandoverParser` class).
* `build_executable.py`: A Python script that automatically builds the PyInstaller executable.
* `splash.png`: The loading screen image.

## 🤝 Contributing

Contributions, issues, and feature requests are welcome! If you find a Cisco iOS output that breaks the current regex engine, please open an issue and include an anonymized snippet of the log.