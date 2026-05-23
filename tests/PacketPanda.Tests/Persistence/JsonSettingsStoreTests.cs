using FluentAssertions;
using PacketPanda.Core.Persistence;

namespace PacketPanda.Tests.Persistence;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private string JsonPath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task RoundTrip_PreservesAllFields()
    {
        var store = new JsonSettingsStore(JsonPath);
        var settings = new AppSettings(
            OpenCollectionPaths: ["/a/b.json", "/c/d.json"],
            ActiveEnvironmentName: "Prod");

        await store.SaveAsync(settings);
        var loaded = await store.LoadAsync();

        loaded.OpenCollectionPaths.Should().BeEquivalentTo(["/a/b.json", "/c/d.json"]);
        loaded.ActiveEnvironmentName.Should().Be("Prod");
    }

    [Fact]
    public async Task Load_MissingFile_ReturnsDefaultSettings()
    {
        var store = new JsonSettingsStore(JsonPath);

        var settings = await store.LoadAsync();

        settings.OpenCollectionPaths.Should().BeNullOrEmpty();
        settings.ActiveEnvironmentName.Should().BeNull();
    }

    [Fact]
    public async Task Load_LegacyTomlExists_WritesFreshJsonAndReturnsDefault()
    {
        Directory.CreateDirectory(_dir);
        var tomlPath = Path.Combine(_dir, "settings.toml");
        await File.WriteAllTextAsync(tomlPath, "last_workspace_path = \"/old\"");

        var store = new JsonSettingsStore(JsonPath);
        var settings = await store.LoadAsync();

        File.Exists(JsonPath).Should().BeTrue("migration should create settings.json");
        settings.OpenCollectionPaths.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task Save_CreatesDirectoryIfMissing()
    {
        var store = new JsonSettingsStore(JsonPath);

        await store.SaveAsync(new AppSettings());

        File.Exists(JsonPath).Should().BeTrue();
    }

    [Fact]
    public async Task Save_NullPaths_OmitsFieldFromJson()
    {
        var store = new JsonSettingsStore(JsonPath);
        await store.SaveAsync(new AppSettings());
        var json = await File.ReadAllTextAsync(JsonPath);

        json.Should().NotContain("openCollectionPaths");
    }

    [Fact]
    public async Task RoundTrip_NewSettings_PreservesAllFields()
    {
        var store = new JsonSettingsStore(JsonPath);
        var settings = new AppSettings(
            SslVerification: false,
            FollowRedirects: false,
            TimeoutSeconds: 60,
            HistoryLimit: 500,
            IsDarkTheme: true);

        await store.SaveAsync(settings);
        var loaded = await store.LoadAsync();

        loaded.SslVerification.Should().BeFalse();
        loaded.FollowRedirects.Should().BeFalse();
        loaded.TimeoutSeconds.Should().Be(60);
        loaded.HistoryLimit.Should().Be(500);
        loaded.IsDarkTheme.Should().BeTrue();
    }

    [Fact]
    public async Task Load_MissingNewFields_UsesDefaults()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(JsonPath, "{\"openCollectionPaths\":[]}");

        var store = new JsonSettingsStore(JsonPath);
        var loaded = await store.LoadAsync();

        loaded.SslVerification.Should().BeTrue();
        loaded.FollowRedirects.Should().BeTrue();
        loaded.TimeoutSeconds.Should().Be(30);
        loaded.HistoryLimit.Should().Be(200);
        loaded.IsDarkTheme.Should().BeFalse();
    }

    [Fact]
    public async Task RoundTrip_IsDarkTheme_IsPersistedCorrectly()
    {
        var store = new JsonSettingsStore(JsonPath);
        await store.SaveAsync(new AppSettings(IsDarkTheme: true));
        var loaded = await store.LoadAsync();

        loaded.IsDarkTheme.Should().BeTrue();
    }
}
