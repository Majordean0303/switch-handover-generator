import re
from collections import Counter

class SwitchHandoverParser:
    # --- Internal Metadata ---
    __author__ = "Yash Nalawde"
    __version__ = "1.0.0"
    def __init__(self, log_text, location="Unknown", location_type="Unknown", mgmt_vlan=None):
        # Normalize: Strip ANSI codes, normalize line endings
        cleaned_text = re.sub(r'\x1b\[[0-9;]*m', '', log_text)
        self.log_text = cleaned_text.replace('\r', '').replace('\\', '')

        self.location = location
        self.location_type = location_type
        self.mgmt_vlan = mgmt_vlan
        self.hostname = self._extract_hostname()
        self.mgmt_ip = self._extract_mgmt_ip()
    # --- Hostname Extraction ---
    def _extract_hostname(self):
        match = re.search(r'^hostname\s+(\S+)', self.log_text, re.MULTILINE)
        return match.group(1) if match else "N/A"
    # --- Management IP Logic ---
    def _extract_mgmt_ip(self):
        if self.mgmt_vlan:
            vlan = f"Vlan{self.mgmt_vlan}"
        else:
            vlan_match = re.search(r'(?:snmp-server trap-source|ip radius source-interface)\s+(Vlan\d+)', self.log_text, re.IGNORECASE)
            vlan = vlan_match.group(1) if vlan_match else r'Vlan\d+'

        ip_block_match = re.search(rf'^interface {vlan}\b(.*?)(?:^!|^interface)', self.log_text, re.MULTILINE | re.DOTALL | re.IGNORECASE)
        if ip_block_match:
            ip_match = re.search(r'ip address\s+(\d+\.\d+\.\d+\.\d+)', ip_block_match.group(1))
            if ip_match: return ip_match.group(1)
        return "N/A"
    # --- Interface Name handeling ---
    def _normalize_intf(self, name):
        name = name.lower()
        # 1. Standardize full names
        name = name.replace("twentyfivegige", "twe")
        name = name.replace("tengigabitethernet", "te")
        name = name.replace("gigabitethernet", "gig")
        name = name.replace("fastethernet", "fa")
        name = name.replace("hundredgige", "hu")
        
        # 2. Standardize Cisco CDP abbreviations
        name = name.replace("ten", "te")
        name = name.replace("fas", "fa")
        name = name.replace("hun", "hu")
        
        # 3. Strip all remaining spaces
        return re.sub(r'\s+', '', name)
    # --- Extracting Stack Member details of a switch ---
    def _parse_stack_members(self):
        """
        Extracts MAC, Serial, and Model for each stack member.
        It hunts for the repeating triplet of patterns in show version.
        """
        members = []
        seen_sns = set()
        
        # Pattern looks for the three required fields in the exact order they appear in logs
        pattern = (
            r'Base Ethernet MAC Address\s*:\s*(?P<mac>[\w:]+).*?'
            r'Model Number\s*:\s*(?P<model>\S+).*?'
            r'System Serial Number\s*:\s*(?P<sn>\S+)'
        )
        
        matches = list(re.finditer(pattern, self.log_text, re.DOTALL | re.IGNORECASE))
        for match in matches:
            sn = match.group('sn')
            
            # Deduplication: Prevents duplicate rows if the log was pasted multiple times
            if sn not in seen_sns:
                seen_sns.add(sn)
                members.append({
                    "Stack Member": f"Switch {len(members) + 1}",
                    "Model": match.group('model'),
                    "Sr No.": sn,
                    "MAC Address": match.group('mac')
                })
                
        if (members==[]):
            sn = re.search(r'System Serial Number\s*:\s*(\S+)', self.log_text)
            mac = re.search(r'Base Ethernet MAC Address\s*:\s*([\w:]+)', self.log_text)
            model = re.search(r'Model Number\s*:\s*(\S+)', self.log_text)
            
            # This ensures even standalone switches get an entry in your inventory list
            members.append({
                "Stack Member": "Switch 1",
                "Model": model.group(1) if model else "N/A",
                "Sr No.": sn.group(1) if sn else "N/A",
                "MAC Address": mac.group(1) if mac else "N/A"
            })
        return members
    # --- Interface Count handeling ---
    def _count_all_ports_by_switch(self):
        """
        Calculates physical port counts per switch ID.
        Uses a 'seen_ports' set to ensure unique ports are counted only once, 
        even if the configuration block appears multiple times in the log.
        """
        port_counts = Counter()
        seen_ports = set()
        
        # Regex matches physical interface naming patterns to filter out Vlan/Port-channel
        physical_intf_pattern = re.compile(
            r'^(?:GigabitEthernet|TenGigabitEthernet|FastEthernet|TwentyFiveGigE|HundredGigE|Gig|Te|Fa|Twe|Hu)(\d+)/\d+/\d+', 
            re.IGNORECASE
        )

        for block in re.split(r'^interface ', self.log_text, flags=re.MULTILINE)[1:]:
            lines = block.strip().split('\n')
            name = lines[0].strip()
            
            match = physical_intf_pattern.match(name)
            if match:
                # Use a unique key for the port: "SwitchID/PortID"
                unique_port_key = name
                
                # Check if we have already counted this exact interface
                if unique_port_key not in seen_ports:
                    switch_id = match.group(1)
                    port_counts[switch_id] += 1
                    seen_ports.add(unique_port_key)
                
        return port_counts
    # --- Counts the Number of active Power Supplies ---
    def _count_active_power_supplies(self):
        """
        Counts active power supplies per switch using 'show env all' or 'show inventory' output.
        Matches common Catalyst formats like "1A  PWR-C1-715WAC-P  DCC2834CN56  OK"
        """
        ps_counts = Counter()
        found_any = False
        
        matches = re.finditer(r'^(\d+)[A-Z]\s+(?:PWR|C\dK)\S+\s+\S+\s+(?:OK|Good)', self.log_text, re.MULTILINE | re.IGNORECASE)
        for m in matches:
            ps_counts[m.group(1)] += 1
            found_any = True
            
        # Fallback for environmental sensor format (e.g., "PS1 Vout  1  GOOD")
        if not found_any:
            fallback_matches = re.finditer(r'^\s*PS\d+\s+Vout\s+(\d+)\s+GOOD', self.log_text, re.MULTILINE | re.IGNORECASE)
            for m in fallback_matches:
                ps_counts[m.group(1)] += 1
                
        return ps_counts
    # --- Combining all the details to create a information about a particular Switch ---
    def parse_switch_details(self):
        port_counts = self._count_all_ports_by_switch()
        ps_counts = self._count_active_power_supplies() # NEW: Get PS counts
        
        is_l3 = re.search(r'^ip routing', self.log_text, re.MULTILINE)
        
        base = {
            "Location": self.location, "Location Type": self.location_type,
            "Hostname": self.hostname, "IP Address": self.mgmt_ip,
            "Device Type": "L3 / Core Switch" if is_l3 else "L2 / Access Switch",
            "Make": "Cisco",
            "Firmware Version": re.search(r'Version\s+([A-Za-z0-9\.\(\)]+)', self.log_text).group(1) if re.search(r'Version\s+([A-Za-z0-9\.\(\)]+)', self.log_text) else "N/A",
            "Uptime": re.search(r'uptime is\s+(.*?)(?:\r|\n)', self.log_text).group(1).strip() if re.search(r'uptime is\s+(.*?)(?:\r|\n)', self.log_text) else "N/A",
            "Default Gateway": re.search(r'ip default-gateway (\d+\.\d+\.\d+\.\d+)', self.log_text).group(1) if re.search(r'ip default-gateway (\d+\.\d+\.\d+\.\d+)', self.log_text) else "N/A",
            "NTP Server": ", ".join(re.findall(r'^ntp server (\d+\.\d+\.\d+\.\d+)', self.log_text, re.MULTILINE))
        }
        
        results = []
        stack_members = self._parse_stack_members()
        
        for member in stack_members:
            entry = base.copy()
            entry.update(member)
            
            switch_num = re.search(r'\d+', entry['Stack Member'])
            s_id = switch_num.group(0) if switch_num else "1"
            
            entry["Total Ports"] = port_counts.get(s_id, 0)
            
            # NEW: Assign Active Power Supplies to the specific switch
            ps_count = ps_counts.get(s_id, 0)
            entry["Active Power Supplies"] = ps_count if ps_count > 0 else "0 / Not Found"
            
            results.append(entry)
            
        return results
    
    def extract_cdp_neighbor_ips(self, raw_text):
        """Scans for 'show cdp neighbors detail' and returns a dictionary of IPs"""
        neighbor_map = {}
        
        # Split the text into chunks based on the CDP separator line
        cdp_blocks = re.split(r'-------------------------', raw_text)
        
        for block in cdp_blocks:
            # 1. Grab the neighbor's hostname
            device_match = re.search(r'Device ID:\s*(\S+)', block)
            # 2. Grab the neighbor's IP
            ip_match = re.search(r'IP address:\s*([\d\.]+)', block)
            
            if device_match and ip_match:
                # Strip domain names if present (e.g., SW1.cisco.com -> SW1)
                raw_hostname = device_match.group(1).split('.')[0]
                ip_address = ip_match.group(1)
                
                # Add to our dictionary
                neighbor_map[raw_hostname] = ip_address
                
        return neighbor_map
    #--- Create the Port Mapping ---
    def parse_port_mapping(self):
        ports = {}
        for block in re.split(r'^interface ', self.log_text, flags=re.MULTILINE)[1:]:
            lines = block.strip().split('\n')
            raw = lines[0].strip()
            
            if any(s in raw for s in ("Vlan", "Loopback", "Port-channel", "Null")): continue
            
            desc = ""
            desc_match = re.search(r'^\s*description\s+(.*)', block, re.MULTILINE | re.IGNORECASE)
            if desc_match:
                desc = desc_match.group(1).strip()
                
            vlan_mode = "Access"
            if re.search(r'^\s*switchport mode trunk', block, re.MULTILINE | re.IGNORECASE) or re.search(r'^\s*switchport trunk allowed vlan', block, re.MULTILINE | re.IGNORECASE):
                vlan_mode = "Trunk"
            else:
                vlan_match = re.search(r'^\s*switchport access vlan\s+(\d+)', block, re.MULTILINE | re.IGNORECASE)
                if vlan_match:
                    vlan_mode = f"Access (VLAN {vlan_match.group(1)})"
                elif re.search(r'^\s*no switchport', block, re.MULTILINE | re.IGNORECASE):
                    vlan_mode = "Routed / L3"
            
            clean = raw.replace("TwentyFiveGigE", "Twe ").replace("TenGigabitEthernet", "Te ").replace("GigabitEthernet", "Gig ").replace("FastEthernet", "Fa ").replace("HundredGigE", "Hu ")
            clean = re.sub(r'\s+', ' ', clean).strip()
            
            key = self._normalize_intf(raw)
            
            ports[key] = {
                "Location": self.location, 
                "Location Type": self.location_type,
                "Device Type": "Core" if "ip routing" in self.log_text else "Access",
                "Hostname": self.hostname, 
                "Device IP": self.mgmt_ip,
                "Port No.": clean, 
                "Description": desc,        
                "VLAN / Mode": vlan_mode,   
                "State": "-", 
                "Neighbour Hostname": "-",
                "Neighbour Device IP": "-", 
                "Neighbour Port No.": "-"
            }
        
        for m in re.finditer(r'^([A-Za-z0-9/]+)\s+(?:\S+)\s+(?:\w+)\s+(?:\w+)\s+(up|down|administratively down)', self.log_text, re.MULTILINE):
            k = self._normalize_intf(m.group(1))
            if k in ports: ports[k]["State"] = m.group(2)
        
        cdp_ip_dict = self.extract_cdp_neighbor_ips(self.log_text)
        
        lines = self.log_text.split('\n')
        in_cdp = False; prev = ""
        # Added 'Gi' and 'Tw' and other abrivations for extra safety with varying Cisco output
        port_pat = re.compile(r'\b(Gig|Gi|Te|Ten|Fa|Fas|Twe|Tw|Hu|Hun)\s*\d+/\d+(?:/\d+)?', re.IGNORECASE)
        for line in lines:
            if "Device ID" in line: in_cdp = True; continue
            if in_cdp and ("#" in line or "Total" in line): in_cdp = False; continue
            if in_cdp:
                m = list(port_pat.finditer(line))
                if m:
                    key = self._normalize_intf(m[0].group(0))
                    if key in ports:
                        raw_h = line[:m[0].start()].strip()
                        neigh_host = (raw_h if raw_h else prev).split('.')[0]
                        
                        ports[key]["Neighbour Hostname"] = neigh_host
                        ports[key]["Neighbour Port No."] = m[-1].group(0).strip()
                        
                        # 2. NEW: Map the IP directly from the detail block if we found it
                        if neigh_host in cdp_ip_dict:
                            ports[key]["Neighbour Device IP"] = cdp_ip_dict[neigh_host]
                            
            prev = line.strip()
            
        return [p for p in ports.values() if p["Neighbour Hostname"] != "-"]