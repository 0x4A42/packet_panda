namespace PacketPanda.Core.Http;

public abstract record SendResult
{
    private SendResult() { }

    public sealed record Ok(HttpResponse Response) : SendResult;

    public sealed record Error(string Message) : SendResult;
}
