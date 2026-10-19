using System.Text;
using ClosedXML.Excel;
using SwitchHandover.Core.Models;

namespace SwitchHandover.Core.Services;

public static class HandoverExcelExporter
{
    private static readonly string[] SwitchCols =
    [
        "Location", "Location Type", "Hostname", "Stack Member",
        "IP Address", "Device Type", "Make", "Model", "Sr No.",
        "Firmware Version", "Uptime", "Total Ports", "MAC Address",
        "Default Gateway", "NTP Server", "Active Power Supplies"
    ];

    private static readonly string[] PortCols =
    [
        "Location", "Location Type", "Device Type", "Hostname", "Device IP",
        "Port No.", "Description", "State", "Mode", "VLAN ID",
        "Neighbour Hostname", "Neighbour Device IP", "Neighbour Port No."
    ];

    private static readonly string[] SfpCols =
    [
        "Location", "Location Type", "Hostname", "Device IP", "Interface",
        "Description", "PID", "Serial Number", "In Use"
    ];

    public static void ExportExcel(
        string filePath,
        string exportType, // "switches", "ports", "both"
        IEnumerable<SwitchDetails> switchDetails,
        IEnumerable<PortMapping> portMappings,
        IEnumerable<SfpInventory>? sfps = null,
        IEnumerable<PortMapping>? completePorts = null)
    {
        using var workbook = new XLWorkbook();

        if (exportType is "switches" or "both")
        {
            var ws = workbook.Worksheets.Add("Switch Details");
            WriteHeaders(ws, SwitchCols);

            int r = 2;
            foreach (var item in switchDetails)
            {
                ws.Cell(r, 1).SetValue(item.Location);
                ws.Cell(r, 2).SetValue(item.LocationType);
                ws.Cell(r, 3).SetValue(item.Hostname);
                ws.Cell(r, 4).SetValue(item.StackMember);
                ws.Cell(r, 5).SetValue(item.IpAddress);
                ws.Cell(r, 6).SetValue(item.DeviceType);
                ws.Cell(r, 7).SetValue(item.Make);
                ws.Cell(r, 8).SetValue(item.Model);
                ws.Cell(r, 9).SetValue(item.SrNo);
                ws.Cell(r, 10).SetValue(item.FirmwareVersion);
                ws.Cell(r, 11).SetValue(item.Uptime);
                ws.Cell(r, 12).SetValue(item.TotalPorts);
                ws.Cell(r, 13).SetValue(item.MacAddress);
                ws.Cell(r, 14).SetValue(item.DefaultGateway);
                ws.Cell(r, 15).SetValue(item.NtpServer);
                ws.Cell(r, 16).SetValue(item.ActivePowerSupplies);
                r++;
            }
            FormatWorksheet(ws, SwitchCols.Length, r - 1);
        }

        if (exportType is "ports" or "both")
        {
            var ws = workbook.Worksheets.Add("Port Mapping");
            WriteHeaders(ws, PortCols);

            int r = 2;
            foreach (var item in portMappings)
            {
                WritePortRow(ws, r, item);
                r++;
            }
            FormatWorksheet(ws, PortCols.Length, r - 1);
        }

        if (exportType == "both")
        {
            var sfpList = sfps?.ToList();
            if (sfpList != null && sfpList.Count > 0)
            {
                var ws = workbook.Worksheets.Add("SFP Inventory");
                WriteHeaders(ws, SfpCols);

                int r = 2;
                foreach (var item in sfpList)
                {
                    ws.Cell(r, 1).SetValue(item.Location);
                    ws.Cell(r, 2).SetValue(item.LocationType);
                    ws.Cell(r, 3).SetValue(item.Hostname);
                    ws.Cell(r, 4).SetValue(item.DeviceIp);
                    ws.Cell(r, 5).SetValue(item.Interface);
                    ws.Cell(r, 6).SetValue(item.Description);
                    ws.Cell(r, 7).SetValue(item.Pid);
                    ws.Cell(r, 8).SetValue(item.SerialNumber);
                    ws.Cell(r, 9).SetValue(item.InUse);
                    r++;
                }
                FormatWorksheet(ws, SfpCols.Length, r - 1);
            }

            var allPortList = completePorts?.ToList();
            if (allPortList != null && allPortList.Count > 0)
            {
                var ws = workbook.Worksheets.Add("Complete Port Config");
                WriteHeaders(ws, PortCols);

                int r = 2;
                foreach (var item in allPortList)
                {
                    WritePortRow(ws, r, item);
                    r++;
                }
                FormatWorksheet(ws, PortCols.Length, r - 1);
            }
        }

        workbook.SaveAs(filePath);
    }

    public static void ExportCsv(
        string filePath,
        string csvType, // "switches", "ports", "sfp", "allports"
        IEnumerable<SwitchDetails> switchDetails,
        IEnumerable<PortMapping> portMappings,
        IEnumerable<SfpInventory>? sfps = null,
        IEnumerable<PortMapping>? completePorts = null)
    {
        string content = GenerateCsvString(csvType, switchDetails, portMappings, sfps, completePorts);
        File.WriteAllText(filePath, content, Encoding.UTF8);
    }

    public static string GenerateCsvString(
        string csvType,
        IEnumerable<SwitchDetails> switchDetails,
        IEnumerable<PortMapping> portMappings,
        IEnumerable<SfpInventory>? sfps = null,
        IEnumerable<PortMapping>? completePorts = null)
    {
        var sb = new StringBuilder();

        switch (csvType)
        {
            case "switches":
                sb.AppendLine(string.Join(",", SwitchCols));
                foreach (var s in switchDetails)
                {
                    sb.AppendLine(CsvRow(
                        s.Location, s.LocationType, s.Hostname, s.StackMember,
                        s.IpAddress, s.DeviceType, s.Make, s.Model, s.SrNo,
                        s.FirmwareVersion, s.Uptime, s.TotalPorts, s.MacAddress,
                        s.DefaultGateway, s.NtpServer, s.ActivePowerSupplies
                    ));
                }
                break;

            case "ports":
                sb.AppendLine(string.Join(",", PortCols));
                foreach (var p in portMappings)
                {
                    sb.AppendLine(CsvRow(
                        p.Location, p.LocationType, p.DeviceType, p.Hostname, p.DeviceIp,
                        p.PortNo, p.Description, p.State, p.Mode, p.VlanId,
                        p.NeighbourHostname, p.NeighbourDeviceIp, p.NeighbourPortNo
                    ));
                }
                break;

            case "sfp":
                sb.AppendLine(string.Join(",", SfpCols));
                if (sfps != null)
                {
                    foreach (var sfp in sfps)
                    {
                        sb.AppendLine(CsvRow(
                            sfp.Location, sfp.LocationType, sfp.Hostname, sfp.DeviceIp,
                            sfp.Interface, sfp.Description, sfp.Pid, sfp.SerialNumber, sfp.InUse
                        ));
                    }
                }
                break;

            case "allports":
                sb.AppendLine(string.Join(",", PortCols));
                if (completePorts != null)
                {
                    foreach (var p in completePorts)
                    {
                        sb.AppendLine(CsvRow(
                            p.Location, p.LocationType, p.DeviceType, p.Hostname, p.DeviceIp,
                            p.PortNo, p.Description, p.State, p.Mode, p.VlanId,
                            p.NeighbourHostname, p.NeighbourDeviceIp, p.NeighbourPortNo
                        ));
                    }
                }
                break;
        }

        return sb.ToString();
    }

    private static string CsvRow(params string[] values)
    {
        return string.Join(",", values.Select(v => $"\"{v?.Replace("\"", "\"\"") ?? ""}\""));
    }

    private static void WritePortRow(IXLWorksheet ws, int row, PortMapping item)
    {
        ws.Cell(row, 1).SetValue(item.Location);
        ws.Cell(row, 2).SetValue(item.LocationType);
        ws.Cell(row, 3).SetValue(item.DeviceType);
        ws.Cell(row, 4).SetValue(item.Hostname);
        ws.Cell(row, 5).SetValue(item.DeviceIp);
        ws.Cell(row, 6).SetValue(item.PortNo);
        ws.Cell(row, 7).SetValue(item.Description);
        ws.Cell(row, 8).SetValue(item.State);
        ws.Cell(row, 9).SetValue(item.Mode);
        ws.Cell(row, 10).SetValue(item.VlanId);
        ws.Cell(row, 11).SetValue(item.NeighbourHostname);
        ws.Cell(row, 12).SetValue(item.NeighbourDeviceIp);
        ws.Cell(row, 13).SetValue(item.NeighbourPortNo);
    }

    private static void WriteHeaders(IXLWorksheet ws, string[] headers)
    {
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.SetValue(headers[i]);
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0xD3, 0xD3, 0xD3);
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }
    }

    private static void FormatWorksheet(IXLWorksheet ws, int colCount, int lastRow)
    {
        for (int i = 1; i <= colCount; i++)
        {
            ws.Column(i).AdjustToContents(1, Math.Max(lastRow, 1));
            if (ws.Column(i).Width < 15)
                ws.Column(i).Width = 15;
        }
    }
}
