using System.Management;
using Microsoft.Win32;

namespace SuperTweaker.Core;

public record WindowsInfo(
    string Caption,
    string Version,
    string Build,
    bool IsWindows11,
    bool IsWindows10,
    bool IsPro,
    string Edition,
    string Architecture,
    string CpuName,
    string MotherboardVendor,
    string MotherboardModel,
    string MotherboardKind,
    string MotherboardVersion,
    string MotherboardSerialNumber,
    string MotherboardPartNumber,
    int CpuPhysicalCores,
    int CpuLogicalCores,
    double CpuMaxClockMhz,
    ulong TotalRamMb,
    string GpuName,
    ulong GpuVramMb,
    string GpuDriverVersion,
    string DiskInfo,
    bool SecureBootEnabled,
    bool VbsEnabled,
    bool TamperProtectionEnabled,
    bool IsElevated
)
{
    public static WindowsInfo Get()
    {
        string caption = "", version = "", build = "", edition = "";
        ulong totalRam = 0;
        string cpuName = "", diskInfo = "";
        string motherboardVendor = "", motherboardModel = "";
        string motherboardKind = "", motherboardVersion = "", motherboardSerial = "", motherboardPart = "";
        int cpuPhysicalCores = 0, cpuLogicalCores = 0;
        double cpuMaxClockMhz = 0;
        string gpuName = "", gpuDriverVersion = "";
        ulong gpuVramMb = 0;
        bool secureBoot = false, vbs = false, tamper = false;

        try
        {
            using var os = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
            foreach (ManagementObject o in os.Get())
            {
                caption  = o["Caption"]?.ToString() ?? "";
                version  = o["Version"]?.ToString() ?? "";
                build    = o["BuildNumber"]?.ToString() ?? "";
                totalRam = Convert.ToUInt64(o["TotalVisibleMemorySize"]) / 1024;
            }

            using var cpu = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
            foreach (ManagementObject o in cpu.Get())
            {
                cpuName = o["Name"]?.ToString() ?? "";
                cpuPhysicalCores += Convert.ToInt32(o["NumberOfCores"] ?? 0);
                cpuLogicalCores  += Convert.ToInt32(o["NumberOfLogicalProcessors"] ?? 0);
                cpuMaxClockMhz   = Math.Max(cpuMaxClockMhz, Convert.ToDouble(o["MaxClockSpeed"] ?? 0));
            }

            TryPopulateMotherboard(
                ref motherboardVendor,
                ref motherboardModel,
                ref motherboardKind,
                ref motherboardVersion,
                ref motherboardSerial,
                ref motherboardPart);

            using var gpu = new ManagementObjectSearcher(
                "SELECT * FROM Win32_VideoController WHERE AdapterRAM IS NOT NULL");
            foreach (ManagementObject o in gpu.Get())
            {
                var name = o["Name"]?.ToString() ?? "";
                var vram = Convert.ToUInt64(o["AdapterRAM"] ?? 0) / (1024UL * 1024);
                var drv  = o["DriverVersion"]?.ToString() ?? "";

                // Pick the GPU with the largest VRAM as primary
                if (vram >= gpuVramMb)
                {
                    gpuName = name;
                    gpuVramMb = vram;
                    gpuDriverVersion = drv;
                }
            }

            using var disk = new ManagementObjectSearcher(
                "SELECT * FROM Win32_LogicalDisk WHERE DriveType=3");
            var disks = new List<string>();
            foreach (ManagementObject o in disk.Get())
            {
                var letter = o["DeviceID"]?.ToString();
                var free   = Convert.ToUInt64(o["FreeSpace"]) / (1024UL * 1024 * 1024);
                var size   = Convert.ToUInt64(o["Size"])      / (1024UL * 1024 * 1024);
                disks.Add($"{letter} {free}GB/{size}GB");
            }
            diskInfo = string.Join("  •  ", disks);
        }
        catch { }

        bool isWin11 = int.TryParse(build, out int b) && b >= 22000;
        bool isWin10 = !isWin11 && b >= 10240;
        bool isPro   = caption.Contains("Pro", StringComparison.OrdinalIgnoreCase);

        edition = Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
            "EditionID", "")?.ToString() ?? "";

        try
        {
            var val = Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\SecureBoot\State",
                "UEFISecureBootEnabled", 0);
            secureBoot = Convert.ToInt32(val) == 1;
        }
        catch { }

        try
        {
            var val = Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\DeviceGuard",
                "EnableVirtualizationBasedSecurity", 0);
            vbs = Convert.ToInt32(val) == 1;
        }
        catch { }

        try
        {
            var val = Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Defender\Features",
                "TamperProtection", 0);
            tamper = Convert.ToInt32(val) == 5;
        }
        catch { }

        bool isElevated = false;
        try
        {
            using var identity  = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal       = new System.Security.Principal.WindowsPrincipal(identity);
            isElevated = principal.IsInRole(
                System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { }

        return new WindowsInfo(
            Caption:              caption,
            Version:              version,
            Build:                build,
            IsWindows11:          isWin11,
            IsWindows10:          isWin10,
            IsPro:                isPro,
            Edition:              edition,
            Architecture:         Environment.Is64BitOperatingSystem ? "x64" : "x86",
            CpuName:              cpuName,
            MotherboardVendor:       motherboardVendor,
            MotherboardModel:        motherboardModel,
            MotherboardKind:         motherboardKind,
            MotherboardVersion:      motherboardVersion,
            MotherboardSerialNumber: motherboardSerial,
            MotherboardPartNumber:   motherboardPart,
            CpuPhysicalCores:     cpuPhysicalCores,
            CpuLogicalCores:      cpuLogicalCores,
            CpuMaxClockMhz:       cpuMaxClockMhz,
            TotalRamMb:           totalRam,
            GpuName:              gpuName,
            GpuVramMb:            gpuVramMb,
            GpuDriverVersion:     gpuDriverVersion,
            DiskInfo:             diskInfo,
            SecureBootEnabled:    secureBoot,
            VbsEnabled:           vbs,
            TamperProtectionEnabled: tamper,
            IsElevated:           isElevated
        );
    }

    /// <summary>
    /// Fills motherboard fields from documented Win32_BaseBoard properties (Model/Product/… — not BoardType,
    /// which is absent from the official schema and can break WQL). "Kind" uses Win32_SystemEnclosure chassis
    /// types, then Win32_ComputerSystem.PCSystemType as fallback.
    /// </summary>
    private static void TryPopulateMotherboard(
        ref string motherboardVendor,
        ref string motherboardModel,
        ref string motherboardKind,
        ref string motherboardVersion,
        ref string motherboardSerial,
        ref string motherboardPart)
    {
        try
        {
            using var board = new ManagementObjectSearcher(
                "SELECT Manufacturer, Model, Product, SerialNumber, Version, PartNumber, Name, HostingBoard " +
                "FROM Win32_BaseBoard");
            ManagementObject? best = null;
            var bestScore = -1;
            foreach (ManagementObject o in board.Get())
            {
                var s = ScoreBaseBoardCandidate(o);
                if (s > bestScore)
                {
                    bestScore = s;
                    best = o;
                }
            }

            if (best != null)
            {
                motherboardVendor = CleanIdent(best["Manufacturer"]?.ToString());
                motherboardModel = PickBoardDisplayName(best);
                motherboardVersion = CleanIdent(best["Version"]?.ToString());
                motherboardSerial = CleanIdent(best["SerialNumber"]?.ToString());
                motherboardPart = CleanIdent(best["PartNumber"]?.ToString());
            }
        }
        catch
        {
            /* ignore — keep defaults */
        }

        motherboardKind = TryResolveChassisKind() ?? TryResolvePcSystemTypeKind() ?? "";
    }

    private static int ScoreBaseBoardCandidate(ManagementObject o)
    {
        var hosting = false;
        try
        {
            if (o["HostingBoard"] != null)
                hosting = Convert.ToBoolean(o["HostingBoard"]);
        }
        catch
        {
            /* ignore */
        }

        var m = $"{o["Manufacturer"]}{o["Model"]}{o["Product"]}";
        var len = m.Length;
        // Prefer the main system board, then the instance with the most identifying text.
        return (hosting ? 1_000_000 : 0) + len;
    }

    /// <summary>
    /// Per Microsoft docs, <c>Model</c> is the element's common name; <c>Product</c> is the manufacturer's part id.
    /// OEMs sometimes fill only one — prefer Model, then Product, then Name (skip generic "Base Board").
    /// </summary>
    private static string PickBoardDisplayName(ManagementObject o)
    {
        var model = CleanIdent(o["Model"]?.ToString());
        var product = CleanIdent(o["Product"]?.ToString());
        var name = CleanIdent(o["Name"]?.ToString());

        if (!string.IsNullOrEmpty(model) && !string.IsNullOrEmpty(product) &&
            !model.Equals(product, StringComparison.OrdinalIgnoreCase))
            return $"{model} · {product}";

        if (!string.IsNullOrEmpty(model))
            return model;
        if (!string.IsNullOrEmpty(product))
            return product;
        if (!string.IsNullOrEmpty(name) &&
            !name.Equals("Base Board", StringComparison.OrdinalIgnoreCase))
            return name;
        return "";
    }

    private static string CleanIdent(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return "";
        var t = s.Trim();
        return t;
    }

    private static string? TryResolveChassisKind()
    {
        try
        {
            using var enc = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure");
            foreach (ManagementObject o in enc.Get())
            {
                var raw = o["ChassisTypes"];
                if (raw == null)
                    continue;
                uint code = 0;
                switch (raw)
                {
                    case ushort u:
                        code = u;
                        break;
                    case short sh:
                        code = (uint)(sh & 0xFFFF);
                        break;
                    case int i:
                        code = (uint)i;
                        break;
                    case uint ui:
                        code = ui;
                        break;
                    case ushort[] ua when ua.Length > 0:
                        code = ua[0];
                        break;
                    case Array arr when arr.Length > 0:
                        code = Convert.ToUInt32(arr.GetValue(0)!);
                        break;
                    default:
                        continue;
                }

                if (code != 0)
                    return SmbiosFormFactorToKind(code);
            }
        }
        catch
        {
            /* ignore */
        }

        return null;
    }

    private static string? TryResolvePcSystemTypeKind()
    {
        try
        {
            using var cs = new ManagementObjectSearcher("SELECT PCSystemType FROM Win32_ComputerSystem");
            foreach (ManagementObject o in cs.Get())
            {
                if (o["PCSystemType"] == null)
                    continue;
                var t = Convert.ToInt32(o["PCSystemType"]);
                return t switch
                {
                    1 => "Desktop",
                    2 => "Mobile",
                    3 => "Workstation",
                    4 => "Enterprise Server",
                    5 => "SOHO Server",
                    6 => "Appliance PC",
                    7 => "Performance Server",
                    8 => "Maximum",
                    _ => null,
                };
            }
        }
        catch
        {
            /* ignore */
        }

        return null;
    }

    /// <summary>DMTF SMBIOS chassis / baseboard form factor codes (1–33).</summary>
    private static string SmbiosFormFactorToKind(uint code)
    {
        if (code == 0)
            return "";

        return code switch
        {
            1  => "Other",
            2  => "Unknown",
            3  => "Desktop",
            4  => "Low Profile Desktop",
            5  => "Pizza Box",
            6  => "Mini Tower",
            7  => "Tower",
            8  => "Portable",
            9  => "Laptop",
            10 => "Notebook",
            11 => "Hand Held",
            12 => "Docking Station",
            13 => "All in One",
            14 => "Sub Notebook",
            15 => "Space-saving",
            16 => "Lunch Box",
            17 => "Main System Chassis",
            18 => "Expansion Chassis",
            19 => "SubChassis",
            20 => "Bus Expansion Chassis",
            21 => "Peripheral Chassis",
            22 => "RAID Chassis",
            23 => "Rack Mount Chassis",
            24 => "Sealed-case PC",
            25 => "Multi-system chassis",
            26 => "Compact PCI",
            27 => "Advanced TCA",
            28 => "Blade",
            29 => "Blade Enclosure",
            30 => "Tablet",
            31 => "Convertible",
            32 => "Detachable",
            33 => "IoT Gateway",
            _  => $"Type {code}",
        };
    }
}
