using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Forms;

// A per-user bootstrapper. The embedded, pinned ZIP is the same independently
// verified payload used by Install.ps1; this wrapper changes only presentation.
internal static class WindowsVaultSetup
{
    [STAThread]
    private static int Main(string[] args)
    {
        string environment = "production", destination = null;
        bool quiet = false;
        foreach (string arg in args)
        {
            if (arg == "/quiet") quiet = true;
            else if (arg == "/environment:development") environment = "development";
            else if (arg.StartsWith("/destination:", StringComparison.Ordinal)) destination = arg.Substring(13);
            else return 2;
        }
        if (quiet)
        {
            try { Install(environment, destination); return 0; }
            catch { return 1; }
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var form = new Form { Text = "Install Windows Vault", ClientSize = new Size(480, 200),
            StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, Font = new Font("Segoe UI", 10) };
        var title = new Label { Text = "Windows Vault", Font = new Font("Segoe UI", 19, FontStyle.Bold),
            Location = new Point(24, 20), Size = new Size(430, 36) };
        var status = new Label { Text = "Installing for your account…", Location = new Point(24, 68), Size = new Size(430, 56) };
        var progress = new ProgressBar { Style = ProgressBarStyle.Marquee, Location = new Point(24, 132), Size = new Size(430, 8) };
        var open = new Button { Text = "Open Windows Vault", Location = new Point(248, 152), Size = new Size(206, 30), Visible = false };
        form.Controls.AddRange(new Control[] { title, status, progress, open });
        bool busy = true;
        int result = 1;
        form.FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) e.Cancel = true; };
        form.Shown += async delegate
        {
            try
            {
                string installed = await Task.Run(() => Install(environment, destination));
                result = 0;
                status.Text = "Installed. You can also open Windows Vault from the Start menu.";
                open.Click += delegate
                {
                    Process.Start(new ProcessStartInfo {
                        FileName = PowerShell(), UseShellExecute = false, CreateNoWindow = true,
                        Arguments = "-NoProfile -ExecutionPolicy Bypass -File " + Quote(Path.Combine(installed, "Start-WindowsVault.ps1")) });
                    form.Close();
                };
                open.Visible = true;
            }
            catch (Exception ex)
            {
                status.Text = "Installation stopped. " + ex.Message;
                form.ClientSize = new Size(480, 270);
                status.Size = new Size(430, 165);
            }
            finally { busy = false; progress.Visible = false; }
        };
        Application.Run(form);
        return result;
    }

    private static string PowerShell()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
    }

    private static string Quote(string value)
    {
        if (value.IndexOf('"') >= 0) throw new ArgumentException("A path cannot contain a quote.");
        return "\"" + value + "\"";
    }

    private static string Install(string environment, string destination)
    {
        if (!Environment.Is64BitOperatingSystem || Environment.Is64BitProcess == false)
            throw new InvalidOperationException("Use x64 Windows 10 22H2 or Windows 11.");
        destination = Path.GetFullPath(destination ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "AdamanciaVault", environment));
        string temporary = Path.Combine(Path.GetTempPath(), "WindowsVaultSetup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string archive = Path.Combine(temporary, "payload.zip");
            using (var input = Assembly.GetExecutingAssembly().GetManifestResourceStream("VaultPayload"))
            using (var output = File.Create(archive)) { input.CopyTo(output); }
            string expected;
            using (var input = Assembly.GetExecutingAssembly().GetManifestResourceStream("VaultPayloadHash"))
            using (var reader = new StreamReader(input)) { expected = reader.ReadToEnd().Trim(); }
            using (var sha = SHA256.Create())
            using (var input = File.OpenRead(archive))
                if (!String.Equals(BitConverter.ToString(sha.ComputeHash(input)).Replace("-", ""), expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Installer payload integrity check failed.");
            string payload = Path.Combine(temporary, "payload");
            Directory.CreateDirectory(payload);
            using (var zip = ZipFile.OpenRead(archive))
                foreach (var entry in zip.Entries)
                {
                    string path = Path.GetFullPath(Path.Combine(payload, entry.FullName));
                    if (!path.StartsWith(payload + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Invalid installer archive path.");
                    if (entry.Name.Length == 0) Directory.CreateDirectory(path);
                    else { Directory.CreateDirectory(Path.GetDirectoryName(path)); entry.ExtractToFile(path); }
                }
            var start = new ProcessStartInfo {
                FileName = PowerShell(), UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                Arguments = "-NoProfile -ExecutionPolicy Bypass -File " + Quote(Path.Combine(payload, "Install.ps1")) +
                    " -Environment " + environment + " -Destination " + Quote(destination) };
            using (var installer = Process.Start(start))
            {
                var output = installer.StandardOutput.ReadToEndAsync();
                var error = installer.StandardError.ReadToEndAsync();
                installer.WaitForExit();
                Task.WaitAll(output, error);
                if (installer.ExitCode != 0)
                {
                    File.WriteAllText(Path.Combine(temporary, "install.log"), output.Result + error.Result);
                    throw new InvalidOperationException("Close existing Vault apps and connected clients before retrying. Details: " + Path.Combine(temporary, "install.log"));
                }
            }
            Directory.Delete(temporary, true);
            return destination;
        }
        catch { throw; } // Keep this attempt's log available if installation fails.
    }
}
