from flask import Flask, request, render_template, send_file
import pandas as pd
import io
import json

from parser import SwitchHandoverParser

app = Flask(__name__)

@app.after_request
def add_attribution_header(response):
    # This header is invisible to the UI but visible in network traffic inspectors
    response.headers['X-Engineered-By'] = 'Yash Nalawde'
    return response
# ----------------------------------------

@app.route('/', methods=['GET'])
def index():
    return render_template('index.html', switches=None, ports=None)

@app.route('/generate', methods=['POST'])
def generate_handover():
    files         = request.files.getlist('config_files')
    location      = request.form.get('location', 'Unknown').strip()
    location_type = request.form.get('location_type', 'Branch')
    mgmt_vlan_raw = request.form.get('mgmt_vlan', '').strip()
    mgmt_vlan     = mgmt_vlan_raw if mgmt_vlan_raw else None  # None → auto-detect in parser

    if not files or files[0].filename == '':
        return render_template('index.html', error="No files uploaded.")

    all_switch_details = []
    all_port_mappings  = []

    for file in files:
        log_text = file.read().decode('utf-8', errors='ignore')
        parser   = SwitchHandoverParser(
            log_text,
            location=location,
            location_type=location_type,
            mgmt_vlan=mgmt_vlan,
        )
        # parse_switch_details() always returns a list (one entry per stack member,
        # or a single-element list for a standalone switch).
        all_switch_details.extend(parser.parse_switch_details())
        all_port_mappings.extend(parser.parse_port_mapping())

    # --- Fallback Cross-Resolution Engine ---
    # Primary neighbor IPs come from 'show cdp neighbors detail' (resolved inside
    # the parser).  This pass fills in only the ports that still have "-" because
    # CDP detail output was not present in the uploaded file.
    ip_directory = {
        s['Hostname']: s['IP Address']
        for s in all_switch_details
        if s['IP Address'] != 'N/A'
    }

    for port in all_port_mappings:
        if port['Neighbour Device IP'] != '-':
            continue  # Already resolved via CDP detail — leave it alone
        neigh_host = port['Neighbour Hostname']
        if neigh_host != '-':
            clean_host = neigh_host.split('.')[0]
            port['Neighbour Device IP'] = (
                ip_directory[clean_host]
                if clean_host in ip_directory
                else "Unknown (Upload config)"
            )

    return render_template(
        'index.html',
        switches=all_switch_details,
        ports=all_port_mappings,
        switches_json=json.dumps(all_switch_details),
        ports_json=json.dumps(all_port_mappings),
    )

@app.route('/download', methods=['POST'])
def download_excel():
    export_type   = request.form.get('export_type')
    switches_data = json.loads(request.form.get('switches_data', '[]'))
    ports_data    = json.loads(request.form.get('ports_data',   '[]'))

    output   = io.BytesIO()
    filename = "Handover_Report.xlsx"

    # Column order for Excel export — Stack Member sits right after Hostname
    switch_columns = [
        'Location', 'Location Type', 'Hostname', 'Stack Member',
        'IP Address', 'Device Type', 'Make', 'Model', 'Sr No.',
        'Firmware Version', 'Uptime', 'Total Ports', 'MAC Address',
        'Default Gateway', 'NTP Server', 'Active Power Supplies',
    ]
    port_columns = [
        'Location', 'Location Type', 'Device Type', 'Hostname', 'Device IP',
        'Port No.', 'Description', 'State',
        'Neighbour Hostname', 'Neighbour Device IP', 'Neighbour Port No.',
    ]

    with pd.ExcelWriter(output, engine='xlsxwriter') as writer:
        if export_type in ['switches', 'both']:
            df_switches = pd.DataFrame(switches_data)
            df_switches = df_switches.reindex(columns=switch_columns)
            df_switches.to_excel(writer, sheet_name="Switch Details", index=False)
            filename = "Switch_Details_Report.xlsx"

        if export_type in ['ports', 'both']:
            df_ports = pd.DataFrame(ports_data)
            df_ports = df_ports.reindex(columns=port_columns)
            df_ports.to_excel(writer, sheet_name="Port Mapping", index=False)
            if export_type == 'ports': filename = "Port_Mapping_Report.xlsx"
            if export_type == 'both':  filename = "Complete_Handover_Report.xlsx"

        for worksheet in writer.sheets.values():
            worksheet.autofit()

    output.seek(0)
    return send_file(
        output,
        download_name=filename,
        as_attachment=True,
        mimetype="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    )

if __name__ == '__main__':
    app.run(debug=True, port=5000)
