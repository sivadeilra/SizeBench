namespace Pdb;

public class ModuleInfo {
    public string ModuleName;
    public string ObjectFile;

    public SectionContribEntry FirstContrib;

    public ushort Flags;
    public ushort Stream;
    public uint SymbolsByteSize;
    public uint C11ByteSize;
    public uint C13ByteSize;
    public ushort SourceFileCount;
    public uint SourceFileNameIndex;
    public uint PdbFilePathNameIndex;

    public ModuleInfo(ref Bytes b) {

        int lenBefore = b.Length;

        uint obsoleteModuleIndex = b.ReadUInt32();
        var rawFirstContrib = b.ReadT<RawSectionContribEntry>();
        SectionContribEntry firstContrib = new SectionContribEntry(ref rawFirstContrib);

        this.Flags = b.ReadUInt16();
        this.Stream = b.ReadUInt16();
        this.SymbolsByteSize = b.ReadUInt32();
        this.C11ByteSize = b.ReadUInt32();
        this.C13ByteSize = b.ReadUInt32();
        this.SourceFileCount = b.ReadUInt16();

        b.ReadUInt16(); // padding
        b.ReadUInt32(); // unused

        this.SourceFileNameIndex = b.ReadUInt32();
        this.PdbFilePathNameIndex = b.ReadUInt32();

        this.ModuleName = b.ReadUtf8String();
        this.ObjectFile = b.ReadUtf8String();

        // The start of each module record is required to be aligned to a 4-byte boundary.
        // Align the reader now, by skipping the needed number of padding bytes at the end.
        int lenAfter = b.Length;
        int recordLen = lenBefore - lenAfter;
        int padding = (4 - (recordLen % 4)) % 4;
        if (padding != 0 && b.HasN(padding)) {
            b.Skip(padding);
        }
    }
}
