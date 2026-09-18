using FluentAssertions;
using SecureFolder.Core.Filesystem;

namespace SecureFolder.Tests.Filesystem;

public class SecureFolderFileSystemTests
{
    [Theory]
    [InlineData("\\", "\\archivo_root.txt", "archivo_root.txt")]
    [InlineData("\\", "\\carpeta", "carpeta")]
    [InlineData("\\", "\\sub\\archivo.txt", null)]
    [InlineData("\\", "\\", null)]
    [InlineData("\\", "\\carpeta\\hijo", null)]
    [InlineData("\\carpeta", "\\carpeta\\archivo.txt", "archivo.txt")]
    [InlineData("\\carpeta", "\\carpeta\\sub\\archivo.txt", null)]
    [InlineData("\\carpeta", "\\otra\\archivo.txt", null)]
    [InlineData("\\carpeta\\sub", "\\carpeta\\sub\\a.txt", "a.txt")]
    [InlineData("\\carpeta", "\\carpeta2\\x.txt", null)]
    public void DirectChildName_ReturnsCorrectName(string dir, string full, string? expected)
    {
        SecureFolderFileSystem.DirectChildName(dir, full).Should().Be(expected);
    }
}