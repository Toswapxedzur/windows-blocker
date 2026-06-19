using System;
using System.IO;

namespace WindowsBlocker.WebUI;

// Windows equivalent of MacBlockerCore/AppGroup + SharedAppGroupStore. There is
// no App Group container on Windows; state lives under %LOCALAPPDATA%.
public static class Storage
{
    public const string WebStoreFileName = "web-store.json";

    public static string RootDirectory
    {
        get
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(local, "WindowsBlocker");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string WebStorePath => Path.Combine(RootDirectory, WebStoreFileName);

    public static string LocalFilesDirectory
    {
        get
        {
            var dir = Path.Combine(RootDirectory, "LocalFiles");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
