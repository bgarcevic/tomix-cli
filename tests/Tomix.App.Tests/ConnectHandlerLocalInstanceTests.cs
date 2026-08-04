using Tomix.App.Connect;
using Tomix.App.State;

namespace Tomix.App.Tests;

/// <summary>
/// A <c>--local</c> session names a Power BI Desktop instance by its discovered
/// <c>localhost:&lt;port&gt;</c> endpoint. If that endpoint is not persisted the session says
/// "local" without naming a target, <c>ActiveModelResolver</c> resolves it to nothing, and every
/// later command fails as though nothing was connected.
/// </summary>
public sealed class ConnectHandlerLocalInstanceTests : IDisposable
{
    private readonly List<string> _cleanup = [];

    public void Dispose()
    {
        foreach (var folder in _cleanup.Where(Directory.Exists))
            Directory.Delete(folder, recursive: true);
    }

    private static ConnectSetRequest LocalRequest(string? server, string? database = null)
        => new(server, database, Model: null, Auth: null, Local: true, Profile: null);

    private static void WithStore(Action<CliStateStore, ConnectHandler> test)
    {
        var dir = Directory.CreateTempSubdirectory("tomix-connect-local-tests-").FullName;
        try
        {
            var store = new CliStateStore(dir);
            test(store, new ConnectHandler(store));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("localhost:61696")]
    [InlineData("127.0.0.1:61696")]
    public void Set_PersistsDiscoveredDesktopEndpoint(string endpoint)
    {
        WithStore((store, handler) =>
        {
            handler.Set(LocalRequest(endpoint, "Sales Model"));

            var session = store.LoadCurrentSession();

            Assert.NotNull(session);
            Assert.Equal(endpoint, session.Server);
            Assert.Equal("Sales Model", session.Database);
            Assert.True(session.Local);
        });
    }

    [Fact]
    public void Set_LocalWithoutEndpoint_LeavesServerUnset()
    {
        WithStore((store, handler) =>
        {
            handler.Set(LocalRequest(server: null));

            Assert.Null(store.LoadCurrentSession()!.Server);
        });
    }

    [Fact]
    public void Set_LocalWithRemoteEndpoint_DoesNotStoreItAsLocal()
    {
        // Only a loopback endpoint is a --local target; a workspace endpoint arriving here would
        // otherwise be persisted as though Desktop were serving it.
        WithStore((store, handler) =>
        {
            handler.Set(LocalRequest("powerbi://api.powerbi.com/v1.0/myorg/ws"));

            Assert.Null(store.LoadCurrentSession()!.Server);
        });
    }

    // --- Cached report name ------------------------------------------------------------------

    private string WritePortFile(string port)
    {
        var folder = Directory.CreateTempSubdirectory("tomix-portfile-tests-").FullName;
        var path = Path.Combine(folder, "msmdsrv.port.txt");
        File.WriteAllBytes(path, System.Text.Encoding.Unicode.GetBytes(port)); // UTF-16LE, no BOM
        _cleanup.Add(folder);
        return path;
    }

    [Fact]
    public void Show_KeepsReportName_WhilePortFileStillHoldsThatPort()
    {
        var portFile = WritePortFile("61696");

        WithStore((_, handler) =>
        {
            handler.Set(LocalRequest("localhost:61696") with
            {
                ReportName = "B4 - Bonustimer",
                ReportPortFile = portFile
            });

            Assert.Equal("B4 - Bonustimer", handler.Show().Data!.Connection!.ReportName);
        });
    }

    [Theory]
    [InlineData("50987")] // Desktop restarted onto a different port
    [InlineData("")]      // port file truncated
    public void Show_DropsReportName_WhenPortFileNoLongerMatches(string nowServing)
    {
        // Without this, a Desktop restart that reused the port would show the previous report's
        // name against a different model.
        var portFile = WritePortFile(nowServing);

        WithStore((_, handler) =>
        {
            handler.Set(LocalRequest("localhost:61696") with
            {
                ReportName = "B4 - Bonustimer",
                ReportPortFile = portFile
            });

            var shown = handler.Show().Data!.Connection!;
            Assert.Null(shown.ReportName);
            Assert.Null(shown.ReportPortFile);
            Assert.Equal("localhost:61696", shown.Server); // the connection itself is untouched
        });
    }

    [Fact]
    public void Show_DropsReportName_WhenPortFileIsGone()
    {
        var portFile = WritePortFile("61696");
        File.Delete(portFile);

        WithStore((_, handler) =>
        {
            handler.Set(LocalRequest("localhost:61696") with
            {
                ReportName = "B4 - Bonustimer",
                ReportPortFile = portFile
            });

            Assert.Null(handler.Show().Data!.Connection!.ReportName);
        });
    }

    [Theory]
    [InlineData("localhost:61696", true)]
    [InlineData("127.0.0.1:61696", true)]
    [InlineData("localhost:50987", false)]
    [InlineData("localhost", false)]        // no port
    [InlineData("localhost:abc", false)]    // unparseable port
    [InlineData("", false)]
    [InlineData(null, false)]
    public void StillServes_ComparesTheEndpointPortAgainstThePortFile(string? endpoint, bool expected)
    {
        var portFile = WritePortFile("61696");

        Assert.Equal(expected, PowerBiDesktopDiscovery.StillServes(portFile, endpoint));
    }

    [Fact]
    public void StillServes_MissingPortFile_IsFalse()
        => Assert.False(PowerBiDesktopDiscovery.StillServes(
            Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.txt"), "localhost:61696"));

    [Fact]
    public void Set_RecordsRecent_ForDesktopInstance()
    {
        WithStore((store, handler) =>
        {
            handler.Set(LocalRequest("localhost:61696", "Sales Model"));

            var recent = Assert.Single(store.LoadRecentConnections());
            Assert.Equal("localhost:61696", recent.Connection.Server);
            Assert.Equal("Sales Model", recent.Connection.Database);
        });
    }
}
