using Jellyfin.Plugin.ThreeCat.Library;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of the checks applied to the destination folder before writing anything to disk.
/// </summary>
public sealed class LibraryPathTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Check_EmptyPath_AsksForAFolder(string? path)
    {
        var status = LibraryPath.Check(path);

        Assert.False(status.IsValid);
        Assert.False(status.Exists);
        Assert.False(status.Writable);
        Assert.Equal("Indica la carpeta de destino.", status.Error);
    }

    [Fact]
    public void Check_RelativePath_IsRejected()
    {
        var status = LibraryPath.Check("media/series");

        Assert.False(status.IsValid);
        Assert.Contains("absoluta", status.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_ExistingFolder_IsValidAndWritable()
    {
        using var folder = new TempFolder();

        var status = LibraryPath.Check(folder.Root);

        Assert.True(status.IsValid);
        Assert.True(status.Exists);
        Assert.True(status.Writable);
        Assert.Null(status.Error);
        Assert.Equal(folder.Root, status.Path);
    }

    [Fact]
    public void Check_FileInsteadOfFolder_IsRejected()
    {
        using var folder = new TempFolder();
        var file = folder.Combine("no-es-una-carpeta.txt");
        File.WriteAllText(file, "x");

        var status = LibraryPath.Check(file);

        Assert.True(status.IsValid);
        Assert.False(status.Writable);
        Assert.Contains("archivo", status.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_MissingFolder_ExplainsThatItIsNotMounted()
    {
        using var folder = new TempFolder();

        var status = LibraryPath.Check(folder.Combine("no-existe"));

        Assert.True(status.IsValid);
        Assert.False(status.Exists);
        Assert.False(status.Writable);
        Assert.Contains("no existe", status.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureWritableRoot_RejectsARelativePath()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => LibraryPath.EnsureWritableRoot("media/series"));

        Assert.Contains("3Cat: no se puede usar la carpeta de destino", error.Message, StringComparison.Ordinal);
        Assert.Contains("absoluta", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureWritableRoot_CreatesTheFolder()
    {
        using var folder = new TempFolder();
        var target = folder.Combine("series", "3cat");

        LibraryPath.EnsureWritableRoot(target);

        Assert.True(Directory.Exists(target));
    }

    [Fact]
    public void EnsureWritableRoot_OnAFile_Throws()
    {
        using var folder = new TempFolder();
        var file = folder.Combine("archivo.txt");
        File.WriteAllText(file, "x");

        var error = Assert.Throws<InvalidOperationException>(() => LibraryPath.EnsureWritableRoot(file));

        Assert.Contains("archivo", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IsWritable_TellsExistingFoldersFromMissingOnes()
    {
        using var folder = new TempFolder();

        Assert.True(LibraryPath.IsWritable(folder.Root));
        Assert.False(LibraryPath.IsWritable(folder.Combine("no-existe")));
    }

    [Fact]
    public void Suggest_ReturnsOnlyFoldersThatExist()
    {
        foreach (var suggestion in LibraryPath.Suggest())
        {
            Assert.True(Directory.Exists(suggestion), suggestion);
        }
    }
}
