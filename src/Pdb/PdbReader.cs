using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pdb;

public sealed class PdbReader : IDisposable {
    readonly MsfReader _reader;

    readonly PdbInfo _info;

    readonly DbiStreamInfo _dbiStreamInfo;

    /// <summary>
    /// The set of modules loaded from the DBI Stream.
    /// Order is significant, since many data structures refer to modules by index.
    /// </summary>
    readonly ModuleInfo[] _modules;

    internal struct ModuleSymbolsEntry
    {
        // remember: module symbols start with a 4-byte prefix
        internal byte[]? symbols;
    }

    /// <summary>
    /// Indexed by module index.
    /// If this is null, then we have not even created the module symbols table.
    /// If this is null, then each entry is individually demand-loaded.
    /// </summary>
    readonly ModuleSymbolsEntry[] _moduleSymbols;

    public void Dispose() {
        _reader.Dispose();
    }

    public static PdbReader Open(string fileName) {
        FileStream f = File.OpenRead(fileName);
        return Open(f);
    }

    public static PdbReader Open(Stream stream) {
        var msf = MsfReader.Open(stream);
        return new PdbReader(msf);
    }

    PdbReader(MsfReader msf) {
        // Read the PDB Information Stream (stream 1). This stream is small and nearly always used.

        var info = PdbInfoStream.ReadPdbInfo(msf);
        DbiStreamInfo dbiStreamInfo = new DbiStreamInfo(msf);

        // The Modules table is important for many PDB queries and is reasonably small.
        // Read it now.

        this._modules = DbiStreamInfo.ReadModules(msf, ref dbiStreamInfo.Header);
        this._moduleSymbols = new ModuleSymbolsEntry[this._modules.Length];
        this._reader = msf;
        this._info = info;
        this._dbiStreamInfo = dbiStreamInfo;
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
            return _dbiStreamInfo.Header.machine;
        }
    }

    /// <summary>
    /// Finds a named stream with the given name and returns the stream index.
    /// If there is no stream with the given name, returns -1.
    /// </summary>
    public int FindNamedStream(string name) {
        return _info.FindNamedStream(name);
    }

    public DbiStreamInfo GetDbiStreamInfo() {
        return _dbiStreamInfo;
    }

    public ModuleInfo[] GetModules() {
        return _modules;
    }

    public int NumModules
    {
        get { return _modules.Length; }
    }

    byte[]? _globalSymbols;

    /// <summary>
    /// Gets access to the Global Symbol Stream (GSS). The GSS is loaded on demand and cached.
    /// </summary>
    public byte[] GetGlobalSymbols() {
        if (_globalSymbols != null) {
            return _globalSymbols;
        }

        var globalSymbols = ReadGlobalSymbols();
        _globalSymbols = globalSymbols;
        return globalSymbols;
    }

    /// <summary>
    /// Reads the Global Symbol Stream (GSS) from disk. This is uncached.
    /// </summary>
    private byte[] ReadGlobalSymbols() {
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
            sectionContribsBytes = Array.Empty<byte>();
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

    internal ModuleSymbolsEntry[] GetModuleSymbolsTable()
    {
        return _moduleSymbols;
    }

    /// <summary>
    /// Gets access to the symbols for a specific module. If the returned buffer is not empty, then it
    /// will start with a 4-byte prefix, which should be ignored. Symbols are loaded on-demand and cached.
    /// </summary>
    /// <remarks>
    /// This will load symbols from disk on-demand and will cache them.
    /// </remarks>
    public byte[] GetModuleSymbols(int module)
    {
        if (module < 0 || module >= _moduleSymbols.Length)
        {
            throw new ArgumentOutOfRangeException("module");
        }

        var symbols = _moduleSymbols[module].symbols;
        if (symbols != null)
        {
            return symbols;
        }

        // Need to load it.
        symbols = ReadModuleSymbols(module);
        _moduleSymbols[module].symbols = symbols;
        return symbols;
    }

    /// <summary>
    /// Reads the symbols for a specific module from the PDB. This is uncached.
    /// </summary>
    private byte[] ReadModuleSymbols(int moduleIndex)
    {
        ModuleInfo[] modules = GetModules();

        if (moduleIndex >= modules.Length)
        {
            throw new ArgumentOutOfRangeException("moduleIndex");
        }

        ModuleInfo moduleInfo = modules[moduleIndex];
        if (moduleInfo.Stream == MsfDefs.NilStreamIndex16 || moduleInfo.SymbolsByteSize == 0)
        {
            return Array.Empty<byte>();
        }

        byte[] symbolsBytes = new byte[moduleInfo.SymbolsByteSize];
        var sr = Msf.GetStreamReader(moduleInfo.Stream);
        sr.ReadAtExact(0, symbolsBytes);
        return symbolsBytes;
    }

    SectionMap? _sectionMap;

    /// <summary>
    /// Gets the SectionMap. This is parsed on-demand and cached.
    /// </summary>
    public SectionMap GetSectionMap() {
        if (_sectionMap != null) {
            return _sectionMap;
        }

        var sectionMap = SectionMap.Read(_reader, ref _dbiStreamInfo.Header);
        _sectionMap = sectionMap;
        return sectionMap;
    }

    SourceFiles? _sourceFiles;

    public SourceFiles GetSourceFiles() {
        if (_sourceFiles != null) {
            return _sourceFiles;
        }

        var sourceFiles = ReadSourceFiles();
        _sourceFiles = sourceFiles;
        return sourceFiles;
    }

    private SourceFiles ReadSourceFiles() {
        uint sourceFilesOffset = (uint)DbiStreamHeader.DbiStreamHeaderSize
            + (uint)_dbiStreamInfo.Header.mod_info_size
            + (uint)_dbiStreamInfo.Header.section_contribution_size
            + (uint)_dbiStreamInfo.Header.section_map_size;

        return SourceFiles.Read(_reader, sourceFilesOffset, _dbiStreamInfo.Header.source_info_size);
    }
}
