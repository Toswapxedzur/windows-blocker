using System.Security.Cryptography;
using System.Text.Json;
using System.Net;
using System.Net.Http;
using WindowsBlocker.Updates;

using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
var key = Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo());
var now = DateTimeOffset.UtcNow;
var feed = new Dictionary<string, object>
{
    ["schema"] = 1,
    ["product"] = "windowsVault",
    ["channel"] = "production",
    ["architecture"] = "x64",
    ["version"] = "0.3.1",
    ["sourceCommit"] = new string('a', 40),
    ["url"] = "https://customblocker.com/downloads/installers/install-v0.3.1-aaaaaaa/Windows-Vault-0.3.1-win-x64-Setup.exe",
    ["bytes"] = 128,
    ["sha256"] = new string('b', 64),
    ["issuedAt"] = now.ToString("O"),
    ["expiresAt"] = now.AddDays(60).ToString("O")
};

void Check(Dictionary<string, object> item, bool valid)
{
    var bytes = JsonSerializer.SerializeToUtf8Bytes(item);
    var signature = signer.SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
    try
    {
        WindowsUpdateService.VerifyFeed(bytes, signature, key);
        if (!valid) throw new Exception("Invalid update feed was accepted.");
    }
    catch (Exception error) when (!valid && error is not ExceptionWithFailedAssertion)
    {
        if (error.Message == "Invalid update feed was accepted.") throw new ExceptionWithFailedAssertion();
    }
}

Check(feed, true);
var bytes = JsonSerializer.SerializeToUtf8Bytes(feed);
var signed = signer.SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
bytes[^2] ^= 1;
try { WindowsUpdateService.VerifyFeed(bytes, signed, key); throw new ExceptionWithFailedAssertion(); }
catch (CryptographicException) { }
Check(new(feed) { ["expiresAt"] = now.AddDays(-1).ToString("O") }, false);
Check(new(feed) { ["product"] = "otherVault" }, false);
Check(new(feed) { ["url"] = "https://elsewhere.example/install.exe" }, false);
Check(new(feed) { ["version"] = "0.3.1.1" }, false);
var payload = Enumerable.Range(0, 128).Select(value => (byte)value).ToArray();
feed["sha256"] = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
var actualFeed = JsonSerializer.SerializeToUtf8Bytes(feed);
var actualSignature = signer.SignData(actualFeed, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
var feedUrl = "https://customblocker.com/downloads/updates/windows/current.json";
var fixtureRoot = Path.Combine(Path.GetTempPath(), "vault-updater-fixture-" + Guid.NewGuid().ToString("N"));
try
{
    using var client = new HttpClient(new FixtureHandler(uri => uri.AbsoluteUri switch
    {
        var url when url == feedUrl => actualFeed,
        var url when url == feedUrl + ".sig" => actualSignature,
        _ => payload
    }));
    var update = new WindowsUpdateService(client, feedUrl, key, fixtureRoot, new Version(0, 3, 0), true);
    if (update.AutoInstall) throw new ExceptionWithFailedAssertion();
    update.SetAutoInstall(true);
    if (!update.AutoInstall) throw new ExceptionWithFailedAssertion();
    await update.CheckAndStageAsync();
    var staged = Directory.GetFiles(fixtureRoot, "pending-*.exe");
    if (!update.HasPending || staged.Length != 1 || !File.ReadAllBytes(staged[0]).SequenceEqual(payload))
        throw new ExceptionWithFailedAssertion();
    update.DiscardPending();
    if (update.HasPending || Directory.GetFiles(fixtureRoot, "pending-*.exe").Length != 0)
        throw new ExceptionWithFailedAssertion();
    update.SetAutoInstall(false);
    if (new WindowsUpdateService(client, feedUrl, key, fixtureRoot, new Version(0, 3, 0), true).AutoInstall)
        throw new ExceptionWithFailedAssertion();
    Console.WriteLine("PASS verified staging, pending cleanup, and off-by-default preference");

    using var corruptClient = new HttpClient(new FixtureHandler(uri => uri.AbsoluteUri switch
    {
        var url when url == feedUrl => actualFeed,
        var url when url == feedUrl + ".sig" => actualSignature,
        _ => new byte[128]
    }));
    var corrupt = new WindowsUpdateService(corruptClient, feedUrl, key, fixtureRoot, new Version(0, 3, 0), true);
    try { await corrupt.CheckAndStageAsync(); throw new ExceptionWithFailedAssertion(); }
    catch (InvalidDataException) { }
    if (Directory.GetFiles(fixtureRoot, "pending-*.exe").Length != 0) throw new ExceptionWithFailedAssertion();
    Console.WriteLine("PASS corrupt installer rejected without staging");
}
finally { Directory.Delete(fixtureRoot, true); }
Console.WriteLine("PASS signed Windows update feed, tamper, expiry, product, URL, version");

sealed class ExceptionWithFailedAssertion : Exception { }

sealed class FixtureHandler(Func<Uri, byte[]> route) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(route(request.RequestUri!)) });
}
