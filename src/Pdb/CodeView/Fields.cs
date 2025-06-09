
using System.Runtime.InteropServices;

namespace Pdb.CodeView;

/// <summary>
/// Given the contents of an `LF_FIELDLIST` type record, this will decode the fields within it.
/// </summary>
public ref struct FieldIter
{
    Span<uint> _data;

    public FieldIter(Span<uint> data)
    {
        _data = data;
    }

    /// <summary>
    /// Iterates the next Field value.
    /// </summary>
    /// <remarks>
    /// This does the minimal work needed to parse a Field. This _does not_ parse all of the values
    /// within each field. All it does is find the end of the record.
    /// 
    /// If the input data is malformed, then this will throw an exception.
    /// </remarks>
    public bool Next(out Field field)
    {
        if (_data.IsEmpty)
        {
            field = default;
            return false;
        }

        // Each field begins with a 2-byte field kind, followed by a payload whose length
        // size dependent on the field kind.  Field are necessarily padded to align to
        // the 4-byte boundaries, so we can safely require that the field list begin with
        // at least 4 bytes (which is why the entire thing is encoded in uint32[].

        ReadOnlySpan<byte> startBytes = MemoryMarshal.AsBytes(_data);
        int originalLength = startBytes.Length;

        Bytes b = new Bytes(startBytes);

        Leaf fieldKind = (Leaf)b.ReadUInt16();

        // The length is dependent on the field kind.
        switch (fieldKind)
        {
            case Leaf.LF_BCLASS:
                {
                    // 2 for attributes
                    // 4 for base type
                    b.Skip(6);
                    Number.Read(ref b, out _);
                    break;
                }

            case Leaf.LF_VBCLASS:
                // 2 for attributes
                // 4 for base type
                // 4 for virtual base type
                b.Skip(10);
                Number.Read(ref b, out _);
                break;

            case Leaf.LF_IVBCLASS:
                // 2 for attr
                // 4 for btype
                // 4 for vbtype
                b.Skip(10);
                Number.Read(ref b, out _); // vbpoff
                Number.Read(ref b, out _); // vboff
                break;

            case Leaf.LF_ENUMERATE:
                // 2 for attr
                b.Skip(2);
                Number.Read(ref b, out _); // value
                b.ReadUtf8Bytes(); // name
                break;

            case Leaf.LF_FRIENDFCN:
                // 4 for typeindex
                b.Skip(4);
                b.ReadUtf8Bytes();
                break;

            case Leaf.LF_INDEX:
                // 2 for padding
                // 4 for type index
                b.Skip(6);
                break;

            case Leaf.LF_MEMBER:
                // 2 for attr
                // 4 for type index
                b.Skip(6);
                Number.Skip(ref b); // offset
                b.ReadUtf8Bytes();
                break;

            case Leaf.LF_STMEMBER:
                // 2 for attr
                // 4 for type index
                b.Skip(6);
                b.ReadUtf8Bytes(); // name
                break;

            case Leaf.LF_METHOD:
                // 2 for count
                // 4 for methods type index
                b.ReadUtf8Bytes();
                break;

            case Leaf.LF_NESTEDTYPE:
                // 4 for nested type index
                b.ReadUtf8Bytes(); // name
                break;

            case Leaf.LF_VFUNCTAB:
                // 2 for padding
                // 4 for type index
                b.Skip(6);
                break;

            case Leaf.LF_FRIENDCLS:
                // 2 for padding
                // 4 for friend class type
                b.Skip(6);
                break;

            case Leaf.LF_ONEMETHOD:
                {
                    ushort attr = b.ReadUInt16();
                    _ = b.ReadUInt32(); // type index
                    if (MethodIntroducesVirtual(attr))
                    {
                        _ = b.ReadUInt32();
                    }
                    b.ReadUtf8Bytes();
                    break;
                }

            case Leaf.LF_VFUNCOFF:
                // 4 for vtable_type_index
                // 4 for offset
                b.Skip(8);
                break;

            case Leaf.LF_NESTEDTYPEEX:
                // 2 for attr
                // 4 for type index
                b.Skip(6);
                b.ReadUtf8Bytes();
                break;

            default:
                throw new Exception("unrecognized item within LF_FIELDLIST");
        }

        int newLength = b.Length;
        int fieldLength = originalLength - newLength;

        ReadOnlySpan<byte> fieldDataIncludingKind = startBytes.Slice(0, fieldLength);
        ReadOnlySpan<byte> fieldData = fieldDataIncludingKind.Slice(2);

        // Fields are always 4-byte aligned. Round up to cover the partial last uint32, if any.
        int fieldLengthInUInt32 = (fieldLength + 3) / 4;
        this._data = this._data.Slice(fieldLengthInUInt32);

        field = new Field
        {
            Kind = fieldKind,
            Data = fieldData
        };
        return true;
    }

    static bool MethodIntroducesVirtual(ushort attrs)
    {
        return ((attrs >> 2) & 0xf) switch
        {
            4 or 6 => true,
            _ => false
        };
    }
}

public ref struct Field
{
    public Leaf Kind;

    /// <summary>
    /// This does not include the Kind itself.
    /// </summary>
    public ReadOnlySpan<byte> Data;
}
