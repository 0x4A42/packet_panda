using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using FluentAssertions;
using Moq;
using PacketPanda.Core.History;
using PacketPanda.Core.Http;
using PacketPanda.Desktop.ViewModels;
using HttpMethod = PacketPanda.Core.Http.HttpMethod;

namespace PacketPanda.Tests.ViewModels;

public sealed class HistorySidebarViewModelTests
{
    private readonly Mock<IHistoryStore> _store = new(MockBehavior.Strict);

    private static HistoryEntry Entry(string url) => new(
        DateTimeOffset.Now, HttpMethod.Get, url,
        Array.Empty<HttpHeader>(), new NoBody(), new NoAuth(),
        200, "ok", null, 5, null);

    [Fact]
    public async Task LoadAsync_PopulatesItemsFromStore()
    {
        _store.Setup(s => s.LoadAsync())
            .ReturnsAsync(new[] { Entry("https://a.test"), Entry("https://b.test") });
        var vm = new HistorySidebarViewModel(_store.Object);

        await vm.LoadAsync();

        vm.Items.Should().HaveCount(2);
        vm.Items[0].Url.Should().Be("https://a.test");
    }

    [Fact]
    public void AddEntry_PrependsToItems()
    {
        var vm = new HistorySidebarViewModel(_store.Object);

        vm.AddEntry(Entry("https://first.test"));
        vm.AddEntry(Entry("https://second.test"));

        vm.Items.Should().HaveCount(2);
        vm.Items[0].Url.Should().Be("https://second.test");
    }

    [Fact]
    public async Task ClearCommand_EmptiesItemsAndCallsStore()
    {
        _store.Setup(s => s.ClearAsync()).Returns(Task.CompletedTask);
        var vm = new HistorySidebarViewModel(_store.Object);
        vm.AddEntry(Entry("https://x.test"));

        await vm.ClearCommand.Execute();

        vm.Items.Should().BeEmpty();
        _store.Verify(s => s.ClearAsync(), Times.Once);
    }

    [Fact]
    public async Task OpenCommand_RaisesHistoryOpenRequestedWithEntry()
    {
        var vm = new HistorySidebarViewModel(_store.Object);
        var item = new HistoryItemViewModel(Entry("https://open.test"));
        var raised = vm.HistoryOpenRequested.FirstAsync().ToTask();

        await vm.OpenCommand.Execute(item);

        (await raised).Url.Should().Be("https://open.test");
    }
}
