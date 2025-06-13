using System.Runtime.InteropServices;

namespace Pdb;

/// <summary>
/// Contains the Public Symbol Index (PSI)
/// </summary>
public sealed class PublicSymbolIndex
{
    readonly SymbolNameTable _names;

    public SymbolNameTable Names { get { return _names; } }

    internal PublicSymbolIndex(MsfReader msf, int stream, bool isFastLink)
    {
        var sr = msf.GetStreamReader(stream);

        PublicSymbolIndexStreamHeader header = default;
        Span<byte> headerSpan = MemoryMarshal.AsBytes(new Span<PublicSymbolIndexStreamHeader>(ref header));
        sr.ReadAtExact(0, headerSpan);

        _names = new SymbolNameTable(ref sr, headerSpan.Length, (int)sr.StreamSize - headerSpan.Length, isFastLink);

        // After the name table, the PSI contains an address-to-symbol-offset table.
        // We do not yet decode it.
    }

    private PublicSymbolIndex()
    {
        _names = SymbolNameTable.MakeEmpty();
    }

    internal static PublicSymbolIndex MakeEmpty()
    {
        return new PublicSymbolIndex();
    }
}

/// <summary>
/// This structure is stored at the beginning of the Public Symbol Index (PSI) stream.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
struct PublicSymbolIndexStreamHeader
{
    /// Length in bytes of the symbol hash table.  This region immediately follows PSGSIHDR.
    internal uint name_table_size;

    /// Length in bytes of the address map.  This region immediately follows the symbol hash.
    internal uint addr_table_size;

    /// The number of thunk records.
    internal uint num_thunks;
    /// Size in bytes of each thunk record.
    internal uint thunk_size;
    internal ushort thunk_table_section;
    internal ushort padding;
    internal uint thunk_table_offset;
    internal uint num_sections;
}

