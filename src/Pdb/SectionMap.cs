using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pdb;

public sealed class SectionMap
{
    public readonly SectionMapHeader Header;
    public readonly SectionMapEntry[] Entries;

    internal SectionMap(SectionMapHeader header, SectionMapEntry[] entries) {
        this.Header = header;
        this.Entries = entries;
    }

    internal static SectionMap Read(MsfReader msf, ref DbiStreamHeader dbiHeader) {
        int sectionMapSize = dbiHeader.section_map_size;

        if (sectionMapSize < Unsafe.SizeOf<SectionMapHeader>()) {
            return SectionMap.MakeEmpty();
        }

        // Read the Section Map.
        byte[] sectionMapBytes = new byte[sectionMapSize];
        var sr = msf.GetStreamReader(PdbDefs.DbiStream);
        uint sectionMapOffset = (uint)DbiStreamHeader.DbiStreamHeaderSize
            + (uint)dbiHeader.mod_info_size
            + (uint)dbiHeader.section_contribution_size;
        sr.ReadAtExact(sectionMapOffset, sectionMapBytes);

        Bytes b = new Bytes(sectionMapBytes);
        SectionMapHeader sectionMapHeader = b.ReadT<SectionMapHeader>();

        ReadOnlySpan<SectionMapEntry> entriesSpan = MemoryMarshal.Cast<byte, SectionMapEntry>(b._data);
        SectionMapEntry[] entries = entriesSpan.ToArray();
        return new SectionMap(sectionMapHeader, entries);
    }

    /// <summary>
    /// Make a bogus, empty SectionMap
    /// </summary>
    public static SectionMap MakeEmpty() {
        return new SectionMap(default, Array.Empty<SectionMapEntry>());
    }
}

[StructLayout(LayoutKind.Explicit, Size = 4)]
public struct SectionMapHeader {
    /// Total number of segment descriptors
    [FieldOffset(0)]
    public ushort num_segments;
    /// Number of logical segment descriptors
    [FieldOffset(1)]
    public ushort num_logical_segments;
}

[StructLayout(LayoutKind.Explicit, Size = 20)]
public struct SectionMapEntry {
    /// Descriptor flags bit field. See `SectionMapEntryFlags`.
    [FieldOffset(0)]
    public ushort flags;
    /// The logical overlay number
    [FieldOffset(2)]
    public ushort overlay;
    /// Group index into the descriptor array
    [FieldOffset(4)]
    public ushort group;
    /// Logical segment index, interpreted via flags
    [FieldOffset(6)]
    public ushort frame;
    /// Byte index of segment / group name in string table, or 0xFFFF.
    [FieldOffset(8)]
    public ushort section_name;
    /// Byte index of class in string table, or 0xFFFF.
    [FieldOffset(10)]
    public ushort class_name;
    /// Byte offset of the logical segment within physical segment.
    /// If group is set in flags, this is the offset of the group.
    [FieldOffset(12)]
    public uint offset;
    /// Byte count of the segment or group.
    [FieldOffset(16)]
    public uint section_length;
}
