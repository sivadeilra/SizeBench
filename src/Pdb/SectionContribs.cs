
using System.Runtime.InteropServices;

namespace Pdb;

/// <summary>
/// Contains the Section Contributions table
/// </summary>
public class SectionContribs {
    public SectionContribEntry[] Contribs;

    const uint SECTION_CONTRIBUTIONS_SUBSTREAM_VER60 = 0xeffe0000 + 19970605;

    internal SectionContribs(byte[] contribsBytes) {
        this.Contribs = Array.Empty<SectionContribEntry>();

        Bytes b = new Bytes(contribsBytes);

        // The section begins with a uint32 that specifies the format of the data.
        if (b.Length < 4) {
            return;
        }

        uint sectionContribsVersion = b.ReadUInt32();
        if (sectionContribsVersion != SECTION_CONTRIBUTIONS_SUBSTREAM_VER60) {
            // This version is not supported.
            return;
        }

        ReadOnlySpan<RawSectionContribEntry> rawContribs = MemoryMarshal.Cast<byte, RawSectionContribEntry>(b._data);
        int numEntries = rawContribs.Length;

        SectionContribEntry[] contribs = new SectionContribEntry[numEntries];
        for (int i = 0; i < contribs.Length; ++i) {
            RawSectionContribEntry entry = rawContribs[i];
            contribs[i] = new SectionContribEntry(ref entry);
        }

        this.Contribs = contribs;
    }

    public (bool, SectionContribEntry) FindForRva(int offset) {
        SectionContribEntry[] contribs = this.Contribs;
        int lo = 0;
        int hi = contribs.Length;

        while (lo < hi) {
            int mid = lo + (hi - lo) / 2;
            ref var c = ref contribs[mid];

            if (offset < c.offset) {
                // The one we're searching for is below the candidate and cannot overlap it.
                hi = mid;
                continue;
            }

            int offsetWithinC = offset - c.offset;
            if (offsetWithinC >= c.size) {
                // The one we're searching for does not overlap the candidate and is higher than it.
                lo = mid + 1;
                continue;
            }

            // Found it
            return (true, c);
        }

        return (false, default(SectionContribEntry));
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct RawSectionContribEntry2 {
    public const int SizeOf = 32;

    public RawSectionContribEntry Base;
    public uint coff_section;

#if todo
    public RawSectionContribEntry2(ref Bytes bytes) {
        this.Base = new SectionContribEntry(ref bytes);
        this.coff_section = bytes.ReadUInt32();
    }
#endif
}

/// <summary>
/// The on-disk layout of the entries in the Section Contributions Table
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct RawSectionContribEntry
{
    public const int SizeOf = 28;

    /// The section index
    public ushort section;
    // Alignment padding
    // public ushort padding1;
    public int offset;
    public int size;
    public uint characteristics;

    /// The zero-based module index of the module containing this section contribution.
    public ushort module_index;

    // Alignment padding
    // public ushort padding2;
    public uint data_crc;
    public uint reloc_crc;
}

/// <summary>
/// The in-memory layout of the entries of the Section Contributions Table.
/// </summary>
/// <remarks>
/// We use a different layout for the in-memory form and the on-disk form because the on-disk
/// form wastes memory due to padding. Also, we simply do not use the `data_crc` and `reloc_crc`
/// fields, so we don't read them.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct SectionContribEntry {
    /// The zero-based module index of the module containing this section contribution.
    public ushort module_index;

    /// The section index
    public ushort section;

    public int offset;
    public int size;
    public uint characteristics;

    public SectionContribEntry(ref RawSectionContribEntry raw)
    {
        this.module_index = raw.module_index;
        this.section = raw.section;
        this.offset = raw.offset;
        this.size = raw.size;
        this.characteristics = raw.characteristics;
    }

    public CodeView.OffsetSegment OffsetSegment
    {
        get { return new CodeView.OffsetSegment((uint)this.offset, this.section); }
    }
}

