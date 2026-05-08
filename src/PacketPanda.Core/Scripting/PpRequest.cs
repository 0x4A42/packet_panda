#pragma warning disable IDE1006
using PacketPanda.Core.Http;

namespace PacketPanda.Core.Scripting;

public sealed class PpRequest
{
    private string _url = string.Empty;
    private string _method = string.Empty;
    private string _body = string.Empty;

    public void SetFrom(HttpRequest req)
    {
        _url = req.Url;
        _method = req.Method.ToString().ToUpperInvariant();
        _body = req.Body switch
        {
            JsonBody j      => j.Text,
            PlainTextBody t => t.Text,
            XmlBody x       => x.Text,
            FormBody f      => string.Join("&", f.Fields.Where(ff => ff.Enabled)
                                  .Select(ff => $"{ff.Name}={ff.Value}")),
            _               => string.Empty
        };
    }

    public string url => _url;
    public string method => _method;
    public string body => _body;
}
