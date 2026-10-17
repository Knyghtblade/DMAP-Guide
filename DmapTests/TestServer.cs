using DmapShared.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DmapTests;

/// <summary>A real DMAP server running in memory, with its own empty data folder.</summary>
public sealed class TestServer : IDisposable
{
    public const string EditorKey = "editor-key-for-tests-0001";
    public const string ViewerKey = "viewer-key-for-tests-0001";

    private readonly WebApplicationFactory<Program> _factory;

    public string DataFolder { get; }

    public TestServer(string? dataFolder = null)
    {
        DataFolder = dataFolder ?? TempFolder.Create("server");
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Production");
            host.UseSetting("Dmap:DataFolder", DataFolder);
            host.UseSetting("Dmap:ApiKeys:0:Key", EditorKey);
            host.UseSetting("Dmap:ApiKeys:0:Role", "editor");
            host.UseSetting("Dmap:ApiKeys:1:Key", ViewerKey);
            host.UseSetting("Dmap:ApiKeys:1:Role", "viewer");
        });
    }

    public DmapApiClient Client(string key = EditorKey, string user = "Alex") =>
        new("http://localhost/", key, user, _factory.Server.CreateHandler());

    public HttpClient RawClient() => _factory.CreateClient();

    public void Dispose()
    {
        _factory.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        TempFolder.Delete(DataFolder);
    }
}

public static class TempFolder
{
    public static string Create(string name)
    {
        string path = Path.Combine(Path.GetTempPath(), "dmap-tests", $"{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    public static void Delete(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
