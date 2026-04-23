using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using FluentAssertions;
using Moq;
using PacketPanda.Core.Environments;
using PacketPanda.Desktop.Services;
using PacketPanda.Desktop.ViewModels;
using Xunit;
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
        Mock<IDialogService>? dialog = null) =>
        new((store ?? NewStore()).Object, (dialog ?? NewDialog()).Object);

    private static Environment DevEnv(params (string k, string v)[] vars) =>
        new("dev", "/ws/environments/dev.toml",
            vars.ToDictionary(p => p.k, p => p.v));

    private static Environment DevEnvWithDescs(
        (string k, string v, string d)[] vars) =>
        new("dev", "/ws/environments/dev.toml",
            vars.ToDictionary(p => p.k, p => p.v),
            vars.Where(p => !string.IsNullOrEmpty(p.d))
                .ToDictionary(p => p.k, p => p.d));

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
            "/ws/environments/dev.toml",
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
            "/ws/environments/dev.toml",
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
            "/ws/environments/dev.toml",
            It.Is<Environment>(e => e.Variables.Count == 0)), Times.Once);
    }
}
