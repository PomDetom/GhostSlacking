using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GhostSlacking.Core.Tests;

public sealed class SignedReleaseManifestTests
{
    [Theory]
    [InlineData("1.2.0-beta.2")]
    [InlineData("1.2.0-rc.1")]
    [InlineData("1.2.0")]
    public void Signed_manifest_accepts_full_release_versions(string version)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var bytes = Manifest(version);
        var signature = Sign(key, bytes);
        Assert.Equal(version, SignedReleaseManifest.Verify(bytes, signature, key.ExportSubjectPublicKeyInfoPem()).Version);
    }

    [Fact]
    public void Manifest_tampering_and_wrong_keys_are_rejected()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var wrong = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var bytes = Manifest("1.2.0");
        var signature = Sign(key, bytes);
        Assert.Throws<InvalidDataException>(() => SignedReleaseManifest.Verify(bytes, signature, wrong.ExportSubjectPublicKeyInfoPem()));
        bytes[^2] ^= 1;
        Assert.Throws<InvalidDataException>(() => SignedReleaseManifest.Verify(bytes, signature, key.ExportSubjectPublicKeyInfoPem()));
    }

    [Theory]
    [InlineData(1, "nsis", "currentUser")]
    [InlineData(2, "msi", "currentUser")]
    [InlineData(2, "nsis", "perMachine")]
    public void Valid_signature_cannot_authorize_an_unsupported_installer(int format, string kind, string scope)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var bytes = Manifest("1.2.0", format, kind, scope);
        Assert.Throws<InvalidDataException>(() => SignedReleaseManifest.Verify(bytes, Sign(key, bytes), key.ExportSubjectPublicKeyInfoPem()));
    }

    [Fact]
    public void Installer_lock_prevents_modification_and_detects_corrupt_cache()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, SignedReleaseManifest.InstallerName("1.2.0"));
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var bytes = Manifest("1.2.0");
            var manifest = SignedReleaseManifest.Verify(bytes, Sign(key, bytes), key.ExportSubjectPublicKeyInfoPem());
            using (manifest.OpenVerifiedInstaller(path))
            {
                Assert.Throws<IOException>(() => File.WriteAllText(path, "modified"));
                Assert.Throws<IOException>(() => File.Move(path, path + ".replaced"));
            }
            File.WriteAllBytes(path, [4, 3, 2, 1]);
            Assert.Throws<InvalidDataException>(() => manifest.OpenVerifiedInstaller(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Missing_signature_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => SignedReleaseManifest.Verify(Manifest("1.2.0"), ""));
    }

    private static byte[] Manifest(string version, int format = 2, string kind = "nsis", string scope = "currentUser") =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            formatVersion = format, product = "GhostSlacking", version,
            channel = version.Contains('-') ? "prerelease" : "stable", prerelease = version.Contains('-'), architecture = "win-x64",
            installer = new { file = SignedReleaseManifest.InstallerName(version), bytes = 4,
                sha256 = Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3, 4 })), kind, scope }
        });
    private static string Sign(ECDsa key, byte[] bytes) => Convert.ToBase64String(key.SignData(bytes,
        HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
}
