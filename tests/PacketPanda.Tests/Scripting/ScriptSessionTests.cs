using FluentAssertions;
using PacketPanda.Core.Scripting;

namespace PacketPanda.Tests.Scripting;

public sealed class ScriptSessionTests
{
    [Fact]
    public void PpExpect_Equal_PassesWhenValuesMatch()
    {
        var expect = new PpExpect(200.0);
        var act = () => expect.to.equal(200.0);
        act.Should().NotThrow();
    }

    [Fact]
    public void PpExpect_Equal_ThrowsWhenValuesDiffer()
    {
        var expect = new PpExpect(200.0);
        var act = () => expect.to.equal(404.0);
        act.Should().Throw<Exception>().WithMessage("*Expected*");
    }

    [Fact]
    public void PpExpect_Above_PassesWhenActualIsHigher()
    {
        var expect = new PpExpect(201.0);
        var act = () => expect.to.be.above(200.0);
        act.Should().NotThrow();
    }

    [Fact]
    public void PpExpect_Above_ThrowsWhenActualIsLower()
    {
        var expect = new PpExpect(100.0);
        var act = () => expect.to.be.above(200.0);
        act.Should().Throw<Exception>().WithMessage("*above*");
    }

    [Fact]
    public void PpExpect_Include_PassesWhenStringContainsSubstring()
    {
        var expect = new PpExpect("hello world");
        var act = () => expect.to.include("world");
        act.Should().NotThrow();
    }

    [Fact]
    public void PpExpect_Include_ThrowsWhenStringDoesNotContain()
    {
        var expect = new PpExpect("hello world");
        var act = () => expect.to.include("xyz");
        act.Should().Throw<Exception>().WithMessage("*include*");
    }

    [Fact]
    public void PpEnvironment_Get_ReadsFromActiveEnv()
    {
        var env = new PacketPanda.Core.Environments.Environment(
            "test", "/path",
            new Dictionary<string, string> { ["token"] = "abc" });
        var overlay = new Dictionary<string, string>();
        var ppEnv = new PpEnvironment(env, overlay);

        ppEnv.get("token").Should().Be("abc");
    }

    [Fact]
    public void PpEnvironment_Set_WritesToOverlay()
    {
        var overlay = new Dictionary<string, string>();
        var ppEnv = new PpEnvironment(null, overlay);

        ppEnv.set("key", "value");

        overlay["key"].Should().Be("value");
    }

    [Fact]
    public void PpEnvironment_Get_OverlayTakesPrecedence()
    {
        var env = new PacketPanda.Core.Environments.Environment(
            "test", "/path",
            new Dictionary<string, string> { ["k"] = "original" });
        var overlay = new Dictionary<string, string> { ["k"] = "overridden" };
        var ppEnv = new PpEnvironment(env, overlay);

        ppEnv.get("k").Should().Be("overridden");
    }

    [Fact]
    public void PpEnvironment_Get_ReturnsNullForMissingKey()
    {
        var ppEnv = new PpEnvironment(null, new Dictionary<string, string>());

        ppEnv.get("missing").Should().BeNull();
    }

    // ScriptSession tests
    [Fact]
    public void RunPreRequest_ValidScript_DoesNotThrow()
    {
        var session = new ScriptSession(null);
        var act = () => session.RunPreRequest("var x = 1 + 1;");
        act.Should().NotThrow();
    }

    [Fact]
    public void RunPreRequest_SyntaxError_ThrowsScriptException()
    {
        var session = new ScriptSession(null);
        var act = () => session.RunPreRequest("this is not valid JS !!!");
        act.Should().Throw<ScriptException>();
    }

    [Fact]
    public void RunPreRequest_UncaughtThrow_ThrowsScriptException()
    {
        var session = new ScriptSession(null);
        var act = () => session.RunPreRequest("throw new Error('oops');");
        act.Should().Throw<ScriptException>().WithMessage("*oops*");
    }

    [Fact]
    public void RunPostResponse_ValidScript_DoesNotThrow()
    {
        var session = new ScriptSession(null);
        var response = new PacketPanda.Core.Http.HttpResponse(200, "ok", 10);
        var act = () => session.RunPostResponse("var x = 1;", response);
        act.Should().NotThrow();
    }

    [Fact]
    public void RunPostResponse_ScriptError_RecordsFailedTestInstead()
    {
        var session = new ScriptSession(null);
        var response = new PacketPanda.Core.Http.HttpResponse(200, "ok", 10);
        session.RunPostResponse("throw new Error('boom');", response);
        session.TestResults.Should().ContainSingle(r => !r.Passed && r.ErrorMessage!.Contains("boom"));
    }

    [Fact]
    public void PpTest_PassingAssertion_RecordsPassedResult()
    {
        var session = new ScriptSession(null);
        var response = new PacketPanda.Core.Http.HttpResponse(200, "ok", 10);
        session.RunPostResponse("pp.test('status ok', function() { pp.expect(200).to.equal(200); });", response);
        session.TestResults.Should().ContainSingle(r => r.Name == "status ok" && r.Passed);
    }

    [Fact]
    public void PpTest_FailingAssertion_RecordsFailedResultWithMessage()
    {
        var session = new ScriptSession(null);
        var response = new PacketPanda.Core.Http.HttpResponse(200, "ok", 10);
        session.RunPostResponse("pp.test('bad', function() { pp.expect(200).to.equal(404); });", response);
        session.TestResults.Should().ContainSingle(r => r.Name == "bad" && !r.Passed && r.ErrorMessage != null);
    }

    [Fact]
    public void PpEnvironmentSet_ViaScript_AppearsInEnvironmentUpdates()
    {
        var session = new ScriptSession(null);
        var response = new PacketPanda.Core.Http.HttpResponse(200, "ok", 10);
        session.RunPostResponse("pp.environment.set('token', 'abc123');", response);
        session.EnvironmentUpdates["token"].Should().Be("abc123");
    }

    [Fact]
    public void PpResponse_AccessInPreRequest_ThrowsScriptException()
    {
        var session = new ScriptSession(null);
        var act = () => session.RunPreRequest("var s = pp.response.status;");
        act.Should().Throw<ScriptException>();
    }

    [Fact]
    public void PpResponseJson_ValidJson_ParsesToAccessibleObject()
    {
        var session = new ScriptSession(null);
        var response = new PacketPanda.Core.Http.HttpResponse(200, "{\"id\":42}", 10);
        session.RunPostResponse("pp.test('has id', function() { pp.expect(pp.response.json().id).to.equal(42); });", response);
        session.TestResults.Should().ContainSingle(r => r.Name == "has id" && r.Passed);
    }
}
