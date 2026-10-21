namespace Migrator.Target.Core.Ir;

/// <summary>Writes generated files under an app folder and records them; every target engine uses it.</summary>
public static class OutputFiles
{
    public static void Put(string dir, string relativePath, string content, List<string> files)
    {
        var path = Path.Combine(dir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.EndsWith('\n') ? content : content + "\n");
        files.Add(path);
    }

    public static void PutBytes(string dir, string relativePath, byte[] data, List<string> files)
    {
        var path = Path.Combine(dir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
        files.Add(path);
    }
}
