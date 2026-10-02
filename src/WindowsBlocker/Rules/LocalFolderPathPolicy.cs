using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace WindowsBlocker.Rules;

/// Cloud placeholders carry a reparse tag without substituting another path.
/// The Windows name-surrogate bit identifies junction/symlink redirection.
public static class LocalFolderPathPolicy
{
    public static string CanonicalRoot(string path)
    {
        // The user's explicit selection grants the target of this root. Follow
        // it once here; descendants remain subject to the redirect guard.
        using var handle=CreateFile(path,0x80,7,IntPtr.Zero,3,0x02000000,IntPtr.Zero);
        if(handle.IsInvalid)throw new IOException("local-file-error");
        var buffer=new StringBuilder(32768);var length=GetFinalPathNameByHandle(handle,buffer,(uint)buffer.Capacity,0);
        // A volume mounted as a folder may have no drive letter. Its GUID path
        // remains a usable absolute Windows path and preserves the granted root.
        if(length==0 && Marshal.GetLastWin32Error()==3)length=GetFinalPathNameByHandle(handle,buffer,(uint)buffer.Capacity,1);
        if(length==0 || length>=buffer.Capacity)throw new IOException("local-file-error");
        var final=buffer.ToString();
        if(final.StartsWith(@"\\?\UNC\",StringComparison.OrdinalIgnoreCase))final=@"\\"+final[8..];
        else if(final.StartsWith(@"\\?\",StringComparison.Ordinal) && final.Length>=7 && final[5]==':')final=final[4..];
        return Path.GetFullPath(final);
    }
    public static bool RedirectsName(uint reparseTag) => (reparseTag & 0x20000000) != 0;
    public static bool RedirectsPath(string path)
    {
        if (!File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) return false;
        using var handle = CreateFile(path, 0x80, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid || !GetFileInformationByHandleEx(handle, 9, out var info, (uint)Marshal.SizeOf<AttributeTagInfo>())) throw new IOException("local-file-error");
        return RedirectsName(info.ReparseTag);
    }
    [StructLayout(LayoutKind.Sequential)] private struct AttributeTagInfo { public uint Attributes; public uint ReparseTag; }
    [DllImport("kernel32.dll", EntryPoint="CreateFileW", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
    [DllImport("kernel32.dll",EntryPoint="GetFinalPathNameByHandleW",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file,StringBuilder path,uint capacity,uint flags);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle file,int infoClass,out AttributeTagInfo info,uint size);
}
