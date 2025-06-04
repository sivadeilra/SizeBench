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

        uint obsoleteModuleIndex = b.ReadUInt32();
        SectionContribEntry firstContrib = new SectionContribEntry(ref b);

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
    }
}
