using System.Text.RegularExpressions;

namespace WindowsBlocker.Bridge;

// Exact executable paths or packaged Application User Model IDs. A filename
// never stands for every executable with that name elsewhere on the machine.
public static class WindowsAppId
{
    public static string? Normalize(string value)
    {
        value=value.Trim();
        if (value.Length is 0 or > 32767 || value.Any(char.IsControl)) return null;
        if (Regex.IsMatch(value,@"^[A-Za-z0-9][A-Za-z0-9._-]*![A-Za-z0-9][A-Za-z0-9._-]*$")) return value;
        if (!Path.IsPathFullyQualified(value) || !value.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)) return null;
        try { return Path.GetFullPath(value); } catch { return null; }
    }
}
