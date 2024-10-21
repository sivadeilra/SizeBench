using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PdbNative;

public sealed class TypeDatabase
{
    readonly MsfStream _typeStream;
    readonly uint _typeIndexBegin;
    readonly uint _typeIndexEnd;
    readonly uint _typeIndexBytes;
    readonly byte[] _typeRecords;
    readonly IndexBufferEntry[] _recordIndexTable;

    const int TpiHeaderSize = 64;

    private TypeDatabase(
        MsfStream typeStream,
        uint typeIndexBegin,
        uint typeIndexEnd,
        uint typeIndexBytes,
        byte[] typeRecords,
        IndexBufferEntry[] recordIndexTable
        )
    {
        this._typeStream = typeStream;
        this._typeIndexBegin = typeIndexBegin;
        this._typeIndexEnd = typeIndexEnd;
        this._typeIndexBytes = typeIndexBytes;
        this._typeRecords = typeRecords;
        this._recordIndexTable = recordIndexTable;
    }

    public static TypeDatabase Open(MsfReader msf, int typeStreamIndex)
    {
        MsfStream typeStream = msf.OpenStream(typeStreamIndex);

        byte[] tpiHeaderBytes = new byte[TpiHeaderSize];

        typeStream.ReadAt(0, tpiHeaderBytes, 0, TpiHeaderSize);

        var header = TpiHeader.FromBytes(tpiHeaderBytes);

        IndexBufferEntry[] recordIndexTable = null;

        if (header.hash_stream_index != MsfDefs.NIL_STREAM_INDEX)
        {
            MsfStream hashStream = msf.OpenStream(header.hash_stream_index);

            if (header.index_offset_buffer_length != 0)
            {
                int numEntries = (int)(header.index_offset_buffer_length / 8);
                byte[] indexOffsetBufferBytes = new byte[header.index_offset_buffer_length];
                hashStream.ReadAt(header.index_offset_buffer_offset, indexOffsetBufferBytes, 0, indexOffsetBufferBytes.Length);

                // Now convert it
                recordIndexTable = new IndexBufferEntry[numEntries];
                for (int i = 0; i < numEntries; ++i)
                {
                    var entryBytes = new Span<byte>(indexOffsetBufferBytes, i * 8);
                    recordIndexTable[i] = new IndexBufferEntry
                    {
                        TypeIndex = BitConverter.ToUInt32(entryBytes[0..4]),
                        StreamOffset = BitConverter.ToUInt32(entryBytes[4..8]),
                    };
                }
            }
        }
        else
        {
            // We don't have a hash stream. Lookups will be slow.
        }

        /*
        // Yeah, we're just gonna load EVERYTHING into memory.
        // Pay a big I/O cost up-front.
        byte[] typeRecords = new byte[typeRecordBytes];

        stream.ReadAt(headerSize, typeRecords, 0, (int)typeRecordBytes);
        */

        return new TypeDatabase(typeStream, typeIndexBegin, typeIndexEnd, typeRecordBytes, typeRecords, recordIndexTable);
    }

    public bool FindTypeRecordSeek(TypeIndex ti, out uint streamOffset, out uint startingTypeIndex)
    {
        if (ti.Value < this._typeIndexBegin || ti.Value >= this._typeIndexEnd)
        {
            streamOffset = 0;
            startingTypeIndex = 0;
            return false;
        }

        int lo = 0;
        int hi = this._recordIndexTable.Length;
        int mid = 0;
        while (lo < hi)
        {
            mid = lo + (hi - lo) / 2;
            uint midTi = this._recordIndexTable[mid].TypeIndex;

            if (ti.Value < midTi)
            {
                hi = mid;
            }
            else if (ti.Value > midTi)
            {
                lo = mid + 1;
            }
            else
            {
                // We got it exactly? Rare, but ok.
                break;
            }
        }

        if (mid < this._recordIndexTable.Length)
        {
            streamOffset = this._recordIndexTable[mid].StreamOffset;
            startingTypeIndex = this._recordIndexTable[mid].TypeIndex;
            return true;
        }
        else
        {
            streamOffset = 0;
            startingTypeIndex = 0;
            return false;
        }
    }

    /*
    public bool GetTypeRecordBytes(TypeIndex ti, out byte[] buffer, out int start, out int length)
    {
        if (ti.Value < this._typeIndexBegin || ti.Value >= this._typeIndexEnd)
        {
            return false;
        }

        uint relti = ti.Value - this._typeIndexBegin;
    }
    */
}

public struct TypeIndex
{
    public uint Value;
}

internal struct TpiHeader
{
    public uint version;
    public uint header_size;

    // Fields for Type Records
    public uint type_index_begin;
    public uint type_index_end;
    public uint type_record_bytes;

    // Fields for Type Hash Stream
    public ushort hash_stream_index;
    public ushort hash_aux_stream_index;
    public uint hash_key_size;
    public uint num_hash_buckets;
    public int hash_value_buffer_offset;
    public uint hash_value_buffer_length;
    public int index_offset_buffer_offset;
    public uint index_offset_buffer_length;
    public int hash_adj_buffer_offset;
    public uint hash_adj_buffer_length;

    public static TpiHeader FromBytes(in Span<byte> bytes)
    {
        return new TpiHeader
        {
            version = BitConverter.ToUInt32(bytes),
            header_size = BitConverter.ToUInt32(bytes[4..8]),
            type_index_begin = BitConverter.ToUInt32(bytes[8..12]),
            type_index_end = BitConverter.ToUInt32(bytes[12..16]),
            type_record_bytes = BitConverter.ToUInt32(bytes[16..20]),
            hash_stream_index = BitConverter.ToUInt16(bytes[20..22]),
            hash_aux_stream_index = BitConverter.ToUInt16(bytes[22..24]),
            hash_key_size = BitConverter.ToUInt32(bytes[24..28]),
            num_hash_buckets = BitConverter.ToUInt32(bytes[28..32]),
            hash_value_buffer_offset = BitConverter.ToInt32(bytes[32..36]),
            hash_value_buffer_length = BitConverter.ToUInt32(bytes[36..40]),
            index_offset_buffer_offset = BitConverter.ToInt32(bytes[40..44]),
            index_offset_buffer_length = BitConverter.ToUInt32(bytes[44..48]),
            hash_adj_buffer_offset = BitConverter.ToInt32(bytes[48..52]),
            hash_adj_buffer_length = BitConverter.ToUInt32(bytes[52..56]),
        };
    }
}

struct IndexBufferEntry
{
    internal uint TypeIndex;
    internal uint StreamOffset;
}
