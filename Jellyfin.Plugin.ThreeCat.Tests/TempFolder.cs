namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Temporary folder that is deleted when the test finishes.
/// </summary>
internal sealed class TempFolder : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TempFolder"/> class.
    /// </summary>
    internal TempFolder()
    {
        Root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "threecat-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Root);
    }

    /// <summary>
    /// Gets the folder created for the test.
    /// </summary>
    internal string Root { get; }

    /// <summary>
    /// Builds a path inside the folder.
    /// </summary>
    /// <param name="parts">Path segments.</param>
    /// <returns>The combined path.</returns>
    internal string Combine(params string[] parts)
        => System.IO.Path.Combine([Root, .. parts]);

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover folder under %TEMP% is harmless.
        }
    }
}
