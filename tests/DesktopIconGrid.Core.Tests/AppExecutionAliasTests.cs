using System.Buffers.Binary;
using System.Text;
using DesktopIconGrid.Core.Launching;

namespace DesktopIconGrid.Core.Tests;

/// <summary>Reading the target executable out of an app-execution alias's reparse data.</summary>
public class AppExecutionAliasTests
{
    private const string Target = @"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal_1.21.0_x64__8wekyb3d8bbwe\wt.exe";

    private static byte[] Buffer(uint tag, params string[] strings)
    {
        var payload = Encoding.Unicode.GetBytes(string.Concat(strings.Select(s => s + "\0")));
        var buffer = new byte[8 + 4 + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, tag);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), (ushort)(4 + payload.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8), 3); // version
        payload.CopyTo(buffer, 12);
        return buffer;
    }

    [Fact]
    public void The_third_string_is_the_executable()
    {
        var buffer = Buffer(AppExecutionAlias.ReparseTag, "Microsoft.WindowsTerminal_8wekyb3d8bbwe", "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", Target, "0");

        Assert.True(AppExecutionAlias.TryParseTarget(buffer, out var target));
        Assert.Equal(Target, target);
    }

    [Fact]
    public void Other_reparse_points_and_short_buffers_are_rejected()
    {
        Assert.False(AppExecutionAlias.TryParseTarget(Buffer(0xA000000C, "a", "b", Target), out _)); // a symbolic link
        Assert.False(AppExecutionAlias.TryParseTarget(Buffer(AppExecutionAlias.ReparseTag, "pkg", "pkg!App"), out _));
        Assert.False(AppExecutionAlias.TryParseTarget(Buffer(AppExecutionAlias.ReparseTag, "pkg", "pkg!App", "not a path"), out _));
        Assert.False(AppExecutionAlias.TryParseTarget(new byte[5], out _));
        Assert.False(AppExecutionAlias.TryParseTarget([], out _));

        var truncated = Buffer(AppExecutionAlias.ReparseTag, "pkg", "pkg!App", Target);
        Assert.False(AppExecutionAlias.TryParseTarget(truncated.AsSpan(0, truncated.Length - 10), out _)); // the length field promises more
    }

    [Fact]
    public void A_file_that_is_not_an_alias_does_not_resolve()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dig-alias-{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(path, []);
        try
        {
            Assert.Null(AppExecutionAlias.ResolveTarget(path));
            Assert.Null(AppExecutionAlias.ResolveTarget(path + ".missing"));
            Assert.Null(AppExecutionAlias.ResolveTarget(string.Empty));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
