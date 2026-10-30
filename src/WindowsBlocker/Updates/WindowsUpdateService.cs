using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WindowsBlocker.WebUI;

namespace WindowsBlocker.Updates;

/// <summary>Opt-in app updates. The signed feed authorizes exact Setup.exe bytes;
/// the separate runner waits for a normal quit before replacing any binaries.</summary>
public sealed class WindowsUpdateService
{
    private const string FeedUrl = "https://customblocker.com/downloads/updates/windows/current.json";
    private const string PublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAElKTk9uWlSTRLYdHyLS+1msTVO0D+0bM0QjSW+6l6e3VD9aXgmh9/HBL5SE5hBZmHiIGFl8E0ZOtHJJYpxD1zhw==";
    private const long MaxInstallerBytes = 1_073_741_824;
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromMinutes(10) };
    private readonly string _root;
    private readonly HttpClient _client;
    private readonly string _feedUrl;
    private readonly string _publicKey;
    private readonly Version _installedVersion;
    private readonly bool _allowDevelopment;
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private string? _pendingPath;
    private string? _pendingHash;
    private long _pendingBytes;

    public bool AutoInstall { get; private set; }
    public bool Available => _allowDevelopment || !Storage.Development;
    public bool HasPending => _pendingPath is not null;

    public WindowsUpdateService() : this(Client, FeedUrl, PublicKey,
        Path.Combine(Storage.RootDirectory, "Updates"),
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0), false) { }

    internal WindowsUpdateService(HttpClient client, string feedUrl, string publicKey, string root,
        Version installedVersion, bool allowDevelopment)
    {
        _client = client;
        _feedUrl = feedUrl;
        _publicKey = publicKey;
        _root = root;
        _installedVersion = installedVersion;
        _allowDevelopment = allowDevelopment;
        Directory.CreateDirectory(_root);
        foreach (var old in Directory.GetFiles(_root, "pending-*.exe"))
            if (File.GetLastWriteTimeUtc(old) < DateTime.UtcNow.AddDays(-1)) File.Delete(old);
        try
        {
            var saved = JsonSerializer.Deserialize<UpdatePreference>(File.ReadAllText(Path.Combine(_root, "preference.json")));
            AutoInstall = saved?.AutoInstall == true;
        }
        catch (IOException) { AutoInstall = false; }
        catch (JsonException) { AutoInstall = false; }
    }

    public void SetAutoInstall(bool enabled)
    {
        if (!Available) throw new InvalidOperationException("Development builds cannot install production updates.");
        var path = Path.Combine(_root, "preference.json");
        var stage = path + ".staging-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(stage, JsonSerializer.Serialize(new UpdatePreference(enabled)));
        File.Move(stage, path, true);
        AutoInstall = enabled;
        if (!enabled) ClearPending();
    }

    public void DiscardPending() => ClearPending();

    public async Task<string> CheckAndStageAsync(CancellationToken cancellation = default)
    {
        if (!Available) return "Production updates are unavailable in Development.";
        await _checkGate.WaitAsync(cancellation);
        try { return await CheckAndStageCoreAsync(cancellation); }
        finally { _checkGate.Release(); }
    }

    private async Task<string> CheckAndStageCoreAsync(CancellationToken cancellation)
    {
        using var feedResponse = await _client.GetAsync(_feedUrl, HttpCompletionOption.ResponseHeadersRead, cancellation);
        feedResponse.EnsureSuccessStatusCode();
        using var signatureResponse = await _client.GetAsync(_feedUrl + ".sig", HttpCompletionOption.ResponseHeadersRead, cancellation);
        signatureResponse.EnsureSuccessStatusCode();
        var feed = await ReadLimitedAsync(feedResponse.Content, 16_384, cancellation);
        var signature = await ReadLimitedAsync(signatureResponse.Content, 256, cancellation);
        var item = VerifyFeed(feed, signature, _publicKey);
        var installed = _installedVersion;
        var offered = Version.Parse(item.Version!);
        if (offered <= installed) return "Windows Vault is up to date.";
        if (_pendingPath is not null && File.Exists(_pendingPath) && _pendingHash == item.Sha256)
            return $"Windows Vault {item.Version} is ready to install after quit.";

        ClearPending();
        var path = Path.Combine(_root, "pending-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            using var response = await _client.GetAsync(item.Url!, HttpCompletionOption.ResponseHeadersRead, cancellation);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != item.Bytes)
                throw new InvalidDataException("Update download length differs from signed metadata.");
            await using var source = await response.Content.ReadAsStreamAsync(cancellation);
            await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[1024 * 1024];
            long count = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellation)) != 0)
            {
                count += read;
                if (count > item.Bytes) throw new InvalidDataException("Update download exceeds signed size.");
                hash.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), cancellation);
            }
            await target.FlushAsync(cancellation);
            if (count != item.Bytes || Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() != item.Sha256)
                throw new InvalidDataException("Update download differs from signed metadata.");
            await target.DisposeAsync();
            // Preserve internet origin so Windows can apply its normal SmartScreen policy.
            File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=" + item.Url + "\r\n");
            _pendingPath = path;
            _pendingHash = item.Sha256;
            _pendingBytes = item.Bytes;
            return $"Windows Vault {item.Version} is ready to install after quit.";
        }
        catch
        {
            try { File.Delete(path); } catch (IOException) { }
            throw;
        }
    }

    public void LaunchPendingAfterQuit(int currentPid)
    {
        if (_pendingPath is null || _pendingHash is null) return;
        var path = _pendingPath;
        var encodedPath = Convert.ToBase64String(Encoding.UTF8.GetBytes(path));
        var log = Convert.ToBase64String(Encoding.UTF8.GetBytes(Path.Combine(_root, "last-install-error.txt")));
        var script = $@"
$ErrorActionPreference='Stop'
$p=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{encodedPath}'))
$log=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{log}'))
try {{
  Wait-Process -Id {currentPid} -ErrorAction SilentlyContinue
  if ((Get-Item -LiteralPath $p).Length -ne {_pendingBytes}) {{ throw 'Update size changed after verification.' }}
  if ((Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant() -ne '{_pendingHash}') {{ throw 'Update hash changed after verification.' }}
  $run=Start-Process -FilePath $p -ArgumentList '/quiet' -PassThru -Wait
  if ($run.ExitCode -ne 0) {{ throw ('Windows setup returned '+$run.ExitCode) }}
  Remove-Item -LiteralPath $p -Force
  Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
}} catch {{ [IO.File]::WriteAllText($log,[string]$_); exit 1 }}
";
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var runner = Process.Start(start) ?? throw new InvalidOperationException("Could not launch the update installer.");
        _pendingPath = null;
    }

    private void ClearPending()
    {
        if (_pendingPath is not null) File.Delete(_pendingPath);
        _pendingPath = null;
        _pendingHash = null;
        _pendingBytes = 0;
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, int limit, CancellationToken cancellation)
    {
        if (content.Headers.ContentLength > limit) throw new InvalidDataException("Update metadata is too large.");
        await using var source = await content.ReadAsStreamAsync(cancellation);
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = await source.ReadAsync(buffer, cancellation)) != 0)
        {
            if (output.Length + read > limit) throw new InvalidDataException("Update metadata is too large.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    internal static UpdateItem VerifyFeed(byte[] feed, byte[] signature, string publicKey)
    {
        if (feed.Length is 0 or > 16_384 || signature.Length is < 64 or > 256)
            throw new InvalidDataException("Update feed size is invalid.");
        using var verifier = ECDsa.Create();
        verifier.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
        if (!verifier.VerifyData(feed, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
            throw new CryptographicException("Update feed signature is invalid.");
        var item = JsonSerializer.Deserialize<UpdateItem>(feed, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Update feed is empty.");
        Validate(item);
        return item;
    }

    private static void Validate(UpdateItem item)
    {
        var now = DateTimeOffset.UtcNow;
        if (!DateTimeOffset.TryParse(item.IssuedAt, out var issued) ||
            !DateTimeOffset.TryParse(item.ExpiresAt, out var expires) ||
            issued > now.AddHours(1) || expires <= now || expires > issued.AddDays(90))
            throw new InvalidDataException("Update metadata is expired or has an invalid date.");
        if (item.Schema != 1 || item.Product != "windowsVault" || item.Channel != "production" || item.Architecture != "x64" ||
            !Regex.IsMatch(item.Version ?? "", @"^\d+\.\d+\.\d+$") ||
            !Regex.IsMatch(item.Sha256 ?? "", "^[0-9a-f]{64}$") ||
            !Regex.IsMatch(item.SourceCommit ?? "", "^[0-9a-f]{40}$") ||
            item.Bytes is <= 0 or > MaxInstallerBytes ||
            !Uri.TryCreate(item.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.Host != "customblocker.com" || uri.Port != 443 ||
            !uri.AbsolutePath.StartsWith("/downloads/installers/install-v", StringComparison.Ordinal) ||
            !uri.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidDataException("Update metadata is not for this Windows Vault installation.");
    }

    private sealed record UpdatePreference(bool AutoInstall);
    internal sealed record UpdateItem(int Schema, string? Product, string? Channel, string? Architecture,
        string? Version, string? SourceCommit, string? Url, long Bytes, string? Sha256,
        string? IssuedAt, string? ExpiresAt);
}
