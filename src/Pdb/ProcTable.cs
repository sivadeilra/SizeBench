using Pdb.CodeView;

namespace Pdb;

public class ProcTable
{
    public ProcEntry[] Entries;

    public ProcTable(ProcEntry[] entries)
    {
        this.Entries = entries;
    }
}

public struct ProcEntry
{
    public ushort Module;
    public SymKind SymKind;
    public uint ProcSymbolOffset;
    public OffsetSegment OffsetSegment;
}
