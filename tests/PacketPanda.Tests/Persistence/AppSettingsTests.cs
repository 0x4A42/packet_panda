using System.Text.Json;
using FluentAssertions;
using PacketPanda.Core.Persistence;

namespace PacketPanda.Tests.Persistence;

public class AppSettingsTests
{
    [Fact]
    public void AppSettings_DefaultsTo_DarkTheme()
    {
        var settings = new AppSettings();
        settings.IsDarkTheme.Should().BeTrue();
    }

    [Fact]
    public void AppSettings_RoundTrips_IsDarkTheme_False()
    {
        var settings = new AppSettings(IsDarkTheme: false);
        var json = JsonSerializer.Serialize(settings);
        var result = JsonSerializer.Deserialize<AppSettings>(json);
        result!.IsDarkTheme.Should().BeFalse();
    }
}
