using System.Runtime.InteropServices;
using GhostSlacking.Platform;

namespace GhostSlacking.App.Tests;

public sealed class InputBridgeLibraryTests
{
    [Fact]
    public void Packaged_bridge_loads_from_cache_while_the_original_payload_can_be_removed()
    {
        using var files = new Files();
        var packaged = Path.Combine(AppContext.BaseDirectory, InputBridgeLibrary.LibraryName);
        Assert.True(File.Exists(packaged), "A normal App build must carry the x64 input bridge into dependent output.");
        File.Copy(packaged, files.Source);
        var cached = InputBridgeLibrary.PrepareCopy(files.Source, files.Cache);
        Assert.StartsWith(files.Cache + Path.DirectorySeparatorChar, cached);
        var handle = NativeLibrary.Load(cached);
        try
        {
            var abi = Marshal.GetDelegateForFunctionPointer<AbiVersion>(NativeLibrary.GetExport(handle, "GS_ABIVersion"));
            Assert.Equal(1U, abi());
            File.Delete(files.Source);
            Directory.Delete(Path.GetDirectoryName(files.Source)!);
            Assert.True(File.Exists(cached));
            Assert.Equal(1U, abi());
        }
        finally { NativeLibrary.Free(handle); }
    }

    [Fact]
    public void Modified_cached_image_is_rebuilt_from_the_installed_payload_before_loading()
    {
        using var files = new Files();
        File.WriteAllText(files.Source, "installed bridge fixture");
        var cached = InputBridgeLibrary.PrepareCopy(files.Source, files.Cache);
        File.WriteAllText(cached, "changed cached fixture");
        Assert.Equal(cached, InputBridgeLibrary.PrepareCopy(files.Source, files.Cache));
        Assert.Equal(File.ReadAllBytes(files.Source), File.ReadAllBytes(cached));
    }

    [Fact]
    public void A_locked_modified_cache_is_rejected_without_forcing_its_holder_to_release_it()
    {
        using var files = new Files();
        File.WriteAllText(files.Source, "installed bridge fixture");
        var cached = InputBridgeLibrary.PrepareCopy(files.Source, files.Cache);
        File.WriteAllText(cached, "changed cached fixture");
        using (File.Open(cached, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Throws<InvalidDataException>(() => InputBridgeLibrary.PrepareCopy(files.Source, files.Cache));
        Assert.Equal(cached, InputBridgeLibrary.PrepareCopy(files.Source, files.Cache));
        Assert.Equal(File.ReadAllBytes(files.Source), File.ReadAllBytes(cached));
    }

    [Fact]
    public void Missing_installed_payload_does_not_fall_back_to_an_old_cached_image()
    {
        using var files = new Files();
        File.WriteAllText(files.Source, "installed bridge fixture");
        InputBridgeLibrary.PrepareCopy(files.Source, files.Cache);
        File.Delete(files.Source);
        Assert.Throws<FileNotFoundException>(() => InputBridgeLibrary.PrepareCopy(files.Source, files.Cache));
    }

    [Fact]
    public void Old_in_use_cache_is_retained_then_cleaned_after_its_holder_releases_it()
    {
        using var files = new Files();
        File.WriteAllText(files.Source, "old bridge fixture");
        var old = InputBridgeLibrary.PrepareCopy(files.Source, files.Cache);
        var unrelated = Path.Combine(files.Cache, "keep.txt");
        File.WriteAllText(unrelated, "unrelated fixture");
        File.WriteAllText(files.Source, "new bridge fixture");
        string current;
        using (File.Open(old, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            current = InputBridgeLibrary.PrepareCopy(files.Source, files.Cache);
            Assert.NotEqual(old, current);
            Assert.True(File.Exists(old));
        }
        Assert.Equal(current, InputBridgeLibrary.PrepareCopy(files.Source, files.Cache));
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(current));
        Assert.True(File.Exists(unrelated));
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint AbiVersion();

    private sealed class Files : IDisposable
    {
        private readonly string _root = Path.Combine(AppContext.BaseDirectory, "input-bridge-tests", Guid.NewGuid().ToString("N"));
        public string Source { get; }
        public string Cache { get; }
        public Files()
        {
            Source = Path.Combine(_root, "app", InputBridgeLibrary.LibraryName);
            Cache = Path.Combine(_root, "cache");
            Directory.CreateDirectory(Path.GetDirectoryName(Source)!);
        }
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
