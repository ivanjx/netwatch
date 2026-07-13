using System.Net;
using System.Text;

namespace NetWatch.Server.Devices;

internal static class DeviceIdentity
{
    public static bool TryNormalizeMacAddress(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var hex = new StringBuilder(12);
        foreach (var character in value)
        {
            if (Uri.IsHexDigit(character))
            {
                hex.Append(char.ToUpperInvariant(character));
            }
            else if (character is not ':' and not '-' and not '.')
            {
                return false;
            }
        }

        if (hex.Length != 12)
        {
            return false;
        }

        normalized = string.Create(17, hex.ToString(), static (span, source) =>
        {
            for (var index = 0; index < 6; index++)
            {
                if (index > 0)
                {
                    span[(index * 3) - 1] = ':';
                }

                span[index * 3] = source[index * 2];
                span[(index * 3) + 1] = source[(index * 2) + 1];
            }
        });
        return true;
    }

    public static bool TryNormalizeIpAddress(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (!IPAddress.TryParse(value, out var address))
        {
            return false;
        }

        normalized = address.ToString();
        return true;
    }

    public static bool TryNormalizeAddressOrSubnet(string? value, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (TryNormalizeIpAddress(value, out var address))
        {
            normalized = address;
            return true;
        }

        if (IPNetwork.TryParse(value, out var network))
        {
            normalized = network.ToString();
            return true;
        }

        return false;
    }
}
