using Tomix.Cli;
using Tomix.Core.Update;

namespace Tomix.Cli.Tests;

public sealed class UpdateNoticeGateTests
{
    private static bool ShouldShow(
        string outputFormat = "text",
        bool quiet = false,
        bool stderrRedirected = false,
        bool ciEnv = false,
        bool envOptOut = false,
        bool configOptOut = false,
        InstallKind kind = InstallKind.Standalone,
        string version = "0.2.0")
        => UpdateNotice.ShouldShow(outputFormat, quiet, stderrRedirected, ciEnv, envOptOut, configOptOut, kind, version);

    [Fact]
    public void DefaultInteractiveTextRun_Shows()
    {
        Assert.True(ShouldShow());
        Assert.True(ShouldShow(kind: InstallKind.DotnetTool));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("csv")]
    [InlineData("tmdl")]
    public void NonTextOutput_Suppresses(string format)
    {
        Assert.False(ShouldShow(outputFormat: format));
    }

    [Fact]
    public void QuietSuppresses() => Assert.False(ShouldShow(quiet: true));

    [Fact]
    public void RedirectedStderrSuppresses() => Assert.False(ShouldShow(stderrRedirected: true));

    [Fact]
    public void CiEnvironmentSuppresses() => Assert.False(ShouldShow(ciEnv: true));

    [Fact]
    public void EnvOptOutSuppresses() => Assert.False(ShouldShow(envOptOut: true));

    [Fact]
    public void ConfigOptOutSuppresses() => Assert.False(ShouldShow(configOptOut: true));

    [Theory]
    [InlineData(InstallKind.Development)]
    [InlineData(InstallKind.Unknown)]
    public void NonUpdatableInstallSuppresses(InstallKind kind)
    {
        Assert.False(ShouldShow(kind: kind));
    }

    [Fact]
    public void MissingVersionSuppresses() => Assert.False(ShouldShow(version: "0.0.0"));
}
