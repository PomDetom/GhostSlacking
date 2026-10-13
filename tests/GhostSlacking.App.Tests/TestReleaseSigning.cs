using System.Security.Cryptography;
using System.Text;
using GhostSlacking.Core;

namespace GhostSlacking.App.Tests;

internal static class TestReleaseSigning
{
    private static readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static readonly object Gate = new();
    public static string PublicKey => Key.ExportSubjectPublicKeyInfoPem();
    public static string Sign(string text)
    {
        lock (Gate) return Convert.ToBase64String(Key.SignData(Encoding.UTF8.GetBytes(text),
            HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }
}
