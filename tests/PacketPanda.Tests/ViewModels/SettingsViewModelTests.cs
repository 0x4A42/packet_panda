using System.Reactive.Linq;
using FluentAssertions;
using Moq;
using PacketPanda.Core.Persistence;
using PacketPanda.Desktop.ViewModels;

namespace PacketPanda.Tests.ViewModels;

public sealed class SettingsViewModelTests
{
    private readonly Mock<ISettingsStore> _store = new(MockBehavior.Strict);

    private SettingsViewModel Create(AppSettings? current = null)
    {
        current ??= new AppSettings();
        _store.Setup(s => s.LoadAsync()).ReturnsAsync(current);
        _store.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);
        return new SettingsViewModel(_store.Object, current);
    }

    [Fact]
    public void Constructor_PopulatesPropertiesFromCurrentSettings()
    {
        var settings = new AppSettings(
            IsDarkTheme: true,
            SslVerification: false,
            FollowRedirects: false,
            TimeoutSeconds: 60,
            HistoryLimit: 500);

        var vm = Create(settings);

        vm.IsDarkTheme.Should().BeTrue();
        vm.SslVerification.Should().BeFalse();
        vm.FollowRedirects.Should().BeFalse();
        vm.TimeoutSeconds.Should().Be(60);
        vm.HistoryLimit.Should().Be(500);
    }

    [Theory]
    [InlineData(0, 1)]      // below min → clamped to 1
    [InlineData(1, 1)]      // at min → unchanged
    [InlineData(300, 300)]  // at max → unchanged
    [InlineData(301, 300)]  // above max → clamped to 300
    public async Task SaveCommand_ClampsTimeoutSeconds(int input, int expected)
    {
        var vm = Create();
        vm.TimeoutSeconds = input;
        AppSettings? saved = null;
        _store.Setup(s => s.SaveAsync(It.IsAny<AppSettings>()))
            .Callback<AppSettings>(s => saved = s)
            .Returns(Task.CompletedTask);

        await vm.SaveCommand.Execute();

        saved!.TimeoutSeconds.Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 50)]       // below min → clamped to 50
    [InlineData(50, 50)]      // at min → unchanged
    [InlineData(1000, 1000)]  // at max → unchanged
    [InlineData(1001, 1000)]  // above max → clamped to 1000
    public async Task SaveCommand_ClampsHistoryLimit(int input, int expected)
    {
        var vm = Create();
        vm.HistoryLimit = input;
        AppSettings? saved = null;
        _store.Setup(s => s.SaveAsync(It.IsAny<AppSettings>()))
            .Callback<AppSettings>(s => saved = s)
            .Returns(Task.CompletedTask);

        await vm.SaveCommand.Execute();

        saved!.HistoryLimit.Should().Be(expected);
    }

    [Fact]
    public async Task SaveCommand_NullTimeout_ClampsToMin()
    {
        var vm = Create();
        vm.TimeoutSeconds = null;
        AppSettings? saved = null;
        _store.Setup(s => s.SaveAsync(It.IsAny<AppSettings>()))
            .Callback<AppSettings>(s => saved = s)
            .Returns(Task.CompletedTask);

        await vm.SaveCommand.Execute();

        saved!.TimeoutSeconds.Should().Be(1);
    }

    [Fact]
    public async Task SaveCommand_NullHistoryLimit_ClampsToMin()
    {
        var vm = Create();
        vm.HistoryLimit = null;
        AppSettings? saved = null;
        _store.Setup(s => s.SaveAsync(It.IsAny<AppSettings>()))
            .Callback<AppSettings>(s => saved = s)
            .Returns(Task.CompletedTask);

        await vm.SaveCommand.Execute();

        saved!.HistoryLimit.Should().Be(50);
    }

    [Fact]
    public async Task SaveCommand_PreservesOtherSettingsFields()
    {
        var current = new AppSettings(
            OpenCollectionPaths: ["/a.json"],
            ActiveEnvironmentName: "Prod");
        var vm = Create(current);
        AppSettings? saved = null;
        _store.Setup(s => s.SaveAsync(It.IsAny<AppSettings>()))
            .Callback<AppSettings>(s => saved = s)
            .Returns(Task.CompletedTask);

        await vm.SaveCommand.Execute();

        saved!.OpenCollectionPaths.Should().BeEquivalentTo(["/a.json"]);
        saved.ActiveEnvironmentName.Should().Be("Prod");
    }
}
