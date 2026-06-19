using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WindowsBlocker.Enforcement;

// The live set of application identities that should be blocked right now,
// recomputed each tick from the active block groups. Matching tolerates the
// three shapes the app picker can produce: a full executable path, a bare
// process name (with or without ".exe"), or — for UWP/Store apps — an
// Application User Model ID (recognized by its "!" separator).
public sealed class BlockedAppRegistry
{
    private readonly object _gate = new();
    private HashSet<string> _paths = new();
    private HashSet<string> _names = new();
    private HashSet<string> _aumids = new();

    public void Update(IEnumerable<string> identities)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var aumids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in identities)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }
            var value = raw.Trim().ToLowerInvariant();
            // AUMIDs look like "PackageFamily_hash!AppId" — the "!" never appears
            // in a path or exe name, so it unambiguously tags a packaged app.
            if (value.Contains('!'))
            {
                aumids.Add(value);
            }
            else if (value.Contains('\\') || value.Contains('/'))
            {
                paths.Add(value);
                names.Add(Path.GetFileName(value));
            }
            else
            {
                names.Add(value.EndsWith(".exe") ? value : value + ".exe");
            }
        }
        lock (_gate)
        {
            _paths = paths;
            _names = names;
            _aumids = aumids;
        }
    }

    public bool HasAny
    {
        get
        {
            lock (_gate)
            {
                return _paths.Count > 0 || _names.Count > 0 || _aumids.Count > 0;
            }
        }
    }

    public bool IsBlocked(AppIdentity identity)
    {
        if (identity.IsEmpty)
        {
            return false;
        }
        lock (_gate)
        {
            if (!string.IsNullOrEmpty(identity.ExecutablePath) && _paths.Contains(identity.ExecutablePath))
            {
                return true;
            }
            if (!string.IsNullOrEmpty(identity.ExecutableName) && _names.Contains(identity.ExecutableName))
            {
                return true;
            }
            return !string.IsNullOrEmpty(identity.Aumid) && _aumids.Contains(identity.Aumid);
        }
    }
}
