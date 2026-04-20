using System.Reactive.Linq;
using FluentAssertions;
using Moq;
using PacketPanda.Core.Persistence;
using PacketPanda.Desktop.Services;
using PacketPanda.Desktop.ViewModels;

namespace PacketPanda.Tests.ViewModels;

public class CollectionSidebarViewModelTests
{
    private readonly Mock<IFolderPickerService> _picker = new(MockBehavior.Strict);
    private readonly Mock<IWorkspaceScanner> _scanner = new(MockBehavior.Strict);
    private readonly Mock<ISettingsStore> _settings = new(MockBehavior.Strict);

    private CollectionSidebarViewModel CreateVm() =>
        new(_picker.Object, _scanner.Object, _settings.Object);

    [Fact]
    public async Task OpenWorkspaceCommand_Cancelled_LeavesStateUnchanged()
    {
        _picker.Setup(p => p.PickFolderAsync()).ReturnsAsync((string?)null);
        var vm = CreateVm();

        await vm.OpenWorkspaceCommand.Execute();

        vm.RootNodes.Should().BeEmpty();
        vm.WorkspacePath.Should().BeNull();
        _scanner.Verify(s => s.ScanAsync(It.IsAny<string>()), Times.Never);
        _settings.Verify(s => s.SaveAsync(It.IsAny<AppSettings>()), Times.Never);
    }

    [Fact]
    public async Task OpenWorkspaceCommand_Success_PopulatesRootNodesAndSavesSettings()
    {
        var tree = new FolderNode("root", "/ws", new CollectionNode[]
        {
            new RequestNode("a.toml", "/ws/a.toml")
        });
        _picker.Setup(p => p.PickFolderAsync()).ReturnsAsync("/ws");
        _scanner.Setup(s => s.ScanAsync("/ws")).ReturnsAsync(tree);
        _settings.Setup(s => s.SaveAsync(It.Is<AppSettings>(a => a.LastWorkspacePath == "/ws")))
                 .Returns(Task.CompletedTask);

        var vm = CreateVm();

        await vm.OpenWorkspaceCommand.Execute();

        vm.WorkspacePath.Should().Be("/ws");
        vm.RootNodes.Should().ContainSingle()
            .Which.Name.Should().Be("a.toml");
        _settings.VerifyAll();
    }

    [Fact]
    public async Task OpenWorkspaceCommand_ScanFailure_LeavesRootNodesEmptyAndSetsError()
    {
        _picker.Setup(p => p.PickFolderAsync()).ReturnsAsync("/bad");
        _scanner.Setup(s => s.ScanAsync("/bad"))
                .ThrowsAsync(new DirectoryNotFoundException("not there"));

        var vm = CreateVm();

        await vm.OpenWorkspaceCommand.Execute();

        vm.RootNodes.Should().BeEmpty();
        vm.ErrorMessage.Should().NotBeNullOrEmpty();
        _settings.Verify(s => s.SaveAsync(It.IsAny<AppSettings>()), Times.Never);
    }

    [Fact]
    public async Task LoadWorkspace_PopulatesRootNodesWithoutPickerOrSettings()
    {
        var tree = new FolderNode("root", "/ws", new CollectionNode[]
        {
            new RequestNode("a.toml", "/ws/a.toml")
        });
        _scanner.Setup(s => s.ScanAsync("/ws")).ReturnsAsync(tree);

        var vm = CreateVm();

        await vm.LoadWorkspaceAsync("/ws");

        vm.WorkspacePath.Should().Be("/ws");
        vm.RootNodes.Should().ContainSingle();
        _picker.Verify(p => p.PickFolderAsync(), Times.Never);
        _settings.Verify(s => s.SaveAsync(It.IsAny<AppSettings>()), Times.Never);
    }

    [Fact]
    public async Task RequestOpened_FiresWhenRequestNodeActivated()
    {
        var vm = CreateVm();
        var node = new CollectionNodeViewModel("a.toml", "/ws/a.toml", isFolder: false, []);

        string? received = null;
        using var _ = vm.RequestOpened.Subscribe(p => received = p);

        await vm.OpenRequestCommand.Execute(node);

        received.Should().Be("/ws/a.toml");
    }

    [Fact]
    public async Task RequestOpened_DoesNotFireForFolderNodes()
    {
        var vm = CreateVm();
        var folder = new CollectionNodeViewModel("api", "/ws/api", isFolder: true, []);

        string? received = null;
        using var _ = vm.RequestOpened.Subscribe(p => received = p);

        await vm.OpenRequestCommand.Execute(folder);

        received.Should().BeNull();
    }
}
