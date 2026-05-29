namespace DiffViewer.Utility;

/// <summary>
/// Detects whether a blob is binary using git's NUL-byte-in-first-8 KiB
/// heuristic, with one exemption: a recognised text BOM (UTF-8, UTF-16
/// LE/BE, UTF-32 LE/BE) is a strong text signal and short-circuits the
/// heuristic. Without that exemption, UTF-16 source files — which legally
/// contain a NUL byte for every ASCII character — would be mis-flagged
/// as binary even though <see cref="EncodingDetector"/> downstream can
/// decode them correctly.
/// </summary>
internal static class BinaryDetector
{
    private const int ProbeBytes = 8 * 1024;

    // Mirrors EncodingDetector's BOM table. Duplicated rather than shared
    // because both files are small, the BOMs are a fixed interchange
    // standard, and exposing the table would invite unrelated callers.
    private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };
    private static readonly byte[] Utf16LeBom = { 0xFF, 0xFE };
    private static readonly byte[] Utf16BeBom = { 0xFE, 0xFF };
    private static readonly byte[] Utf32LeBom = { 0xFF, 0xFE, 0x00, 0x00 };
    private static readonly byte[] Utf32BeBom = { 0x00, 0x00, 0xFE, 0xFF };

    public static bool IsBinary(ReadOnlySpan<byte> bytes)
    {
        if (HasTextBom(bytes)) return false;
        var probe = bytes.Length > ProbeBytes ? bytes[..ProbeBytes] : bytes;
        return probe.IndexOf((byte)0) >= 0;
    }

    /// <summary>
    /// Returns true when <paramref name="bytes"/> starts with a recognised
    /// text BOM. Exposed so callers that combine our heuristic with an
    /// upstream signal (e.g. LibGit2Sharp's <c>blob.IsBinary</c>, which
    /// runs its own NUL-byte heuristic) can apply the same exemption.
    /// </summary>
    public static bool HasTextBom(ReadOnlySpan<byte> bytes)
    {
        // UTF-32 BOMs must be checked before UTF-16 because UTF-32 LE
        // (FF FE 00 00) shares its first two bytes with UTF-16 LE.
        // Both still mean "text", so a mismatch here is harmless — but
        // we keep the canonical ordering for parity with EncodingDetector.
        return StartsWith(bytes, Utf32LeBom)
            || StartsWith(bytes, Utf32BeBom)
            || StartsWith(bytes, Utf8Bom)
            || StartsWith(bytes, Utf16LeBom)
            || StartsWith(bytes, Utf16BeBom);
    }

    private static bool StartsWith(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> prefix) =>
        bytes.Length >= prefix.Length && bytes[..prefix.Length].SequenceEqual(prefix);
}
