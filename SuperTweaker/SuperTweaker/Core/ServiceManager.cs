using System.ServiceProcess;

namespace SuperTweaker.Core;

public static class ServiceManager
{
    public static ServiceStartMode? GetStartMode(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            return sc.StartType;
        }
        catch { return null; }
    }

    public static ServiceControllerStatus? GetStatus(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            return sc.Status;
        }
        catch { return null; }
    }

    public static bool SetStartMode(string serviceName, string mode, Logger? log = null)
    {
        try
        {
            // sc.exe is reliable across all builds
            var result = RunSc($"config {serviceName} start= {mode}");
            log?.Info($"Service [{serviceName}] start mode → {mode}  ({result})");
            return true;
        }
        catch (Exception ex)
        {
            log?.Error($"Failed to set service [{serviceName}]: {ex.Message}");
            return false;
        }
    }

    public static bool Stop(string serviceName, Logger? log = null)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            if (sc.Status == ServiceControllerStatus.Running)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                log?.Info($"Service [{serviceName}] stopped.");
            }
            return true;
        }
        catch (Exception ex)
        {
            log?.Warn($"Could not stop [{serviceName}]: {ex.Message}");
            return false;
        }
    }

    public static bool Start(string serviceName, Logger? log = null)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            if (sc.Status != ServiceControllerStatus.Running)
            {
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                log?.Info($"Service [{serviceName}] started.");
            }
            return true;
        }
        catch (Exception ex)
        {
            log?.Warn($"Could not start [{serviceName}]: {ex.Message}");
            return false;
        }
    }

    private static string RunSc(string args)
    {
        using var p = new System.Diagnostics.Process();
        p.StartInfo = new System.Diagnostics.ProcessStartInfo("sc.exe", args)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        p.Start();
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(5000);
        return output.Trim();
    }
}
