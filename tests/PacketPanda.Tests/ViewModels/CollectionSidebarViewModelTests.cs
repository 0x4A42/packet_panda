using System.Reactive.Linq;
using FluentAssertions;
using Moq;
using PacketPanda.Core.Persistence;
using PacketPanda.Desktop.Services;
using PacketPanda.Desktop.ViewModels;

namespace PacketPanda.Tests.ViewModels;

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
}
