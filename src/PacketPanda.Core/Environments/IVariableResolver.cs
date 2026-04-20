namespace PacketPanda.Core.Environments;

public interface IVariableResolver
{
    ResolveResult Resolve(string template, Environment? activeEnv);
}
