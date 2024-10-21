namespace PdbNative;

internal static class MsfDefs
{
    public const int MsfFileHeaderSize = 52;


    /// <summary>
    /// File offset for the Stream Pages Map when using Big MSF.
    /// </summary>
    public const int BigStreamPagesMapOffset = 52;

    public static readonly byte[] FileHeaderMagic = {
        0x4d, 0x69, 0x63, 0x72, 0x6f, 0x73, 0x6f, 0x66, // Microsof
        0x74, 0x20, 0x43, 0x2f, 0x43, 0x2b, 0x2b, 0x20, // t C/C++
        0x4d, 0x53, 0x46, 0x20, 0x37, 0x2e, 0x30, 0x30, // MSF 7.00
        0x0d, 0x0a, 0x1a, 0x44, 0x53, 0x00, 0x00, 0x00, // ...DS...
    };

    public static readonly uint[] AllowedPageSizes = {
        0x800, 0x1000, 0x2000, 0x4000
    };

    public static bool IsAllowedPageSize(uint pageSize)
    {
        foreach (var allowedPageSize in AllowedPageSizes)
        {
            if (allowedPageSize == pageSize)
            {
                return true;
            }
        }

        return false;
    }

    public const uint NIL_STREAM_SIZE = 0xffffffffu;

    public static uint GetNumPages(uint numBytes, uint pageSize)
    {
        return (numBytes + pageSize - 1) / pageSize;
    }

    public static uint GetNumPagesInStream(uint streamSize, uint pageSize)
    {
        if (streamSize == NIL_STREAM_SIZE)
        {
            return 0;
        }
        else
        {
            return GetNumPages(streamSize, pageSize);
        }
    }

    public const int NIL_STREAM_INDEX = 0xffff;
}
