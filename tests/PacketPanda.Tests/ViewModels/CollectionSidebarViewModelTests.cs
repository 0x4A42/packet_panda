using System.Reactive.Linq;
using FluentAssertions;
using Moq;
using PacketPanda.Core.Persistence;
using PacketPanda.Desktop.ViewModels;

namespace PacketPanda.Tests.ViewModels;

public class CollectionSidebarViewModelTests
{
    private readonly Mock<IWorkspaceScanner> _scanner = new(MockBehavior.Strict);
    private readonly Mock<ISettingsStore> _settings = new(MockBehavior.Strict);

    private CollectionSidebarViewModel CreateVm() =>
        new(_scanner.Object, _settings.Object);

    [Fact]
    public async Task LoadWorkspaceAsync_Success_PopulatesRootNodesAndSavesSettings()
    {
        var tree = new FolderNode("root", "/ws", new CollectionNode[]
        {
            new RequestNode("a.toml", "/ws/a.toml")
        });
        _scanner.Setup(s => s.ScanAsync("/ws")).ReturnsAsync(tree);
        _settings.Setup(s => s.SaveAsync(It.Is<AppSettings>(a => a.LastWorkspacePath == "/ws")))
                 .Returns(Task.CompletedTask);

        var vm = CreateVm();
        await vm.LoadWorkspaceAsync("/ws");

        vm.WorkspacePath.Should().Be("/ws");
        vm.RootNodes.Should().ContainSingle().Which.Name.Should().Be("a.toml");
        _settings.Verify(s => s.SaveAsync(It.Is<AppSettings>(a => a.LastWorkspacePath == "/ws")), Times.Once);
    }

    [Fact]
    public async Task LoadWorkspaceAsync_ScanFailure_SetsErrorAndDoesNotSaveSettings()
    {
        _scanner.Setup(s => s.ScanAsync("/bad"))
                .ThrowsAsync(new DirectoryNotFoundException("not there"));

        var vm = CreateVm();
        await vm.LoadWorkspaceAsync("/bad");

        vm.RootNodes.Should().BeEmpty();
        vm.ErrorMessage.Should().NotBeNullOrEmpty();
        _settings.Verify(s => s.SaveAsync(It.IsAny<AppSettings>()), Times.Never);
    }

    [Fact]
    public async Task CloseWorkspaceCommand_ClearsStateAndSavesSettings()
    {
        var tree = new FolderNode("root", "/ws", []);
        _scanner.Setup(s => s.ScanAsync("/ws")).ReturnsAsync(tree);
        _settings.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);

        var vm = CreateVm();
        await vm.LoadWorkspaceAsync("/ws");

        await vm.CloseWorkspaceCommand.Execute();

        vm.WorkspacePath.Should().BeNull();
        vm.RootNodes.Should().BeEmpty();
        _settings.Verify(s => s.SaveAsync(It.Is<AppSettings>(a => a.LastWorkspacePath == null)), Times.Once);
    }

    [Fact]
    public void SelectedNode_SetToRequestNode_FiresRequestOpened()
    {
        var vm = CreateVm();
        var node = new CollectionNodeViewModel("a.toml", "/ws/a.toml", isFolder: false, method: null, []);

        string? received = null;
        vm.RequestOpened.Subscribe(p => received = p);

        vm.SelectedNode = node;

        received.Should().Be("/ws/a.toml");
    }

    [Fact]
    public void SelectedNode_SetToFolderNode_DoesNotFireRequestOpened()
    {
        var vm = CreateVm();
        var folder = new CollectionNodeViewModel("api", "/ws/api", isFolder: true, method: null, []);

        string? received = null;
        vm.RequestOpened.Subscribe(p => received = p);

        vm.SelectedNode = folder;

        received.Should().BeNull();
    }

    [Fact]
    public async Task RequestOpened_FiresWhenRequestNodeActivated()
    {
        var vm = CreateVm();
        var node = new CollectionNodeViewModel("a.toml", "/ws/a.toml", isFolder: false, method: null, []);

        string? received = null;
        using var _ = vm.RequestOpened.Subscribe(p => received = p);

        await vm.OpenRequestCommand.Execute(node);

        received.Should().Be("/ws/a.toml");
    }

    [Fact]
    public async Task RequestOpened_DoesNotFireForFolderNodes()
    {
        var vm = CreateVm();
        var folder = new CollectionNodeViewModel("api", "/ws/api", isFolder: true, method: null, []);

        string? received = null;
        using var _ = vm.RequestOpened.Subscribe(p => received = p);

        await vm.OpenRequestCommand.Execute(folder);

        received.Should().BeNull();
    }
}
