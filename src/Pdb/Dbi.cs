
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Pdb;

// This structure would be bit-blittable, if we had that capability.
public struct DbiStreamHeader {
    public uint version;
    public uint age;
    public ushort GlobalSymbolIndexStream;
    public ushort BuildNumber;
    public ushort PublicSymbolIndexStream;
    public ushort PdbDllVersion;
    public ushort GlobalSymbolStream;
    public ushort PdbDllRbld;

    // Substreams
    public int mod_info_size;
    public int section_contribution_size;
    public int section_map_size;
    public int source_info_size;
    public int type_server_map_size;
    /// This field is _not_ a substream size. Not sure what it is.
    public uint mfc_type_server_index;
    public int optional_dbg_header_size;
    public int edit_and_continue_size;

    public ushort flags;
    public ushort machine;
    public uint padding;

    public const int DbiStreamHeaderSize = 64;


    public DbiStreamHeader(byte[] headerBuf) {
        var br = new Bytes(headerBuf);

        uint signature = br.ReadUInt32();
        uint version = br.ReadUInt32();
        uint age = br.ReadUInt32();

        this.GlobalSymbolIndexStream = br.ReadUInt16();
        this.BuildNumber = br.ReadUInt16();
        this.PublicSymbolIndexStream = br.ReadUInt16();
        this.PdbDllVersion = br.ReadUInt16();
        this.GlobalSymbolStream = br.ReadUInt16();
        this.PdbDllRbld = br.ReadUInt16();

        // Substreams
        this.mod_info_size = br.ReadInt32();
        this.section_contribution_size = br.ReadInt32();
        this.section_map_size = br.ReadInt32();
        this.source_info_size = br.ReadInt32();
        this.type_server_map_size = br.ReadInt32();
        this.mfc_type_server_index = br.ReadUInt32();
        this.optional_dbg_header_size = br.ReadInt32();
        this.edit_and_continue_size = br.ReadInt32();

        this.flags = br.ReadUInt16();
        this.machine = br.ReadUInt16();
        this.padding = br.ReadUInt32();
    }
}

public sealed class DbiStreamInfo {
    public DbiStreamHeader Header;
    public uint StreamSize;

    // on-demand
    ModuleInfo[]? _modules;

    public DbiStreamInfo(MsfReader msf) {
        var sr = msf.GetStreamReader(PdbDefs.DbiStream);

        byte[] headerBuf = new byte[DbiStreamHeader.DbiStreamHeaderSize];

        sr.ReadAt(0, headerBuf);
        DbiStreamHeader header = new DbiStreamHeader(headerBuf);


        this.Header = header;
        this.StreamSize = (uint)sr.StreamSize;
    }

    internal ModuleInfo[] GetModules(MsfReader msf) {
        if (_modules != null) {
            return _modules;
        }

        byte[] modulesBytes = new byte[this.Header.mod_info_size];

        var sr = msf.GetStreamReader(PdbDefs.DbiStream);

        int modulesStreamOffset = DbiStreamHeader.DbiStreamHeaderSize;
        sr.ReadAtExact(modulesStreamOffset, modulesBytes);

        // The Modules immediately follow the stream header.
        // Decode the module records.

        var modules = ParseModules(modulesBytes);
        _modules = modules;
        return modules;
    }

    static ModuleInfo[] ParseModules(byte[] modulesBytes) {
        List<ModuleInfo> modules = new();
        Bytes b = new Bytes(modulesBytes);
        while (!b.IsEmpty) {
            ModuleInfo module = new ModuleInfo(ref b);
            modules.Add(module);
        }
        return modules.ToArray();
    }
}
