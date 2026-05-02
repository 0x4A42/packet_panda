using System.Collections.Generic;
using System.IO;
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
    private static string NewTempEnvDir() =>
        Path.Combine(Path.GetTempPath(), "panda-test-" + Guid.NewGuid().ToString("N"));

    private static EnvironmentSidebarViewModel NewVm(IFilePickerService? picker = null) =>
        new(new Mock<IEnvironmentFileStore>().Object,
            new Mock<IDialogService>().Object,
            picker ?? new Mock<IFilePickerService>().Object,
            () => Path.GetTempPath(),
            NewTempEnvDir());

    [Fact]
    public async Task NewEnvironment_DoesNotSaveFileToDisk_AndDoesNotAddToItems()
    {
        var store = new Mock<IEnvironmentFileStore>();
        var vm = new EnvironmentSidebarViewModel(
            store.Object,
            new Mock<IDialogService>().Object,
            new Mock<IFilePickerService>().Object,
            () => Path.GetTempPath(),
            NewTempEnvDir());

        await vm.NewEnvironmentCommand.Execute();

        store.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Environment>()), Times.Never);
        vm.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task NewEnvironment_FiresNewEnvironmentCreated_WithNameAndJsonPath()
    {
        var vm = NewVm();
        (string Name, string FilePath)? received = null;
        vm.NewEnvironmentCreated.Subscribe(ev => received = ev);

        await vm.NewEnvironmentCommand.Execute();

        received.Should().NotBeNull();
        received!.Value.Name.Should().Be("New Environment");
        received.Value.FilePath.Should().EndWith("New Environment.json");
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
    public async Task CommitEnvironment_WhenPathAlreadyCommitted_UpdatesName()
    {
        var vm = NewVm();
        string? receivedPath = null;
        vm.NewEnvironmentCreated.Subscribe(ev => receivedPath = ev.FilePath);
        await vm.NewEnvironmentCommand.Execute();

        vm.CommitEnvironment(receivedPath!, "Original Name");
        vm.CommitEnvironment(receivedPath!, "Updated Name");

        vm.Items.Should().ContainSingle(i => i.Name == "Updated Name" && i.FilePath == receivedPath);
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

    [Fact]
    public async Task OpenEnvironmentFile_PickerReturnsPath_FiresEnvironmentOpenRequested()
    {
        var picker = new Mock<IFilePickerService>();
        picker.Setup(p => p.PickOpenAsync()).ReturnsAsync("/tmp/my-env.toml");
        var vm = NewVm(picker.Object);

        string? receivedPath = null;
        vm.EnvironmentOpenRequested.Subscribe(p => receivedPath = p);

        await vm.OpenEnvironmentFileCommand.Execute();

        receivedPath.Should().Be("/tmp/my-env.toml");
    }

    [Fact]
    public async Task OpenEnvironmentFile_PickerCancelled_DoesNotFireEnvironmentOpenRequested()
    {
        var picker = new Mock<IFilePickerService>();
        picker.Setup(p => p.PickOpenAsync()).ReturnsAsync((string?)null);
        var vm = NewVm(picker.Object);

        var fired = false;
        vm.EnvironmentOpenRequested.Subscribe(_ => fired = true);

        await vm.OpenEnvironmentFileCommand.Execute();

        fired.Should().BeFalse();
    }

    [Fact]
    public void BeginRename_SetsIsEditingAndEditingNameOnItem()
    {
        var vm = NewVm();
        var envRef = new EnvironmentRef("Dev", "/tmp/dev.toml");
        vm.SetEnvironments(new[] { envRef });
        var item = vm.Items.Single();

        vm.RenameEnvironmentCommand.Execute(item).Subscribe();

        item.IsEditing.Should().BeTrue();
        item.EditingName.Should().Be("Dev");
        vm.EditingItem.Should().BeSameAs(item);
    }

    [Fact]
    public void CancelRename_ClearsIsEditingAndEditingItem()
    {
        var vm = NewVm();
        vm.SetEnvironments(new[] { new EnvironmentRef("Dev", "/tmp/dev.toml") });
        var item = vm.Items.Single();
        vm.RenameEnvironmentCommand.Execute(item).Subscribe();

        vm.CancelRenameCommand.Execute(item).Subscribe();

        item.IsEditing.Should().BeFalse();
        vm.EditingItem.Should().BeNull();
    }

    [Fact]
    public async Task CommitRename_WithBlankName_CancelsWithoutFiringRenamed()
    {
        var vm = NewVm();
        vm.SetEnvironments(new[] { new EnvironmentRef("Dev", "/tmp/dev.toml") });
        var item = vm.Items.Single();
        vm.RenameEnvironmentCommand.Execute(item).Subscribe();
        item.EditingName = "   ";

        var renamed = false;
        vm.EnvironmentRenamed.Subscribe(_ => renamed = true);
        await vm.CommitRenameCommand.Execute(item);

        item.IsEditing.Should().BeFalse();
        renamed.Should().BeFalse();
    }

    [Fact]
    public async Task CommitRename_WithSameName_CancelsWithoutFiringRenamed()
    {
        var vm = NewVm();
        vm.SetEnvironments(new[] { new EnvironmentRef("Dev", "/tmp/dev.toml") });
        var item = vm.Items.Single();
        vm.RenameEnvironmentCommand.Execute(item).Subscribe();
        item.EditingName = "Dev";

        var renamed = false;
        vm.EnvironmentRenamed.Subscribe(_ => renamed = true);
        await vm.CommitRenameCommand.Execute(item);

        item.IsEditing.Should().BeFalse();
        renamed.Should().BeFalse();
    }

    [Fact]
    public void BeginRename_WhenAnotherItemAlreadyEditing_CancelsPreviousItem()
    {
        var vm = NewVm();
        vm.SetEnvironments(new[]
        {
            new EnvironmentRef("Dev", "/tmp/dev.toml"),
            new EnvironmentRef("Prod", "/tmp/prod.toml"),
        });
        var first = vm.Items[0];
        var second = vm.Items[1];
        vm.RenameEnvironmentCommand.Execute(first).Subscribe();

        vm.RenameEnvironmentCommand.Execute(second).Subscribe();

        first.IsEditing.Should().BeFalse();
        second.IsEditing.Should().BeTrue();
        vm.EditingItem.Should().BeSameAs(second);
    }
}
