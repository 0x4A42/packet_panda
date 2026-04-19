namespace PacketPanda.Core.Persistence;

public interface IWorkspaceScanner
{
    Task<FolderNode> ScanAsync(string rootPath);
}
