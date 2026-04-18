using System.Reactive.Linq;
using FluentAssertions;
using Moq;
using PacketPanda.Core.Http;
using PacketPanda.Desktop.ViewModels;
using HttpMethod = PacketPanda.Core.Http.HttpMethod;

namespace PacketPanda.Tests.ViewModels;

public class MainWindowViewModelTests
{
    private readonly Mock<IHttpRequestSender> _sender = new(MockBehavior.Strict);

    private MainWindowViewModel CreateVm() => new(_sender.Object);

    [Fact]
    public async Task Send_populates_response_fields_from_Ok_result()
    {
        _sender
            .Setup(s => s.SendAsync(
                It.Is<HttpRequest>(r => r.Method == HttpMethod.Get && r.Url == "https://example.com"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendResult.Ok(new HttpResponse(200, "hi", 42)));

        var vm = CreateVm();
        vm.Url = "https://example.com";
        vm.SelectedMethod = HttpMethod.Get;

        await vm.SendCommand.Execute();

        vm.StatusCode.Should().Be(200);
        vm.ResponseBody.Should().Be("hi");
        vm.ElapsedMilliseconds.Should().Be(42);
        vm.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task Send_populates_error_and_clears_response_fields_on_Error_result()
    {
        _sender
            .Setup(s => s.SendAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendResult.Error("connection refused"));

        var vm = CreateVm();
        vm.Url = "https://example.com";
        vm.StatusCode = 999;
        vm.ResponseBody = "stale";
        vm.ElapsedMilliseconds = 123;

        await vm.SendCommand.Execute();

        vm.ErrorMessage.Should().Be("connection refused");
        vm.StatusCode.Should().BeNull();
        vm.ResponseBody.Should().BeNull();
        vm.ElapsedMilliseconds.Should().BeNull();
    }

    [Fact]
    public async Task CanExecute_is_false_when_url_is_empty()
    {
        var vm = CreateVm();
        vm.Url = "";

        var canExecute = await vm.SendCommand.CanExecute.FirstAsync();

        canExecute.Should().BeFalse();
    }

    [Fact]
    public async Task CanExecute_is_true_when_url_is_non_empty()
    {
        var vm = CreateVm();
        vm.Url = "https://example.com";

        var canExecute = await vm.SendCommand.CanExecute.FirstAsync();

        canExecute.Should().BeTrue();
    }

    [Fact]
    public async Task Send_forwards_non_empty_body_to_sender()
    {
        HttpRequest? captured = null;
        _sender
            .Setup(s => s.SendAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .Callback<HttpRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new SendResult.Ok(new HttpResponse(200, "", 0)));

        var vm = CreateVm();
        vm.Url = "https://example.com";
        vm.SelectedMethod = HttpMethod.Post;
        vm.RequestBody = "{\"x\":1}";

        await vm.SendCommand.Execute();

        captured.Should().NotBeNull();
        captured!.Body.Should().Be("{\"x\":1}");
    }

    [Fact]
    public async Task Send_forwards_null_body_when_request_body_is_empty()
    {
        HttpRequest? captured = null;
        _sender
            .Setup(s => s.SendAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .Callback<HttpRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new SendResult.Ok(new HttpResponse(200, "", 0)));

        var vm = CreateVm();
        vm.Url = "https://example.com";
        vm.RequestBody = "";

        await vm.SendCommand.Execute();

        captured!.Body.Should().BeNull();
    }
}
