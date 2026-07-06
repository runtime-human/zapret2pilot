using System;
using System.IO;

namespace Zapret2Pilot.App.Input;

/// <summary>
/// Risk classification for a candidate file import. Today the
/// application only accepts a small allowlist of file shapes
/// (<see cref="FileImportBoundary"/>), so anything else is
/// <see cref="Blocked"/>.
/// </summary>
public enum ImportRisk
{
    Safe = 0,
    Blocked = 1,
}

/// <summary>
/// Outcome of validating a candidate file for import. Only
/// <see cref="IsAllowed"/> = <c>true</c> is considered safe to
/// hand off to a feature consumer.
/// </summary>
public sealed record FileImportResult(
    bool IsAllowed,
    ImportRisk Risk,
    string? ErrorCode,
    string? ErrorMessage);

/// <summary>
/// Trusted boundary for any file the user is asked to import. The
/// rules are intentionally narrow: only a small allowlist of
/// extensions, sizes, and header magic may pass. Anything else is
/// rejected before it reaches a feature consumer.
/// </summary>
public static class FileImportBoundary
{
    public const long DefaultMaxBytes = 10 * 1024 * 1024;

    internal const string ErrorCodePathMissing = "Z2P.INPUT.FILE_PATH_MISSING";
    internal const string ErrorCodePathInvalid = "Z2P.INPUT.FILE_PATH_INVALID";
    internal const string ErrorCodeExtensionBlocked = "Z2P.INPUT.FILE_EXTENSION_BLOCKED";
    internal const string ErrorCodeFileNotFound = "Z2P.INPUT.FILE_NOT_FOUND";
    internal const string ErrorCodeOversize = "Z2P.INPUT.FILE_OVERSIZE";
    internal const string ErrorCodeMagicMismatch = "Z2P.INPUT.FILE_MAGIC_MISMATCH";

    // PK\x03\x04 (local file header) and PK\x05\x06 (end of central
    // directory record) are the two signatures that any conforming
    // ZIP archive or its empty variant must start with.
    private static readonly byte[] ZipLocalFileHeaderMagic = new byte[] { 0x50, 0x4B, 0x03, 0x04 };
    private static readonly byte[] ZipEndOfCentralDirectoryMagic = new byte[] { 0x50, 0x4B, 0x05, 0x06 };

    public static FileImportResult Validate(string filePath, long maxBytes = DefaultMaxBytes)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Blocked(ErrorCodePathMissing, "File path is required.");
        }

        try
        {
            // Path.GetFullPath throws on illegal characters and on
            // rooted paths that exceed the platform limit. We treat
            // any such failure as an invalid-path error.
            _ = Path.GetFullPath(filePath);
        }
        catch (ArgumentException ex)
        {
            return Blocked(ErrorCodePathInvalid, ex.Message);
        }
        catch (PathTooLongException ex)
        {
            return Blocked(ErrorCodePathInvalid, ex.Message);
        }
        catch (NotSupportedException ex)
        {
            return Blocked(ErrorCodePathInvalid, ex.Message);
        }
        catch (System.Security.SecurityException ex)
        {
            return Blocked(ErrorCodePathInvalid, ex.Message);
        }

        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        bool isJson = extension == ".json";
        bool isZip = extension == ".zip";

        if (!isJson && !isZip)
        {
            return Blocked(ErrorCodeExtensionBlocked, $"Extension '{extension}' is not allowed.");
        }

        FileInfo info;
        try
        {
            info = new FileInfo(filePath);
        }
        catch (ArgumentException ex)
        {
            return Blocked(ErrorCodePathInvalid, ex.Message);
        }
        catch (PathTooLongException ex)
        {
            return Blocked(ErrorCodePathInvalid, ex.Message);
        }
        catch (NotSupportedException ex)
        {
            return Blocked(ErrorCodePathInvalid, ex.Message);
        }
        catch (System.Security.SecurityException ex)
        {
            return Blocked(ErrorCodePathInvalid, ex.Message);
        }

        if (!info.Exists)
        {
            return Blocked(ErrorCodeFileNotFound, "File does not exist.");
        }

        if (info.Length > maxBytes)
        {
            return Blocked(ErrorCodeOversize, $"File exceeds the {maxBytes} byte limit.");
        }

        byte[] header;
        try
        {
            using FileStream stream = new(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            header = ReadHeader(stream, 4);
        }
        catch (FileNotFoundException)
        {
            return Blocked(ErrorCodeFileNotFound, "File does not exist.");
        }
        catch (DirectoryNotFoundException)
        {
            return Blocked(ErrorCodeFileNotFound, "File does not exist.");
        }
        catch (IOException ex)
        {
            return Blocked(ErrorCodePathInvalid, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Blocked(ErrorCodePathInvalid, ex.Message);
        }

        if (isJson)
        {
            if (!IsJsonHeader(header))
            {
                return Blocked(ErrorCodeMagicMismatch, "File is not valid JSON.");
            }
        }
        else if (isZip)
        {
            if (!IsZipHeader(header))
            {
                return Blocked(ErrorCodeMagicMismatch, "File is not a valid ZIP archive.");
            }
        }

        return new FileImportResult(IsAllowed: true, Risk: ImportRisk.Safe, ErrorCode: null, ErrorMessage: null);
    }

    private static FileImportResult Blocked(string code, string message) =>
        new(IsAllowed: false, Risk: ImportRisk.Blocked, ErrorCode: code, ErrorMessage: message);

    private static byte[] ReadHeader(FileStream stream, int length)
    {
        byte[] buffer = new byte[length];
        int read = 0;
        while (read < length)
        {
            int chunk = stream.Read(buffer, read, length - read);
            if (chunk <= 0)
            {
                break;
            }

            read += chunk;
        }

        if (read < length)
        {
            Array.Resize(ref buffer, read);
        }

        return buffer;
    }

    private static bool IsJsonHeader(byte[] header)
    {
        // The header must start with either an object ('{') or an
        // array ('[') after the optional UTF-8 BOM and any leading
        // whitespace. We deliberately do not read the entire file
        // and we do not perform full JSON validation here — only
        // the trusted boundary check on the first 4 bytes.
        ReadOnlySpan<byte> span = header;

        if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
        {
            span = span[3..];
        }

        // Skip leading JSON whitespace.
        int index = 0;
        while (index < span.Length && IsJsonWhitespace(span[index]))
        {
            index++;
        }

        if (index >= span.Length)
        {
            // All 4 bytes (post-BOM) were whitespace. That is still
            // a legal JSON document prefix.
            return true;
        }

        byte first = span[index];
        return first == (byte)'{' || first == (byte)'[';
    }

    private static bool IsJsonWhitespace(byte b) =>
        b == (byte)' ' || b == (byte)'\t' || b == (byte)'\r' || b == (byte)'\n';

    private static bool IsZipHeader(byte[] header)
    {
        return StartsWith(header, ZipLocalFileHeaderMagic)
            || StartsWith(header, ZipEndOfCentralDirectoryMagic);
    }

    private static bool StartsWith(byte[] haystack, byte[] prefix)
    {
        if (haystack.Length < prefix.Length)
        {
            return false;
        }

        for (int i = 0; i < prefix.Length; i++)
        {
            if (haystack[i] != prefix[i])
            {
                return false;
            }
        }

        return true;
    }
}
