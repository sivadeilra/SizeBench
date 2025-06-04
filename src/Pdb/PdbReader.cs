using System;
using System.IO;

namespace Pdb;

public sealed class PdbReader : IDisposable {
    readonly MsfReader _reader;

    readonly PdbInfo _info;

    DbiStreamInfo? _dbiStreamInfo;

    public void Dispose() {
        _reader.Dispose();
    }

    public static PdbReader Open(string fileName) {
        FileStream f = File.OpenRead(fileName);
        return Open(f);
    }

    public static PdbReader Open(Stream stream) {
        var msf = MsfReader.Open(stream);

        // Read the PDB Information Stream (stream 1). This stream is small and nearly always used.

        var info = PdbInfoStream.ReadPdbInfo(msf);

        return new PdbReader(msf, info);
    }

    PdbReader(MsfReader msf, PdbInfo info) {
        this._reader = msf;
        this._info = info;
    }

    public MsfReader Msf {
        get { return this._reader; }
    }

    public Guid Guid {
        get { return _info.Guid; }
    }

    public uint Age {
        get { return _info.Age; }
    }

    public ushort MachineType
    {
        get
        {
            var dbi = this.GetDbiStreamInfo();
            return dbi.Header.machine;
        }
    }

    public int FindNamedStream(string name) {
        return _info.FindNamedStream(name);
    }

    public DbiStreamInfo GetDbiStreamInfo() {
        if (_dbiStreamInfo != null) {
            return _dbiStreamInfo;
        }

        DbiStreamInfo dbiStreamInfo = new DbiStreamInfo(_reader);
        _dbiStreamInfo = dbiStreamInfo;
        return dbiStreamInfo;
    }

    public ModuleInfo[] GetModules() {
        DbiStreamInfo dbi = GetDbiStreamInfo();
        return dbi.GetModules(_reader);
    }

    public byte[] ReadModuleSymbols(int moduleIndex) {
        ModuleInfo[] modules = GetModules();

        if (moduleIndex >= modules.Length) {
            throw new ArgumentOutOfRangeException("moduleIndex");
        }

        ModuleInfo moduleInfo = modules[moduleIndex];
        if (moduleInfo.Stream == MsfDefs.NilStreamIndex16 || moduleInfo.SymbolsByteSize == 0) {
            return Array.Empty<byte>();
        }

        byte[] symbolsBytes = new byte[moduleInfo.SymbolsByteSize];
        var sr = Msf.GetStreamReader(moduleInfo.Stream);
        sr.ReadAtExact(0, symbolsBytes);
        return symbolsBytes;
    }

    public byte[] ReadGlobalSymbols() {
        var dbi = GetDbiStreamInfo();
        if (dbi.Header.GlobalSymbolStream == MsfDefs.NilStreamIndex16) {
            return Array.Empty<byte>();
        }

        var sr = Msf.GetStreamReader(dbi.Header.GlobalSymbolStream);
        return sr.ReadStreamToArray();
    }

    SectionContribs? _sectionContribs;

    public SectionContribs GetSectionContribs() {
        if (_sectionContribs != null) {
            return _sectionContribs;
        }

        var dbi = GetDbiStreamInfo();

        byte[] sectionContribsBytes;

        if (dbi.Header.section_contribution_size == 0) {
            sectionContribsBytes = new byte[0];
        } else {
            sectionContribsBytes = new byte[dbi.Header.section_contribution_size];
            int sectionContribsOffset = DbiStreamHeader.DbiStreamHeaderSize + dbi.Header.mod_info_size;
            var sr = Msf.GetStreamReader(PdbDefs.DbiStream);
            sr.ReadAtExact(sectionContribsOffset, sectionContribsBytes);
        }

        var sectionContribs = new SectionContribs(sectionContribsBytes);

        _sectionContribs = sectionContribs;
        return sectionContribs;
    }
}


