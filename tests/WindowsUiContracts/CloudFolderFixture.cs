using System.Runtime.InteropServices;

/// An owned Cloud Files root and placeholder; no account, provider network or
/// machine-wide registration. Dispose unregisters only this fixture directory.
internal sealed class CloudFolderFixture : IDisposable
{
    private readonly string _root;
    private bool _registered;
    public CloudFolderFixture(string root)
    {
        _root=root;Directory.CreateDirectory(root);
        var registration=new Registration { Size=(uint)Marshal.SizeOf<Registration>(),Name="Vault owned cloud fixture",Version="1",ProviderId=Guid.Parse("0e32238e-8bfa-464b-81b7-eaecaf12ebdc") };
        var policy=new Policies { Size=(uint)Marshal.SizeOf<Policies>(),Hydration=2,Population=3 };
        Marshal.ThrowExceptionForHR(CfRegisterSyncRoot(root,ref registration,ref policy,6));_registered=true;
        var identity=Marshal.AllocHGlobal(1);Marshal.WriteByte(identity,42);
        try
        {
            var path=Path.Combine(root,"cloud.txt");File.WriteAllText(path,"cloud content");
            int result;
            using(var file=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
                result=CfConvertToPlaceholder(file.SafeFileHandle,identity,1,3,out _,IntPtr.Zero);
            Marshal.ThrowExceptionForHR(result);
            File.WriteAllText(Path.Combine(root,"hydrated.txt"),"available cloud content");
            Console.WriteLine($"Cloud fixture conversion={result:X8}, attributes={File.GetAttributes(path)}, tag={ReparseTag:X8}");
        }
        catch {Dispose();throw;}
        finally {Marshal.FreeHGlobal(identity);}
    }
    public void Dispose(){if(_registered){Marshal.ThrowExceptionForHR(CfUnregisterSyncRoot(_root));_registered=false;}}
    public uint ReparseTag
    {
        get
        {
            using var handle=CreateFile(Path.Combine(_root,"cloud.txt"),0x80,7,IntPtr.Zero,3,0x02200000,IntPtr.Zero);
            if(handle.IsInvalid || !GetFileInformationByHandleEx(handle,9,out var info,8))throw new IOException("Cloud fixture tag query failed");
            return info.Tag;
        }
    }
    [StructLayout(LayoutKind.Sequential)]private struct TagInfo {public uint Attributes,Tag;}
    [DllImport("kernel32.dll",EntryPoint="CreateFileW",CharSet=CharSet.Unicode)]private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
    [DllImport("kernel32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetFileInformationByHandleEx(Microsoft.Win32.SafeHandles.SafeFileHandle handle,int infoClass,out TagInfo info,uint length);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct Registration { public uint Size;[MarshalAs(UnmanagedType.LPWStr)]public string Name;[MarshalAs(UnmanagedType.LPWStr)]public string Version;public IntPtr RootIdentity;public uint RootIdentityLength;public IntPtr FileIdentity;public uint FileIdentityLength;public Guid ProviderId; }
    [StructLayout(LayoutKind.Sequential)] private struct Policies {public uint Size;public ushort Hydration,HydrationModifier,Population,PopulationModifier;public uint InSync,HardLink,Management;}
    [DllImport("cldapi.dll",CharSet=CharSet.Unicode)]private static extern int CfRegisterSyncRoot(string root,ref Registration registration,ref Policies policies,uint flags);
    [DllImport("cldapi.dll",CharSet=CharSet.Unicode)]private static extern int CfUnregisterSyncRoot(string root);
    [DllImport("cldapi.dll")]private static extern int CfConvertToPlaceholder(Microsoft.Win32.SafeHandles.SafeFileHandle file,IntPtr identity,uint identityLength,uint flags,out long usn,IntPtr overlapped);
}
