
namespace Pdb.CodeView;

public ref struct SymIter
{
    public Bytes _data;

    SymIter(Span<byte> data)
    {
        this._data = new Bytes(data);
    }

    public static SymIter ForGlobalSymbols(Span<byte> data) {
        return new SymIter(data);
    }

    public static SymIter ForModuleSymbols(Span<byte> data) {
        // Module symbols have a 4-byte prefix, which we ignore.

        if (data.Length < 4) {
            return new SymIter(Span<byte>.Empty);
        }

        return new SymIter(data.Slice(4));
    }

    // The format of a symbol record is:
    //      uint16 record_len;  // covers the length of the next field AND the payload
    //      uint16 kind;
    //      uint8 record_data[record_len - 2];
    //
    public bool Next(out SymKind kind, out ReadOnlySpan<byte> recordBytes)
    {
        kind = (SymKind)0;
        recordBytes = Span<byte>.Empty;

        if (_data._data.Length < 4)
        {
            return false;
        }

        ushort recordSize = _data.ReadUInt16();
        if (recordSize < 2)
        {
            return false;
        }

        kind = (SymKind)_data.ReadUInt16();

        recordBytes = _data.ReadN(recordSize - 2);

        return true;
    }
}
