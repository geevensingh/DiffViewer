using System.Linq;
using DiffViewer.Utility;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Utility;

public class BinaryDetectorTests
{
    // BOM constants duplicated from the production code so a typo on
    // either side cannot silently agree with itself.
    private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };
    private static readonly byte[] Utf16LeBom = { 0xFF, 0xFE };
    private static readonly byte[] Utf16BeBom = { 0xFE, 0xFF };
    private static readonly byte[] Utf32LeBom = { 0xFF, 0xFE, 0x00, 0x00 };
    private static readonly byte[] Utf32BeBom = { 0x00, 0x00, 0xFE, 0xFF };

    [Fact]
    public void IsBinary_EmptyBuffer_ReturnsFalse()
        => BinaryDetector.IsBinary(System.Array.Empty<byte>()).Should().BeFalse();

    [Fact]
    public void IsBinary_PlainAscii_ReturnsFalse()
        => BinaryDetector.IsBinary("hello world"u8).Should().BeFalse();

    [Fact]
    public void IsBinary_BufferWithNul_ReturnsTrue()
        => BinaryDetector.IsBinary(new byte[] { 1, 2, 3, 0, 4, 5 }).Should().BeTrue();

    [Fact]
    public void IsBinary_NulOnlyAfter8Kib_ReturnsFalse()
    {
        // Bytes 0..8191 are filled with 'A'; byte 8192 is NUL. The probe
        // window stops at 8 KiB, so the NUL is invisible to the detector.
        var bytes = new byte[8 * 1024 + 1];
        for (int i = 0; i < 8 * 1024; i++) bytes[i] = (byte)'A';
        bytes[8 * 1024] = 0;

        BinaryDetector.IsBinary(bytes).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(BomCases))]
    public void IsBinary_TextBomPrefix_ReturnsFalse(string label, byte[] bom)
    {
        _ = label; // surfaced in xUnit test display only.

        // Simulate a UTF-16-style payload: BOM followed by ASCII characters
        // where every other byte is NUL. Without the BOM exemption, this
        // would trip the NUL-byte heuristic.
        var bytes = bom.Concat(new byte[] { (byte)'h', 0, (byte)'i', 0 }).ToArray();

        BinaryDetector.IsBinary(bytes).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(BomCases))]
    public void HasTextBom_BomPrefix_ReturnsTrue(string label, byte[] bom)
    {
        _ = label;
        BinaryDetector.HasTextBom(bom).Should().BeTrue();
    }

    [Fact]
    public void HasTextBom_BomNotAtStart_ReturnsFalse()
    {
        // A BOM embedded mid-buffer (after one prefix byte) is not a BOM.
        var bytes = new byte[] { 0x00, 0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i' };
        BinaryDetector.HasTextBom(bytes).Should().BeFalse();
    }

    [Fact]
    public void HasTextBom_TruncatedBom_ReturnsFalse()
    {
        // A single 0xFF is not a BOM. The check must require the full prefix.
        BinaryDetector.HasTextBom(new byte[] { 0xFF }).Should().BeFalse();
    }

    [Fact]
    public void IsBinary_Utf32LeBomFollowedByLowNulHeavyText_ReturnsFalse()
    {
        // UTF-32 LE has three NULs per ASCII character ('A' = 41 00 00 00).
        // The BOM exemption must let this through.
        var bytes = Utf32LeBom.Concat(new byte[] { (byte)'A', 0, 0, 0, (byte)'B', 0, 0, 0 }).ToArray();
        BinaryDetector.IsBinary(bytes).Should().BeFalse();
    }

    public static System.Collections.Generic.IEnumerable<object[]> BomCases()
    {
        yield return new object[] { "UTF-8", Utf8Bom };
        yield return new object[] { "UTF-16 LE", Utf16LeBom };
        yield return new object[] { "UTF-16 BE", Utf16BeBom };
        yield return new object[] { "UTF-32 LE", Utf32LeBom };
        yield return new object[] { "UTF-32 BE", Utf32BeBom };
    }
}
