using System.Collections.Generic;
using System.Reactive.Linq;
using FluentAssertions;
using Moq;
using PacketPanda.Core.Environments;
using PacketPanda.Desktop.Services;
using PacketPanda.Desktop.ViewModels;
using Xunit;
using Environment = PacketPanda.Core.Environments.Environment;

namespace PacketPanda.Tests.ViewModels;

public class EnvironmentSidebarViewModelTests
{
    private static EnvironmentSidebarViewModel NewVm() =>
        new(new Mock<IEnvironmentFileStore>().Object,
            new Mock<IDialogService>().Object,
            () => System.IO.Path.GetTempPath());

    [Fact]
    public async Task NewEnvironment_DoesNotSaveFileToDisk_AndDoesNotAddToItems()
    {
        var store = new Mock<IEnvironmentFileStore>();
        var vm = new EnvironmentSidebarViewModel(
            store.Object, new Mock<IDialogService>().Object, () => System.IO.Path.GetTempPath());

        await vm.NewEnvironmentCommand.Execute();

        store.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Environment>()), Times.Never);
        vm.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task NewEnvironment_FiresNewEnvironmentCreated_WithNameAndPath()
    {
        var vm = NewVm();
        (string Name, string FilePath)? received = null;
        vm.NewEnvironmentCreated.Subscribe(ev => received = ev);

        await vm.NewEnvironmentCommand.Execute();

        received.Should().NotBeNull();
        received!.Value.Name.Should().Be("New Environment");
        received.Value.FilePath.Should().EndWith("New Environment.toml");
    }

    [Fact]
    public async Task NewEnvironment_TwiceBeforeSaving_ProducesDifferentPaths()
    {
        var vm = NewVm();
        var paths = new List<string>();
        vm.NewEnvironmentCreated.Subscribe(ev => paths.Add(ev.FilePath));

        await vm.NewEnvironmentCommand.Execute();
        await vm.NewEnvironmentCommand.Execute();

        paths.Should().HaveCount(2);
        paths[0].Should().NotBe(paths[1]);
    }

    [Fact]
    public async Task CommitEnvironment_AddsEntryToItems()
    {
        var vm = NewVm();
        string? receivedPath = null;
        vm.NewEnvironmentCreated.Subscribe(ev => receivedPath = ev.FilePath);
        await vm.NewEnvironmentCommand.Execute();

        vm.CommitEnvironment(receivedPath!, "My Env");

        vm.Items.Should().ContainSingle(i => i.Name == "My Env" && i.FilePath == receivedPath);
    }

    [Fact]
    public async Task DiscardPendingEnvironment_AllowsReuseOfSameNameAndPath()
    {
        var vm = NewVm();
        var events = new List<(string Name, string FilePath)>();
        vm.NewEnvironmentCreated.Subscribe(ev => events.Add(ev));

        await vm.NewEnvironmentCommand.Execute();
        vm.DiscardPendingEnvironment(events[0].FilePath);
        await vm.NewEnvironmentCommand.Execute();

        events.Should().HaveCount(2);
        events[1].FilePath.Should().Be(events[0].FilePath);
        events[1].Name.Should().Be(events[0].Name);
    }
}
