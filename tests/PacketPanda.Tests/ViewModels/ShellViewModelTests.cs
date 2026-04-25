using System.Reactive.Linq;
using System.Reactive.Subjects;
using FluentAssertions;
using Moq;
using PacketPanda.Core.Environments;
using PacketPanda.Core.Http;
using PacketPanda.Core.Persistence;
using PacketPanda.Desktop.Services;
using PacketPanda.Desktop.ViewModels;
using Environment = PacketPanda.Core.Environments.Environment;
using HttpMethod = PacketPanda.Core.Http.HttpMethod;

namespace PacketPanda.Tests.ViewModels;

public class ShellViewModelTests
{
    private readonly Mock<IHttpRequestSender> _sender = new(MockBehavior.Strict);
    private readonly Mock<IRequestFileStore> _store = new(MockBehavior.Strict);
    private readonly Mock<IFilePickerService> _picker = new(MockBehavior.Strict);
    private readonly Mock<IDialogService> _dialog = new(MockBehavior.Strict);

    private readonly Mock<ICollectionFileStore> _collectionStore = new(MockBehavior.Strict);
    private readonly Mock<ISettingsStore> _settings = new(MockBehavior.Strict);
    private readonly Mock<IEnvironmentFileStore> _environmentStore = new(MockBehavior.Strict);

    private CollectionSidebarViewModel NewSidebar() =>
        new(_collectionStore.Object, _settings.Object, _picker.Object, _dialog.Object);

    private RequestEditorViewModel NewEditor() =>
        new(_sender.Object, _store.Object, _picker.Object,
            new VariableResolver(),
            new BehaviorSubject<Environment?>(null));

    private ShellViewModel CreateShell(CollectionSidebarViewModel? sidebar = null) =>
        new(sidebar ?? NewSidebar(), NewEditor, _dialog.Object, _store.Object, _picker.Object,
            _environmentStore.Object, _settings.Object);

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
        ((RequestEditorViewModel)shell.ActiveTab.Content).Url.Should().Be("https://example.com");
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
        ((RequestEditorViewModel)shell.ActiveTab!.Content).ErrorMessage.Should().Be("Invalid TOML: bad");
        shell.ActiveTab.FilePath.Should().Be("/tmp/bad.toml");
    }

    [Fact]
    public async Task SidebarRequestOpened_TriggersOpenRequestNodeInTab()
    {
        var sidebar = NewSidebar();
        var shell = CreateShell(sidebar);
        var rn = new RequestNode("r1", "my-request", "/ws/col.json", HttpMethod.Get, "https://x.com", null);

        // Load a fake collection so the sidebar wires up the callback -> RequestOpened
        var collection = new PacketPanda.Core.Persistence.Collection("col", "/ws/col.json",
            new FolderNode("root", "root", [rn]));
        _collectionStore.Setup(s => s.LoadAsync("/ws/col.json")).ReturnsAsync(collection);
        _settings.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
        _settings.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);
        await sidebar.LoadCollectionsAsync(["/ws/col.json"]);

        // Open the request node via its view-model callback
        sidebar.Collections[0].Items[0].Open();

        shell.Tabs.Should().ContainSingle();
        ((RequestEditorViewModel)shell.ActiveTab!.Content).CollectionFilePath.Should().Be("/ws/col.json");
    }

    [Fact]
    public async Task SidebarRequestOpened_WithAlreadyOpenRequest_FocusesExistingTab()
    {
        var sidebar = NewSidebar();
        var shell = CreateShell(sidebar);
        var rn = new RequestNode("r1", "my-request", "/ws/col.json", HttpMethod.Get, "https://x.com", null);

        var collection = new PacketPanda.Core.Persistence.Collection("col", "/ws/col.json",
            new FolderNode("root", "root", [rn]));
        _collectionStore.Setup(s => s.LoadAsync("/ws/col.json")).ReturnsAsync(collection);
        _settings.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
        _settings.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);
        await sidebar.LoadCollectionsAsync(["/ws/col.json"]);

        sidebar.Collections[0].Items[0].Open();
        await shell.NewTabCommand.Execute(); // make a different tab active

        sidebar.Collections[0].Items[0].Open();

        shell.Tabs.Should().HaveCount(2);
        shell.ActiveTab!.FilePath.Should().Be("/ws/col.json");
        ((RequestEditorViewModel)shell.ActiveTab.Content).RequestId.Should().Be("r1");
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
        ((RequestEditorViewModel)shell.Tabs[0].Content).Url = "https://dirty.com";

        await shell.Tabs[0].CloseCommand.Execute();

        shell.Tabs.Should().BeEmpty();
    }

    [Fact]
    public async Task CloseTab_DirtyTabUserCancels_DoesNotRemoveTab()
    {
        _dialog.Setup(d => d.ConfirmDiscardAsync(It.IsAny<string>())).ReturnsAsync(false);

        var shell = CreateShell();
        await shell.NewTabCommand.Execute();
        ((RequestEditorViewModel)shell.Tabs[0].Content).Url = "https://dirty.com";

        await shell.Tabs[0].CloseCommand.Execute();

        shell.Tabs.Should().ContainSingle();
    }

    [Fact]
    public async Task OpenFileCommand_PickerReturnsPath_OpensFileInTab()
    {
        const string pickedPath = "/tmp/picked.toml";

        _picker.Setup(p => p.PickOpenAsync()).ReturnsAsync(pickedPath);
        _store.Setup(s => s.LoadAsync(pickedPath))
              .ReturnsAsync(new HttpRequest(HttpMethod.Get, "https://picked.com", null));

        var shell = CreateShell();

        await shell.OpenFileCommand.Execute();

        shell.Tabs.Should().ContainSingle();
        shell.ActiveTab!.FilePath.Should().Be(pickedPath);
        ((RequestEditorViewModel)shell.ActiveTab.Content).Url.Should().Be("https://picked.com");
    }

    [Fact]
    public async Task OpenFileCommand_PickerCancelled_DoesNotAddTab()
    {
        _picker.Setup(p => p.PickOpenAsync()).ReturnsAsync((string?)null);

        var shell = CreateShell();

        await shell.OpenFileCommand.Execute();

        shell.Tabs.Should().BeEmpty();
    }

    [Fact]
    public async Task OpenNewEnvironmentInTabAsync_DoesNotLoadFromStore_OpensPristineTab()
    {
        var shell = CreateShell();

        await shell.OpenNewEnvironmentInTabAsync("New Environment", "/tmp/envs/New Environment.toml");

        _environmentStore.Verify(s => s.LoadAsync(It.IsAny<string>()), Times.Never);
        shell.Tabs.Should().ContainSingle();
        shell.ActiveTab!.IsDirty.Should().BeFalse();
        shell.ActiveTab.DisplayTitle.Should().Be("New Environment");
    }

    [Fact]
    public async Task OpenNewEnvironmentInTabAsync_OnFirstSave_AddsToEnvironmentsForPicker()
    {
        var shell = CreateShell();
        await shell.OpenNewEnvironmentInTabAsync("New Environment", "/tmp/envs/New Environment.toml");
        _environmentStore
            .Setup(s => s.SaveAsync("/tmp/envs/New Environment.toml", It.IsAny<Environment>()))
            .Returns(Task.CompletedTask);

        var envEditor = (EnvironmentEditorViewModel)shell.ActiveTab!.Content;
        await envEditor.SaveCommand.Execute();

        shell.EnvironmentsForPicker.Should().Contain(e =>
            e.Name == "New Environment" && e.FilePath == "/tmp/envs/New Environment.toml");
    }

    [Fact]
    public async Task OpenNewEnvironmentInTabAsync_OnCloseWithoutChanges_SilentlyDiscardsWithoutPrompt()
    {
        var shell = CreateShell();
        await shell.OpenNewEnvironmentInTabAsync("New Environment", "/tmp/envs/New Environment.toml");

        await shell.ActiveTab!.CloseCommand.Execute();

        shell.Tabs.Should().BeEmpty();
        // _dialog is MockBehavior.Strict — ConfirmDiscardAsync being called would throw here,
        // so this assertion doubles as "no discard dialog was shown"
        shell.EnvironmentsForPicker.Should().NotContain(e =>
            e.FilePath == "/tmp/envs/New Environment.toml");
    }

    [Fact]
    public async Task OpenNewEnvironmentInTabAsync_OnSecondSave_UpdatesPickerEntryViaSync()
    {
        var shell = CreateShell();
        await shell.OpenNewEnvironmentInTabAsync("New Environment", "/tmp/envs/New Environment.toml");
        _environmentStore
            .Setup(s => s.SaveAsync("/tmp/envs/New Environment.toml", It.IsAny<Environment>()))
            .Returns(Task.CompletedTask);

        var envEditor = (EnvironmentEditorViewModel)shell.ActiveTab!.Content;
        // First save — adds to picker
        await envEditor.SaveCommand.Execute();
        // Rename and second save — picker entry name should update
        envEditor.EnvironmentName = "Staging";
        await envEditor.SaveCommand.Execute();

        shell.EnvironmentsForPicker.Should().Contain(e =>
            e.Name == "Staging" && e.FilePath == "/tmp/envs/New Environment.toml");
        shell.EnvironmentsForPicker.Should().NotContain(e => e.Name == "New Environment");
    }

    [Fact]
    public async Task OpenNewEnvironmentInTabAsync_WithAlreadyOpenPath_FocusesExistingTab()
    {
        var shell = CreateShell();
        await shell.OpenNewEnvironmentInTabAsync("New Environment", "/tmp/envs/New Environment.toml");
        await shell.NewTabCommand.Execute(); // make a different tab active

        await shell.OpenNewEnvironmentInTabAsync("New Environment", "/tmp/envs/New Environment.toml");

        shell.Tabs.Should().ContainSingle(t => t.FilePath == "/tmp/envs/New Environment.toml");
        shell.ActiveTab!.FilePath.Should().Be("/tmp/envs/New Environment.toml");
    }

    [Fact]
    public async Task DuplicateEnvironment_AddsEntryToEnvironmentsForPicker()
    {
        _dialog.Setup(d => d.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        var envRef = new EnvironmentRef("Dev", "/tmp/dev.toml");
        _environmentStore.Setup(s => s.LoadAsync("/tmp/dev.toml"))
            .ReturnsAsync(new Environment("Dev", "/tmp/dev.toml",
                new Dictionary<string, string>(StringComparer.Ordinal)));
        _environmentStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Environment>()))
            .Returns(Task.CompletedTask);

        var shell = CreateShell();
        shell.EnvironmentSidebar.SetEnvironments(new[] { envRef });

        await shell.EnvironmentSidebar.DuplicateEnvironmentCommand.Execute(envRef);

        shell.EnvironmentsForPicker.Should().Contain(e => e.Name == "Dev - duplicate");
    }

    [Fact]
    public async Task DeleteEnvironment_RemovesEntryFromEnvironmentsForPicker()
    {
        _dialog.Setup(d => d.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

        var shell = CreateShell();
        var envRef = new EnvironmentRef("Dev", "/tmp/dev.toml");
        shell.EnvironmentSidebar.SetEnvironments(new[] { envRef });
        shell.EnvironmentsForPicker.Add(envRef);

        await shell.EnvironmentSidebar.DeleteEnvironmentCommand.Execute(envRef);

        shell.EnvironmentsForPicker.Should().NotContain(e => e.FilePath == "/tmp/dev.toml");
    }

    [Fact]
    public async Task DeleteEnvironment_ClosesOpenTab()
    {
        _dialog.Setup(d => d.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        _environmentStore.Setup(s => s.LoadAsync("/tmp/dev.toml"))
            .ReturnsAsync(new Environment("Dev", "/tmp/dev.toml",
                new Dictionary<string, string>(StringComparer.Ordinal)));

        var shell = CreateShell();
        var envRef = new EnvironmentRef("Dev", "/tmp/dev.toml");
        shell.EnvironmentSidebar.SetEnvironments(new[] { envRef });
        shell.EnvironmentsForPicker.Add(envRef);
        await shell.OpenEnvironmentInTabAsync("/tmp/dev.toml");
        shell.Tabs.Should().ContainSingle();

        await shell.EnvironmentSidebar.DeleteEnvironmentCommand.Execute(envRef);

        shell.Tabs.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteEnvironment_WhenActiveEnvDeleted_ResetsSelectionToNoEnvironment()
    {
        _dialog.Setup(d => d.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        _environmentStore.Setup(s => s.LoadAsync(It.IsAny<string>()))
            .ThrowsAsync(new EnvironmentFileException("gone"));

        var shell = CreateShell();
        var envRef = new EnvironmentRef("Dev", "/tmp/dev.toml");
        shell.EnvironmentSidebar.SetEnvironments(new[] { envRef });
        shell.EnvironmentsForPicker.Add(envRef);
        shell.SelectedEnvironment = envRef;

        await shell.EnvironmentSidebar.DeleteEnvironmentCommand.Execute(envRef);

        shell.SelectedEnvironment!.FilePath.Should().BeEmpty();
        shell.SelectedEnvironment.Name.Should().Be("No environment");
    }
}
