#pragma warning disable IDE1006
using Jint;
using Jint.Native;
using Jint.Runtime;
using PacketPanda.Core.Http;
using Environment = PacketPanda.Core.Environments.Environment;

namespace PacketPanda.Core.Scripting;

public sealed class ScriptSession
{
    private readonly Engine _engine;
    private readonly List<TestResult> _results = new();
    private readonly Dictionary<string, string> _envOverlay = new();
    private readonly PpResponse _ppResponse;
    private readonly PpRequest _ppRequest;
    private readonly PpEnvironment _ppEnvironment;

    public IReadOnlyList<TestResult> TestResults => _results;
    public IReadOnlyDictionary<string, string> EnvironmentUpdates => _envOverlay;

    public ScriptSession(Environment? activeEnv)
    {
        _engine = new Engine();
        _ppResponse = new PpResponse(_engine);
        _ppRequest = new PpRequest();
        _ppEnvironment = new PpEnvironment(activeEnv, _envOverlay);

        // Register each sub-object under a temp name, then assemble pp as a plain JS object.
        _engine.SetValue("_ppEnv", _ppEnvironment);
        _engine.SetValue("_ppReq", _ppRequest);
        _engine.SetValue("_ppRes", _ppResponse);
        _engine.SetValue("_ppCtx", this);
        _engine.Execute("""
            var pp = {
                environment: _ppEnv,
                request: _ppReq,
                response: _ppRes,
                test: function(name, fn) { _ppCtx.test(name, fn); },
                expect: function(val) { return _ppCtx.expect(val); }
            };
        """);
    }

    public void test(string name, JsValue fn)
    {
        try
        {
            _engine.Invoke(fn, Array.Empty<object>());
            _results.Add(new TestResult(name, true));
        }
        catch (JavaScriptException ex) { _results.Add(new TestResult(name, false, ex.Message)); }
        catch (Exception ex) { _results.Add(new TestResult(name, false, ex.Message)); }
    }

    public PpExpect expect(object? value) => new(value);

    public void RunPreRequest(string script)
    {
        try { _engine.Execute(script); }
        catch (JavaScriptException ex) { throw new ScriptException(ex.Message); }
        catch (Exception ex) { throw new ScriptException(ex.Message); }
    }

    public void RunPostResponse(string script, HttpResponse response)
    {
        _ppResponse.SetFrom(response);
        try { _engine.Execute(script); }
        catch (JavaScriptException ex) { _results.Add(new TestResult("Script error", false, ex.Message)); }
        catch (Exception ex) { _results.Add(new TestResult("Script error", false, ex.Message)); }
    }
}
