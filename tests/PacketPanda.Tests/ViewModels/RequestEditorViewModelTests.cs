using System.Reactive.Linq;
using System.Reactive.Subjects;
using FluentAssertions;
using Moq;
using PacketPanda.Core.Environments;
using PacketPanda.Core.Http;
using PacketPanda.Core.Persistence;
using PacketPanda.Desktop.ViewModels;
using Environment = PacketPanda.Core.Environments.Environment;
using HttpMethod = PacketPanda.Core.Http.HttpMethod;

namespace PacketPanda.Tests.ViewModels;

public sealed class RequestEditorViewModelTests
{
    private readonly Mock<IHttpRequestSender> _sender = new(MockBehavior.Strict);
    private readonly Mock<ICollectionFileStore> _collectionStore = new(MockBehavior.Strict);

    private RequestEditorViewModel CreateVm() => new(
        _sender.Object,
        _collectionStore.Object,
        new VariableResolver(),
        new BehaviorSubject<EnvironmentContext?>(null));

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
    public async Task Save_CallsUpdateRequestAsync_WithCorrectArgs()
    {
        _collectionStore
            .Setup(s => s.UpdateRequestAsync("/col.json", "req-1",
                It.Is<HttpRequest>(r => r.Url == "https://example.com" && r.Method == HttpMethod.Delete)))
            .Returns(Task.CompletedTask);

        var vm = CreateVm();
        vm.RequestId = "req-1";
        vm.CollectionFilePath = "/col.json";
        vm.Url = "https://example.com";
        vm.SelectedMethod = HttpMethod.Delete;

        await vm.SaveCommand.Execute();

        _collectionStore.Verify(s => s.UpdateRequestAsync("/col.json", "req-1", It.IsAny<HttpRequest>()), Times.Once);
    }

    [Fact]
    public async Task Save_WhenRequestIdIsNull_DoesNothing()
    {
        var vm = CreateVm();
        vm.CollectionFilePath = "/col.json";
        vm.RequestId = null;
        vm.Url = "https://example.com";

        await vm.SaveCommand.Execute();

        _collectionStore.Verify(s => s.UpdateRequestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<HttpRequest>()), Times.Never);
    }

    [Fact]
    public async Task Save_Success_ClearsErrorMessageAndMarksSaved()
    {
        _collectionStore
            .Setup(s => s.UpdateRequestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<HttpRequest>()))
            .Returns(Task.CompletedTask);

        var vm = CreateVm();
        vm.RequestId = "r1";
        vm.CollectionFilePath = "/col.json";
        vm.Url = "https://example.com";
        vm.ErrorMessage = "previous error";

        await vm.SaveCommand.Execute();

        vm.ErrorMessage.Should().BeNull();
        vm.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task Save_Failure_SetsErrorMessage()
    {
        _collectionStore
            .Setup(s => s.UpdateRequestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<HttpRequest>()))
            .ThrowsAsync(new RequestFileException("Could not write file: access denied"));

        var vm = CreateVm();
        vm.RequestId = "r1";
        vm.CollectionFilePath = "/col.json";
        vm.Url = "https://example.com";

        await vm.SaveCommand.Execute();

        vm.ErrorMessage.Should().Be("Could not write file: access denied");
        vm.IsDirty.Should().BeTrue();
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
    public void IsDirty_FreshViewModel_IsFalse()
    {
        var vm = CreateVm();

        vm.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void IsDirty_AfterEditingUrl_IsTrue()
    {
        var vm = CreateVm();

        vm.Url = "https://example.com";

        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void IsDirty_AfterEditingBody_IsTrue()
    {
        var vm = CreateVm();

        vm.RequestBody = "{}";

        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void IsDirty_AfterEditingMethod_IsTrue()
    {
        var vm = CreateVm();

        vm.SelectedMethod = HttpMethod.Post;

        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public async Task IsDirty_IsFalseAfterSuccessfulSave()
    {
        _collectionStore
            .Setup(s => s.UpdateRequestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<HttpRequest>()))
            .Returns(Task.CompletedTask);

        var vm = CreateVm();
        vm.RequestId = "r1";
        vm.CollectionFilePath = "/col.json";
        vm.Url = "https://example.com";

        await vm.SaveCommand.Execute();

        vm.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task IsDirty_StaysTrueAfterFailedSave()
    {
        _collectionStore
            .Setup(s => s.UpdateRequestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<HttpRequest>()))
            .ThrowsAsync(new RequestFileException("disk full"));

        var vm = CreateVm();
        vm.RequestId = "r1";
        vm.CollectionFilePath = "/col.json";
        vm.Url = "https://example.com";

        await vm.SaveCommand.Execute();

        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public async Task Send_UnresolvedVariables_DoesNotCallSenderAndSetsError()
    {
        var vm = new RequestEditorViewModel(
            _sender.Object,
            _collectionStore.Object,
            new VariableResolver(),
            new BehaviorSubject<EnvironmentContext?>(null));
        vm.Url = "{{baseUrl}}/users";

        await vm.SendCommand.Execute();

        vm.ErrorMessage.Should().Contain("baseUrl");
        _sender.Verify(s => s.SendAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Send_AllResolved_PassesResolvedUrlToSender()
    {
        _sender.Setup(s => s.SendAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new SendResult.Ok(new HttpResponse(200, "ok", 10)));
        var env = new Environment("dev", "/e.toml",
            new Dictionary<string, string> { ["baseUrl"] = "https://api" });
        var activeEnv = new BehaviorSubject<EnvironmentContext?>(new EnvironmentContext(env, null, env.Variables));
        var vm = new RequestEditorViewModel(
            _sender.Object,
            _collectionStore.Object,
            new VariableResolver(),
            activeEnv);
        vm.Url = "{{baseUrl}}/users";

        await vm.SendCommand.Execute();

        _sender.Verify(s => s.SendAsync(
            It.Is<HttpRequest>(r => r.Url == "https://api/users"),
            It.IsAny<CancellationToken>()), Times.Once);
        vm.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task UpdateVariableAsync_InvokesCallback_WithCorrectArgs()
    {
        string? capturedName = null;
        string? capturedValue = null;
        VariableTarget? capturedTarget = null;
        var vm = new RequestEditorViewModel(
            _sender.Object,
            _collectionStore.Object,
            new VariableResolver(),
            new BehaviorSubject<EnvironmentContext?>(null),
            updateVariable: (n, v, t) => { capturedName = n; capturedValue = v; capturedTarget = t; return Task.CompletedTask; });

        await vm.UpdateVariableAsync("token", "abc123", VariableTarget.ActiveEnv);

        capturedName.Should().Be("token");
        capturedValue.Should().Be("abc123");
        capturedTarget.Should().Be(VariableTarget.ActiveEnv);
    }

    [Fact]
    public async Task UpdateVariableAsync_NoCallback_DoesNotThrow()
    {
        var vm = CreateVm();

        var act = async () => await vm.UpdateVariableAsync("x", "y", VariableTarget.Globals);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void OpenEnvironmentEditor_InvokesCallback()
    {
        var called = false;
        var vm = new RequestEditorViewModel(
            _sender.Object,
            _collectionStore.Object,
            new VariableResolver(),
            new BehaviorSubject<EnvironmentContext?>(null),
            openEnvironmentEditor: () => called = true);

        vm.OpenEnvironmentEditor();

        called.Should().BeTrue();
    }
}
