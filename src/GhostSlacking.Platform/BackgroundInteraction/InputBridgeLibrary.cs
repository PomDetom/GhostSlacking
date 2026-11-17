using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace GhostSlacking.Platform;

// Only activated when an experimental interaction starts. Target processes pin
// the cached image, leaving the installed payload replaceable after GS exits.
internal static class InputBridgeLibrary
{
    internal const string LibraryName = "GhostSlacking.InputBridge.dll";
    private const string CachePrefix = "GhostSlacking.InputBridge.";
    private static readonly Lazy<nint> Library = new(Load);

    static InputBridgeLibrary() => NativeLibrary.SetDllImportResolver(typeof(InputBridgeLibrary).Assembly, Resolve);

    internal static nint EnsureLoaded() => Library.Value;

    private static nint Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath) =>
        name == LibraryName ? EnsureLoaded() : 0;

    private static nint Load()
    {
        var source = Path.Combine(AppContext.BaseDirectory, LibraryName);
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GhostSlacking", "InputBridge");
        var cached = PrepareCopy(source, directory);
        // Hold the verified file against writes/deletion until image loading
        // finishes. Never fall back to PATH, the working directory or source.
        using var image = File.Open(cached, FileMode.Open, FileAccess.Read, FileShare.Read);
        var expected = Path.GetFileNameWithoutExtension(cached)[CachePrefix.Length..];
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(image)), expected, StringComparison.Ordinal))
            throw new InvalidDataException("The input bridge cache failed integrity verification.");
        var handle = NativeLibrary.Load(cached);
        try
        {
            var version = Marshal.GetDelegateForFunctionPointer<AbiVersion>(NativeLibrary.GetExport(handle, "GS_ABIVersion"));
            if (version() != 1) throw new BadImageFormatException("The input bridge ABI does not match this application.");
            return handle;
        }
        catch { NativeLibrary.Free(handle); throw; }
    }

    internal static string PrepareCopy(string source, string directory)
    {
        using var original = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        var digest = Convert.ToHexString(SHA256.HashData(original));
        original.Position = 0;
        Directory.CreateDirectory(directory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The input bridge cache must be a regular directory.");
        var cached = Path.Combine(directory, CachePrefix + digest + ".dll");
        var rebuild = !File.Exists(cached);
        if (!rebuild)
        {
            if ((File.GetAttributes(cached) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The cached input bridge must be a regular file.");
            using var existing = File.Open(cached, FileMode.Open, FileAccess.Read, FileShare.Read);
            rebuild = !string.Equals(Convert.ToHexString(SHA256.HashData(existing)), digest, StringComparison.Ordinal);
        }
        if (rebuild)
        {
            var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    original.CopyTo(output);
                    output.Flush(flushToDisk: true);
                }
                try { File.Move(temporary, cached, overwrite: true); }
                // A concurrent instance may have prepared/loaded the same image.
                // Verify the winner below; a locked corrupt image stays rejected.
                catch (IOException) when (File.Exists(cached)) { }
                catch (UnauthorizedAccessException) when (File.Exists(cached)) { }
            }
            finally { TryDelete(temporary); }
        }
        if ((File.GetAttributes(cached) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The cached input bridge must be a regular file.");
        using (var existing = File.Open(cached, FileMode.Open, FileAccess.Read, FileShare.Read))
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(existing)), digest, StringComparison.Ordinal))
                throw new InvalidDataException("The cached input bridge differs from the installed payload.");
        Cleanup(directory, cached);
        return cached;
    }

    private static void Cleanup(string directory, string retained)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, CachePrefix + "*.dll"))
            {
                var hash = Path.GetFileNameWithoutExtension(file)[CachePrefix.Length..];
                if (file == retained || hash.Length != 64 || !hash.All(Uri.IsHexDigit)) continue;
                try
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0) TryDelete(file);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint AbiVersion();
}
