
using Pdb.CodeView;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pdb;

/// <summary>
/// Contains a Type Database.
/// </summary>
/// <remarks>
/// This is used for reading the TPI Stream and IPI Stream.
/// </remarks>
public sealed class Types
{
    /// <summary>
    /// Contains the stream data.  We store this in uint instead of byte so that we can simplify
    /// scanning through type records.  Type records are always stored at uint32-aligned boundaries.
    /// </summary>
    readonly uint[] _data;

    TypeStreamHeader _header;

    readonly IndexOffsetEntry[] _indexOffsets;

    internal Types(MsfReader msf, int stream) {
        var sr = msf.GetStreamReader(PdbDefs.TpiStream);

        // Read the header.
        TypeStreamHeader header = default;
        sr.ReadAtExact(0, MemoryMarshal.AsBytes(new Span<TypeStreamHeader>(ref header)));
        _header = header;

        // Read the type stream. We read it into a uint[] buffer instead of a byte[] buffer.
        int typesLen32 = (int)(header.type_record_bytes / 4u);
        uint[] data32 = new uint[typesLen32];
        Span<byte> dataBytes = MemoryMarshal.Cast<uint, byte>(data32.AsSpan());
        sr.ReadAtExact(header.header_size, dataBytes);

        _data = data32;

        // If the type stream contains an index offset table, then read it.
        IndexOffsetEntry[] indexOffsets;
        if (header.index_offset_buffer_length != 0 && header.hash_stream_index != MsfDefs.NilStreamIndex16) {
            var hashSr = msf.GetStreamReader(header.hash_stream_index);
            int numIndexOffsets = (int)(header.index_offset_buffer_length / 8); // 8 = sizeof(IndexOffsetEntry)
            indexOffsets = new IndexOffsetEntry[numIndexOffsets];
            Span<byte> indexOffsetsBytes = MemoryMarshal.AsBytes(new Span<IndexOffsetEntry>(indexOffsets));
            hashSr.ReadAtExact(header.index_offset_buffer_offset, indexOffsetsBytes);
        } else {
            // The stream does not contain an index offset table.
            // Add a single entry.
            indexOffsets = new IndexOffsetEntry[1];
            indexOffsets[0] = new IndexOffsetEntry {
                typeIndex = header.type_index_begin,
                streamOffset = 0
            };
        }
        _indexOffsets = indexOffsets;
    }

    public uint TypeIndexBegin { get { return _header.type_index_begin; } }
    public uint TypeIndexEnd { get { return _header.type_index_end; } }

    public TypesIter Iter() {
        return new TypesIter(_data);
    }

    public bool IsPrimitiveType(uint typeIndex) {
        return typeIndex < _header.type_index_begin;
    }

    public bool FindTypeSearchStart(uint typeIndex, out int byteOffset, out int numRecordsToSkip) {
        if (typeIndex < _header.type_index_begin) {
            byteOffset = 0;
            numRecordsToSkip = 0;
            return false;
        }

        var offsets = _indexOffsets;
        if (offsets.Length == 0) {
            byteOffset = 0;
            numRecordsToSkip = 0;
            return false;
        }

        int lo = 0;
        int hi = offsets.Length;

        int bestNumRecordsToSkip = (int)(typeIndex - _header.type_index_begin);
        int bestByteOffset = 0;

        while (lo < hi) {
            int mid = lo + (hi - lo) / 2;
            ref var m = ref offsets[mid];

            // Desired record is below the current range.
            if (typeIndex < m.typeIndex) {
                hi = mid;
                continue;
            }

            // An exact hit should be rare.
            if (typeIndex == m.typeIndex) {
                byteOffset = (int)m.streamOffset;
                numRecordsToSkip = 0;
                return true;
            }

            // If we use this entry, how many records would we need to skip?
            int thisNumSkip = (int)(typeIndex - _header.type_index_begin);
            if (thisNumSkip < bestNumRecordsToSkip) {
                // This is closer.
                bestNumRecordsToSkip = thisNumSkip;
                bestByteOffset = (int)m.streamOffset;
            }

            lo = mid + 1;
        }

        numRecordsToSkip = bestNumRecordsToSkip;
        byteOffset = bestByteOffset;
        return true;
    }
}

public ref struct TypesIter {
    Span<uint> _data;

    internal TypesIter(Span<uint> data) {
        _data = data;
    }

    public bool Next(out Leaf kind, out ReadOnlySpan<byte> recordData) {
        kind = (Leaf)0;
        recordData = default;

        if (_data.IsEmpty) {
            return false;
        }

        uint word0 = _data[0];
        int payloadLen = (ushort)(word0 & 0xffffu);

        // The length includes the 'kind' field, so it must be large enough
        // to cover at least the kind. Check that now and adjust len.
        if (payloadLen < 2) {
            return false;
        }
        payloadLen -= 2;

        // Check that we have enough bytes left to cover the record payload.
        Span<byte> restBytes = MemoryMarshal.Cast<uint, byte>(_data.Slice(1));
        if (payloadLen > restBytes.Length) {
            // TODO: report error?
            return false;
        }
        
        kind = (Leaf)(ushort)(word0 >> 16);
        recordData = restBytes.Slice(0, payloadLen);

        // The length may be odd or may not be aligned to a 4-byte boundary.
        // Handle aligning to a 4-byte boundary now.

        // +3 to align to next boundary
        // >> 2 to divide bytes to uints
        // +1 for the record length and kind fields
        int advance32 = ((payloadLen + 3) >> 2) + 1;
        this._data = this._data.Slice(advance32);

        return true;
    }
}

public ref struct FieldIter {
    Span<uint> _data;

    public FieldIter(Span<uint> data) {
        _data = data;
    }

    public bool Next() {
    }
}

/// The header of the TPI stream.
[StructLayout(LayoutKind.Sequential, Size = 56)]
internal struct TypeStreamHeader {
    public uint version;
    public uint header_size;
    public uint type_index_begin;
    public uint type_index_end;
    /// The number of bytes of type record data following the `TypeStreamHeader`.
    public uint type_record_bytes;

    public ushort hash_stream_index;
    public ushort hash_aux_stream_index;

    /// The size of each hash key in the Hash Value Substream. For the current version of TPI,
    /// this value should always be 4.
    public uint hash_key_size;
    /// The number of hash buckets. This is used when calculating the record hashes. Each hash
    /// is computed, and then it is divided by num_hash_buckets and the remainder becomes the
    /// final hash.
    ///
    /// If `hash_value_buffer_length` is non-zero, then `num_hash_buckets` must also be non-zero.
    public uint num_hash_buckets;
    public int hash_value_buffer_offset;
    public uint hash_value_buffer_length;

    public int index_offset_buffer_offset;
    public uint index_offset_buffer_length;

    public int hash_adj_buffer_offset;
    public uint hash_adj_buffer_length;
}

[StructLayout(LayoutKind.Sequential)]
struct IndexOffsetEntry {
    public uint typeIndex;
    public uint streamOffset;
}
