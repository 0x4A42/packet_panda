using System.Reactive.Linq;
using FluentAssertions;
using Moq;
using PacketPanda.Core.Http;
using PacketPanda.Core.Persistence;
using PacketPanda.Desktop.Services;
using PacketPanda.Desktop.ViewModels;

namespace PacketPanda.Tests.ViewModels;

// ReSharper disable NullableWarningSuppressionIsUsed
public sealed class CollectionSidebarViewModelTests
{
    private readonly Mock<ICollectionFileStore> _storeMock = new();
    private readonly Mock<ISettingsStore> _settingsMock = new();
    private readonly Mock<IFilePickerService> _pickerMock = new();
    private readonly Mock<IDialogService> _dialogMock = new();

    private CollectionSidebarViewModel Build() =>
        new(_storeMock.Object, _settingsMock.Object, _pickerMock.Object, _dialogMock.Object);

    private static Collection FakeCollection(string name, string path) =>
        new(name, path, new FolderNode(Guid.NewGuid().ToString(), name, []));

    private CollectionViewModel FakeCollectionVm(Collection col) =>
        new(col, _storeMock.Object, _dialogMock.Object, _ => { });

    [Fact]
    public async Task LoadCollections_AppendsToCollectionsList()
    {
        _storeMock.Setup(s => s.LoadAsync("/a.json")).ReturnsAsync(FakeCollection("A", "/a.json"));
        _settingsMock.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
        _settingsMock.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);
        var vm = Build();

        await vm.LoadCollectionsAsync(["/a.json"]);

        vm.Collections.Should().ContainSingle().Which.Name.Should().Be("A");
    }

    [Fact]
    public async Task LoadCollections_SkipsDuplicatePath()
    {
        _storeMock.Setup(s => s.LoadAsync("/a.json")).ReturnsAsync(FakeCollection("A", "/a.json"));
        _settingsMock.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
        _settingsMock.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);
        var vm = Build();

        await vm.LoadCollectionsAsync(["/a.json", "/a.json"]);

        vm.Collections.Should().ContainSingle();
    }

    [Fact]
    public void RemoveCollection_RemovesFromList()
    {
        _settingsMock.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
        _settingsMock.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);
        var vm = Build();
        var colVm = new CollectionViewModel(FakeCollection("X", "/x.json"), _storeMock.Object, _dialogMock.Object, _ => { });
        vm.Collections.Add(colVm);

        vm.RemoveCollection(colVm);

        vm.Collections.Should().BeEmpty();
    }

    [Fact]
    public async Task RenameFolder_UpdatesNameInVmAndPersists()
    {
        var folderId = Guid.NewGuid().ToString();
        var folder = new FolderNode(folderId, "OldName", []);
        var col = new Collection("Col", "/col.json",
            new FolderNode(Guid.NewGuid().ToString(), "Col", [folder]));
        _storeMock.Setup(s => s.SaveAsync("/col.json", It.IsAny<Collection>())).Returns(Task.CompletedTask);
        var vm = FakeCollectionVm(col);
        var folderVm = vm.Items.Single(i => i.Id == folderId);

        folderVm.RenameFolderCommand!.Execute().Subscribe();  // begins inline edit
        folderVm.EditingName = "NewName";
        await folderVm.CommitRenameCommand!.Execute();

        vm.Items.Should().ContainSingle(i => i.Name == "NewName");
        _storeMock.Verify(s => s.SaveAsync("/col.json",
            It.Is<Collection>(c => ContainsFolder(c.Root, folderId, "NewName"))), Times.Once);
    }

    [Fact]
    public async Task RenameFolder_BlankInput_DoesNotSave()
    {
        var folderId = Guid.NewGuid().ToString();
        var folder = new FolderNode(folderId, "OldName", []);
        var col = new Collection("Col", "/col.json",
            new FolderNode(Guid.NewGuid().ToString(), "Col", [folder]));
        var vm = FakeCollectionVm(col);
        var folderVm = vm.Items.Single(i => i.Id == folderId);

        folderVm.RenameFolderCommand!.Execute().Subscribe();
        folderVm.EditingName = "   ";
        await folderVm.CommitRenameCommand!.Execute();

        _storeMock.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Collection>()), Times.Never);
    }

    [Fact]
    public async Task CopyFolder_AddsCopyWithDifferentIdAndSameName()
    {
        var folderId = Guid.NewGuid().ToString();
        var folder = new FolderNode(folderId, "MyFolder", []);
        var col = new Collection("Col", "/col.json",
            new FolderNode(Guid.NewGuid().ToString(), "Col", [folder]));
        _storeMock.Setup(s => s.SaveAsync("/col.json", It.IsAny<Collection>())).Returns(Task.CompletedTask);
        var vm = FakeCollectionVm(col);

        await vm.Items.Single(i => i.Id == folderId).CopyFolderCommand!.Execute();

        vm.Items.Should().HaveCount(2);
        vm.Items.Should().Contain(i => i.Name == "MyFolder - copy");
        vm.Items.Select(i => i.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task DeleteFolder_Confirmed_RemovesFolderFromVmAndPersists()
    {
        var folderId = Guid.NewGuid().ToString();
        var folder = new FolderNode(folderId, "ToDelete", []);
        var col = new Collection("Col", "/col.json",
            new FolderNode(Guid.NewGuid().ToString(), "Col", [folder]));
        _storeMock.Setup(s => s.SaveAsync("/col.json", It.IsAny<Collection>())).Returns(Task.CompletedTask);
        _dialogMock.Setup(d => d.ConfirmAsync("Delete Folder", It.IsAny<string>())).ReturnsAsync(true);
        var vm = FakeCollectionVm(col);

        await vm.Items.Single(i => i.Id == folderId).DeleteFolderCommand!.Execute();

        vm.Items.Should().BeEmpty();
        _storeMock.Verify(s => s.SaveAsync("/col.json",
            It.Is<Collection>(c => !ContainsAnyFolder(c.Root, folderId))), Times.Once);
    }

    [Fact]
    public async Task DeleteFolder_Cancelled_DoesNotRemoveOrSave()
    {
        var folderId = Guid.NewGuid().ToString();
        var folder = new FolderNode(folderId, "ToDelete", []);
        var col = new Collection("Col", "/col.json",
            new FolderNode(Guid.NewGuid().ToString(), "Col", [folder]));
        _dialogMock.Setup(d => d.ConfirmAsync("Delete Folder", It.IsAny<string>())).ReturnsAsync(false);
        var vm = FakeCollectionVm(col);

        await vm.Items.Single(i => i.Id == folderId).DeleteFolderCommand!.Execute();

        vm.Items.Should().ContainSingle();
        _storeMock.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Collection>()), Times.Never);
    }

    private static bool ContainsFolder(FolderNode root, string id, string name)
    {
        if (root.Id == id) return root.Name == name;
        return root.Children.OfType<FolderNode>().Any(f => ContainsFolder(f, id, name));
    }

    private static bool ContainsAnyFolder(FolderNode root, string id)
    {
        if (root.Id == id) return true;
        return root.Children.OfType<FolderNode>().Any(f => ContainsAnyFolder(f, id));
    }

    [Fact]
    public async Task OpenFilesCommand_AddsCollectionsForEachPickedFile()
    {
        _pickerMock.Setup(p => p.PickOpenMultipleAsync()).ReturnsAsync(["/b.json", "/c.json"]);
        _storeMock.Setup(s => s.LoadAsync("/b.json")).ReturnsAsync(FakeCollection("B", "/b.json"));
        _storeMock.Setup(s => s.LoadAsync("/c.json")).ReturnsAsync(FakeCollection("C", "/c.json"));
        _settingsMock.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
        _settingsMock.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);
        var vm = Build();

        await vm.OpenFilesCommand.Execute();

        vm.Collections.Should().HaveCount(2);
    }

    [Fact]
    public async Task RunCollectionCommand_InvokesOnFolderRunRequestedWithRoot()
    {
        var root = new FolderNode(Guid.NewGuid().ToString(), "My Collection", []);
        var collection = new Collection("My Collection", "/my.json", root);
        FolderNode? captured = null;

        var vm = new CollectionViewModel(
            collection,
            _storeMock.Object,
            _dialogMock.Object,
            _ => { },
            onFolderRunRequested: f => captured = f);

        await vm.RunCollectionCommand.Execute();

        captured.Should().BeSameAs(root);
    }

    [Fact]
    public async Task PasteCurl_ValidCurlString_FiresPasteCurlRequested()
    {
        _dialogMock.Setup(d => d.ShowImportCurlAsync())
            .ReturnsAsync("curl https://example.com");

        var vm = Build();
        ParsedCurlRequest? fired = null;
        vm.PasteCurlRequested.Subscribe(r => fired = r);

        await vm.PasteCurlCommand.Execute();

        fired.Should().NotBeNull();
        fired!.Url.Should().Be("https://example.com");
    }

    [Fact]
    public async Task PasteCurl_UnparseableCurlString_SetsErrorMessage()
    {
        _dialogMock.Setup(d => d.ShowImportCurlAsync())
            .ReturnsAsync("not a curl command");

        var vm = Build();
        await vm.PasteCurlCommand.Execute();

        vm.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task PasteCurl_UserCancels_DoesNotFireEventOrSetError()
    {
        _dialogMock.Setup(d => d.ShowImportCurlAsync()).ReturnsAsync((string?)null);

        var vm = Build();
        ParsedCurlRequest? fired = null;
        vm.PasteCurlRequested.Subscribe(r => fired = r);

        await vm.PasteCurlCommand.Execute();

        fired.Should().BeNull();
        vm.ErrorMessage.Should().BeNull();
    }
}
