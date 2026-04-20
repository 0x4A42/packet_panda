using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using FluentAssertions;
using Moq;
using PacketPanda.Core.Environments;
using PacketPanda.Desktop.ViewModels;
using Xunit;
using Environment = PacketPanda.Core.Environments.Environment;

namespace PacketPanda.Tests.ViewModels;

public class EnvironmentEditorViewModelTests
{
    private static Mock<IEnvironmentFileStore> NewStore() => new();

    private static Environment DevEnv(params (string k, string v)[] vars) =>
        new("dev", "/ws/environments/dev.toml",
            vars.ToDictionary(p => p.k, p => p.v));

    [Fact]
    public void Load_PopulatesRowsFromVariables()
    {
        var vm = new EnvironmentEditorViewModel(NewStore().Object);
        vm.Load(DevEnv(("baseUrl", "https://api"), ("apiKey", "abc")));

        vm.Rows.Select(r => (r.Name, r.Value)).Should().Equal(
            ("baseUrl", "https://api"), ("apiKey", "abc"));
        vm.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void EditingRowValue_MarksDirty()
    {
        var vm = new EnvironmentEditorViewModel(NewStore().Object);
        vm.Load(DevEnv(("a", "1")));
        vm.Rows[0].Value = "changed";
        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void AddRow_MarksDirtyAndAppendsBlankRow()
    {
        var vm = new EnvironmentEditorViewModel(NewStore().Object);
        vm.Load(DevEnv());
        vm.AddRow();
        vm.Rows.Should().HaveCount(1);
        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void RemoveRow_MarksDirtyAndRemovesRow()
    {
        var vm = new EnvironmentEditorViewModel(NewStore().Object);
        vm.Load(DevEnv(("a", "1")));
        vm.RemoveRow(vm.Rows[0]);
        vm.Rows.Should().BeEmpty();
        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public async Task Save_DuplicateNames_SetsErrorAndDoesNotCallStore()
    {
        var store = NewStore();
        var vm = new EnvironmentEditorViewModel(store.Object);
        vm.Load(DevEnv());
        vm.AddRow();
        vm.Rows[0].Name = "dup";
        vm.Rows[0].Value = "1";
        vm.AddRow();
        vm.Rows[1].Name = "dup";
        vm.Rows[1].Value = "2";

        await vm.SaveCommand.Execute();

        vm.ErrorMessage.Should().Contain("dup");
        store.Verify(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<Environment>()), Times.Never);
    }

    [Fact]
    public async Task Save_BlankName_SetsErrorAndDoesNotCallStore()
    {
        var store = NewStore();
        var vm = new EnvironmentEditorViewModel(store.Object);
        vm.Load(DevEnv());
        vm.AddRow();
        vm.Rows[0].Name = "  ";

        await vm.SaveCommand.Execute();

        vm.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        store.Verify(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<Environment>()), Times.Never);
    }

    [Fact]
    public async Task Save_ValidRows_CallsStoreAndClearsDirty()
    {
        var store = NewStore();
        var vm = new EnvironmentEditorViewModel(store.Object);
        vm.Load(DevEnv());
        vm.AddRow();
        vm.Rows[0].Name = "a";
        vm.Rows[0].Value = "1";

        await vm.SaveCommand.Execute();

        store.Verify(s => s.SaveAsync(
            "/ws/environments/dev.toml",
            It.Is<Environment>(e => e.Variables["a"] == "1")), Times.Once);
        vm.IsDirty.Should().BeFalse();
        vm.ErrorMessage.Should().BeNull();
    }
}
