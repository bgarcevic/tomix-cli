using System.Runtime.Versioning;
using Tomix.Core.Models;

namespace Tomix.Core.Tests;

public sealed class ModelProviderResolverTests
{
    [Fact]
    public void ResolveSingle_NoProviderClaims_ReturnsNull()
    {
        var providers = new IModelProvider[] { new StubProvider(claims: false), new StubProvider(claims: false) };

        Assert.Null(providers.ResolveSingle(new ModelReference("model")));
    }

    [Fact]
    public void ResolveSingle_ExactlyOneClaims_ReturnsIt()
    {
        var expected = new StubProvider(claims: true);
        var providers = new IModelProvider[] { new StubProvider(claims: false), expected };

        Assert.Same(expected, providers.ResolveSingle(new ModelReference("model")));
    }

    [Fact]
    public void ResolveSingle_MultipleClaim_ThrowsWithEveryClaimant()
    {
        // Overlapping CanOpen contracts are a registration bug; they must surface instead of
        // being resolved silently by list order.
        var providers = new IModelProvider[]
        {
            new StubProvider(claims: true),
            new StubProvider(claims: false),
            new OtherStubProvider(),
        };

        var ex = Assert.Throws<AmbiguousModelProviderException>(
            () => providers.ResolveSingle(new ModelReference("model")));

        Assert.Contains("model", ex.Message);
        Assert.Contains(nameof(StubProvider), ex.Message);
        Assert.Contains(nameof(OtherStubProvider), ex.Message);
    }

    [Fact]
    public void ResolveSingle_UnclaimedUnreadableFile_ThrowsModelLoadException()
    {
        if (!CanDropUnixReadPermission())
            return;

        var path = Path.Combine(Path.GetTempPath(), $"tomix-unreadable-{Guid.NewGuid():N}.pbip");
        File.WriteAllText(path, "{}");
        File.SetUnixFileMode(path, UnixFileMode.None);
        try
        {
            var providers = new IModelProvider[] { new StubProvider(claims: false) };

            // CanOpen is total, so providers treat an unreadable source as unowned; the resolver
            // must name the unreadable file instead of yielding the no-provider diagnostic.
            var ex = Assert.Throws<ModelLoadException>(
                () => providers.ResolveSingle(new ModelReference(path)));

            Assert.Contains(path, ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ResolveSingle_UnclaimedUnreadableDirectory_ThrowsModelLoadException()
    {
        if (!CanDropUnixReadPermission())
            return;

        var path = Path.Combine(Path.GetTempPath(), $"tomix-unreadable-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        File.SetUnixFileMode(path, UnixFileMode.None);
        try
        {
            var providers = new IModelProvider[] { new StubProvider(claims: false) };

            var ex = Assert.Throws<ModelLoadException>(
                () => providers.ResolveSingle(new ModelReference(path)));

            Assert.Contains(path, ex.Message);
        }
        finally
        {
            File.SetUnixFileMode(
                path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.Delete(path);
        }
    }

    [Fact]
    public void ResolveSingle_UnclaimedReadableFile_ReturnsNull()
    {
        // The unreadable-source probe must not misfire on sources that are merely unowned.
        var path = Path.Combine(Path.GetTempPath(), $"tomix-readable-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "not a model");
        try
        {
            var providers = new IModelProvider[] { new StubProvider(claims: false) };

            Assert.Null(providers.ResolveSingle(new ModelReference(path)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ResolveSingle_ClaimedUnreadableFile_ReturnsProviderWithoutProbing()
    {
        if (!CanDropUnixReadPermission())
            return;

        // A provider can claim a reference without reading it (e.g. by extension plus sibling
        // folders); a match must win over the unreadable-source probe.
        var path = Path.Combine(Path.GetTempPath(), $"tomix-unreadable-{Guid.NewGuid():N}.pbip");
        File.WriteAllText(path, "{}");
        File.SetUnixFileMode(path, UnixFileMode.None);
        try
        {
            var expected = new StubProvider(claims: true);
            var providers = new IModelProvider[] { expected };

            Assert.Same(expected, providers.ResolveSingle(new ModelReference(path)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // Unix permission bits are the only way these tests can make a path unreadable; on Windows
    // (no unix modes) or as root (permission checks bypassed) the condition cannot be set up,
    // so the tests no-op there. CI and dev runs on macOS/Linux exercise them.
    [UnsupportedOSPlatformGuard("windows")]
    private static bool CanDropUnixReadPermission()
        => !OperatingSystem.IsWindows() && !Environment.IsPrivilegedProcess;

    private sealed class StubProvider(bool claims) : IModelProvider
    {
        public bool CanOpen(ModelReference reference) => claims;
        public Task<IModelSession> OpenAsync(ModelReference reference, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class OtherStubProvider : IModelProvider
    {
        public bool CanOpen(ModelReference reference) => true;
        public Task<IModelSession> OpenAsync(ModelReference reference, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
