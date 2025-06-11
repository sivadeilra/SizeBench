
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
    // the main hash table
    // hashes are implied, not stored
    //
    // the index of each item is based on its hash code (modulo)
    // the value of each item is the byte offset within the symbol stream of the symbol record, plus 1.  the value 0 means "no value".
    internal uint[] offsets;

    internal uint[] buckets;

    internal int hashModulus;

    internal SymbolNameTable(MsfReader msf, int stream, bool isFastLink)
    {
        int numBuckets = isFastLink ? NUM_BUCKETS_MINI : NUM_BUCKETS_NORMAL;
        this.hashModulus = numBuckets;

        if (stream == -1)
        {
            this.offsets = new uint[] { 0 };
            this.buckets = new uint[] { 0 };
            this.hashModulus = 1;
            return;
        }

        var sr = msf.GetStreamReader(stream);
        NameTableHeader header = default;
        Span<byte> headerBytes = MemoryMarshal.AsBytes(new Span<NameTableHeader>(ref header));
        sr.ReadAtExact(0, headerBytes);

        if (header.signature != 0xFFFFFFFFu)
        {
            throw new Exception("Symbol name table has incorrect signature");
        }

        if (header.version != 0xF12F091Au)
        {
            throw new Exception("Symbol name table has incorrect version");
        }

        if (header.hash_records_size % 8 != 0)
        {
            throw new Exception("Symbol name table has invalid value for hash_records_size (is not divisible by 8)");
        }

        int numHashRecords = (int)(header.hash_records_size / 8);

        NameTableHashRecord[] rawRecords = new NameTableHashRecord[numHashRecords];
        Span<byte> rawRecordsBytes = MemoryMarshal.AsBytes(new Span<NameTableHashRecord>(rawRecords));
        sr.ReadAtExact(headerBytes.Length, rawRecordsBytes);

        uint[] offsets = new uint[rawRecords.Length];
        for (int i = 0; i < offsets.Length; ++i)
        {
            offsets[i] = (uint)rawRecords[i].offset;
        }

        // Next, read the hash_buckets data.
        byte[] compressedBuckets = new byte[(int)header.hash_buckets_size];
        sr.ReadAtExact(headerBytes.Length + rawRecordsBytes.Length, new Span<byte>(compressedBuckets));

        uint[] buckets = ExpandBuckets(compressedBuckets, numBuckets, numHashRecords);

        this.offsets = offsets;
        this.buckets = buckets;
    }

    private SymbolNameTable(uint[] offsets, uint[] buckets)
    {
        this.offsets = offsets;
        this.buckets = buckets;
    }

    public static SymbolNameTable MakeEmpty()
    {
        return new SymbolNameTable(new uint[] { 0 }, new uint[] { 0 });
    }

    public int HashModulus => this.hashModulus;

    /// This is the size used for calculating hash indices. It was the size of the in-memory form
    /// of the hash records, on 32-bit machines. It is not the same as the length of the hash records
    /// that are stored on disk (which is 8).
    const int HASH_RECORD_CALC_LEN = 12;


    const int NUM_BUCKETS_NORMAL = 0x1000;
    const int NUM_BUCKETS_MINI = 0x3ffff;

    /*
    /// Scan hash_records and figure out how many hash buckets are _not_ empty. Because hash_records
    /// is sorted by hash, we can do a single scan through it and find all of the "edges" (places where
    /// the `hash` value changes).
    static int count_nonempty_buckets(ReadOnlySpan<NameTableHashRecord> sorted_hash_records) {
        return iter_nonempty_buckets(sorted_hash_records).count()
    }
    */

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
        int  num_buckets,
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
    /// <!CDATA[
    /// let buckets = expand_buckets(...)?;
    /// let bucket_index = 10;
    /// let hash_records: &[HashRecord] = &hash_records[buckets[bucket_index]..buckets[bucket_index + 1]];
    /// ]]>
    static uint[] ExpandBuckets(
        ReadOnlySpan<byte> buckets_bytes,
        int num_buckets,
        int num_hash_records
    )
    {
        Bytes p = new Bytes(buckets_bytes);

        int output_len = num_buckets + 1;

        int bitmap_len_in_bytes = nonempty_bitmap_size_bytes(num_buckets);

        ReadOnlySpan<byte> bitmap_bytes = p.ReadN(bitmap_len_in_bytes);

        // bool[] bv /* : &BitSlice<u8, Lsb0> */ = BitSlice::from_slice(bitmap_bytes);

        // Count the number of 1 bits set in the non-empty bucket bitmap.
        int num_nonempty_buckets = 0;
        for (int i = 0; i < bitmap_bytes.Length; ++i)
        {
            byte b = bitmap_bytes[i];
            num_nonempty_buckets += BitOperations.PopCount(b);
        }
        // Use min(num_buckets) so that we ignore any extra bits in the bitmap.
        num_nonempty_buckets = Math.Min(num_nonempty_buckets, num_buckets);

        ReadOnlySpan<int> nonempty_pointers = MemoryMarshal.Cast<byte, int>(p.ReadN(num_nonempty_buckets * 4));

        // var nonempty_pointers_iter = nonempty_pointers.iter();
        int nonempty_pointers_iter = 0; // index into nonempty_pointers

        uint[] hash_buckets = new uint[output_len];
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

            // The unwrap() cannot fail because we computed the slice length (num_nonempty_buckets)
            // from the number of 1 bits in the non-empty mask (bv).
            if (nonempty_pointers_iter >= nonempty_pointers.Length)
            {
                throw new Exception("ran out of non-empty pointers during table code");
            }

            int offset_x12 = nonempty_pointers[nonempty_pointers_iter++];
            if (offset_x12 < 0)
            {
                throw new Exception("found a negative offset in hash buckets");
            }
            if (offset_x12 % HASH_RECORD_CALC_LEN != 0)
            {
                throw new Exception("hash record offset {offset_x12} is not a multiple of 12 (as required)");
            }
            int offset = (offset_x12 / HASH_RECORD_CALC_LEN);

            // It would be strange for offset to be equal to num_hash_records because that would
            // imply an empty "non-empty" hash bucket.
            if (offset >= num_hash_records)
            {
                throw new Exception("hash record offset {offset_x12} is beyond range of hash records table");
            }

            // Record offsets must be non-decreasing. They should actually be strictly increasing,
            // but we tolerate repeated values.
            if (hash_buckets_len > 0)
            {
                uint prev_offset = hash_buckets[hash_buckets_len - 1];
                if (offset < prev_offset)
                {
                    throw new Exception("hash record offset {offset} is less than previous offset {prev_offset}");
                }
            }
            else if (offset != 0)
            {
                throw new Exception("First hash record offset should be zero, but instead it is: 0x{offset:x}");
            }

            // Add offsets for any previous buckets, which were all empty.
            while (hash_buckets_len < bucket_index)
            {
                // trace!("    bucket: 0x{:08x} .. 0x{bucket_index:08x} : range is empty, pushing offset: 0x{offset:8x} {offset:10}", hash_buckets.len());
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

        /*
    if !nonempty_pointers_iter.as_slice().is_empty() {
        warn!(
            num_extra_bytes = p.len(),
            rest = p.peek_rest(),
            "Compressed hash buckets table contains extra byte(s)"
        );
    }
        */

        return hash_buckets;
    }

    /// <summary>
    /// Given a name, hashes the name and returns the set of offsets that the caller should check.
    /// The offsets point into the Global Symbol Stream.
    /// </summary>
    public ReadOnlySpan<uint> GetOffsetsForName(string name)
    {
        if (this.buckets.Length <= 1)
        {
            return new ReadOnlySpan<uint>();
        }

        int hash = (int)CVHash.hash_mod_u32_ascii(name, (uint)this.hashModulus);
        int start = (int)this.buckets[hash];
        int end = (int)this.buckets[hash + 1];

        Debug.WriteLine($"hash 0x{(uint)hash:x08} - {name}, start {start}, end {end}, len {end - start}");

        return new ReadOnlySpan<uint>(this.offsets, start, end - start);
    }
}

[StructLayout(LayoutKind.Sequential)]
struct NameTableHeader
{
    // Always 0xFFFF_FFFF. This value indicates that the "small" representation is being used.
    public uint signature;

    // Always 0xF12F_091A.  This value is 0xEFFE_0000 + 19990810.
    // This suggests that this version was created on August 10, 1999.
    public uint version;

    // The size in bytes of hash_records. Since each hash record has a fixed
    // size of 8 bytes, this determines the number of hash records.
    public uint hash_records_size;

    // The size in bytes of the hash_buckets region.
    public uint hash_buckets_size;

    // Contains one record for each symbol in the Name Table.
    // HashRecord hash_records[header.hash_records_size / 8];

    // uint8_t hash_buckets[header.hash_buckets_size];
}

[StructLayout(LayoutKind.Sequential)]
struct NameTableHashRecord
{
    public int offset;
    public int crefs;
}
