using System.Runtime.InteropServices;

namespace Pdb.CodeView;

using TypeIndex = uint;

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 6)]
public struct OffsetSegment
{
    public const int SizeOf = 6;

    [FieldOffset(0)]
    public uint offset;

    [FieldOffset(4)]
    public ushort segment;
}

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 7)]
public struct SymLabelHeader
{
    [FieldOffset(0)]
    public OffsetSegment offsetSegment;

    [FieldOffset(6)]
    public byte flags;
}

/// <summary>
/// Fixed-size header for `S_GPROC32` and `S_LPROC32`.
/// </summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 35)]
public struct SymProcHeader
{
    [FieldOffset(0)]
    public uint p_parent;
    [FieldOffset(4)]
    public uint p_end;
    [FieldOffset(8)]
    public uint p_next;
    [FieldOffset(12)]
    public uint proc_len;

    /// The offset in bytes from the start of the procedure to the point where the stack frame has
    /// been set up. Parameter and frame variables can be viewed at this point.
    [FieldOffset(16)]
    public uint debug_start;

    /// The offset in bytes from the start of the procedure to the point where the procedure is
    /// ready to return and has calculated its return value, if any. Frame and register variables
    /// can still be viewed.
    [FieldOffset(20)]
    public uint debug_end;

    /// This field is either a `TypeIndex` that points into the TPI or is an `ItemId` that
    /// points into the IPI.
    ///
    /// This field is a `TypeIndex` for the following symbols: `S_GPROC32`, `S_LPROC32`,
    /// `S_LPROC32EX`, `S_LPROC32_DPC`, `S_GPROC32EX`.
    ///
    /// This field is a `ItemId` for `S_LPROC32_ID`, `S_GPROC32_ID`, `S_LPROC32_DPC_ID`,
    /// `S_GPROC32EX_ID`, `S_LPROC32EX_ID`.
    [FieldOffset(24)]
    public TypeIndex proc_type;

    [FieldOffset(28)]
    public OffsetSegment offset_segment;

    [FieldOffset(34)]
    public byte flags;
}

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 18)]
public struct SymBlockHeader {
    [FieldOffset(0)]
    public uint p_parent;

    [FieldOffset(4)]
    public uint p_end;

    /// Length in bytes of the scope of this block within the executable code stream.
    [FieldOffset(8)]
    public uint length;

    [FieldOffset(12)]
    public OffsetSegment offset_segment;
}
