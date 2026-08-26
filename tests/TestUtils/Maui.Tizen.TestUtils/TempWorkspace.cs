namespace Maui.Tizen.TestUtils;

/// <summary>
/// A disposable temporary directory used for generated consumer projects, local package feeds and
/// build fixtures.
/// </summary>
/// <remarks>
/// Directories are created under the OS temp path rather than the repository so a failed run can
/// never leave the working tree dirty and trip the "no uncommitted changes" CI check.
/// </remarks>
public sealed class TempWorkspace : IDisposable
{
    TempWorkspace(string path) => Path = path;

    /// <summary>Absolute path of the temporary directory.</summary>
    public string Path { get; }

    /// <summary>Creates a uniquely named temporary directory.</summary>
    /// <param name="prefix">Short label included in the directory name to aid debugging.</param>
    public static TempWorkspace Create(string prefix = "maui-tizen")
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"{prefix}-{Guid.NewGuid():N}");

        Directory.CreateDirectory(path);
        return new TempWorkspace(path);
    }

    /// <summary>Creates a subdirectory and returns its absolute path.</summary>
    public string CreateSubdirectory(string name)
    {
        var path = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Writes <paramref name="content"/> to <paramref name="relativePath"/>.</summary>
    public string WriteFile(string relativePath, string content)
    {
        var path = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

        // Normalized newlines keep generated-file assertions stable across Windows and Unix runners.
        File.WriteAllText(path, content.ReplaceLineEndings("\n"));
        return path;
    }

    /// <summary>Combines a path relative to the workspace root.</summary>
    public string Combine(params string[] segments) =>
        System.IO.Path.Combine(new[] { Path }.Concat(segments).ToArray());

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A locked file on a CI agent must not fail an otherwise passing test.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
