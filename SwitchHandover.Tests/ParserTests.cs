using SwitchHandover.Core.Parsers;
using Xunit;

namespace SwitchHandover.Tests;

public class ParserTests
{
    private const string SampleLog = """
        Backup Date: 2026-09-07 18:35:02.893062
        Device IP: 10.30.128.3
        Device Type: Switch
        Protocol: SSH
        ==================================================

        ==============================
        Command: show version
        ==============================
        Cisco IOS XE Software, Version 17.15.04
        Switch Ports Model                     SW Version            Mode
        ------ ----- -----                     ----------            ----
        *    1 28    C9200L-24P-4G             17.15.04              INSTALL
             2 28    C9200L-24P-4G             17.15.04              INSTALL
             3 28    C9200L-24P-4G             17.15.04              INSTALL

        Base Ethernet MAC Address          : ec:dd:24:42:f7:80
        Model Number                       : C9200L-24P-4G
        System Serial Number               : FVH292834YQ

        Base Ethernet MAC Address          : f0:03:bc:be:51:80
        Model Number                       : C9200L-24P-4G
        System Serial Number               : FVH292833SA

        Base Ethernet MAC Address          : f0:03:bc:bf:82:80
        Model Number                       : C9200L-24P-4G
        System Serial Number               : FVH2928352W

        uptime is 2 days, 2 hours, 42 minutes

        ==============================
        Command: show inventory
        ==============================
        NAME: "Switch 1 - GigabitEthernet1/1/1", DESCR: "1000BaseLX SFP"
        PID: GLC-LH-SMD          , VID: V02  , SN: FNS29270XP4

        NAME: "Switch 3 - Gi3/1/1", DESCR: "1000BaseLX SFP"
        PID: GLC-LH-SMD          , VID: V02  , SN: FNS292710M1

        ==============================
        Command: show ip interface brief
        ==============================
        Interface              IP-Address      OK? Method Status                Protocol
        GigabitEthernet1/1/1   unassigned      YES unset  up                    up      
        GigabitEthernet3/1/1   unassigned      YES unset  up                    up      
        GigabitEthernet3/1/2   unassigned      YES unset  down                  down    
        """;

    [Fact]
    public void TestSwitchDetailsParsing()
    {
        var parser = new SwitchHandoverParser(SampleLog, "TestLoc", "DC");
        var details = parser.ParseSwitchDetails();

        Assert.Equal(3, details.Count);
        Assert.Equal("FVH292834YQ", details[0].SrNo);
        Assert.Equal("FVH292833SA", details[1].SrNo);
        Assert.Equal("FVH2928352W", details[2].SrNo);
        Assert.Equal("17.15.04", details[0].FirmwareVersion);
    }

    [Fact]
    public void TestSfpInventoryInUseNormalization()
    {
        var parser = new SwitchHandoverParser(SampleLog, "TestLoc", "DC");
        var sfps = parser.ParseSfpInventory();

        Assert.Equal(2, sfps.Count);
        // Both Gi1/1/1 and Gi3/1/1 have status 'up', so In Use should be 'Yes'
        Assert.Equal("Yes", sfps[0].InUse);
        Assert.Equal("Yes", sfps[1].InUse);
    }

    [Fact]
    public void TestAPHandoverParsing()
    {
        const string apLog = """
            Backup Date: 2026-09-07 18:37:12.375136
            Device IP: 10.30.128.49
            Device Type: AP
            Protocol: SSH
            ==============================
            Command: show run
            ==============================
            AP Name                    : JSW-NGR-CCR-AP5
            ==============================
            Command: show interfaces wired 0
            ==============================
            wired0    Link encap:Ethernet  HWaddr 44:C2:0C:0A:5A:E8  
                      inet addr: 10.30.128.49
            ==============================
            Command: show inventory
            ==============================
            NAME: C9120AX, DESCR: Cisco Catalyst 9120AX Series
            PID: C9120AXI-D , VID: V08, SN: FVT2932L61V
            """;

        var apParser = new APHandoverParser(apLog, "Factory", "Branch");
        var details = apParser.ParseDeviceDetails();

        Assert.Single(details);
        Assert.Equal("JSW-NGR-CCR-AP5", details[0].Hostname);
        Assert.Equal("10.30.128.49", details[0].IpAddress);
        Assert.Equal("44:C2:0C:0A:5A:E8", details[0].MacAddress);
        Assert.Equal("C9120AXI-D", details[0].Model);
        Assert.Equal("FVT2932L61V", details[0].SrNo);
        Assert.Equal("AP", details[0].DeviceType);
    }
}
