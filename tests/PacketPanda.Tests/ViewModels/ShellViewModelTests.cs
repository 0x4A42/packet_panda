using System.Reactive.Linq;
using System.Reactive.Subjects;
using FluentAssertions;
using Moq;
using PacketPanda.Core.Environments;
using PacketPanda.Core.History;
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
    private readonly Mock<IFilePickerService> _picker = new(MockBehavior.Strict);
    private readonly Mock<IDialogService> _dialog = new(MockBehavior.Strict);

    private readonly Mock<ICollectionFileStore> _collectionStore = new(MockBehavior.Strict);
    private readonly Mock<ISettingsStore> _settings = new(MockBehavior.Strict);
    private readonly Mock<IEnvironmentFileStore> _environmentStore = new(MockBehavior.Strict);
    private readonly Mock<IHistoryStore> _historyStore = new(MockBehavior.Loose);

    private CollectionSidebarViewModel NewSidebar() =>
        new(_collectionStore.Object, _settings.Object, _picker.Object, _dialog.Object);

    private RequestEditorViewModel NewEditor() =>
        new(_sender.Object, _collectionStore.Object,
            new VariableResolver(),
            new BehaviorSubject<EnvironmentContext?>(null));

    private ShellViewModel CreateShell(CollectionSidebarViewModel? sidebar = null)
    {
        _historyStore.Setup(s => s.LoadAsync()).ReturnsAsync(Array.Empty<HistoryEntry>());
        return new(sidebar ?? NewSidebar(), NewEditor, _dialog.Object, _environmentStore.Object, _picker.Object,
            _settings.Object, _historyStore.Object);
    }

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
    public async Task SidebarRequestOpened_TriggersOpenRequestNodeInTab()
    {
        var sidebar = NewSidebar();
        var shell = CreateShell(sidebar);
        var rn = new RequestNode("r1", "my-request", "/ws/col.json", HttpMethod.Get, "https://x.com", new NoBody(),
            Array.Empty<HttpHeader>(), new NoAuth(), Array.Empty<QueryParam>());

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
        var rn = new RequestNode("r1", "my-request", "/ws/col.json", HttpMethod.Get, "https://x.com", new NoBody(),
            Array.Empty<HttpHeader>(), new NoAuth(), Array.Empty<QueryParam>());

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
    public async Task OpenNewEnvironmentInTabAsync_DoesNotLoadFromStore_OpensPristineTab()
    {
        var shell = CreateShell();

        await shell.OpenNewEnvironmentInTabAsync("New Environment", "/tmp/envs/New Environment.json");

        _environmentStore.Verify(s => s.LoadAsync(It.IsAny<string>()), Times.Never);
        shell.Tabs.Should().ContainSingle();
        shell.ActiveTab!.IsDirty.Should().BeFalse();
        shell.ActiveTab.DisplayTitle.Should().Be("New Environment");
    }

    [Fact]
    public async Task OpenNewEnvironmentInTabAsync_OnFirstSave_AddsToEnvironmentsForPicker()
    {
        var shell = CreateShell();
        await shell.OpenNewEnvironmentInTabAsync("New Environment", "/tmp/envs/New Environment.json");
        _picker.Setup(p => p.PickSaveEnvironmentAsync("New Environment"))
            .ReturnsAsync("/tmp/envs/New Environment.json");
        _environmentStore
            .Setup(s => s.SaveAsync("/tmp/envs/New Environment.json", It.IsAny<Environment>()))
            .Returns(Task.CompletedTask);

        var envEditor = (EnvironmentEditorViewModel)shell.ActiveTab!.Content;
        await envEditor.SaveCommand.Execute();

        shell.EnvironmentsForPicker.Should().Contain(e =>
            e.Name == "New Environment" && e.FilePath == "/tmp/envs/New Environment.json");
    }

    [Fact]
    public async Task OpenNewEnvironmentInTabAsync_OnCloseWithoutChanges_SilentlyDiscardsWithoutPrompt()
    {
        var shell = CreateShell();
        await shell.OpenNewEnvironmentInTabAsync("New Environment", "/tmp/envs/New Environment.json");

        await shell.ActiveTab!.CloseCommand.Execute();

        shell.Tabs.Should().BeEmpty();
        // _dialog is MockBehavior.Strict — ConfirmDiscardAsync being called would throw here,
        // so this assertion doubles as "no discard dialog was shown"
        shell.EnvironmentsForPicker.Should().NotContain(e =>
            e.FilePath == "/tmp/envs/New Environment.json");
    }

    [Fact]
    public async Task OpenNewEnvironmentInTabAsync_OnSecondSave_UpdatesPickerEntryViaSync()
    {
        var shell = CreateShell();
        await shell.OpenNewEnvironmentInTabAsync("New Environment", "/tmp/envs/New Environment.json");
        _picker.Setup(p => p.PickSaveEnvironmentAsync("New Environment"))
            .ReturnsAsync("/tmp/envs/New Environment.json");
        _environmentStore
            .Setup(s => s.SaveAsync("/tmp/envs/New Environment.json", It.IsAny<Environment>()))
            .Returns(Task.CompletedTask);

        var envEditor = (EnvironmentEditorViewModel)shell.ActiveTab!.Content;
        // First save — adds to picker
        await envEditor.SaveCommand.Execute();
        // Rename and second save — picker entry name should update
        envEditor.EnvironmentName = "Staging";
        await envEditor.SaveCommand.Execute();

        shell.EnvironmentsForPicker.Should().Contain(e =>
            e.Name == "Staging" && e.FilePath == "/tmp/envs/New Environment.json");
        shell.EnvironmentsForPicker.Should().NotContain(e => e.Name == "New Environment");
    }

    [Fact]
    public async Task OpenNewEnvironmentInTabAsync_WithAlreadyOpenPath_FocusesExistingTab()
    {
        var shell = CreateShell();
        await shell.OpenNewEnvironmentInTabAsync("New Environment", "/tmp/envs/New Environment.json");
        await shell.NewTabCommand.Execute(); // make a different tab active

        await shell.OpenNewEnvironmentInTabAsync("New Environment", "/tmp/envs/New Environment.json");

        shell.Tabs.Should().ContainSingle(t => t.FilePath == "/tmp/envs/New Environment.json");
        shell.ActiveTab!.FilePath.Should().Be("/tmp/envs/New Environment.json");
    }

    [Fact]
    public async Task DuplicateEnvironment_AddsEntryToEnvironmentsForPicker()
    {
        _dialog.Setup(d => d.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        var envRef = new EnvironmentRef("Dev", "/tmp/dev.json");
        _environmentStore.Setup(s => s.LoadAsync("/tmp/dev.json"))
            .ReturnsAsync(new Environment("Dev", "/tmp/dev.json",
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
        var envRef = new EnvironmentRef("Dev", "/tmp/dev.json");
        shell.EnvironmentSidebar.SetEnvironments(new[] { envRef });
        shell.EnvironmentsForPicker.Add(envRef);

        await shell.EnvironmentSidebar.DeleteEnvironmentCommand.Execute(envRef);

        shell.EnvironmentsForPicker.Should().NotContain(e => e.FilePath == "/tmp/dev.json");
    }

    [Fact]
    public async Task DeleteEnvironment_ClosesOpenTab()
    {
        _dialog.Setup(d => d.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        _environmentStore.Setup(s => s.LoadAsync("/tmp/dev.json"))
            .ReturnsAsync(new Environment("Dev", "/tmp/dev.json",
                new Dictionary<string, string>(StringComparer.Ordinal)));

        var shell = CreateShell();
        var envRef = new EnvironmentRef("Dev", "/tmp/dev.json");
        shell.EnvironmentSidebar.SetEnvironments(new[] { envRef });
        shell.EnvironmentsForPicker.Add(envRef);
        await shell.OpenEnvironmentInTabAsync("/tmp/dev.json");
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
        var envRef = new EnvironmentRef("Dev", "/tmp/dev.json");
        shell.EnvironmentSidebar.SetEnvironments(new[] { envRef });
        shell.EnvironmentsForPicker.Add(envRef);
        shell.SelectedEnvironment = envRef;

        await shell.EnvironmentSidebar.DeleteEnvironmentCommand.Execute(envRef);

        shell.SelectedEnvironment!.FilePath.Should().BeEmpty();
        shell.SelectedEnvironment.Name.Should().Be("No environment");
    }

    [Fact]
    public async Task InitializeAsync_WithActiveEnvironmentName_RestoresSelectedEnvironment()
    {
        var globalsPath = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "PacketPanda", "globals.json");

        // InitializeAsync gates on File.Exists, so we need a real path on disk.
        var devPath = Path.GetTempFileName();
        try
        {
            var devEnv = new Environment("dev", devPath,
                new Dictionary<string, string>(StringComparer.Ordinal));
            var globals = new Environment("Globals", globalsPath,
                new Dictionary<string, string>(StringComparer.Ordinal));

            _environmentStore.Setup(s => s.LoadAsync(devPath)).ReturnsAsync(devEnv);
            _environmentStore.Setup(s => s.LoadAsync(globalsPath)).ReturnsAsync(globals);
            _settings.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
            _settings.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);

            var shell = CreateShell();
            await shell.InitializeAsync(new AppSettings
            {
                ActiveEnvironmentName = "dev",
                KnownEnvironmentPaths = [devPath],
            });

            shell.SelectedEnvironment.Should().NotBeNull();
            shell.SelectedEnvironment!.Name.Should().Be("dev");
        }
        finally
        {
            File.Delete(devPath);
        }
    }

    [Fact]
    public async Task UpdateActiveEnvironmentVariableAsync_Globals_WritesToGlobalsRegardlessOfActiveEnv()
    {
        var globalsPath = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "PacketPanda", "globals.json");

        var activeEnv = new Environment("dev", "/dev.json",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["token"] = "active-val" });
        var globals = new Environment("Globals", globalsPath,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["token"] = "global-val" });

        _environmentStore.Setup(s => s.LoadAsync("/dev.json")).ReturnsAsync(activeEnv);
        _environmentStore.Setup(s => s.LoadAsync(globalsPath)).ReturnsAsync(globals);

        Environment? savedGlobals = null;
        _environmentStore
            .Setup(s => s.SaveAsync(globalsPath, It.IsAny<Environment>()))
            .Callback<string, Environment>((_, e) => savedGlobals = e)
            .Returns(Task.CompletedTask);
        _environmentStore
            .Setup(s => s.SaveAsync("/dev.json", It.IsAny<Environment>()))
            .Returns(Task.CompletedTask);

        _settings.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
        _settings.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);

        var shell = CreateShell();
        await shell.InitializeAsync(new AppSettings
        {
            ActiveEnvironmentName = "dev",
            KnownEnvironmentPaths = ["/dev.json"],
        });

        await shell.UpdateActiveEnvironmentVariableAsync("token", "new-val", VariableTarget.Globals);

        savedGlobals.Should().NotBeNull();
        savedGlobals!.Variables["token"].Should().Be("new-val");
    }

    [Fact]
    public async Task UpdateActiveEnvironmentVariableAsync_ActiveEnv_WritesToActiveEnvNotGlobals()
    {
        var globalsPath = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "PacketPanda", "globals.json");

        var activeEnv = new Environment("dev", "/dev.json",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["token"] = "active-val" });
        var globals = new Environment("Globals", globalsPath,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["token"] = "global-val" });

        _environmentStore.Setup(s => s.LoadAsync("/dev.json")).ReturnsAsync(activeEnv);
        _environmentStore.Setup(s => s.LoadAsync(globalsPath)).ReturnsAsync(globals);

        Environment? savedActiveEnv = null;
        _environmentStore
            .Setup(s => s.SaveAsync("/dev.json", It.IsAny<Environment>()))
            .Callback<string, Environment>((_, e) => savedActiveEnv = e)
            .Returns(Task.CompletedTask);
        _environmentStore
            .Setup(s => s.SaveAsync(globalsPath, It.IsAny<Environment>()))
            .Returns(Task.CompletedTask);

        _settings.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
        _settings.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);

        var shell = CreateShell();
        await shell.InitializeAsync(new AppSettings
        {
            ActiveEnvironmentName = "dev",
            KnownEnvironmentPaths = ["/dev.json"],
        });
        shell.SelectedEnvironment = new EnvironmentRef("dev", "/dev.json");

        await shell.UpdateActiveEnvironmentVariableAsync("token", "new-val", VariableTarget.ActiveEnv);

        savedActiveEnv.Should().NotBeNull();
        savedActiveEnv!.Variables["token"].Should().Be("new-val");
    }

    [Fact]
    public async Task UpdateActiveEnvironmentVariableAsync_ActiveEnvTarget_VariableOnlyInGlobals_WritesToActiveEnvNotGlobals()
    {
        var globalsPath = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "PacketPanda", "globals.json");

        // Variable exists only in globals, not in the active env.
        var activeEnv = new Environment("dev", "/dev.json",
            new Dictionary<string, string>(StringComparer.Ordinal));
        var globals = new Environment("Globals", globalsPath,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["token"] = "global-val" });

        _environmentStore.Setup(s => s.LoadAsync("/dev.json")).ReturnsAsync(activeEnv);
        _environmentStore.Setup(s => s.LoadAsync(globalsPath)).ReturnsAsync(globals);

        Environment? savedActiveEnv = null;
        _environmentStore
            .Setup(s => s.SaveAsync("/dev.json", It.IsAny<Environment>()))
            .Callback<string, Environment>((_, e) => savedActiveEnv = e)
            .Returns(Task.CompletedTask);
        _environmentStore
            .Setup(s => s.SaveAsync(globalsPath, It.IsAny<Environment>()))
            .Returns(Task.CompletedTask);

        _settings.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
        _settings.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);

        var shell = CreateShell();
        await shell.InitializeAsync(new AppSettings
        {
            ActiveEnvironmentName = "dev",
            KnownEnvironmentPaths = ["/dev.json"],
        });
        shell.SelectedEnvironment = new EnvironmentRef("dev", "/dev.json");

        await shell.UpdateActiveEnvironmentVariableAsync("token", "new-val", VariableTarget.ActiveEnv);

        savedActiveEnv.Should().NotBeNull();
        savedActiveEnv!.Variables["token"].Should().Be("new-val");
        _environmentStore.Verify(s => s.SaveAsync(globalsPath, It.IsAny<Environment>()), Times.Never);
    }

    [Fact]
    public async Task SelectedEnvironment_SetToNull_DoesNotClearActiveEnvironment()
    {
        // Simulate the transient null that Avalonia's ComboBox sends through the two-way
        // binding when a collection Replace event fires (e.g. during SyncEnvRefOnSave).
        var globalsPath = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "PacketPanda", "globals.json");

        var activeEnv = new Environment("dev", "/dev.json",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["x"] = "1" });
        var globals = new Environment("Globals", globalsPath,
            new Dictionary<string, string>(StringComparer.Ordinal));

        _environmentStore.Setup(s => s.LoadAsync("/dev.json")).ReturnsAsync(activeEnv);
        _environmentStore.Setup(s => s.LoadAsync(globalsPath)).ReturnsAsync(globals);
        _settings.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
        _settings.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);

        var shell = CreateShell();
        await shell.InitializeAsync(new AppSettings());
        shell.SelectedEnvironment = new EnvironmentRef("dev", "/dev.json");

        EnvironmentContext? lastCtx = null;
        shell.ActiveEnvironmentObservable.Subscribe(ctx => lastCtx = ctx);

        // Simulate what Avalonia's ComboBox does during a collection Replace event.
        shell.SelectedEnvironment = null;

        // The transient null must be discarded entirely — backing field and env context unchanged.
        shell.SelectedEnvironment.Should().NotBeNull();
        lastCtx.Should().NotBeNull();
        lastCtx!.ActiveEnvVars.Should().ContainKey("x");
    }

    [Fact]
    public void OpenDocInTab_SamePathTwice_DoesNotCreateDuplicateTab()
    {
        var shell = CreateShell();
        const string docPath = "/fake/docs/scripting.md";

        shell.OpenDocInTab(docPath);
        var firstTab = shell.ActiveTab;

        shell.OpenDocInTab(docPath);

        shell.Tabs.Should().ContainSingle();
        shell.ActiveTab.Should().BeSameAs(firstTab);
    }

    [Fact]
    public void SetHistoryTabCommand_SetsActiveSidebarTabToHistory()
    {
        var shell = CreateShell();

        shell.SetHistoryTabCommand.Execute().Subscribe();

        shell.ActiveSidebarTab.Should().Be(SidebarTab.History);
    }

    [Fact]
    public async Task SelectedEnvironment_PickerReplaceFiresNull_SelectionNotCleared()
    {
        // Regression: Avalonia ComboBox sends null through the two-way binding during a
        // collection Replace. Before the fix, RaiseAndSetIfChanged wrote null to the backing
        // field, so the FilePath check in HandleEnvironmentRenamed / SyncEnvRefOnSave always
        // evaluated null?.FilePath (false) and skipped re-selecting the renamed env.
        var globalsPath = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "PacketPanda", "globals.json");

        var oldRef = new EnvironmentRef("dev", "/envs/dev.json");
        var newRef = new EnvironmentRef("staging", "/envs/staging.json");
        var activeEnv = new Environment("dev", "/envs/dev.json",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["base"] = "https://api" });
        var renamedEnv = activeEnv with { Name = "staging", FilePath = "/envs/staging.json" };
        var globals = new Environment("Globals", globalsPath,
            new Dictionary<string, string>(StringComparer.Ordinal));

        _environmentStore.Setup(s => s.LoadAsync("/envs/dev.json")).ReturnsAsync(activeEnv);
        _environmentStore.Setup(s => s.LoadAsync("/envs/staging.json")).ReturnsAsync(renamedEnv);
        _environmentStore.Setup(s => s.LoadAsync(globalsPath)).ReturnsAsync(globals);
        _settings.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
        _settings.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);

        var shell = CreateShell();
        await shell.InitializeAsync(new AppSettings());
        shell.EnvironmentsForPicker.Add(oldRef);
        shell.SelectedEnvironment = oldRef;

        // Simulate ComboBox Replace pulse (transient null) then re-select new ref.
        // This mirrors HandleEnvironmentRenamed: capture wasSelected, do Replace, re-select.
        var wasSelected = shell.SelectedEnvironment?.FilePath == oldRef.FilePath;
        shell.EnvironmentsForPicker[shell.EnvironmentsForPicker.IndexOf(oldRef)] = newRef;
        shell.SelectedEnvironment = null; // transient null from ComboBox
        if (wasSelected) shell.SelectedEnvironment = newRef;

        shell.SelectedEnvironment.Should().Be(newRef);
        shell.SelectedEnvironment!.Name.Should().Be("staging");
    }
}
