using System;
using System.Diagnostics;

namespace RetryTests;

/// <summary>
/// Blocks and allows outbound Azure SQL traffic on this Windows machine with a named Windows
/// Firewall rule. Only SQL is blocked; DNS and the managed identity token endpoint keep working.
/// Needs an elevated (administrator) process.
/// </summary>
internal static class SqlPortFirewall
{
    public const string RuleName = "RetryTest-BlockAzureSql";

    // 1433 is the gateway port. Inside Azure the default Redirect policy then moves the connection
    // to a port in 11000-11999, so both are blocked.
    public static void Block() =>
        RunPowerShell($"New-NetFirewallRule -DisplayName '{RuleName}' -Direction Outbound " +
            "-Protocol TCP -RemotePort 1433,11000-11999 -Action Block");

    // Safe to run repeatedly: does nothing when the rule is already gone.
    public static void Allow() =>
        RunPowerShell($"Remove-NetFirewallRule -DisplayName '{RuleName}' -ErrorAction SilentlyContinue");

    private static void RunPowerShell(string script)
    {
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        // Without Stop, a failed command (e.g. not running as admin) still exits with 0.
        startInfo.ArgumentList.Add("$ErrorActionPreference = 'Stop'; " + script);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start powershell.exe.");
        var errorOutput = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"PowerShell exited with {process.ExitCode} running '{script}': {errorOutput}");
        }
    }
}
