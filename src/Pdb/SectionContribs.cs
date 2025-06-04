
using System.Runtime.InteropServices;

namespace Pdb;

/// <summary>
/// Contains the Section Contributions table
/// </summary>
public class SectionContribs {
    public SectionContribEntry2[] Contribs;

    const uint SECTION_CONTRIBUTIONS_SUBSTREAM_VER60 = 0xeffe0000 + 19970605;

    internal SectionContribs(byte[] contribsBytes) {
        Bytes b = new Bytes(contribsBytes);

        // The section begins with a uint32 that specifies the format of the data.
        if (b.Length < 4) {
            this.Contribs = Array.Empty<SectionContribEntry2>();
            return;
        }

        uint sectionContribsVersion = b.ReadUInt32();
        if (sectionContribsVersion != SECTION_CONTRIBUTIONS_SUBSTREAM_VER60) {
            // This version is not supported.
            this.Contribs = Array.Empty<SectionContribEntry2>();
            return;
        }

        int numEntries = b.Length / SectionContribEntry.SizeOf;

        SectionContribEntry2[] contribs = new SectionContribEntry2[numEntries];
        for (int i = 0; i < contribs.Length; ++i) {
            var entry = new SectionContribEntry(ref b);
            contribs[i].Base = entry;
        }

        this.Contribs = contribs;
    }

    public (bool, SectionContribEntry2) FindForRva(int offset) {
        SectionContribEntry2[] contribs = this.Contribs;
        int lo = 0;
        int hi = contribs.Length;

        while (lo < hi) {
            int mid = lo + (hi - lo) / 2;
            ref var c = ref contribs[mid];

            if (offset < c.Base.offset) {
                // The one we're searching for is below the candidate and cannot overlap it.
                hi = mid;
                continue;
            }

            int offsetWithinC = offset - c.Base.offset;
            if (offsetWithinC >= c.Base.size) {
                // The one we're searching for does not overlap the candidate and is higher than it.
                lo = mid + 1;
                continue;
            }

            // Found it
            return (true, c);
        }

        return (false, default(SectionContribEntry2));
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct SectionContribEntry2 {
    public const int SizeOf = 32;

    public SectionContribEntry Base;
    public uint coff_section;

    public SectionContribEntry2(ref Bytes bytes) {
        this.Base = new SectionContribEntry(ref bytes);
        this.coff_section = bytes.ReadUInt32();
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct SectionContribEntry {
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

    public SectionContribEntry(ref Bytes bytes) {
        // Give the optimizer a chance to combine all the range checks below
        if (bytes.Length < SectionContribEntry.SizeOf) {
            throw new ArgumentException("input data is too short for SectionContribEntry");
        }

        this.section = bytes.ReadUInt16();
        // this.padding1 = bytes.ReadUInt16();
        bytes.ReadUInt16();
        
        this.offset = bytes.ReadInt32();
        this.size = bytes.ReadInt32();
        this.characteristics = bytes.ReadUInt32();
        this.module_index = bytes.ReadUInt16();

        // this.padding2 = bytes.ReadUInt16();
        bytes.ReadUInt16();

        this.data_crc = bytes.ReadUInt32();
        this.reloc_crc = bytes.ReadUInt32();
    }
}

