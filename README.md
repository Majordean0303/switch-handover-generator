```markdown
# Switch Handover Generator 🚀

An automated Python and Flask-based internal tool designed to eliminate the manual, tedious process of creating network handover documentation. 

This tool parses raw Cisco CLI logs (specifically Catalyst 9200/9300 series) and intelligently extracts hardware inventory, stack member details, physical port counts, and CDP-based topology mappings to generate a clean, client-ready Excel Handover Matrix.

## ✨ Key Features

* **Smart Stack Detection:** Automatically navigates Cisco's interleaved `show version` logs to extract correct MAC addresses, Serial Numbers, and Models for *every individual switch* within a stack (dynamically handling model-specific CLI inconsistencies).
* **Automated Topology Mapping:** Parses `show cdp neighbors` to map local interfaces to neighbor hostnames, IP addresses, and remote ports.
* **Physical Port Counting:** Uses strict regex to count true physical hardware interfaces, filtering out virtual interfaces (VLANs, Port-channels, App-hosting).
* **L2 / L3 Context:** Automatically identifies Core (L3) vs. Access (L2) switches based on routing configurations.
* **Deep Interface Context:** Extracts port descriptions, VLAN assignments, and Trunk/Access modes directly from the running config.
* **Power Supply Health:** Scans environmental logs to report the exact number of *active/healthy* power supplies per stack member.
* **Modern Web UI:** Built with Tailwind CSS featuring a drag-and-drop file uploader, dark mode, and an instant web-dashboard preview before downloading to Excel.

## 🛠️ Tech Stack

* **Backend:** Python 3.x, Flask
* **Data Processing:** Pandas, Python `re` (Regular Expressions), `collections.Counter`
* **Frontend:** HTML5, Tailwind CSS (via CDN), Jinja2 Templating
* **Export:** `openpyxl` (for Excel `.xlsx` generation)

## 📦 Installation & Setup

1. **Clone the repository** (or download the source files):
   ```bash
   git clone [https://github.com/yourusername/switch-handover-generator.git](https://github.com/yourusername/switch-handover-generator.git)
   cd switch-handover-generator

```

2. **Create a Virtual Environment (Optional but recommended):**
```bash
python -m venv venv
source venv/bin/activate  # On Windows use: venv\Scripts\activate

```


3. **Install Required Dependencies:**
```bash
pip install Flask pandas openpyxl werkzeug

```


4. **Run the Application:**
```bash
python app.py

```


5. **Access the Web UI:**
Open your web browser and navigate to `http://127.0.0.1:5000`

## 💻 Usage & Required CLI Commands

To generate a **100% complete** handover matrix, the tool requires specific output from the switch.

When pulling logs from SecureCRT or PuTTY, ensure you execute the following commands in order and save the output as a `.txt` or `.log` file:

```
terminal length 0
show running-config
show version
show cdp neighbors
show ip int bri
show env all      (or 'show inventory')

```

### Workflow:

1. Open the Web UI.
2. Fill in the generic site details (Location, Location Type, Management VLAN).
3. **Drag & Drop** your `.txt` config files into the drop zone.
4. Click **"Analyze & Generate"**.
5. Review the parsed data on the web dashboard.
6. Click **"Download Complete Handover"** to export your Excel workbook.

## 🗂️ Project Structure

* `app.py`: The main Flask routing engine and Pandas Excel-export logic.
* `parser.py`: The core regex parsing engine (`SwitchHandoverParser` class) that reads the raw text and structures the dictionaries.
* `templates/index.html`: The Jinja2/Tailwind HTML frontend.

## 🔮 Future Roadmap

## 🤝 Contributing

Contributions, issues, and feature requests are welcome! If you find a Cisco iOS output that breaks the current regex engine, please open an issue and include an anonymized snippet of the log.

```

```