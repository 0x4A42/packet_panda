using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using FluentAssertions;
using Moq;
using PacketPanda.Core.Environments;
using PacketPanda.Core.Http;
using PacketPanda.Core.Persistence;
using PacketPanda.Core.Scripting;
using PacketPanda.Desktop.ViewModels;
using HttpMethod = PacketPanda.Core.Http.HttpMethod;

namespace PacketPanda.Tests.ViewModels;

public sealed class CollectionRunnerViewModelTests
{
    private static FolderNode MakeFolder(string name, IEnumerable<CollectionNode> children) =>
        new(Guid.NewGuid().ToString(), name, children.ToList());

    private static RequestNode MakeRequest(string name, HttpMethod method = HttpMethod.Get) =>
        new(Guid.NewGuid().ToString(), name, "/col.json", method,
            "https://example.com", new NoBody(),
            Array.Empty<HttpHeader>(), new NoAuth(), Array.Empty<QueryParam>());

    private readonly Mock<IHttpRequestSender> _sender = new(MockBehavior.Strict);

    private CollectionRunnerViewModel CreateVm(FolderNode folder) =>
        new(folder,
            _sender.Object,
            new VariableResolver(),
            new BehaviorSubject<EnvironmentContext?>(null));

    [Fact]
    public void FlattenFolder_SingleLevel_ReturnsAllRequests()
    {
        var folder = MakeFolder("Root",
        [
            MakeRequest("R1"),
            MakeRequest("R2"),
            MakeRequest("R3")
        ]);

        var vm = CreateVm(folder);

        vm.RunResults.Should().HaveCount(3);
        vm.RunResults.Select(r => r.RequestName).Should().BeEquivalentTo(["R1", "R2", "R3"]);
    }

    [Fact]
    public void FlattenFolder_NestedFolder_FlattensDepthFirst()
    {
        var folder = MakeFolder("Root",
        [
            MakeRequest("R1"),
            MakeFolder("Sub", [MakeRequest("R2"), MakeRequest("R3")]),
            MakeRequest("R4")
        ]);

        var vm = CreateVm(folder);

        vm.RunResults.Select(r => r.RequestName).Should()
            .BeEquivalentTo(["R1", "R2", "R3", "R4"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void InitialState_AllResultsArePending()
    {
        var folder = MakeFolder("Root", [MakeRequest("R1"), MakeRequest("R2")]);
        var vm = CreateVm(folder);
        vm.RunResults.Should().AllSatisfy(r => r.Status.Should().Be(RunStatus.Pending));
    }

    [Fact]
    public async Task RunCommand_SuccessfulRequest_MarksPass()
    {
        _sender
            .Setup(s => s.SendAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendResult.Ok(new HttpResponse(200, "ok", 50)));

        var folder = MakeFolder("Root", [MakeRequest("R1")]);
        var vm = CreateVm(folder);

        await vm.RunCommand.Execute();

        vm.RunResults[0].Status.Should().Be(RunStatus.Pass);
        vm.RunResults[0].StatusCode.Should().Be(200);
        vm.RunResults[0].DurationMs.Should().Be(50);
    }

    [Fact]
    public async Task RunCommand_NetworkError_MarksError()
    {
        _sender
            .Setup(s => s.SendAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendResult.Error("connection refused"));

        var folder = MakeFolder("Root", [MakeRequest("R1")]);
        var vm = CreateVm(folder);

        await vm.RunCommand.Execute();

        vm.RunResults[0].Status.Should().Be(RunStatus.Error);
    }

    [Fact]
    public async Task RunCommand_FailingTest_MarksFailNotError()
    {
        _sender
            .Setup(s => s.SendAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendResult.Ok(new HttpResponse(200, "ok", 10)));

        var req = new RequestNode(Guid.NewGuid().ToString(), "R1", "/col.json",
            HttpMethod.Get, "https://example.com", new NoBody(),
            Array.Empty<HttpHeader>(), new NoAuth(), Array.Empty<QueryParam>(),
            PostResponseScript: "pp.test('fails', function() { pp.expect(1).to.equal(2); });");

        var folder = MakeFolder("Root", [req]);
        var vm = CreateVm(folder);

        await vm.RunCommand.Execute();

        vm.RunResults[0].Status.Should().Be(RunStatus.Fail);
        vm.RunResults[0].TestsTotal.Should().Be(1);
        vm.RunResults[0].TestsPassed.Should().Be(0);
    }

    [Fact]
    public async Task RunCommand_UpdatesSummaryText()
    {
        _sender
            .Setup(s => s.SendAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendResult.Ok(new HttpResponse(200, "ok", 10)));

        var folder = MakeFolder("Root", [MakeRequest("R1"), MakeRequest("R2")]);
        var vm = CreateVm(folder);

        await vm.RunCommand.Execute();

        vm.SummaryText.Should().Contain("2 passed");
        vm.SummaryText.Should().Contain("0 failed");
    }

    [Fact]
    public void DisplayTitle_IsFolderName()
    {
        var folder = MakeFolder("My Folder", []);
        var vm = CreateVm(folder);
        vm.DisplayTitle.Should().Be("Runner: My Folder");
    }

    [Fact]
    public async Task RunCommand_PassingTest_SetsPassWithTestCounts()
    {
        _sender
            .Setup(s => s.SendAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendResult.Ok(new HttpResponse(200, "ok", 10)));

        var req = new RequestNode(Guid.NewGuid().ToString(), "R1", "/col.json",
            HttpMethod.Get, "https://example.com", new NoBody(),
            Array.Empty<HttpHeader>(), new NoAuth(), Array.Empty<QueryParam>(),
            PostResponseScript: "pp.test('status ok', function() { pp.expect(pp.response.status).to.equal(200); });");

        var folder = MakeFolder("Root", [req]);
        var vm = CreateVm(folder);

        await vm.RunCommand.Execute();

        vm.RunResults[0].Status.Should().Be(RunStatus.Pass);
        vm.RunResults[0].TestsTotal.Should().Be(1);
        vm.RunResults[0].TestsPassed.Should().Be(1);
    }

    [Fact]
    public async Task RunCommand_SecondRun_ResetsResultsBeforeRunning()
    {
        _sender
            .Setup(s => s.SendAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendResult.Ok(new HttpResponse(200, "ok", 10)));

        var folder = MakeFolder("Root", [MakeRequest("R1")]);
        var vm = CreateVm(folder);

        await vm.RunCommand.Execute();
        await vm.RunCommand.Execute();

        vm.RunResults.Should().HaveCount(1);
        vm.RunResults[0].Status.Should().Be(RunStatus.Pass);
    }

    [Fact]
    public async Task RunCommand_SuccessfulRequest_StoresResponseBodyAndHeaders()
    {
        var headers = new List<HttpHeader> { new("Content-Type", "application/json") };
        var response = new HttpResponse(200, "{\"ok\":true}", 10, headers);
        _sender
            .Setup(s => s.SendAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendResult.Ok(response));

        var folder = MakeFolder("Root", [MakeRequest("R1")]);
        var vm = CreateVm(folder);

        await vm.RunCommand.Execute();

        vm.RunResults[0].ResponseBody.Should().Be("{\"ok\":true}");
        vm.RunResults[0].ResponseHeaders.Should().ContainSingle(h => h.Name == "Content-Type");
    }

    [Fact]
    public async Task RunCommand_SecondRun_ClearsResponseBodyFromFirstRun()
    {
        _sender
            .Setup(s => s.SendAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendResult.Ok(new HttpResponse(200, "first body", 10)));

        var folder = MakeFolder("Root", [MakeRequest("R1")]);
        var vm = CreateVm(folder);

        await vm.RunCommand.Execute();
        vm.RunResults[0].ResponseBody.Should().Be("first body");

        // Change the response for the second run
        _sender
            .Setup(s => s.SendAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendResult.Error("network failure"));

        await vm.RunCommand.Execute();
        vm.RunResults[0].ResponseBody.Should().BeNull();
    }

    [Fact]
    public async Task ReRunCommand_ErrorResponse_ClearsPreviousStatusCodeAndDuration()
    {
        _sender
            .Setup(s => s.SendAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendResult.Ok(new HttpResponse(200, "ok", 50)));

        var folder = MakeFolder("Root", [MakeRequest("R1")]);
        var vm = CreateVm(folder);

        await vm.RunCommand.Execute();
        vm.RunResults[0].StatusCode.Should().Be(200);

        _sender
            .Setup(s => s.SendAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendResult.Error("timeout"));

        await vm.ReRunCommand.Execute(vm.RunResults[0]);

        vm.RunResults[0].Status.Should().Be(RunStatus.Error);
        vm.RunResults[0].StatusCode.Should().BeNull();
        vm.RunResults[0].DurationMs.Should().BeNull();
    }

    [Fact]
    public async Task RunCommand_4xxResponse_SetsStatusToFail()
    {
        var response = new HttpResponse(429, "rate limited", 10);
        _sender.Setup(s => s.SendAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new SendResult.Ok(response));

        var folder = MakeFolder("Root", [MakeRequest("R1")]);
        var vm = CreateVm(folder);

        await vm.RunCommand.Execute();

        vm.RunResults[0].Status.Should().Be(RunStatus.Fail);
        vm.RunResults[0].StatusCode.Should().Be(429);
    }

    [Fact]
    public async Task ReRunCommand_RerequestsAndUpdatesRow()
    {
        _sender
            .Setup(s => s.SendAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendResult.Ok(new HttpResponse(200, "body", 10)));

        var folder = MakeFolder("Root", [MakeRequest("R1")]);
        var vm = CreateVm(folder);

        await vm.RunCommand.Execute();
        vm.RunResults[0].Status.Should().Be(RunStatus.Pass);

        _sender
            .Setup(s => s.SendAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendResult.Ok(new HttpResponse(500, "error", 5)));

        await vm.ReRunCommand.Execute(vm.RunResults[0]);

        vm.RunResults[0].StatusCode.Should().Be(500);
        vm.RunResults[0].ResponseBody.Should().Be("error");
    }
}
