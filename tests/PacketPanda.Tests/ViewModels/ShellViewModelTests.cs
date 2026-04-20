using System.Reactive.Linq;
using FluentAssertions;
using Moq;
using PacketPanda.Core.Http;
using PacketPanda.Core.Persistence;
using PacketPanda.Desktop.Services;
using PacketPanda.Desktop.ViewModels;
using HttpMethod = PacketPanda.Core.Http.HttpMethod;

namespace PacketPanda.Tests.ViewModels;

public class ShellViewModelTests
{
    private readonly Mock<IHttpRequestSender> _sender = new(MockBehavior.Strict);
    private readonly Mock<IRequestFileStore> _store = new(MockBehavior.Strict);
    private readonly Mock<IFilePickerService> _picker = new(MockBehavior.Strict);
    private readonly Mock<IDialogService> _dialog = new(MockBehavior.Strict);

    private readonly Mock<IWorkspaceScanner> _scanner = new(MockBehavior.Strict);
    private readonly Mock<ISettingsStore> _settings = new(MockBehavior.Strict);

    private CollectionSidebarViewModel NewSidebar() =>
        new(_scanner.Object, _settings.Object);

    private RequestEditorViewModel NewEditor() =>
        new(_sender.Object, _store.Object, _picker.Object);

    private ShellViewModel CreateShell(CollectionSidebarViewModel? sidebar = null) =>
        new(sidebar ?? NewSidebar(), NewEditor, _dialog.Object, _store.Object, _picker.Object);

    [Fact]
    public async Task NewTabCommand_AddsUntitledTabAndSetsItActive()
    {
        var shell = CreateShell();

        await shell.NewTabCommand.Execute();

        shell.Tabs.Should().ContainSingle();
        shell.ActiveTab.Should().Be(shell.Tabs[0]);
        shell.ActiveTab!.Title.Should().Be("Untitled");
    }

    [Fact]
    public async Task OpenFileInTab_WithNewPath_LoadsFileAndAddsTab()
    {
        _store.Setup(s => s.LoadAsync("/tmp/a.toml"))
              .ReturnsAsync(new HttpRequest(HttpMethod.Get, "https://example.com", null));

        var shell = CreateShell();

        await shell.OpenFileInTabCommand.Execute("/tmp/a.toml");

        shell.Tabs.Should().ContainSingle();
        shell.ActiveTab!.FilePath.Should().Be("/tmp/a.toml");
        shell.ActiveTab.Editor.Url.Should().Be("https://example.com");
    }

    [Fact]
    public async Task OpenFileInTab_WithAlreadyOpenPath_FocusesExistingTab()
    {
        _store.Setup(s => s.LoadAsync("/tmp/a.toml"))
              .ReturnsAsync(new HttpRequest(HttpMethod.Get, "https://example.com", null));

        var shell = CreateShell();
        await shell.OpenFileInTabCommand.Execute("/tmp/a.toml");
        await shell.NewTabCommand.Execute(); // active tab is now the blank one

        await shell.OpenFileInTabCommand.Execute("/tmp/a.toml");

        shell.Tabs.Should().HaveCount(2);
        shell.ActiveTab!.FilePath.Should().Be("/tmp/a.toml");
        _store.Verify(s => s.LoadAsync("/tmp/a.toml"), Times.Once);
    }

    [Fact]
    public async Task OpenFileInTab_LoadFailure_AddsTabWithErrorMessage()
    {
        _store.Setup(s => s.LoadAsync("/tmp/bad.toml"))
              .ThrowsAsync(new RequestFileException("Invalid TOML: bad"));

        var shell = CreateShell();

        await shell.OpenFileInTabCommand.Execute("/tmp/bad.toml");

        shell.Tabs.Should().ContainSingle();
        shell.ActiveTab!.Editor.ErrorMessage.Should().Be("Invalid TOML: bad");
        shell.ActiveTab.FilePath.Should().Be("/tmp/bad.toml");
    }

    [Fact]
    public async Task SidebarRequestOpened_TriggersOpenFileInTab()
    {
        _store.Setup(s => s.LoadAsync("/ws/x.toml"))
              .ReturnsAsync(new HttpRequest(HttpMethod.Get, "https://x.com", null));

        var sidebar = NewSidebar();
        var shell = CreateShell(sidebar);
        var node = new CollectionNodeViewModel("x.toml", "/ws/x.toml", isFolder: false, method: null, []);

        await sidebar.OpenRequestCommand.Execute(node);

        shell.Tabs.Should().ContainSingle();
        shell.ActiveTab!.FilePath.Should().Be("/ws/x.toml");
    }

    [Fact]
    public async Task CloseTab_CleanTab_RemovesTabAndActivatesAdjacent()
    {
        var shell = CreateShell();
        await shell.NewTabCommand.Execute();
        await shell.NewTabCommand.Execute();
        var first = shell.Tabs[0];
        shell.ActiveTab = first;

        await first.CloseCommand.Execute();

        shell.Tabs.Should().ContainSingle();
        shell.ActiveTab.Should().Be(shell.Tabs[0]);
    }

    [Fact]
    public async Task CloseTab_LastTab_LeavesActiveTabNull()
    {
        var shell = CreateShell();
        await shell.NewTabCommand.Execute();

        await shell.Tabs[0].CloseCommand.Execute();

        shell.Tabs.Should().BeEmpty();
        shell.ActiveTab.Should().BeNull();
    }

    [Fact]
    public async Task CloseTab_DirtyTabUserConfirms_RemovesTab()
    {
        _dialog.Setup(d => d.ConfirmDiscardAsync(It.IsAny<string>())).ReturnsAsync(true);

        var shell = CreateShell();
        await shell.NewTabCommand.Execute();
        shell.Tabs[0].Editor.Url = "https://dirty.com";

        await shell.Tabs[0].CloseCommand.Execute();

        shell.Tabs.Should().BeEmpty();
    }

    [Fact]
    public async Task CloseTab_DirtyTabUserCancels_DoesNotRemoveTab()
    {
        _dialog.Setup(d => d.ConfirmDiscardAsync(It.IsAny<string>())).ReturnsAsync(false);

        var shell = CreateShell();
        await shell.NewTabCommand.Execute();
        shell.Tabs[0].Editor.Url = "https://dirty.com";

        await shell.Tabs[0].CloseCommand.Execute();

        shell.Tabs.Should().ContainSingle();
    }

    [Fact]
    public async Task OpenFileCommand_PickerReturnsPath_OpensFileInTabAndLoadsWorkspace()
    {
        const string pickedPath = "/tmp/picked.toml";

        _picker.Setup(p => p.PickOpenAsync()).ReturnsAsync(pickedPath);
        _store.Setup(s => s.LoadAsync(pickedPath))
              .ReturnsAsync(new HttpRequest(HttpMethod.Get, "https://picked.com", null));
        _scanner.Setup(s => s.ScanAsync(It.IsAny<string>()))
                .ReturnsAsync(new WorkspaceScan(
                    new FolderNode("dir", "dir", Array.Empty<CollectionNode>()),
                    Array.Empty<PacketPanda.Core.Environments.EnvironmentRef>()));
        _settings.Setup(s => s.SaveAsync(It.IsAny<AppSettings>()))
                 .Returns(Task.CompletedTask);

        var shell = CreateShell();

        await shell.OpenFileCommand.Execute();

        shell.Tabs.Should().ContainSingle();
        shell.ActiveTab!.FilePath.Should().Be(pickedPath);
        shell.ActiveTab.Editor.Url.Should().Be("https://picked.com");
        shell.Sidebar.WorkspacePath.Should().NotBeNull();
    }

    [Fact]
    public async Task OpenFileCommand_PickerCancelled_DoesNotAddTab()
    {
        _picker.Setup(p => p.PickOpenAsync()).ReturnsAsync((string?)null);

        var shell = CreateShell();

        await shell.OpenFileCommand.Execute();

        shell.Tabs.Should().BeEmpty();
    }
}
