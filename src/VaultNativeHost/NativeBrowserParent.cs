using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace VaultNativeHost;

/// <summary>Native proofs are available only to a signed browser launch, including
/// Chromium's single system cmd.exe intermediary. Process creation times reject
/// recycled parent IDs; the verified signer and signed PE metadata establish identity.</summary>
public static class NativeBrowserParent
{
    public static string? CurrentProgram(bool development)
    {
        try
        {
            var child = ReadProcess((uint)Environment.ProcessId);
            if (child == null) return null;
            var parent = ReadProcess(child.ParentId);
            if (parent == null || parent.Created > child.Created) return null;
            var program = BrowserFile(parent.Path, development);
            if (program != null) return program;
            if (!TrustedCommandFile(parent.Path)) return null;
            var browser = ReadProcess(parent.ParentId);
            return browser != null && browser.Created <= parent.Created ? BrowserFile(browser.Path, development) : null;
        }
        catch { return null; }
    }

    public static string? BrowserFile(string path, bool development)
    {
        var signer = VerifiedSigner(path);
        if (signer == null) return null;
        var version = FileVersionInfo.GetVersionInfo(path);
        return BrowserIdentity(version.OriginalFilename, version.ProductName, signer, development);
    }

    public static string? BrowserIdentity(string? originalName, string? product, string signer, bool development)
    {
        if (string.Equals(originalName, "msedge.exe", StringComparison.OrdinalIgnoreCase) && product == "Microsoft Edge" && signer == "Microsoft Corporation") return "edge";
        if (string.Equals(originalName, "chrome.exe", StringComparison.OrdinalIgnoreCase) && signer is "Google LLC" or "Google Inc" &&
            (product == "Google Chrome" || development && product == "Google Chrome for Testing")) return "chrome";
        return null;
    }

    private static bool TrustedCommandFile(string path) =>
        string.Equals(Path.GetFullPath(path), Path.Combine(Environment.SystemDirectory, "cmd.exe"), StringComparison.OrdinalIgnoreCase) &&
        VerifiedSigner(path) == "Microsoft Windows";

    private sealed record ProcessIdentity(uint ParentId, string Path, long Created);
    private static ProcessIdentity? ReadProcess(uint id)
    {
        if (id == 0) return null;
        using var handle = OpenProcess(0x1000, false, id);
        if (handle.IsInvalid || !GetProcessTimes(handle, out var created, out _, out _, out _)) return null;
        var path = new StringBuilder(32768); var length = (uint)path.Capacity;
        if (!QueryFullProcessImageName(handle, 0, path, ref length)) return null;
        var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == new IntPtr(-1)) return null;
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            if (!Process32First(snapshot, ref entry)) return null;
            do { if (entry.Id == id) return new(entry.ParentId, path.ToString(), created); } while (Process32Next(snapshot, ref entry));
            return null;
        }
        finally { CloseHandle(snapshot); }
    }

    // Read the signer from WinVerifyTrust's verified state, not an independent
    // certificate extraction which might select an unverified signature.
    public static string? VerifiedSigner(string path)
    {
        var file = new TrustFile { Size = (uint)Marshal.SizeOf<TrustFile>(), Path = path };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
        try
        {
            Marshal.StructureToPtr(file, pointer, false);
            return Verify(pointer, 1) ?? CatalogSigner(path, "SHA256") ?? CatalogSigner(path, "SHA1");
        }
        catch { return null; }
        finally { Marshal.DestroyStructure<TrustFile>(pointer); Marshal.FreeHGlobal(pointer); }
    }

    private static string? CatalogSigner(string path, string algorithm)
    {
        IntPtr admin = IntPtr.Zero, catalog = IntPtr.Zero, pointer = IntPtr.Zero, hashPointer = IntPtr.Zero;
        try
        {
            if (!CryptCATAdminAcquireContext2(out admin, IntPtr.Zero, algorithm, IntPtr.Zero, 0)) return null;
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            uint length = 0;
            CryptCATAdminCalcHashFromFileHandle2(admin, file.SafeFileHandle, ref length, null, 0);
            if (length is 0 or > 128) return null;
            var hash = new byte[length];
            if (!CryptCATAdminCalcHashFromFileHandle2(admin, file.SafeFileHandle, ref length, hash, 0)) return null;
            catalog = CryptCATAdminEnumCatalogFromHash(admin, hash, length, 0, IntPtr.Zero);
            if (catalog == IntPtr.Zero) return null;
            var info = new CatalogInfo { Size = (uint)Marshal.SizeOf<CatalogInfo>() };
            if (!CryptCATCatalogInfoFromContext(catalog, ref info, 0)) return null;
            hashPointer = Marshal.AllocHGlobal((int)length); Marshal.Copy(hash, 0, hashPointer, (int)length);
            var trust = new TrustCatalog { Size = (uint)Marshal.SizeOf<TrustCatalog>(), CatalogPath = info.Path, MemberTag = Convert.ToHexString(hash), MemberPath = path, MemberFile = file.SafeFileHandle.DangerousGetHandle(), Hash = hashPointer, HashSize = length, Admin = admin };
            pointer = Marshal.AllocHGlobal(Marshal.SizeOf<TrustCatalog>()); Marshal.StructureToPtr(trust, pointer, false);
            return Verify(pointer, 2);
        }
        catch { return null; }
        finally
        {
            if (pointer != IntPtr.Zero) { Marshal.DestroyStructure<TrustCatalog>(pointer); Marshal.FreeHGlobal(pointer); }
            if (hashPointer != IntPtr.Zero) Marshal.FreeHGlobal(hashPointer);
            if (catalog != IntPtr.Zero) CryptCATAdminReleaseCatalogContext(admin, catalog, 0);
            if (admin != IntPtr.Zero) CryptCATAdminReleaseContext(admin, 0);
        }
    }

    private static string? Verify(IntPtr subject, uint choice)
    {
        var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), UiChoice = 2, UnionChoice = choice, File = subject, StateAction = 1, ProviderFlags = 0x1000 | 0x2000 };
        var action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
        try
        {
            if (WinVerifyTrust(new IntPtr(-1), ref action, ref data) != 0) return null;
            var provider = WTHelperProvDataFromStateData(data.StateData);
            var signerPointer = provider == IntPtr.Zero ? IntPtr.Zero : WTHelperGetProvSignerFromChain(provider, 0, false, 0);
            if (signerPointer == IntPtr.Zero) return null;
            var signer = Marshal.PtrToStructure<ProviderSigner>(signerPointer);
            if (signer.CertificateCount == 0 || signer.Certificates == IntPtr.Zero) return null;
            var certificate = Marshal.PtrToStructure<ProviderCertificate>(signer.Certificates);
            if (certificate.Context == IntPtr.Zero) return null;
            using var x509 = new X509Certificate2(certificate.Context);
            return x509.GetNameInfo(X509NameType.SimpleName, false);
        }
        finally { data.StateAction = 2; WinVerifyTrust(new IntPtr(-1), ref action, ref data); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct TrustFile { public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path; public IntPtr FileHandle, KnownSubject; }
    [StructLayout(LayoutKind.Sequential)] private struct TrustData { public uint Size; public IntPtr PolicyCallback, SipClient; public uint UiChoice, RevocationChecks, UnionChoice; public IntPtr File; public uint StateAction; public IntPtr StateData, UrlReference; public uint ProviderFlags, UiContext; public IntPtr SignatureSettings; }
    [StructLayout(LayoutKind.Sequential)] private struct ProviderSigner { public uint Size, VerifyAsOfLow, VerifyAsOfHigh, CertificateCount; public IntPtr Certificates; }
    [StructLayout(LayoutKind.Sequential)] private struct ProviderCertificate { public uint Size; public IntPtr Context; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ProcessEntry { public uint Size, Usage, Id; public UIntPtr Heap; public uint ModuleId, Threads, ParentId; public int BasePriority; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct CatalogInfo { public uint Size; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Path; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct TrustCatalog { public uint Size, Version; [MarshalAs(UnmanagedType.LPWStr)] public string CatalogPath; [MarshalAs(UnmanagedType.LPWStr)] public string MemberTag; [MarshalAs(UnmanagedType.LPWStr)] public string MemberPath; public IntPtr MemberFile, Hash; public uint HashSize; public IntPtr Context, Admin; }
    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptCATAdminAcquireContext2(out IntPtr admin, IntPtr subsystem, string algorithm, IntPtr policy, uint flags);
    [DllImport("wintrust.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptCATAdminCalcHashFromFileHandle2(IntPtr admin, Microsoft.Win32.SafeHandles.SafeFileHandle file, ref uint hashLength, [Out] byte[]? hash, uint flags);
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern IntPtr CryptCATAdminEnumCatalogFromHash(IntPtr admin, byte[] hash, uint hashLength, uint flags, IntPtr previous);
    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptCATCatalogInfoFromContext(IntPtr catalog, ref CatalogInfo info, uint flags);
    [DllImport("wintrust.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptCATAdminReleaseCatalogContext(IntPtr admin, IntPtr catalog, uint flags);
    [DllImport("wintrust.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptCATAdminReleaseContext(IntPtr admin, uint flags);
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern IntPtr WTHelperProvDataFromStateData(IntPtr state);
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr data, uint signer, [MarshalAs(UnmanagedType.Bool)] bool counterSigner, uint counterSignerIndex);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryFullProcessImageName(Microsoft.Win32.SafeHandles.SafeProcessHandle process, uint flags, StringBuilder path, ref uint length);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern Microsoft.Win32.SafeHandles.SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessTimes(Microsoft.Win32.SafeHandles.SafeProcessHandle process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
}
