using System.Reactive.Linq;
using FluentAssertions;
using Moq;
using PacketPanda.Core.Http;
using PacketPanda.Core.Persistence;
using PacketPanda.Desktop.Services;
using PacketPanda.Desktop.ViewModels;
using HttpMethod = PacketPanda.Core.Http.HttpMethod;

namespace PacketPanda.Tests.ViewModels;

public class RequestEditorViewModelTests
{
    private readonly Mock<IHttpRequestSender> _sender = new(MockBehavior.Strict);
    private readonly Mock<IRequestFileStore> _store = new(MockBehavior.Strict);
    private readonly Mock<IFilePickerService> _picker = new(MockBehavior.Strict);

    private RequestEditorViewModel CreateVm() => new(_sender.Object, _store.Object, _picker.Object);

    [Fact]
    public async Task Send_PopulatesResponseFieldsFromOkResult()
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
    public async Task Send_PopulatesErrorAndClearsResponseFields_OnErrorResult()
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
    public async Task SendCommand_CanExecute_IsFalseWhenUrlIsEmpty()
    {
        var vm = CreateVm();
        vm.Url = "";

        var canExecute = await vm.SendCommand.CanExecute.FirstAsync();

        canExecute.Should().BeFalse();
    }

    [Fact]
    public async Task SendCommand_CanExecute_IsTrueWhenUrlIsNonEmpty()
    {
        var vm = CreateVm();
        vm.Url = "https://example.com";

        var canExecute = await vm.SendCommand.CanExecute.FirstAsync();

        canExecute.Should().BeTrue();
    }

    [Fact]
    public async Task Send_ForwardsNonEmptyBodyToSender()
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
    public async Task Send_ForwardsNullBodyWhenRequestBodyIsEmpty()
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

    [Fact]
    public async Task Open_PopulatesEditorFieldsAndSetsCurrentFilePath_OnSuccess()
    {
        _picker.Setup(p => p.PickOpenAsync()).ReturnsAsync("/tmp/req.toml");
        _store.Setup(s => s.LoadAsync("/tmp/req.toml"))
              .ReturnsAsync(new HttpRequest(HttpMethod.Post, "https://api.example.com", """{"a":1}"""));

        var vm = CreateVm();
        vm.StatusCode = 200;
        vm.ResponseBody = "stale";
        vm.ElapsedMilliseconds = 50;
        vm.ErrorMessage = "old error";

        await vm.OpenCommand.Execute();

        vm.SelectedMethod.Should().Be(HttpMethod.Post);
        vm.Url.Should().Be("https://api.example.com");
        vm.RequestBody.Should().Be("""{"a":1}""");
        vm.StatusCode.Should().BeNull();
        vm.ResponseBody.Should().BeNull();
        vm.ElapsedMilliseconds.Should().BeNull();
        vm.ErrorMessage.Should().BeNull();
        vm.CurrentFilePath.Should().Be("/tmp/req.toml");
    }

    [Fact]
    public async Task Open_Cancelled_DoesNothing()
    {
        _picker.Setup(p => p.PickOpenAsync()).ReturnsAsync((string?)null);
        var vm = CreateVm();
        vm.Url = "https://original.com";

        await vm.OpenCommand.Execute();

        vm.Url.Should().Be("https://original.com");
        vm.CurrentFilePath.Should().BeNull();
        _store.Verify(s => s.LoadAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Open_Failure_SetsErrorMessageAndLeavesStateUnchanged()
    {
        _picker.Setup(p => p.PickOpenAsync()).ReturnsAsync("/tmp/bad.toml");
        _store.Setup(s => s.LoadAsync("/tmp/bad.toml"))
              .ThrowsAsync(new RequestFileException("Invalid TOML: unexpected token"));

        var vm = CreateVm();
        vm.Url = "https://original.com";

        await vm.OpenCommand.Execute();

        vm.ErrorMessage.Should().Be("Invalid TOML: unexpected token");
        vm.Url.Should().Be("https://original.com");
        vm.CurrentFilePath.Should().BeNull();
    }

    [Fact]
    public async Task Save_WritesToCurrentFilePathWithoutPicker_WhenPathIsSet()
    {
        var vm = CreateVm();
        vm.Url = "https://example.com";
        vm.SelectedMethod = HttpMethod.Delete;
        vm.CurrentFilePath = "/tmp/existing.toml";

        _store.Setup(s => s.SaveAsync("/tmp/existing.toml",
            It.Is<HttpRequest>(r => r.Url == "https://example.com" && r.Method == HttpMethod.Delete)))
            .Returns(Task.CompletedTask);

        await vm.SaveCommand.Execute();

        _store.Verify(s => s.SaveAsync("/tmp/existing.toml", It.IsAny<HttpRequest>()), Times.Once);
        _picker.Verify(p => p.PickSaveAsync(), Times.Never);
    }

    [Fact]
    public async Task Save_InvokesPickerAndSaves_WhenCurrentFilePathIsNull()
    {
        _picker.Setup(p => p.PickSaveAsync()).ReturnsAsync("/tmp/new.toml");
        _store.Setup(s => s.SaveAsync("/tmp/new.toml", It.IsAny<HttpRequest>()))
              .Returns(Task.CompletedTask);

        var vm = CreateVm();
        vm.Url = "https://example.com";

        await vm.SaveCommand.Execute();

        vm.CurrentFilePath.Should().Be("/tmp/new.toml");
        _store.Verify(s => s.SaveAsync("/tmp/new.toml", It.IsAny<HttpRequest>()), Times.Once);
    }

    [Fact]
    public async Task SaveCommand_CanExecute_IsFalseWhenUrlIsEmpty()
    {
        var vm = CreateVm();
        vm.Url = "";

        var canExecute = await vm.SaveCommand.CanExecute.FirstAsync();

        canExecute.Should().BeFalse();
    }

    [Fact]
    public async Task SaveAs_AlwaysInvokesPickerAndUpdatesCurrentFilePath()
    {
        _picker.Setup(p => p.PickSaveAsync()).ReturnsAsync("/tmp/saveas.toml");
        _store.Setup(s => s.SaveAsync("/tmp/saveas.toml", It.IsAny<HttpRequest>()))
              .Returns(Task.CompletedTask);

        var vm = CreateVm();
        vm.Url = "https://example.com";
        vm.CurrentFilePath = "/tmp/old.toml";

        await vm.SaveAsCommand.Execute();

        vm.CurrentFilePath.Should().Be("/tmp/saveas.toml");
        _picker.Verify(p => p.PickSaveAsync(), Times.Once);
    }

    [Fact]
    public async Task SaveAs_Cancelled_LeavesCurrentFilePathUnchanged()
    {
        _picker.Setup(p => p.PickSaveAsync()).ReturnsAsync((string?)null);

        var vm = CreateVm();
        vm.Url = "https://example.com";
        vm.CurrentFilePath = "/tmp/original.toml";

        await vm.SaveAsCommand.Execute();

        vm.CurrentFilePath.Should().Be("/tmp/original.toml");
        _store.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<HttpRequest>()), Times.Never);
    }

    [Fact]
    public async Task SaveAsCommand_CanExecute_IsFalseWhenUrlIsEmpty()
    {
        var vm = CreateVm();
        vm.Url = "";

        var canExecute = await vm.SaveAsCommand.CanExecute.FirstAsync();

        canExecute.Should().BeFalse();
    }

    [Fact]
    public async Task Save_Success_ClearsErrorMessage()
    {
        _store.Setup(s => s.SaveAsync("/tmp/req.toml", It.IsAny<HttpRequest>()))
              .Returns(Task.CompletedTask);

        var vm = CreateVm();
        vm.Url = "https://example.com";
        vm.CurrentFilePath = "/tmp/req.toml";
        vm.ErrorMessage = "previous error";

        await vm.SaveCommand.Execute();

        vm.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task Save_Failure_SetsErrorMessageAndLeavesCurrentFilePathUnchanged()
    {
        _store.Setup(s => s.SaveAsync("/tmp/req.toml", It.IsAny<HttpRequest>()))
              .ThrowsAsync(new RequestFileException("Could not write file: access denied"));

        var vm = CreateVm();
        vm.Url = "https://example.com";
        vm.CurrentFilePath = "/tmp/req.toml";

        await vm.SaveCommand.Execute();

        vm.ErrorMessage.Should().Be("Could not write file: access denied");
        vm.CurrentFilePath.Should().Be("/tmp/req.toml");
    }
}
