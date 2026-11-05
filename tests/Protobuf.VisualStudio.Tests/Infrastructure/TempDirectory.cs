namespace Protobuf.VisualStudio.Tests.Infrastructure;

/// <summary>A unique scratch folder under %TEMP% that is deleted on dispose.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        this.Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "protobuf-vs-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.Path);
    }

    public string Path { get; }

    /// <summary>Creates <paramref name="relativePath"/> (and its folders) with the given content and returns its full path.</summary>
    public string File(string relativePath, string content = "")
    {
        var full = System.IO.Path.Combine(this.Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
        return full;
    }

    public string Folder(string relativePath)
    {
        var full = System.IO.Path.Combine(this.Path, relativePath);
        Directory.CreateDirectory(full);
        return full;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(this.Path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
