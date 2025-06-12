using System.Runtime.InteropServices;

namespace Pdb;

/// <summary>
/// Provides access to the Names Stream
/// </summary>
public sealed class Names
{
    readonly uint _version;
    readonly byte[] _stringData;
    readonly uint[] _hashTable;

    internal Names(MsfReader msf, int stream)
    {
        var sr = msf.GetStreamReader(stream);
        byte[] streamBytes = sr.ReadStreamToArray();
        Bytes streamData = new Bytes(streamBytes);

        // Read the header.
        NamesStreamHeader header = default;        
        Span<byte> headerBytes = MemoryMarshal.AsBytes(new Span<NamesStreamHeader>(ref header));
        streamData.ReadN(headerBytes.Length).CopyTo(headerBytes);

        if (header.Signature != NamesSignature)
        {
            throw new InvalidNamesStreamException("Signature is invalid");
        }

        switch (header.Version)
        {
            case VersionV1:
            case VersionV2:
                break;
            default:
                throw new InvalidNamesStreamException($"Version {header.Version} is not supported.");
        }

        if (header.StringsSize > (uint)int.MaxValue || (int)header.StringsSize > streamData.Length) {
            throw new InvalidNamesStreamException("Size of strings data is too large");
        }

        byte[] stringData = streamData.ReadN((int)header.StringsSize).ToArray();

        uint numHashes = streamData.ReadUInt32();
        if (numHashes > (uint)int.MaxValue || (int)numHashes > streamData.Length / 4)
        {
            throw new InvalidNamesStreamException("Number of hashes is excessively large");
        }

        uint[] hashTable = MemoryMarshal.Cast<byte, uint>(streamData.ReadN((int)numHashes * 4)).ToArray();

        uint numNames = streamData.ReadUInt32();
        if (numNames > (uint)int.MaxValue || (int)numNames > stringData.Length)
        {
            throw new InvalidNamesStreamException("Number of names is excessively large");
        }

        this._version = header.Version;
        this._stringData = stringData;
        this._hashTable = hashTable;
    }

    private Names()
    {
        this._version = VersionV1;
        this._stringData = new byte[] { 0 };
        this._hashTable = Array.Empty<uint>();
    }

    internal static Names MakeEmpty()
    {
        return new Names();
    }

    const uint NamesSignature = 0xEFFEEFFEu;
    const uint VersionV1 = 1;
    const uint VersionV2 = 2;

    [StructLayout(LayoutKind.Sequential)]
    struct NamesStreamHeader
    {
        internal uint Signature;
        internal uint Version;
        internal uint StringsSize;
    }

    public Utf8Span GetStringUtf8Bytes(NameIndex nameIndex)
    {
        if ((uint)nameIndex > (uint)this._stringData.Length) {
            throw new ArgumentOutOfRangeException("nameIndex");
        }

        int pos = (int)nameIndex;
        Bytes b = new Bytes(new ReadOnlySpan<byte>(this._stringData).Slice((int)nameIndex));
        return b.ReadUtf8Bytes();
    }
}

/// <summary>
/// Identifies a string index within the Names stream.
/// </summary>
public enum NameIndex : uint { }

public sealed class InvalidNamesStreamException : Exception
{
    public InvalidNamesStreamException(string message) : base(message) { }
}
