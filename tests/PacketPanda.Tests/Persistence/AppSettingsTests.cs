using FluentAssertions;
using PacketPanda.Core.Persistence;

namespace PacketPanda.Tests.Persistence;

public class AppSettingsTests
{
    [Fact]
    public void AppSettings_DefaultsTo_LightTheme()
    {
        var settings = new AppSettings();
        settings.IsDarkTheme.Should().BeFalse();
    }

}
