using System.Security.Cryptography;
using System.Text.Json;

namespace GhostSlacking.Core;

public sealed record ReleaseInstaller(string File, long Bytes, string Sha256, string Kind, string Scope);

public sealed record SignedReleaseManifest(
    int FormatVersion,
    string Product,
    string Version,
    string Channel,
    bool Prerelease,
    string Architecture,
    ReleaseInstaller Installer)
{
    public static string InstallerName(string version) => $"GhostSlacking-{version}-win-x64-setup.exe";

    public static SignedReleaseManifest VerifyFiles(string manifestPath, string signaturePath, string? publicKey = null)
    {
        var manifest = ReadBounded(manifestPath, 1048576);
        var signature = System.Text.Encoding.UTF8.GetString(ReadBounded(signaturePath, 4096));
        return Verify(manifest, signature, publicKey);
    }

    private static byte[] ReadBounded(string path, int maximum)
    {
        using var file = File.OpenRead(path);
        if (file.Length <= 0 || file.Length > maximum) throw new InvalidDataException("Signed release file exceeds the allowed size.");
        var bytes = new byte[(int)file.Length];
        file.ReadExactly(bytes);
        return bytes;
    }

    public static SignedReleaseManifest Verify(byte[] bytes, string signature, string? publicKey = null)
    {
        if (bytes.Length is 0 or > 1048576 || signature.Length > 4096)
            throw new InvalidDataException("The signed manifest exceeds the allowed size.");
        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(publicKey ?? PublicKey);
            var decoded = Convert.FromBase64String(signature);
            if (key.KeySize != 256 || decoded.Length != 64 ||
                !key.VerifyData(bytes, decoded, HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                throw new InvalidDataException("The release signature is invalid.");
            var manifest = JsonSerializer.Deserialize<SignedReleaseManifest>(bytes,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (manifest is null || manifest.FormatVersion != 2 || manifest.Product != "GhostSlacking" ||
                !ReleaseVersion.TryParse(manifest.Version, out var version) ||
                manifest.Channel != (version.IsPreRelease ? "prerelease" : "stable") ||
                manifest.Prerelease != version.IsPreRelease || manifest.Architecture != "win-x64" ||
                manifest.Installer is null || manifest.Installer.Kind != "nsis" ||
                manifest.Installer.Scope != "currentUser" ||
                manifest.Installer.File != InstallerName(version.Text) ||
                manifest.Installer.Bytes is <= 0 or > 268435456 ||
                manifest.Installer.Sha256 is null || manifest.Installer.Sha256.Length != 64 ||
                !manifest.Installer.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("The release manifest is not supported.");
            return manifest;
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException or ArgumentException)
        {
            throw new InvalidDataException("The release signature or manifest is invalid.", exception);
        }
    }

    public FileStream OpenVerifiedInstaller(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (Path.GetFileName(path) != Installer.File || stream.Length != Installer.Bytes ||
                !Convert.ToHexString(SHA256.HashData(stream)).Equals(Installer.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The cached installer does not match the signed release.");
            stream.Position = 0;
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public static string PublicKey
    {
        get
        {
            using var stream = typeof(SignedReleaseManifest).Assembly.GetManifestResourceStream(
                "GhostSlacking.Core.release-public-key.pem") ?? throw new InvalidOperationException("Release public key is missing.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
