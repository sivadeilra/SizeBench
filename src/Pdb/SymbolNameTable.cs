
using Pdb.CodeView;
using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Pdb;

/// <summary>
/// This maps from a symbol name (string) to the byte offset within the Global Symbol Stream
/// of a symbol record.
/// </summary>
public sealed class SymbolNameTable
{
    /// <summary>
    /// Contains entries in the hash table, packed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The values in this table are indexed by the <c>_buckets</c> table. The <c>_buckets</c>
    /// table uses the "compressed spans" layout (similar to CSC/CSR), where <c>_buckets[i]</c> gives the
    /// beginning of bucket <c>i</c> and the _end_ of bucket <c>i - 1</c>.
    /// </para>
    /// 
    /// <para>
    /// The values stored in this table are offsets (plus 1) into the Global Symbol Stream, of a RefSym-based
    /// symbol record. These include S_PROCREF, S_LPROCREF, S_DATAREF, and a few other S_*REF symbol records.
    /// </para>
    /// 
    /// <para>
    /// A value of 0 means "no entry at this location in the hash table", but because the table is packed
    /// (encoded with a bitmap of which hash buckets are present and indirected through another table),
    /// in practice there should never be any 0 values.  However, the offsets still have a +1 bias, so when
    /// using them, you need to check for 0 (and treat it as "no such symbol") and if non-zero, you need
    /// to subtract 1 before indexing into the Global Symbol Stream.
    /// </para>
    /// 
    /// <para>
    /// We use <c>uint</c> and not <c>int</c> here because these values are offsets that point into the
    /// Global Symbol Stream.  Currently, streams can be up to 4GB in size, requiring <c>uint</c>.
    /// I have not yet seen a GSS that was more than 2GB, but 
    /// </para>
    /// </remarks>
    internal uint[] _offsets;

    /// <summary>
    /// Contains pointers into the <c>_offsets</c> table, which describe the start/end of hash buckets.
    /// </summary>
    /// <remarks>
    /// <c>_buckets[i]</c> gives the (inclusive) start index within <c>_offsets</c> of the
    /// offsets for bucket <c>i</c> and <c>_buckets[i + 1]</c> givest the (exclusive) end index.
    /// </remarks>
    internal uint[] _buckets;

    /// <summary>
    /// The modulus to apply to hash table lookups. This is always equal to <c>_buckets.Length - 1</c>, since
    /// <c>_buckets</c> adds an extra value at the end for the end-index of the last hash bucket.
    /// </summary>
    internal int _hashModulus;

    /// <summary>
    /// Reads and decodes a symbol name table from an MSF file. This is used for the GSI and PSI.
    /// </summary>
    /// <param name="stream"></param>
    /// <param name="isFastLink">
    /// The caller must indicate whether this <c>SymbolNameTable</c> was encoded for a "fastlink PDB"
    /// (also known as a mini PDB). This is indicated by the presence of the fastlink feature in the PDBI
    /// stream. This feature is obsolete, but we still need to support reading it.
    /// </param>
    internal SymbolNameTable(ref IMsfStreamReader stream, long streamOffset, int streamSize, bool isFastLink)
    {
        int numBuckets = isFastLink ? NUM_BUCKETS_MINI : NUM_BUCKETS_NORMAL;
        this._hashModulus = numBuckets;

        NameTableHeader header = default;
        Span<byte> headerBytes = MemoryMarshal.AsBytes(new Span<NameTableHeader>(ref header));
        stream.ReadAtExact(streamOffset, headerBytes);

        if (header.signature != 0xFFFFFFFFu)
        {
            throw new InvalidSymbolNameTableException("Symbol name table has incorrect signature");
        }

        if (header.version != 0xF12F091Au)
        {
            throw new InvalidSymbolNameTableException("Symbol name table has incorrect version");
        }

        if (header.hash_records_size % 8 != 0)
        {
            throw new InvalidSymbolNameTableException("Symbol name table has invalid value for hash_records_size (is not divisible by 8)");
        }

        int numHashRecords = (int)(header.hash_records_size / 8);

        NameTableHashRecord[] rawRecords = new NameTableHashRecord[numHashRecords];
        Span<byte> rawRecordsBytes = MemoryMarshal.AsBytes(new Span<NameTableHashRecord>(rawRecords));
        stream.ReadAtExact(streamOffset + headerBytes.Length, rawRecordsBytes);

        uint[] offsets = new uint[rawRecords.Length];
        for (int i = 0; i < offsets.Length; ++i)
        {
            offsets[i] = (uint)rawRecords[i].offset;
        }

        // Next, read the hash_buckets data.
        byte[] compressedBuckets = new byte[(int)header.hash_buckets_size];
        stream.ReadAtExact(streamOffset + headerBytes.Length + rawRecordsBytes.Length, new Span<byte>(compressedBuckets));

        uint[] buckets = ExpandBuckets(compressedBuckets, numBuckets, numHashRecords);

        this._offsets = offsets;
        this._buckets = buckets;
    }

    private SymbolNameTable(uint[] offsets, uint[] buckets, int hashModulus)
    {
        this._offsets = offsets;
        this._buckets = buckets;
        this._hashModulus = hashModulus;
    }

    public static SymbolNameTable MakeEmpty()
    {
        return new SymbolNameTable(Array.Empty<uint>(), new uint[] { 0 }, 1);
    }

    /// <summary>
    /// The hash modulus to use when computing hashes for lookups.
    /// </summary>
    public int HashModulus => this._hashModulus;

    /// This is the size used for calculating hash indices. It was the size of the in-memory form
    /// of the hash records, on 32-bit machines. It is not the same as the length of the hash records
    /// that are stored on disk (which is 8).
    const int HASH_RECORD_CALC_LEN = 12;

    const int NUM_BUCKETS_NORMAL = 0x1000;
    const int NUM_BUCKETS_MINI = 0x3ffff;

    /// Compute the size in bytes of the bitmap of non-empty buckets.
    static int nonempty_bitmap_size_bytes(int num_buckets)
    {
        int compressed_bitvec_size_u32s = ((num_buckets + 1) + 31) >> 5;
        return compressed_bitvec_size_u32s * 4;
    }

    /*
    /// Compute the size in bytes of the name hash table. This includes the header.
    static int compute_hash_table_size_bytes(
        int num_hash_records,
        int num_buckets,
        int num_nonempty_buckets,
    ) {
        size_of::<NameTableHeader>()
            + num_hash_records * size_of::<HashRecord>()
            + nonempty_bitmap_size_bytes(num_buckets)
            + num_nonempty_buckets * size_of::<i32>()
    }
    */

    /// Expands a compressed bucket. Returns a vector of offsets.
    ///
    /// The input contains a bitmap, followed by an array of offsets. The bitmap determines how many
    /// items there are in the array of offsets. The length of the bitmap is specified by num_buckets.
    ///
    /// This function returns a vector that contains hash indices. The hash records for a given hash
    /// bucket can be found as:
    ///
    /// <code>
    /// let buckets = expand_buckets(...)?;
    /// let bucket_index = 10;
    /// let hash_records: Span&lt;HashRecord&gt; = ref hash_records[buckets[bucket_index]..buckets[bucket_index + 1]];
    /// </code>
    static uint[] ExpandBuckets(
        ReadOnlySpan<byte> buckets_bytes,
        int num_buckets,
        int num_hash_records)
    {
        Bytes p = new Bytes(buckets_bytes);

        int bitmap_len_in_bytes = nonempty_bitmap_size_bytes(num_buckets);

        ReadOnlySpan<byte> bitmap_bytes = p.ReadN(bitmap_len_in_bytes);

        // Count the number of 1 bits set in the non-empty bucket bitmap.
        int num_nonempty_buckets = 0;
        for (int i = 0; i < bitmap_bytes.Length; ++i)
        {
            num_nonempty_buckets += BitOperations.PopCount(bitmap_bytes[i]);
        }
        // Use min(num_buckets) so that we ignore any extra bits in the bitmap.
        num_nonempty_buckets = Math.Min(num_nonempty_buckets, num_buckets);

        // After the bitmap, we find the array of non-empty pointers. Each non-empty
        // bucket begins with a non-empty pointer.
        ReadOnlySpan<int> nonempty_pointers = MemoryMarshal.Cast<byte, int>(p.ReadN(num_nonempty_buckets * 4));

        int nonempty_pointers_iter = 0; // index into nonempty_pointers

        uint[] hash_buckets = new uint[num_buckets + 1];
        int hash_buckets_len = 0;

        int next_bucket_index = 0; // next bit position we're going to check (within bitmap_bytes)

        // Iterate the bits that are set in the bitmap.
        while (next_bucket_index < num_buckets)
        {
            int bucket_index = next_bucket_index;
            if (((bitmap_bytes[next_bucket_index >> 3] >> (next_bucket_index & 7)) & 1) == 0)
            {
                next_bucket_index++;
                continue;
            }
            next_bucket_index++;

            // The nonempty_pointers[nonempty_pointers_iter] index should never overflow, because we
            // computed the length of the nonempty_pointers span from the number of bits that are set
            // in the bitmap, and we use the same information to control the iterations of this loop.
            // However, just in case we got the logic wrong, double-check it and throw a descriptive
            // exception.
            if (nonempty_pointers_iter >= nonempty_pointers.Length)
            {
                throw new InvalidSymbolNameTableException("ran out of non-empty pointers during table code");
            }
            int offset_x12 = nonempty_pointers[nonempty_pointers_iter++];

            // This next part looks super weird.  We require that the offset is a multiple of 12
            // because in the original implementation, the C code was working with a structure
            // that contained memory pointers. These structures did not have the same size on
            // 32-bit architectures and 64-bit architectures, so of course the solution was to
            // standardize on the existing size, which was based on the 32-bit size.
            //
            // So now we divide by 12 because we want the array index, not byte index, that points
            // into the hash records (hash offsets) table.
            if (offset_x12 < 0)
            {
                throw new InvalidSymbolNameTableException("found a negative offset in hash buckets");
            }
            if (offset_x12 % HASH_RECORD_CALC_LEN != 0)
            {
                throw new InvalidSymbolNameTableException($"hash record offset {offset_x12} is not a multiple of 12 (as required)");
            }
            int offset = (offset_x12 / HASH_RECORD_CALC_LEN);

            // It would be strange for offset to be equal to num_hash_records because that would
            // imply an empty "non-empty" hash bucket.
            if (offset >= num_hash_records)
            {
                throw new InvalidSymbolNameTableException($"hash record offset {offset_x12} is beyond range of hash records table");
            }

            // Record offsets must be non-decreasing. They should actually be strictly increasing,
            // but we tolerate repeated values.
            if (hash_buckets_len > 0)
            {
                uint prev_offset = hash_buckets[hash_buckets_len - 1];
                if (offset < prev_offset)
                {
                    throw new InvalidSymbolNameTableException($"hash record offset {offset} is less than previous offset {prev_offset}");
                }
            }
            else if (offset != 0)
            {
                throw new InvalidSymbolNameTableException($"First hash record offset should be zero, but instead it is: 0x{offset:x}");
            }

            // Add offsets for any previous buckets, which were all empty.
            while (hash_buckets_len < bucket_index)
            {
                hash_buckets[hash_buckets_len++] = (uint)offset;
            }

            hash_buckets[hash_buckets_len++] = (uint)offset;
            Debug.Assert(hash_buckets_len <= num_buckets);
        }

        // Fill in the offsets for the remaining empty buckets (if any), and push an extra offset for
        // the end of the hash records array.
        Debug.Assert(hash_buckets_len <= num_buckets);
        while (hash_buckets_len <= num_buckets)
        {
            hash_buckets[hash_buckets_len++] = (uint)num_hash_records;
        }

        if (nonempty_pointers_iter < nonempty_pointers.Length)
        {
            Debug.WriteLine("SymbolNameTable: warning: did not use all of the non-empty hash pointers during decompression");
        }

        return hash_buckets;
    }

    /// <summary>
    /// Given a name, hashes the name and returns the set of offsets that the caller should check.
    /// The offsets point into the Global Symbol Stream.
    /// </summary>
    public ReadOnlySpan<uint> GetOffsetsForName(ReadOnlySpan<char> name)
    {
        int hash = (int)CVHash.hash_mod_u32_ascii(name, (uint)this._hashModulus);
        int start = (int)this._buckets[hash];
        int end = (int)this._buckets[hash + 1];
        return new ReadOnlySpan<uint>(this._offsets, start, end - start);
    }
}

/// <summary>
/// The header of the Name Symbol stream.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
struct NameTableHeader
{
    /// <summary>
    /// Always 0xFFFF_FFFF. This value indicates that the "small" representation is being used.
    /// </summary>
    public uint signature;

    /// <summary>
    /// Always 0xF12F_091A.  This value is 0xEFFE_0000 + 19990810.
    /// This suggests that this version was created on August 10, 1999.
    /// </summary>
    public uint version;

    /// <summary>
    /// The size in bytes of hash_records. Since each hash record has a fixed
    /// size of 8 bytes, this determines the number of hash records.
    /// </summary>
    public uint hash_records_size;

    /// <summary>
    /// The size in bytes of the hash_buckets region.
    /// </summary>
    public uint hash_buckets_size;

    // Contains one record for each symbol in the Name Table.
    // HashRecord hash_records[header.hash_records_size / 8];

    // uint8_t hash_buckets[header.hash_buckets_size];
}

/// <summary>
/// Describes the on-disk layout of hash records.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NameTableHashRecord
{
    /// <summary>
    /// The byte offset of the corresponding symbol in the Global Symbol Stream, plus 1.
    /// If this record is "empty", then <c>offset</c> is equal to zero.
    /// </summary>
    internal int offset;

    /// <summary>
    /// The number of references to this value, in memory.
    /// </summary>
    /// <remarks>
    /// When stored on disk, this field is meaningless. Decoders should ignore this field. Encoders should set this field to 1.
    /// </remarks>
    internal int crefs;
}

public sealed class InvalidSymbolNameTableException : Exception
{
    public InvalidSymbolNameTableException(string message) : base(message) { }
}
