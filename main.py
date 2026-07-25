import customtkinter as ctk
from tkinter import filedialog, messagebox
from tkinter import ttk
import xlsxwriter
import threading
import os

from parser import SwitchHandoverParser

ctk.set_appearance_mode("Dark")
ctk.set_default_color_theme("blue")

class App(ctk.CTk):
    def __init__(self):
        super().__init__()

        self.title("Switch Handover Generator")
        self.geometry("1200x820")
        self.minsize(1000, 720)

        self.selected_files = []
        self.all_switch_details = []
        self.all_port_mappings = []

        self.title_font  = ctk.CTkFont(family="Segoe UI", size=26, weight="bold")
        self.header_font = ctk.CTkFont(family="Segoe UI", size=13, weight="bold")
        self.normal_font = ctk.CTkFont(family="Segoe UI", size=13)
        self.small_font  = ctk.CTkFont(family="Segoe UI", size=11)

        # ── Title bar ──────────────────────────────────────────────────
        title_bar = ctk.CTkFrame(self, fg_color="#1a1a2e", corner_radius=0, height=60)
        title_bar.pack(fill="x")
        title_bar.pack_propagate(False)
        ctk.CTkLabel(title_bar, text="⬡  Switch Handover Generator",
                     font=self.title_font, text_color="#4da6ff").pack(side="left", padx=30, pady=10)

        # ── Top config row ─────────────────────────────────────────────
        top = ctk.CTkFrame(self, fg_color="transparent")
        top.pack(fill="x", padx=24, pady=(16, 4))
        top.grid_columnconfigure((0, 1, 2, 3, 4, 5), weight=1)

        def field(parent, label, row, col, placeholder, width=180, **kw):
            ctk.CTkLabel(parent, text=label, font=self.header_font,
                         text_color="#aaaaaa").grid(row=row, column=col, sticky="w",
                                                    padx=(0, 12), pady=(0, 4))
            e = ctk.CTkEntry(parent, placeholder_text=placeholder,
                             width=width, font=self.normal_font,
                             corner_radius=8, **kw)
            e.grid(row=row + 1, column=col, sticky="ew", padx=(0, 12), pady=(0, 0))
            return e

        self.loc_entry  = field(top, "SITE LOCATION",    0, 0, "e.g. Mumbai")
        self.vlan_entry = field(top, "MANAGEMENT VLAN",  0, 1, "Optional")

        ctk.CTkLabel(top, text="LOCATION TYPE", font=self.header_font,
                     text_color="#aaaaaa").grid(row=0, column=2, sticky="w", padx=(0, 12), pady=(0, 4))
        self.loc_type_combo = ctk.CTkComboBox(top, values=["Branch", "DC", "DR", "Vendor"],
                                              width=180, font=self.normal_font, corner_radius=8)
        self.loc_type_combo.grid(row=1, column=2, sticky="ew", padx=(0, 12))

        # Spacer column
        top.grid_columnconfigure(3, weight=2)

        # File section right-aligned
        self.browse_btn = ctk.CTkButton(top, text="＋ Browse Config Files",
                                        font=self.normal_font, corner_radius=8,
                                        command=self.browse_files, height=38, width=190)
        self.browse_btn.grid(row=0, column=4, rowspan=2, sticky="e", padx=(12, 8))

        self.file_label = ctk.CTkLabel(top, text="No files selected",
                                       font=self.small_font, text_color="#666666",
                                       anchor="w", width=160)
        self.file_label.grid(row=0, column=5, rowspan=2, sticky="w")

        # ── Analyse button ─────────────────────────────────────────────
        self.generate_btn = ctk.CTkButton(
            self, text="ANALYZE & GENERATE",
            font=ctk.CTkFont(family="Segoe UI", size=15, weight="bold"),
            height=44, corner_radius=8,
            command=self.start_generation,
            fg_color="#1a7a3c", hover_color="#145f2e")
        self.generate_btn.pack(pady=(14, 8), padx=24, fill="x")

        # ── Tabs ───────────────────────────────────────────────────────
        self.tabview = ctk.CTkTabview(self, corner_radius=8)
        self.tabview.pack(fill="both", expand=True, padx=24, pady=(4, 8))

        self.tab_logs     = self.tabview.add("Analysis Logs")
        self.tab_switches = self.tabview.add("Switch Details")
        self.tab_ports    = self.tabview.add("Port Mappings")

        self.log_box = ctk.CTkTextbox(
            self.tab_logs, state="disabled",
            font=ctk.CTkFont(family="Consolas", size=12),
            corner_radius=6, fg_color="#111111")
        self.log_box.pack(fill="both", expand=True, padx=4, pady=4)

        # Treeview style
        style = ttk.Style(self)
        style.theme_use("default")
        BG, FG, HEAD = "#161616", "#e0e0e0", "#1f2430"
        style.configure("Treeview",
                        background=BG, foreground=FG,
                        rowheight=28, fieldbackground=BG,
                        borderwidth=0, font=("Segoe UI", 11))
        style.map("Treeview", background=[("selected", "#1c5f99")])
        style.configure("Treeview.Heading",
                        background=HEAD, foreground="#aaccff",
                        relief="flat", font=("Segoe UI", 11, "bold"))
        style.map("Treeview.Heading", background=[("active", "#1c5f99")])

        self.tree_switches = self._make_tree(self.tab_switches)
        self.tree_ports    = self._make_tree(self.tab_ports)

        # ── Download buttons ───────────────────────────────────────────
        btn_row = ctk.CTkFrame(self, fg_color="transparent")
        btn_row.pack(fill="x", padx=24, pady=(0, 14))
        btn_row.grid_columnconfigure((0, 1, 2), weight=1)

        self.btn_export_switches = ctk.CTkButton(
            btn_row, text="Download Switch Details",
            command=lambda: self.export_excel("switches"),
            state="disabled", corner_radius=8, height=40, font=self.normal_font)
        self.btn_export_switches.grid(row=0, column=0, padx=(0, 6), sticky="ew")

        self.btn_export_ports = ctk.CTkButton(
            btn_row, text="Download Port Mappings",
            command=lambda: self.export_excel("ports"),
            state="disabled", corner_radius=8, height=40, font=self.normal_font)
        self.btn_export_ports.grid(row=0, column=1, padx=6, sticky="ew")

        self.btn_export_both = ctk.CTkButton(
            btn_row, text="Download Complete Handover",
            command=lambda: self.export_excel("both"),
            state="disabled", corner_radius=8, height=40, font=self.normal_font,
            fg_color="#6F42C1", hover_color="#5A32A3")
        self.btn_export_both.grid(row=0, column=2, padx=(6, 0), sticky="ew")

    # ── Helpers ──────────────────────────────────────────────────────────
    def _make_tree(self, parent):
        """Create a Treeview with modern CTk scrollbars inside `parent` tab."""
        frame = ctk.CTkFrame(parent, fg_color="transparent")
        frame.pack(fill="both", expand=True, padx=4, pady=4)

        vsb = ctk.CTkScrollbar(frame, orientation="vertical")
        vsb.pack(side="right", fill="y")
        hsb = ctk.CTkScrollbar(frame, orientation="horizontal")
        hsb.pack(side="bottom", fill="x")

        tree = ttk.Treeview(frame, show="headings",
                            selectmode="extended",
                            yscrollcommand=vsb.set,
                            xscrollcommand=hsb.set)
        tree.pack(fill="both", expand=True)
        vsb.configure(command=tree.yview)
        hsb.configure(command=tree.xview)
        
        def select_all(event):
            tree.selection_set(tree.get_children())
            return "break"
            
        tree.bind("<Control-a>", select_all)
        
        def copy_selection(event):
            selected = tree.selection()
            if not selected:
                return
            lines = []
            for item in selected:
                values = tree.item(item, "values")
                lines.append("\t".join(str(v) for v in values))
            self.clipboard_clear()
            self.clipboard_append("\n".join(lines))
            self.update()
            
        tree.bind("<Control-c>", copy_selection)
        
        return tree

    def log(self, message):
        def _do():
            self.log_box.configure(state="normal")
            self.log_box.insert("end", message + "\n")
            self.log_box.see("end")
            self.log_box.configure(state="disabled")
        self.after(0, _do)

    def browse_files(self):
        files = filedialog.askopenfilenames(
            title="Select Cisco Config Logs",
            filetypes=[("Text/Log Files", "*.txt *.log"), ("All files", "*.*")])
        if files:
            self.selected_files = list(files)
            self.file_label.configure(
                text=f"{len(self.selected_files)} file(s) selected",
                text_color="#4da6ff")
            self.log(f"Selected {len(self.selected_files)} file(s).")

    def start_generation(self):
        if not self.selected_files:
            messagebox.showwarning("Warning", "Please select at least one configuration file.")
            return
        if not self.loc_entry.get().strip():
            messagebox.showwarning("Warning", "Please enter a Site Location.")
            return
        self.generate_btn.configure(state="disabled", text="PROCESSING…")
        self.log("Starting analysis…")
        threading.Thread(target=self.process_files, daemon=True).start()

    def _populate_tree(self, tree, rows):
        if not rows:
            return
        cols = list(rows[0].keys())
        tree["columns"] = cols
        for col in cols:
            tree.heading(col, text=col)
            tree.column(col, width=160, minwidth=80, anchor="w")
        for item in tree.get_children():
            tree.delete(item)
        for row in rows:
            tree.insert("", "end", values=[row.get(c, "") for c in cols])

    def populate_tables(self):
        self._populate_tree(self.tree_switches, self.all_switch_details)
        self._populate_tree(self.tree_ports,    self.all_port_mappings)
        if self.all_switch_details:
            self.tabview.set("Switch Details")
        for b in (self.btn_export_switches, self.btn_export_ports, self.btn_export_both):
            b.configure(state="normal")

    def process_files(self):
        try:
            location      = self.loc_entry.get().strip()
            location_type = self.loc_type_combo.get()
            mgmt_vlan     = self.vlan_entry.get().strip() or None

            self.all_switch_details = []
            self.all_port_mappings  = []

            for filepath in self.selected_files:
                self.log(f"Parsing: {os.path.basename(filepath)}")
                with open(filepath, 'r', encoding='utf-8', errors='ignore') as f:
                    log_text = f.read()
                p = SwitchHandoverParser(log_text, location=location,
                                         location_type=location_type,
                                         mgmt_vlan=mgmt_vlan)
                self.all_switch_details.extend(p.parse_switch_details())
                self.all_port_mappings.extend(p.parse_port_mapping())

            # Cross-resolution
            self.log("Resolving neighbour IPs…")
            ip_dir = {s['Hostname']: s['IP Address']
                      for s in self.all_switch_details if s['IP Address'] != 'N/A'}
            for port in self.all_port_mappings:
                if port['Neighbour Device IP'] != '-':
                    continue
                nh = port['Neighbour Hostname']
                if nh != '-':
                    clean = nh.split('.')[0]
                    port['Neighbour Device IP'] = ip_dir.get(clean, "Unknown (Upload config)")

            self.log("✔ Analysis complete — download reports below.")
            self.after(0, self.populate_tables)

        except Exception as e:
            self.log(f"✘ Error: {e}")
            self.after(0, lambda: messagebox.showerror("Error", str(e)))
        finally:
            self.after(0, lambda: self.generate_btn.configure(
                state="normal", text="ANALYZE & GENERATE"))

    def export_excel(self, export_type):
        default_names = {
            "switches": "Switch_Details_Report.xlsx",
            "ports": "Port_Mapping_Report.xlsx",
            "both": "Complete_Handover_Report.xlsx"
        }
        
        path = filedialog.asksaveasfilename(
            defaultextension=".xlsx",
            initialfile=default_names.get(export_type, "Handover_Report.xlsx"),
            filetypes=[("Excel files", "*.xlsx")],
            title="Save Handover Report")
        if not path:
            return

        switch_cols = [
            'Location', 'Location Type', 'Hostname', 'Stack Member',
            'IP Address', 'Device Type', 'Make', 'Model', 'Sr No.',
            'Firmware Version', 'Uptime', 'Total Ports', 'MAC Address',
            'Default Gateway', 'NTP Server', 'Active Power Supplies',
        ]
        port_cols = [
            'Location', 'Location Type', 'Device Type', 'Hostname', 'Device IP',
            'Port No.', 'Description', 'State',
            'Neighbour Hostname', 'Neighbour Device IP', 'Neighbour Port No.',
        ]

        try:
            workbook = xlsxwriter.Workbook(path)
            
            # Helper to write sheet data
            def write_sheet(sheet_name, columns, data_list):
                ws = workbook.add_worksheet(sheet_name)
                # Format for headers
                header_format = workbook.add_format({'bold': True, 'bg_color': '#D3D3D3', 'border': 1})
                
                # Write Headers
                for col_num, col_name in enumerate(columns):
                    ws.write(0, col_num, col_name, header_format)
                    # Set approximate width
                    ws.set_column(col_num, col_num, max(len(col_name) + 4, 15))
                    
                # Write Rows
                for row_num, row_data in enumerate(data_list):
                    for col_num, col_name in enumerate(columns):
                        val = row_data.get(col_name, "")
                        ws.write(row_num + 1, col_num, str(val))

            if export_type in ('switches', 'both'):
                write_sheet("Switch Details", switch_cols, self.all_switch_details)
                
            if export_type in ('ports', 'both'):
                write_sheet("Port Mapping", port_cols, self.all_port_mappings)
                
            workbook.close()
            
            self.log(f"✔ Saved: {path}")
            messagebox.showinfo("Success", "Report exported successfully!")
        except Exception as e:
            self.log(f"✘ Export failed: {e}")
            messagebox.showerror("Export Error", str(e))


if __name__ == "__main__":
    app = App()
    
    # Close the PyInstaller Splash Screen if we are running from a bundled executable
    try:
        import pyi_splash
        pyi_splash.close()
    except ImportError:
        pass
        
    app.mainloop()
