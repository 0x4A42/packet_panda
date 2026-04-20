namespace PacketPanda.Core.Environments;

public interface IEnvironmentFileStore
{
    Task<Environment> LoadAsync(string path);
    Task SaveAsync(string path, Environment env);
}
