using FluentAssertions;
using PacketPanda.Core.History;
using PacketPanda.Core.Http;
using PacketPanda.Desktop.ViewModels;
using HttpMethod = PacketPanda.Core.Http.HttpMethod;

namespace PacketPanda.Tests.ViewModels;

public sealed class HistoryItemViewModelTests
{
    private static HistoryEntry EntryAt(DateTimeOffset timestamp, int? statusCode = 200, string? error = null) =>
        new(timestamp, HttpMethod.Get, "https://example.com",
            Array.Empty<HttpHeader>(), new NoBody(), new NoAuth(),
            statusCode, "ok", null, 10, error);

    [Fact]
    public void TimeText_VeryRecent_ReturnsJustNow()
    {
        var vm = new HistoryItemViewModel(EntryAt(DateTimeOffset.Now.AddSeconds(-10)));

        vm.TimeText.Should().Be("just now");
    }

    [Fact]
    public void TimeText_SomeMinutesAgo_ReturnsMinuteCount()
    {
        var vm = new HistoryItemViewModel(EntryAt(DateTimeOffset.Now.AddMinutes(-30)));

        vm.TimeText.Should().Be("30m ago");
    }

    [Fact]
    public void TimeText_SomeHoursAgo_ReturnsHourCount()
    {
        var vm = new HistoryItemViewModel(EntryAt(DateTimeOffset.Now.AddHours(-5)));

        vm.TimeText.Should().Be("5h ago");
    }

    [Fact]
    public void TimeText_MoreThanOneDayAgo_ReturnsFormattedDate()
    {
        var when = DateTimeOffset.Now.AddDays(-3);
        var vm = new HistoryItemViewModel(EntryAt(when));

        vm.TimeText.Should().Be(when.LocalDateTime.ToString("MMM d"));
    }

    [Fact]
    public void StatusText_WithStatusCode_ShowsCode()
    {
        var vm = new HistoryItemViewModel(EntryAt(DateTimeOffset.Now, statusCode: 404));

        vm.StatusText.Should().Be("404");
    }

    [Fact]
    public void StatusText_WithError_ShowsErr()
    {
        var vm = new HistoryItemViewModel(EntryAt(DateTimeOffset.Now, error: "connection refused"));

        vm.StatusText.Should().Be("ERR");
    }

    [Fact]
    public void StatusText_NullStatusCodeNoError_ShowsEmDash()
    {
        var vm = new HistoryItemViewModel(EntryAt(DateTimeOffset.Now, statusCode: null));

        vm.StatusText.Should().Be("—");
    }

    [Fact]
    public void Method_IsUppercase()
    {
        var vm = new HistoryItemViewModel(EntryAt(DateTimeOffset.Now));

        vm.Method.Should().Be("GET");
    }
}
