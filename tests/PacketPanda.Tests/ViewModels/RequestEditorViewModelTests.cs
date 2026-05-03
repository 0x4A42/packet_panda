using System.Reactive;
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

    [Fact]
    public void Headers_FreshViewModel_HasOneTrailingEmptyRow()
    {
        var vm = CreateVm();
        vm.Headers.Should().HaveCount(1);
        vm.Headers[0].Name.Should().BeEmpty();
    }

    [Fact]
    public void AddHeader_AddsRowToHeaders()
    {
        var vm = CreateVm();
        vm.AddHeaderCommand.Execute(Unit.Default).Subscribe();
        vm.Headers.Should().HaveCount(2);
    }

    [Fact]
    public void RemoveHeader_TrailingEmptyRowAlwaysPresent()
    {
        var vm = CreateVm();
        vm.AddHeaderCommand.Execute(Unit.Default).Subscribe();
        vm.Headers[0].RemoveCommand.Execute(Unit.Default).Subscribe();
        vm.Headers.Should().HaveCount(1);
        vm.Headers[0].Name.Should().BeEmpty();
    }

    [Fact]
    public void Headers_AddEmptyRow_DoesNotMarkDirty()
    {
        var vm = CreateVm();
        vm.Url = "https://example.com";
        vm.MarkSaved();

        vm.AddHeaderCommand.Execute(Unit.Default).Subscribe();

        vm.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void Headers_EditRowName_MarksDirty()
    {
        var vm = CreateVm();
        vm.Url = "https://example.com";
        vm.MarkSaved();

        vm.Headers[0].Name = "X-Foo";

        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void AuthType_Change_MarksDirty()
    {
        var vm = CreateVm();
        vm.Url = "https://example.com";
        vm.MarkSaved();

        vm.SelectedAuthType = AuthType.Bearer;

        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void BearerToken_Change_MarksDirty()
    {
        var vm = CreateVm();
        vm.Url = "https://example.com";
        vm.SelectedAuthType = AuthType.Bearer;
        vm.MarkSaved();

        vm.BearerToken = "new-token";

        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void BuildRequest_IncludesEnabledHeadersOnly()
    {
        var vm = CreateVm();
        vm.Url = "https://example.com";
        vm.AddHeaderCommand.Execute(Unit.Default).Subscribe();
        vm.Headers[0].Name = "X-Keep";
        vm.Headers[0].Value = "yes";
        vm.AddHeaderCommand.Execute(Unit.Default).Subscribe();
        vm.Headers[1].Name = "X-Skip";
        vm.Headers[1].Value = "no";
        vm.Headers[1].Enabled = false;

        var req = vm.BuildRequest();

        req.Headers.Should().HaveCount(2);
        req.Headers!.Where(h => h.Enabled).Should().ContainSingle(h => h.Name == "X-Keep");
        req.Headers!.Where(h => !h.Enabled).Should().ContainSingle(h => h.Name == "X-Skip");
    }

    [Fact]
    public async Task Send_WithBearerAuth_SendsResolvedToken()
    {
        _sender
            .Setup(s => s.SendAsync(
                It.Is<HttpRequest>(r => r.Auth is BearerAuth && ((BearerAuth)r.Auth!).Token == "tok"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendResult.Ok(new HttpResponse(200, "", 1)));

        var vm = CreateVm();
        vm.Url = "https://example.com";
        vm.SelectedAuthType = AuthType.Bearer;
        vm.BearerToken = "tok";

        await vm.SendCommand.Execute();

        vm.StatusCode.Should().Be(200);
    }

    [Fact]
    public void BasicPassword_Change_MarksDirty()
    {
        var vm = CreateVm();
        vm.Url = "https://example.com";
        vm.SelectedAuthType = AuthType.Basic;
        vm.MarkSaved();

        vm.BasicPassword = "s3cr3t";

        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void LoadHeaders_PopulatesHeadersCollectionWithTrailingEmptyRow()
    {
        var vm = CreateVm();
        var headers = new List<HttpHeader>
        {
            new HttpHeader("X-Foo", "bar", true),
            new HttpHeader("X-Off", "skip", false)
        };

        vm.LoadHeaders(headers);

        vm.Headers.Should().HaveCount(3); // 2 real + 1 trailing empty
        vm.Headers[0].Name.Should().Be("X-Foo");
        vm.Headers[0].Value.Should().Be("bar");
        vm.Headers[0].Enabled.Should().BeTrue();
        vm.Headers[1].Name.Should().Be("X-Off");
        vm.Headers[1].Enabled.Should().BeFalse();
        vm.Headers[2].Name.Should().BeEmpty();
    }

    [Fact]
    public void LoadAuth_Bearer_SetsAuthTypeAndToken()
    {
        var vm = CreateVm();

        vm.LoadAuth(new BearerAuth("my-token"));

        vm.SelectedAuthType.Should().Be(AuthType.Bearer);
        vm.BearerToken.Should().Be("my-token");
    }

    [Fact]
    public void LoadAuth_ApiKey_SetsAllFields()
    {
        var vm = CreateVm();

        vm.LoadAuth(new ApiKeyAuth("X-Key", "secret", ApiKeyTarget.QueryParam));

        vm.SelectedAuthType.Should().Be(AuthType.ApiKey);
        vm.ApiKeyName.Should().Be("X-Key");
        vm.ApiKeyValue.Should().Be("secret");
        vm.ApiKeyTarget.Should().Be(ApiKeyTarget.QueryParam);
    }

    [Fact]
    public void LoadAuth_Basic_SetsUsernameAndPassword()
    {
        var vm = CreateVm();

        vm.LoadAuth(new BasicAuth("admin", "pass"));

        vm.SelectedAuthType.Should().Be(AuthType.Basic);
        vm.BasicUsername.Should().Be("admin");
        vm.BasicPassword.Should().Be("pass");
    }

    // ── Query Params ────────────────────────────────────────────

    [Fact]
    public void QueryParams_FreshViewModel_HasOneTrailingEmptyRow()
    {
        var vm = CreateVm();
        vm.QueryParams.Should().HaveCount(1);
        vm.QueryParams[0].Key.Should().BeEmpty();
    }

    [Fact]
    public void AddQueryParam_AddsRow()
    {
        var vm = CreateVm();
        vm.AddQueryParamCommand.Execute(Unit.Default).Subscribe();
        vm.QueryParams.Should().HaveCount(2);
    }

    [Fact]
    public void AddQueryParam_EmptyRow_DoesNotMarkDirty()
    {
        var vm = CreateVm();
        vm.Url = "https://example.com";
        vm.MarkSaved();

        vm.AddQueryParamCommand.Execute(Unit.Default).Subscribe();

        vm.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void QueryParam_EditKey_MarksDirty()
    {
        var vm = CreateVm();
        vm.Url = "https://example.com";
        vm.MarkSaved();

        vm.QueryParams[0].Key = "foo";

        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void LoadQueryParams_PopulatesCollectionWithTrailingEmptyRow()
    {
        var vm = CreateVm();
        var qp = new List<QueryParam>
        {
            new QueryParam("foo", "bar", true),
            new QueryParam("debug", "1", false)
        };

        vm.LoadQueryParams(qp);

        vm.QueryParams.Should().HaveCount(3); // 2 real + 1 trailing empty
        vm.QueryParams[0].Key.Should().Be("foo");
        vm.QueryParams[0].Value.Should().Be("bar");
        vm.QueryParams[0].Enabled.Should().BeTrue();
        vm.QueryParams[1].Key.Should().Be("debug");
        vm.QueryParams[1].Enabled.Should().BeFalse();
        vm.QueryParams[2].Key.Should().BeEmpty();
    }

    [Fact]
    public void BuildRequest_ExcludesDisabledAndEmptyKeyQueryParams()
    {
        var vm = CreateVm();
        vm.Url = "https://example.com";
        vm.QueryParams[0].Key = "active";
        vm.QueryParams[0].Value = "true";
        vm.AddQueryParamCommand.Execute(Unit.Default).Subscribe();
        vm.QueryParams[1].Key = "debug";
        vm.QueryParams[1].Value = "1";
        vm.QueryParams[1].Enabled = false;

        var req = vm.BuildRequest();

        req.QueryParams.Should().HaveCount(1);
        req.QueryParams![0].Key.Should().Be("active");
        req.QueryParams[0].Value.Should().Be("true");
    }

    [Fact]
    public void Url_WithQueryString_PopulatesParamRows()
    {
        var vm = CreateVm();

        vm.Url = "https://api.example.com/search?q=hello&page=2";

        var namedRows = vm.QueryParams.Where(p => !string.IsNullOrEmpty(p.Key)).ToList();
        namedRows.Should().HaveCount(2);
        namedRows[0].Key.Should().Be("q");
        namedRows[0].Value.Should().Be("hello");
        namedRows[1].Key.Should().Be("page");
        namedRows[1].Value.Should().Be("2");
    }

    [Fact]
    public void ParamRow_EditKey_UpdatesUrl()
    {
        var vm = CreateVm();
        vm.Url = "https://api.example.com";

        vm.QueryParams[0].Key = "foo";
        vm.QueryParams[0].Value = "bar";

        vm.Url.Should().Be("https://api.example.com?foo=bar");
    }

    [Fact]
    public void ParamRow_Disabled_ExcludedFromUrl()
    {
        var vm = CreateVm();
        vm.LoadQueryParams(new[] { new QueryParam("foo", "bar", true) });

        vm.QueryParams[0].Enabled = false;

        vm.Url.Should().NotContain("foo");
        vm.QueryParams.Should().Contain(p => p.Key == "foo" && !p.Enabled);
    }

    // ── Response headers / cookies ───────────────────────────────

    [Fact]
    public async Task Send_PopulatesResponseHeaders()
    {
        var responseHeaders = new List<HttpHeader> { new HttpHeader("Content-Type", "application/json") };
        _sender
            .Setup(s => s.SendAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendResult.Ok(new HttpResponse(200, "{}", 10, responseHeaders)));

        var vm = CreateVm();
        vm.Url = "https://example.com";

        await vm.SendCommand.Execute();

        vm.ResponseHeaders.Should().ContainSingle(h => h.Name == "Content-Type" && h.Value == "application/json");
    }

    [Fact]
    public async Task Send_PopulatesResponseCookies_FromSetCookieHeaders()
    {
        var responseHeaders = new List<HttpHeader>
        {
            new HttpHeader("Set-Cookie", "session=abc123; Path=/; HttpOnly")
        };
        _sender
            .Setup(s => s.SendAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendResult.Ok(new HttpResponse(200, "", 10, responseHeaders)));

        var vm = CreateVm();
        vm.Url = "https://example.com";

        await vm.SendCommand.Execute();

        vm.ResponseCookies.Should().ContainSingle(c => c.Name == "session" && c.Value == "abc123");
    }

    [Fact]
    public async Task Send_ClearsResponseHeadersAndCookies_OnEachSend()
    {
        _sender
            .SetupSequence(s => s.SendAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendResult.Ok(new HttpResponse(200, "", 10,
                new List<HttpHeader> { new HttpHeader("X-Foo", "1") })))
            .ReturnsAsync(new SendResult.Ok(new HttpResponse(200, "", 10,
                new List<HttpHeader>())));

        var vm = CreateVm();
        vm.Url = "https://example.com";

        await vm.SendCommand.Execute();
        vm.ResponseHeaders.Should().HaveCount(1);

        await vm.SendCommand.Execute();
        vm.ResponseHeaders.Should().BeEmpty();
    }

    [Fact]
    public void ResponseHeadersTabLabel_NoResponse_ReturnsPlainHeaders()
    {
        var vm = CreateVm();
        vm.ResponseHeadersTabLabel.Should().Be("Headers");
    }

    [Fact]
    public async Task ResponseHeadersTabLabel_AfterSend_IncludesCount()
    {
        var responseHeaders = new List<HttpHeader>
        {
            new HttpHeader("Content-Type", "application/json"),
            new HttpHeader("X-Request-Id", "123")
        };
        _sender
            .Setup(s => s.SendAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendResult.Ok(new HttpResponse(200, "", 10, responseHeaders)));

        var vm = CreateVm();
        vm.Url = "https://example.com";

        await vm.SendCommand.Execute();

        vm.ResponseHeadersTabLabel.Should().Be("Headers (2)");
    }
}
