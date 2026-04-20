namespace PacketPanda.Core.Persistence;

public interface IWorkspaceScanner
{
    Task<WorkspaceScan> ScanAsync(string rootPath);
}
