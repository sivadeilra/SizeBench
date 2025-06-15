
namespace Pdb.CodeView;

public ref struct SymIter
{
    public Bytes _data;

    SymIter(ReadOnlySpan<byte> data)
    {
        this._data = new Bytes(data);
    }

    public static SymIter ForRaw(ReadOnlySpan<byte> data)
    {
        return new SymIter(data);
    }

    public static SymIter ForGlobalSymbols(ReadOnlySpan<byte> data)
    {
        return new SymIter(data);
    }

    public static SymIter ForModuleSymbols(ReadOnlySpan<byte> data)
    {
        // Module symbols have a 4-byte prefix, which we ignore.

        if (data.Length < 4)
        {
            return new SymIter(Span<byte>.Empty);
        }

        return new SymIter(data.Slice(4));
    }

    /// <summary>
    /// Skips ahead by <c>n</c> bytes in the input data. This is used for
    /// seeking to a specific record in a symbol stream.
    /// </summary>
    /// <remarks>
    /// If <c>n</c> is larger than the number of bytes remaining in the
    /// input data, then this sets the input data span to empty (and does
    /// not throw an exception).
    /// </remarks>
    /// <param name="n">The number of bytes to skip.</param>
    public void Skip(int n)
    {
        if (this._data.Length < n)
        {
            this._data._data = new ReadOnlySpan<byte>();
        }
        else
        {
            this._data._data = this._data._data.Slice(n);
        }
    }

    /// <summary>
    /// Decodes a single record
    /// </summary>
    public static bool NextOne(ReadOnlySpan<byte> data, out SymRecord sym)
    {
        SymIter iter = new SymIter(data);
        return iter.Next(out sym);
    }

    // The format of a symbol record is:
    //      uint16 record_len;  // covers the length of the next field AND the payload
    //      uint16 kind;
    //      uint8 record_data[record_len - 2];
    //
    public bool Next(out SymRecord sym)
    {
        sym= default;

        if (_data._data.Length < 4)
        {
            return false;
        }

        ushort recordSize = _data.ReadUInt16();
        if (recordSize < 2)
        {
            return false;
        }

        SymKind kind = (SymKind)_data.ReadUInt16();
        ReadOnlySpan<byte> recordBytes = _data.ReadN(recordSize - 2);
        sym = new SymRecord(kind, recordBytes);

        return true;
    }
}
