using System.Reactive.Linq;
using FluentAssertions;
using Moq;
using PacketPanda.Core.Environments;
using PacketPanda.Desktop.Services;
using PacketPanda.Desktop.ViewModels;
using Environment = PacketPanda.Core.Environments.Environment;

namespace PacketPanda.Tests.ViewModels;

public class EnvironmentEditorViewModelTests
{
    private static Mock<IEnvironmentFileStore> NewStore() => new();
    private static Mock<IDialogService> NewDialog()
    {
        var mock = new Mock<IDialogService>();
        mock.Setup(d => d.AlertAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static EnvironmentEditorViewModel NewVm(
        Mock<IEnvironmentFileStore>? store = null,
        Mock<IDialogService>? dialog = null,
        Mock<IFilePickerService>? picker = null) =>
        new((store ?? NewStore()).Object, (dialog ?? NewDialog()).Object,
            (picker ?? new Mock<IFilePickerService>()).Object);

    private static Environment DevEnv(params (string k, string v)[] vars) =>
        new("dev", "/ws/environments/dev.json",
            vars.ToDictionary(p => p.k, p => p.v));

    private static Environment DevEnvWithDescs(
        (string k, string v, string d)[] vars) =>
        new("dev", "/ws/environments/dev.json",
            vars.ToDictionary(p => p.k, p => p.v),
            vars.Where(p => !string.IsNullOrEmpty(p.d))
                .ToDictionary(p => p.k, p => p.d));

    private static Environment BlankNewEnv() =>
        new("New Environment", "/ws/environments/New Environment.json",
            new Dictionary<string, string>(StringComparer.Ordinal));

    [Fact]
    public void Load_PopulatesRowsFromVariables()
    {
        var vm = NewVm();
        vm.Load(DevEnv(("baseUrl", "https://api"), ("apiKey", "abc")));

        // Trailing empty row is always appended
        vm.Rows.Where(r => !string.IsNullOrEmpty(r.Name))
            .Select(r => (r.Name, r.Value)).Should().Equal(
                ("baseUrl", "https://api"), ("apiKey", "abc"));
        vm.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void Load_PopulatesDescriptions()
    {
        var vm = NewVm();
        vm.Load(DevEnvWithDescs(new[] { ("token", "abc", "Auth token") }));

        vm.Rows[0].Description.Should().Be("Auth token");
    }

    [Fact]
    public void EditingRowValue_MarksDirty()
    {
        var vm = NewVm();
        vm.Load(DevEnv(("a", "1")));
        vm.Rows[0].Value = "changed";
        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void TypingInTrailingRow_AddsNewTrailingRow()
    {
        var vm = NewVm();
        vm.Load(DevEnv());

        var initialCount = vm.Rows.Count; // 1 trailing empty row
        vm.Rows[0].Name = "newVar";

        vm.Rows.Count.Should().Be(initialCount + 1);
        vm.Rows.Last().Name.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_DuplicateNames_ShowsAlertAndDoesNotCallStore()
    {
        var store = NewStore();
        var dialog = NewDialog();
        var vm = NewVm(store, dialog);
        vm.Load(DevEnv());
        vm.Rows[0].Name = "dup";
        vm.Rows[0].Value = "1";
        vm.Rows[1].Name = "dup";
        vm.Rows[1].Value = "2";

        await vm.SaveCommand.Execute();

        vm.ErrorMessage.Should().Contain("dup");
        dialog.Verify(d => d.AlertAsync(It.IsAny<string>(), It.Is<string>(m => m.Contains("dup"))), Times.Once);
        store.Verify(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<Environment>()), Times.Never);
    }

    [Fact]
    public async Task Save_ValidRows_CallsStoreAndClearsDirty()
    {
        var store = NewStore();
        var vm = NewVm(store);
        vm.Load(DevEnv());
        vm.Rows[0].Name = "a";
        vm.Rows[0].Value = "1";

        await vm.SaveCommand.Execute();

        store.Verify(s => s.SaveAsync(
            "/ws/environments/dev.json",
            It.Is<Environment>(e => e.Variables["a"] == "1")), Times.Once);
        vm.IsDirty.Should().BeFalse();
        vm.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task Save_Description_PersistedToStore()
    {
        var store = NewStore();
        var vm = NewVm(store);
        vm.Load(DevEnv());
        vm.Rows[0].Name = "token";
        vm.Rows[0].Value = "abc";
        vm.Rows[0].Description = "Auth token";

        await vm.SaveCommand.Execute();

        store.Verify(s => s.SaveAsync(
            "/ws/environments/dev.json",
            // ReSharper disable once NullableWarningSuppressionIsUsed
            It.Is<Environment>(e => e.Descriptions!["token"] == "Auth token")), Times.Once);
    }

    [Fact]
    public async Task Save_EmptyNameRows_AreSkipped()
    {
        var store = NewStore();
        var vm = NewVm(store);
        vm.Load(DevEnv());
        // trailing row has empty name — should be ignored

        await vm.SaveCommand.Execute();

        store.Verify(s => s.SaveAsync(
            "/ws/environments/dev.json",
            It.Is<Environment>(e => e.Variables.Count == 0)), Times.Once);
    }

    [Fact]
    public void MarkAsNew_PristineState_IsDirtyFalse()
    {
        var vm = NewVm();
        vm.Load(BlankNewEnv());
        vm.MarkAsNew();

        vm.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void MarkAsNew_AfterNameChange_IsDirtyTrue()
    {
        var vm = NewVm();
        vm.Load(BlankNewEnv());
        vm.MarkAsNew();

        vm.EnvironmentName = "My Env";

        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void MarkAsNew_AfterAddingVariable_IsDirtyTrue()
    {
        var vm = NewVm();
        vm.Load(BlankNewEnv());
        vm.MarkAsNew();

        vm.Rows[0].Name = "myVar";

        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void MarkAsNew_RecomputeWithNoUserEdit_StaysClean()
    {
        var vm = NewVm();
        vm.Load(BlankNewEnv());
        vm.MarkAsNew();

        // Assigning the same value triggers the setter but makes no real change
        vm.EnvironmentName = vm.EnvironmentName;

        vm.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task Save_WhenNew_PickerCancelled_DoesNotSave()
    {
        var store = NewStore();
        var picker = new Mock<IFilePickerService>();
        picker.Setup(p => p.PickSaveEnvironmentAsync(It.IsAny<string>())).ReturnsAsync((string?)null);
        var vm = NewVm(store, picker: picker);
        vm.Load(BlankNewEnv());
        vm.MarkAsNew();

        await vm.SaveCommand.Execute();

        store.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Environment>()), Times.Never);
    }

    [Fact]
    public async Task Save_WhenNew_SavesWithPickedPath()
    {
        const string pickedPath = "/ws/environments/New Environment.json";
        var store = NewStore();
        var picker = new Mock<IFilePickerService>();
        picker.Setup(p => p.PickSaveEnvironmentAsync(It.IsAny<string>())).ReturnsAsync(pickedPath);
        var vm = NewVm(store, picker: picker);
        vm.Load(BlankNewEnv());
        vm.MarkAsNew();

        await vm.SaveCommand.Execute();

        store.Verify(s => s.SaveAsync(pickedPath, It.IsAny<Environment>()), Times.Once);
        vm.CurrentFilePath.Should().Be(pickedPath);
    }

    [Fact]
    public async Task Save_WhenNew_SuggestsCurrentNameToFilePicker()
    {
        var store = NewStore();
        var picker = new Mock<IFilePickerService>();
        picker.Setup(p => p.PickSaveEnvironmentAsync("Staging")).ReturnsAsync("/ws/environments/Staging.json");
        var vm = NewVm(store, picker: picker);
        vm.Load(BlankNewEnv());
        vm.MarkAsNew();
        vm.EnvironmentName = "Staging";

        await vm.SaveCommand.Execute();

        picker.Verify(p => p.PickSaveEnvironmentAsync("Staging"), Times.Once);
    }

    [Fact]
    public async Task Save_WhenNew_SecondSave_DoesNotReopenPicker()
    {
        const string pickedPath = "/ws/environments/Staging.json";
        var store = NewStore();
        var picker = new Mock<IFilePickerService>();
        picker.Setup(p => p.PickSaveEnvironmentAsync(It.IsAny<string>())).ReturnsAsync(pickedPath);
        var vm = NewVm(store, picker: picker);
        vm.Load(BlankNewEnv());
        vm.MarkAsNew();
        vm.EnvironmentName = "Staging";
        await vm.SaveCommand.Execute();  // first save → picks path, clears _isNew

        vm.EnvironmentName = "Production";
        await vm.SaveCommand.Execute();  // second save → uses stored path, no picker

        picker.Verify(p => p.PickSaveEnvironmentAsync(It.IsAny<string>()), Times.Once);
        store.Verify(s => s.SaveAsync(pickedPath, It.IsAny<Environment>()), Times.Exactly(2));
    }

    [Fact]
    public async Task DeleteCommand_RemovesRowAndKeepsTrailingEmptyRow()
    {
        var vm = NewVm();
        vm.Load(DevEnv(("a", "1"), ("b", "2")));

        var rowToDelete = vm.Rows.First(r => r.Name == "a");
        await rowToDelete.DeleteCommand.Execute();

        vm.Rows.Select(r => r.Name).Where(n => !string.IsNullOrEmpty(n))
            .Should().Equal("b");
        vm.Rows.Last().Name.Should().BeEmpty();
        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void DeleteCommand_CanExecute_IsFalseForEmptyRow()
    {
        var vm = NewVm();
        vm.Load(DevEnv());

        var emptyRow = vm.Rows.Last();
        emptyRow.Name.Should().BeEmpty();

        // CanExecute emits false immediately for an empty-name row.
        var canExecute = emptyRow.DeleteCommand.CanExecute.FirstAsync().GetAwaiter().GetResult();
        canExecute.Should().BeFalse();
    }
}
