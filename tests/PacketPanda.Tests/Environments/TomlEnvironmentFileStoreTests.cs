using System.IO;
using FluentAssertions;
using PacketPanda.Core.Environments;
using Xunit;
using Environment = PacketPanda.Core.Environments.Environment;

namespace PacketPanda.Tests.Environments;

public class TomlEnvironmentFileStoreTests : IDisposable
{
    private readonly string _tempDir = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), $"pp-env-{Guid.NewGuid():N}");

    public TomlEnvironmentFileStoreTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private string TempFile(string name) => System.IO.Path.Combine(_tempDir, name);

    [Fact]
    public async Task LoadAsync_ValidFile_ReturnsEnvironmentWithVariables()
    {
        var p = TempFile("dev.toml");
        await File.WriteAllTextAsync(p, "name = \"dev\"\n\n[variables]\nbaseUrl = \"https://api\"\napiKey = \"abc\"\n");

        var env = await new TomlEnvironmentFileStore().LoadAsync(p);

        env.Name.Should().Be("dev");
        env.FilePath.Should().Be(p);
        env.Variables.Should().HaveCount(2);
        env.Variables["baseUrl"].Should().Be("https://api");
        env.Variables["apiKey"].Should().Be("abc");
    }

    [Fact]
    public async Task LoadAsync_MissingName_FallsBackToFilenameWithoutExtension()
    {
        var p = TempFile("prod.toml");
        await File.WriteAllTextAsync(p, "[variables]\nx = \"1\"\n");

        var env = await new TomlEnvironmentFileStore().LoadAsync(p);

        env.Name.Should().Be("prod");
    }

    [Fact]
    public async Task LoadAsync_InvalidToml_ThrowsEnvironmentFileException()
    {
        var p = TempFile("broken.toml");
        await File.WriteAllTextAsync(p, "not = valid = toml");

        var act = () => new TomlEnvironmentFileStore().LoadAsync(p);

        await act.Should().ThrowAsync<EnvironmentFileException>();
    }

    [Fact]
    public async Task LoadAsync_NonStringVariableValue_ThrowsEnvironmentFileException()
    {
        var p = TempFile("typed.toml");
        await File.WriteAllTextAsync(p, "[variables]\nport = 8080\n");

        var act = () => new TomlEnvironmentFileStore().LoadAsync(p);

        await act.Should().ThrowAsync<EnvironmentFileException>();
    }

    [Fact]
    public async Task SaveAsync_ThenLoad_RoundTripsNameAndVariables()
    {
        var p = TempFile("rt.toml");
        var store = new TomlEnvironmentFileStore();
        var env = new Environment("rt", p, new Dictionary<string, string>
        {
            ["a"] = "1",
            ["b"] = "two",
        });

        await store.SaveAsync(p, env);
        var loaded = await store.LoadAsync(p);

        loaded.Name.Should().Be("rt");
        loaded.Variables.Should().BeEquivalentTo(env.Variables);
    }
}
