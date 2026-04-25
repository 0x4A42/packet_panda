using FluentAssertions;
using PacketPanda.Core.Environments;
using Environment = PacketPanda.Core.Environments.Environment;

namespace PacketPanda.Tests.Environments;

public sealed class JsonEnvironmentFileStoreTests : IDisposable
{
    private readonly string _tempPath = Path.GetTempFileName() + ".json";
    private readonly IEnvironmentFileStore _store = new JsonEnvironmentFileStore();

    public void Dispose()
    {
        if (File.Exists(_tempPath)) File.Delete(_tempPath);
    }

    [Fact]
    public async Task RoundTrip_PreservesNameAndVariables()
    {
        var env = new Environment("Local",
            _tempPath,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["baseUrl"] = "http://localhost" },
            Descriptions: null,
            DisabledVariables: null);

        await _store.SaveAsync(_tempPath, env);
        var loaded = await _store.LoadAsync(_tempPath);

        loaded.Name.Should().Be("Local");
        loaded.Variables["baseUrl"].Should().Be("http://localhost");
        loaded.DisabledVariables.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task Load_DisabledEntry_PopulatesDisabledVariables()
    {
        await File.WriteAllTextAsync(_tempPath, """
            {
              "id": "env-1",
              "name": "Prod",
              "values": [
                { "key": "apiKey", "value": "secret", "type": "default", "enabled": true },
                { "key": "debug", "value": "true", "type": "default", "enabled": false }
              ]
            }
            """);

        var env = await _store.LoadAsync(_tempPath);

        env.Variables.ContainsKey("apiKey").Should().BeTrue();
        env.Variables.ContainsKey("debug").Should().BeTrue();
        env.DisabledVariables.Should().ContainSingle().Which.Should().Be("debug");
    }

    [Fact]
    public async Task Save_DisabledVariables_WritesEnabledFalse()
    {
        var env = new Environment("Test", _tempPath,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["x"] = "1" },
            Descriptions: null,
            DisabledVariables: ["x"]);

        await _store.SaveAsync(_tempPath, env);
        var json = await File.ReadAllTextAsync(_tempPath);

        json.Should().Contain("\"enabled\": false");
    }

    [Fact]
    public async Task Load_MissingId_IsToleratedNameFallsBackToFileName()
    {
        await File.WriteAllTextAsync(_tempPath, """{ "name": "Local", "values": [] }""");

        var env = await _store.LoadAsync(_tempPath);

        env.Name.Should().Be("Local");
    }

    [Fact]
    public async Task Load_MissingFile_ThrowsEnvironmentFileException()
    {
        var act = async () => await _store.LoadAsync("/nonexistent/env.json");

        await act.Should().ThrowAsync<EnvironmentFileException>();
    }

    [Fact]
    public async Task Load_NameFallsBackToFileName_WhenNameMissing()
    {
        await File.WriteAllTextAsync(_tempPath, """{ "id": "x", "values": [] }""");

        var env = await _store.LoadAsync(_tempPath);

        env.Name.Should().Be(Path.GetFileNameWithoutExtension(_tempPath));
    }
}
