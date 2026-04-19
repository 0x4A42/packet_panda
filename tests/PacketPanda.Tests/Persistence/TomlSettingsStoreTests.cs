using FluentAssertions;
using PacketPanda.Core.Persistence;

namespace PacketPanda.Tests.Persistence;

public class TomlSettingsStoreTests : IDisposable
{
    private readonly string _tempPath = Path.GetTempFileName() + ".toml";

    public void Dispose()
    {
        if (File.Exists(_tempPath)) File.Delete(_tempPath);
    }

    [Fact]
    public async Task Load_MissingFile_ReturnsEmptyAppSettings()
    {
        File.Delete(_tempPath);
        var store = new TomlSettingsStore(_tempPath);

        var settings = await store.LoadAsync();

        settings.LastWorkspacePath.Should().BeNull();
    }

    [Fact]
    public async Task RoundTrip_PreservesLastWorkspacePath()
    {
        var store = new TomlSettingsStore(_tempPath);
        await store.SaveAsync(new AppSettings("/tmp/workspace"));

        var loaded = await store.LoadAsync();

        loaded.LastWorkspacePath.Should().Be("/tmp/workspace");
    }

    [Fact]
    public async Task Load_UnknownKey_IsIgnored()
    {
        await File.WriteAllTextAsync(_tempPath, """
            last_workspace_path = "/tmp/workspace"
            future_key = "whatever"
            """);
        var store = new TomlSettingsStore(_tempPath);

        var settings = await store.LoadAsync();

        settings.LastWorkspacePath.Should().Be("/tmp/workspace");
    }

    [Fact]
    public async Task Load_InvalidToml_ThrowsSettingsException()
    {
        await File.WriteAllTextAsync(_tempPath, "not [ valid toml !!!");
        var store = new TomlSettingsStore(_tempPath);

        var act = async () => await store.LoadAsync();

        await act.Should().ThrowAsync<SettingsException>();
    }

    [Fact]
    public async Task Save_CreatesParentDirectoryIfMissing()
    {
        var nested = Path.Combine(Path.GetTempPath(), "pp-settings-" + Guid.NewGuid().ToString("N"), "settings.toml");
        try
        {
            var store = new TomlSettingsStore(nested);
            await store.SaveAsync(new AppSettings("/tmp/x"));

            File.Exists(nested).Should().BeTrue();
        }
        finally
        {
            var dir = Path.GetDirectoryName(nested)!;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
