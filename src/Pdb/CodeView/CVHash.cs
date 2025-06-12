using System.Diagnostics;

namespace Pdb.CodeView;

public static class CVHash
{
    /// Computes a 32-bit hash. This produces the same results as the hash function used in the
    /// MSVC PDB reader library.
    ///
    /// This is a port of the `LHashPbCb` function.
    ///
    /// # WARNING! WARNING! WARNING!
    ///
    /// This is a **VERY POOR HASH FUNCTION** and it should not be used for *ANY* new code. This
    /// function should only be used for compatibility with PDB data structures.
    ///
    /// # References
    ///
    /// * [`misc.h](https://github.com/microsoft/microsoft-pdb/blob/805655a28bd8198004be2ac27e6e0290121a5e89/PDB/include/misc.h#L15)
    public static uint hash_mod_u32(ReadOnlySpan<byte> pb, uint m)
    {
        return hash_u32(pb) % m;
    }

    /// Computes a 16-bit hash
    ///
    /// This is a port of the `HashPbCb` function.
    public static ushort hash_mod_u16(ReadOnlySpan<byte> pb, uint m)
    {
        return (ushort)hash_mod_u32(pb, m);
    }

    /// Computes a 32-bit hash, but does not compute a remainder (modulus).
    public static uint hash_u32(ReadOnlySpan<byte> pb)
    {
        uint h = 0;

        var b = new Bytes(pb);

        while (b.Length >= 4)
        {
            h ^= (uint)b.ReadUInt32();
        }

        // The tail is handled differently.
        if (b.Length >= 2)
        {
            h ^= (uint)b.ReadUInt16();
        }

        Debug.Assert(b.Length == 0 || b.Length == 1);

        if (b.Length > 0)
        {
            h ^= (uint)b._data[0];
        }

        h |= 0x20202020;
        h ^= h >> 11;
        return h ^ (h >> 16);
    }

    /// Computes a 32-bit hash. This produces the same results as the hash function used in the
    /// MSVC PDB reader library.
    ///
    /// This is a port of the `LHashPbCb` function.
    ///
    /// # WARNING! WARNING! WARNING!
    ///
    /// This is a **VERY POOR HASH FUNCTION** and it should not be used for *ANY* new code. This
    /// function should only be used for compatibility with PDB data structures.
    ///
    /// # References
    ///
    /// * [`misc.h](https://github.com/microsoft/microsoft-pdb/blob/805655a28bd8198004be2ac27e6e0290121a5e89/PDB/include/misc.h#L15)
    public static uint hash_mod_u32_ascii(ReadOnlySpan<char> pb, uint m)
    {
        return hash_u32_ascii(pb) % m;
    }

    /// Computes a 16-bit hash
    ///
    /// This is a port of the `HashPbCb` function.
    public static ushort hash_mod_u16_ascii(ReadOnlySpan<char> pb, uint m)
    {
        return (ushort)hash_mod_u32_ascii(pb, m);
    }

    /// <summary>
    /// This does the equivalent of hash_u32 for an ASCII string.
    /// 
    /// This will NOT produce the expected hash code if pb contains any non-ASCII characters.
    /// </summary>
    public static uint hash_u32_ascii(ReadOnlySpan<char> pb)
    {
        uint h = 0;

        while (pb.Length >= 4)
        {
            uint c0 = (uint)pb[0];
            uint c1 = (uint)pb[1];
            uint c2 = (uint)pb[2];
            uint c3 = (uint)pb[3];
            uint u = c0 | (c1 << 8) | (c2 << 16) | (c3 << 24);
            h ^= u;
            pb = pb.Slice(4);
        }

        // The tail is handled differently.
        if (pb.Length >= 2)
        {
            uint c0 = (uint)pb[0];
            uint c1 = (uint)pb[1];
            uint u = c0 | (c1 << 8);
            h ^= u;
            pb = pb.Slice(2);
        }

        Debug.Assert(pb.Length == 0 || pb.Length == 1);

        if (pb.Length > 0)
        {
            h ^= (uint)pb[0];
        }

        h |= 0x20202020;
        h ^= h >> 11;
        return h ^ (h >> 16);
    }
}
