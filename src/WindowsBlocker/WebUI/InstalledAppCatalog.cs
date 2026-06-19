using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace WindowsBlocker.WebUI;

// Enumerates every launchable application via the Windows Shell "AppsFolder"
// (shell:AppsFolder) — the same surface as the Start menu's "All apps" — so the
// picker can list apps that aren't running right now, including UWP/Store apps.
//
// Identity rules (must match how Enforcement/ProcessIdentity resolves a running
// window, so what you pick is what gets blocked):
//   - UWP/Store app  → its Application User Model ID (matched by AUMID).
//   - Win32 desktop  → its target executable path (matched by path/name).
//
// All members here touch single-threaded-apartment COM and must be called on an
// STA thread (AppInventory marshals onto one).
internal static class InstalledAppCatalog
{
    public readonly record struct AppEntry(string Id, string Name, string Icon);

    public static List<AppEntry> EnumerateInstalled()
    {
        var entries = new List<AppEntry>();
        object? appsFolderObj = null;
        try
        {
            var iidShellItem = typeof(IShellItem).GUID;
            SHCreateItemFromParsingName("shell:AppsFolder", IntPtr.Zero, ref iidShellItem, out appsFolderObj);
        }
        catch
        {
            return entries;
        }
        if (appsFolderObj is not IShellItem appsFolder)
        {
            return entries;
        }

        IEnumShellItems? enumerator = null;
        try
        {
            var bhidEnumItems = new Guid("94f60519-2850-4924-aa5a-d15e84868039");
            var iidEnum = typeof(IEnumShellItems).GUID;
            appsFolder.BindToHandler(IntPtr.Zero, ref bhidEnumItems, ref iidEnum, out var ppv);
            if (ppv == IntPtr.Zero)
            {
                return entries;
            }
            enumerator = (IEnumShellItems)Marshal.GetObjectForIUnknown(ppv);
            Marshal.Release(ppv);

            while (enumerator.Next(1, out var item, out var fetched) == 0 && fetched == 1 && item != null)
            {
                try
                {
                    var entry = Describe(item);
                    if (entry.HasValue)
                    {
                        entries.Add(entry.Value);
                    }
                }
                catch
                {
                    // Skip any item that won't describe cleanly.
                }
                finally
                {
                    Marshal.FinalReleaseComObject(item);
                }
            }
        }
        catch
        {
            // Return whatever was gathered before the failure.
        }
        finally
        {
            if (enumerator != null) Marshal.FinalReleaseComObject(enumerator);
            Marshal.FinalReleaseComObject(appsFolder);
        }
        return entries;
    }

    // Describes a concrete executable path (used to fold in currently-running
    // apps that have no AppsFolder entry, e.g. portable exes).
    public static AppEntry? DescribeExecutable(string path, string fallbackName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        object? obj = null;
        try
        {
            var iid = typeof(IShellItem).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out obj);
        }
        catch
        {
            return new AppEntry(path.ToLowerInvariant(), fallbackName, "");
        }
        if (obj is not IShellItem item)
        {
            return new AppEntry(path.ToLowerInvariant(), fallbackName, "");
        }
        try
        {
            var name = GetDisplayName(item, SIGDN_NORMALDISPLAY);
            var icon = GetIconDataUrl(item);
            return new AppEntry(
                path.ToLowerInvariant(),
                string.IsNullOrWhiteSpace(name) ? fallbackName : name,
                icon);
        }
        finally
        {
            Marshal.FinalReleaseComObject(item);
        }
    }

    private static AppEntry? Describe(IShellItem item)
    {
        var name = GetDisplayName(item, SIGDN_NORMALDISPLAY);
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        string aumid = "", packageFamily = "", targetPath = "";
        if (item is IShellItem2 item2)
        {
            aumid = TryGetString(item2, PKEY_AppUserModel_ID);
            packageFamily = TryGetString(item2, PKEY_AppUserModel_PackageFamilyName);
            targetPath = TryGetString(item2, PKEY_Link_TargetParsingPath);
        }

        string id;
        if (!string.IsNullOrEmpty(packageFamily) && !string.IsNullOrEmpty(aumid))
        {
            // Packaged (UWP/Store) app → AUMID is the stable, matchable identity.
            id = aumid.ToLowerInvariant();
        }
        else if (!string.IsNullOrEmpty(targetPath) &&
                 targetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            // Win32 desktop app → its shortcut's target executable path.
            id = targetPath.ToLowerInvariant();
        }
        else
        {
            // No identity the enforcement matcher can recognize; skip rather than
            // list an app that could never actually be blocked.
            return null;
        }

        return new AppEntry(id, name, GetIconDataUrl(item));
    }

    private static string GetDisplayName(IShellItem item, uint sigdn)
    {
        var ptr = IntPtr.Zero;
        try
        {
            item.GetDisplayName(sigdn, out ptr);
            return ptr == IntPtr.Zero ? "" : Marshal.PtrToStringUni(ptr) ?? "";
        }
        catch
        {
            return "";
        }
        finally
        {
            if (ptr != IntPtr.Zero) Marshal.FreeCoTaskMem(ptr);
        }
    }

    private static string TryGetString(IShellItem2 item2, PROPERTYKEY key)
    {
        try
        {
            return item2.GetString(ref key, out var value) == 0 ? value ?? "" : "";
        }
        catch
        {
            return "";
        }
    }

    private static string GetIconDataUrl(IShellItem item)
    {
        if (item is not IShellItemImageFactory factory)
        {
            return "";
        }
        var hBitmap = IntPtr.Zero;
        try
        {
            var size = new SIZE { cx = 48, cy = 48 };
            if (factory.GetImage(size, SIIGBF_ICONONLY, out hBitmap) != 0 || hBitmap == IntPtr.Zero)
            {
                return "";
            }
            var source = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
        }
        catch
        {
            return "";
        }
        finally
        {
            if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
        }
    }

    // ----- Shell COM interop -------------------------------------------------

    private const uint SIGDN_NORMALDISPLAY = 0x00000000;
    private const int SIIGBF_ICONONLY = 0x00000004;

    private static readonly PROPERTYKEY PKEY_AppUserModel_ID =
        new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
    private static readonly PROPERTYKEY PKEY_AppUserModel_PackageFamilyName =
        new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 17);
    private static readonly PROPERTYKEY PKEY_Link_TargetParsingPath =
        new(new Guid("B9B4B3FC-2B51-4A42-B5D8-324146AFCF25"), 2);

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
        public PROPERTYKEY(Guid fmtid, uint pid) { this.fmtid = fmtid; this.pid = pid; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    // IShellItem2 — only GetString is used; the earlier slots are declared as
    // placeholders so the vtable offset of GetString is correct.
    [ComImport, Guid("7e9fb0d3-919f-4307-ab2e-9b1860310c93"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem2
    {
        // IShellItem
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
        // IShellItem2 (placeholders up to GetString)
        [PreserveSig] int GetPropertyStore(uint flags, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetPropertyStoreWithCreateObject(uint flags, IntPtr punkCreateObject, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetPropertyStoreForKeys(IntPtr rgKeys, uint cKeys, uint flags, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetPropertyDescriptionList(ref PROPERTYKEY keyType, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int Update(IntPtr pbc);
        [PreserveSig] int GetProperty(ref PROPERTYKEY key, IntPtr ppropvar);
        [PreserveSig] int GetCLSID(ref PROPERTYKEY key, out Guid pclsid);
        [PreserveSig] int GetFileTime(ref PROPERTYKEY key, out long pft);
        [PreserveSig] int GetInt32(ref PROPERTYKEY key, out int pi);
        [PreserveSig] int GetString(ref PROPERTYKEY key, [MarshalAs(UnmanagedType.LPWStr)] out string ppsz);
    }

    [ComImport, Guid("70629033-e363-4a28-a567-0db78006e6d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumShellItems
    {
        [PreserveSig] int Next(uint celt, out IShellItem? rgelt, out uint pceltFetched);
        [PreserveSig] int Skip(uint celt);
        void Reset();
        void Clone(out IEnumShellItems ppenum);
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
    }
}
