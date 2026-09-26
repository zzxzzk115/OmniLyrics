using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using OmniLyrics.Core.Configuration;

namespace OmniLyrics.Core.Network;

public sealed record LanPeer(string Id, string Name, string Host, int Port, string Fingerprint, string Token, bool AllowControl)
{
    public override string ToString() => $"{Name} · {Host}:{Port}";
}
public sealed record LanGrant(string Id, string Name, string TokenHash, bool AllowControl);
public sealed record PairingInvitation(int Version, string Id, string Name, string Host, int Port, string Fingerprint, string Secret);
public sealed record PairingRequest(string Secret, string Name);
public sealed record PairingResult(string DeviceId, string Name, string Token, bool AllowControl);

/// <summary>Private installation identity shared by the elected host and all local frontends.</summary>
public sealed class LanTrustStore
{
    private sealed record Invite(string Hash, DateTimeOffset Expires, bool AllowControl);
    private sealed class Document
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Certificate { get; set; } = "";
        public Invite? Invitation { get; set; }
        public List<LanGrant> Grants { get; set; } = [];
        public List<LanPeer> Peers { get; set; } = [];
    }
    private readonly string _directory;
    public LanTrustStore(string? directory = null) => _directory = directory ?? UserConfiguration.DirectoryPath;
    private string FilePath => Path.Combine(_directory, "lan-trust.json");
    private T Access<T>(Func<Document, T> action, bool write = false)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(_directory);
        else Directory.CreateDirectory(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        // FileShare.None serializes GUI/CLI/TUI updates across processes, including initial identity creation.
        using var lease = AcquireLock();
        var exists = File.Exists(FilePath);
        var document = exists ? JsonSerializer.Deserialize<Document>(File.ReadAllText(FilePath))
            ?? throw new InvalidDataException("Invalid LAN identity.") : new Document();
        var result = action(document);
        if (write || !exists)
        {
            var temporary = Path.Combine(_directory, ".lan-" + Guid.NewGuid().ToString("N"));
            try
            {
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using (var stream = new FileStream(temporary, options)) JsonSerializer.Serialize(stream, document);
                File.Move(temporary, FilePath, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return result;
    }
    private FileStream AcquireLock()
    {
        var until = Environment.TickCount64 + 3000;
        while (true)
        {
            try { return new FileStream(Path.Combine(_directory, "lan-trust.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (Environment.TickCount64 < until) { Thread.Sleep(20); }
        }
    }
    public string DeviceId => Access(d => d.Id);
    public X509Certificate2 Certificate() => Access(d =>
    {
        if (d.Certificate.Length == 0)
        {
            using var key = RSA.Create(3072);
            var request = new CertificateRequest("CN=OmniLyrics", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(5));
            d.Certificate = Convert.ToBase64String(certificate.Export(X509ContentType.Pfx));
        }
        return X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(d.Certificate), null, X509KeyStorageFlags.EphemeralKeySet);
    }, true);
    public static string Fingerprint(X509Certificate2 certificate) => certificate.GetCertHashString(HashAlgorithmName.SHA256);
    public static string Name(string value) => string.IsNullOrWhiteSpace(value) ? Environment.MachineName : value.Trim();
    private static string Secret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool ValidSecret(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    public string CreateInvitation(string host, LanSettings settings, bool allowControl = false)
    {
        if (!settings.Enabled) throw new InvalidOperationException(Localization.Get("LanEnableFirst"));
        LanAddress.Validate(host);
        using var certificate = Certificate();
        var secret = Secret();
        return Access(d =>
        {
            d.Invitation = new(Hash(secret), DateTimeOffset.UtcNow.AddMinutes(5), allowControl);
            var invitation = new PairingInvitation(1, d.Id, Name(settings.DeviceName), host, settings.HttpsPort, Fingerprint(certificate), secret);
            // Fragment stays out of HTTP requests, browser logs and referrers. This is an out-of-band invitation, not a web URL.
            return "omnilyrics://pair#" + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(invitation)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }, true);
    }
    public static PairingInvitation ParseInvitation(string text)
    {
        const string prefix = "omnilyrics://pair#";
        if (text.Length > 4096 || !text.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("Invalid pairing invitation.");
        var value = text[prefix.Length..].Replace('-', '+').Replace('_', '/');
        value = value.PadRight((value.Length + 3) / 4 * 4, '=');
        var invite = JsonSerializer.Deserialize<PairingInvitation>(Convert.FromBase64String(value));
        if (invite is not { Version: 1 } || !Guid.TryParseExact(invite.Id, "N", out _) || !ValidSecret(invite.Secret)
            || !ValidSecret(invite.Fingerprint) || invite.Port is < 1024 or > 65535 || invite.Name is not { Length: > 0 and <= 64 } || invite.Name.Any(char.IsControl))
            throw new InvalidDataException("Invalid pairing invitation.");
        LanAddress.Validate(invite.Host);
        return invite;
    }
    public void CancelInvitation() => Access(d => { d.Invitation = null; return true; }, true);
    public PairingResult? Redeem(PairingRequest request, string localName) => Access(d =>
    {
        if (!ValidSecret(request.Secret) || string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 64
            || request.Name.Any(char.IsControl) || d.Grants.Count >= 32 || d.Invitation is not { } invite
            || invite.Expires <= DateTimeOffset.UtcNow || !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(invite.Hash), Convert.FromHexString(Hash(request.Secret)))) return null;
        var token = Secret();
        d.Grants.Add(new(Guid.NewGuid().ToString("N"), request.Name.Trim(), Hash(token), invite.AllowControl));
        d.Invitation = null; // Single use, consumed under the same cross-process lock as granting access.
        return new PairingResult(d.Id, localName, token, invite.AllowControl);
    }, true);
    public LanGrant? Authorize(string token) => !ValidSecret(token) ? null : Access(d =>
        d.Grants.FirstOrDefault(g => CryptographicOperations.FixedTimeEquals(Convert.FromHexString(g.TokenHash), Convert.FromHexString(Hash(token)))));
    public IReadOnlyList<LanGrant> Grants() => Access(d => d.Grants.ToArray());
    public IReadOnlyList<LanPeer> Peers() => Access(d => d.Peers.ToArray());
    public void Revoke(string id) => Access(d => d.Grants.RemoveAll(p => p.Id == id), true);
    public void Forget(string id) => Access(d => d.Peers.RemoveAll(p => p.Id == id), true);
    public void SavePeer(LanPeer peer) => Access(d =>
    {
        d.Peers.RemoveAll(p => p.Id == peer.Id);
        if (d.Peers.Count >= 32) throw new InvalidOperationException("Too many paired devices.");
        d.Peers.Add(peer); return true;
    }, true);
}

public static class LanAddress
{
    public static bool IsPrivate(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        var b = address.GetAddressBytes();
        return b.Length == 4 ? b[0] == 10 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168
            || b[0] == 169 && b[1] == 254 : address.IsIPv6LinkLocal || (b[0] & 0xfe) == 0xfc;
    }
    public static IPAddress Validate(string host) => IPAddress.TryParse(host, out var address) && IsPrivate(address)
        ? address : throw new ArgumentException("Use a private LAN IP address.");
    public static string[] LocalAddresses() => System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address)
        .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(a) && IsPrivate(a))
        .Select(a => a.ToString()).Distinct().ToArray();
}
