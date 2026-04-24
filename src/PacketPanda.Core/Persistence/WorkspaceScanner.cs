using PacketPanda.Core.Environments;
using Tomlyn;
using Tomlyn.Model;

namespace PacketPanda.Core.Persistence;

public sealed class WorkspaceScanner : IWorkspaceScanner
{
    private const string EnvFolderName = "environments";

    public Task<WorkspaceScan> ScanAsync(string rootPath) => Task.Run(() =>
    {
        var collections = ScanFolder(rootPath, isRoot: true);
        var envDir = Path.Combine(rootPath, EnvFolderName);
        var envs = Directory.Exists(envDir)
            ? Directory.GetFiles(envDir, "*.toml")
                .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                .Select(BuildEnvRef)
                .ToList()
            : new List<EnvironmentRef>();
        return new WorkspaceScan(collections, envs);
    });

    private static FolderNode ScanFolder(string path, bool isRoot = false)
    {
        var dirs = Directory.GetDirectories(path)
            .Where(d => !(isRoot && string.Equals(
                Path.GetFileName(d), EnvFolderName, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .Select(p => ScanFolder(p))
            .Cast<CollectionNode>();

        var files = Directory.GetFiles(path, "*.toml")
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .Select(BuildRequestNode)
            .Cast<CollectionNode>();

        return new FolderNode(Path.GetFileName(path), path, dirs.Concat(files).ToList());
    }

    private static RequestNode BuildRequestNode(string path)
    {
        string? method = null;
        try
        {
            var table = Toml.ToModel(File.ReadAllText(path));
            if (table.TryGetValue("method", out var v) && v is string m)
                method = m.ToUpperInvariant();
        }
        catch { }
        return new RequestNode(Path.GetFileName(path), path, method);
    }

    private static EnvironmentRef BuildEnvRef(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        try
        {
            var table = Toml.ToModel(File.ReadAllText(path));
            if (table.TryGetValue("name", out var v) && v is string s && s.Length > 0)
                name = s;
        }
        catch { }
        return new EnvironmentRef(name, path);
    }
}
