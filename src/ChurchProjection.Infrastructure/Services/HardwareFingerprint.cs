using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace ChurchProjection.Infrastructure.Services;

/// <summary>
/// Derives a stable, machine-specific fingerprint used to bind a seat to a physical device.
///
/// The goal is anti-abuse, not DRM: it must be stable across app reinstalls on the same machine
/// (so a legitimate reinstall reuses its seat) yet differ on a different machine (so copying the
/// app folder — including the saved token — onto another computer does not yield a free seat).
///
/// We combine the machine name with the most stable physical NIC's MAC address and hash the
/// result, so the raw identifiers never leave the device. If hardware enumeration fails we fall
/// back to the machine name alone; the seat simply rebinds on next sign-in.
/// </summary>
public static class HardwareFingerprint
{
    private static string? _cached;

    public static string Get()
    {
        if (_cached is not null) return _cached;

        var material = new StringBuilder();
        material.Append(Environment.MachineName);

        try
        {
            var mac = BestPhysicalMac();
            if (!string.IsNullOrEmpty(mac))
                material.Append('|').Append(mac);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Hardware MAC enumeration failed; using machine name only for fingerprint");
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(material.ToString()));
        _cached = Convert.ToHexString(bytes);
        return _cached;
    }

    // Pick a deterministic physical adapter: real (non-virtual) NICs with a usable MAC, choosing the
    // lexicographically smallest address so adapter reordering or VPN/virtual adapters don't change it.
    private static string? BestPhysicalMac()
    {
        string? best = null;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            var addr = nic.GetPhysicalAddress().ToString();
            if (string.IsNullOrWhiteSpace(addr) || addr.Length < 12)
                continue;

            // Skip obviously virtual/placeholder all-zero addresses.
            if (addr.All(c => c == '0'))
                continue;

            if (best is null || string.CompareOrdinal(addr, best) < 0)
                best = addr;
        }

        return best;
    }
}
