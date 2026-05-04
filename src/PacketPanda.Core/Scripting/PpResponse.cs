#pragma warning disable IDE1006
using Jint;
using Jint.Native;
using PacketPanda.Core.Http;

namespace PacketPanda.Core.Scripting;

internal sealed class PpResponse
{
    private readonly Engine _engine;
    private bool _available;
    private int _status;
    private string _body = string.Empty;

    internal PpResponse(Engine engine) { _engine = engine; }

    internal void SetFrom(HttpResponse response)
    {
        _available = true;
        _status = response.StatusCode;
        _body = response.Body;
    }

    private void EnsureAvailable()
    {
        if (!_available)
            throw new InvalidOperationException("pp.response is not available in pre-request scripts");
    }

    public int status { get { EnsureAvailable(); return _status; } }
    public string body { get { EnsureAvailable(); return _body; } }

    public JsValue json()
    {
        EnsureAvailable();
        try
        {
            var jsonLiteral = System.Text.Json.JsonSerializer.Serialize(_body);
            return _engine.Evaluate($"JSON.parse({jsonLiteral})");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Response body is not valid JSON", ex);
        }
    }
}
