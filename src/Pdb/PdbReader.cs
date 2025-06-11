using Pdb.CodeView;
using System.Diagnostics;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pdb;

public sealed class PdbReader : IDisposable
{
    readonly MsfReader _msf;

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

    public void Dispose()
    {
        _msf.Dispose();
    }

    public static PdbReader Open(string fileName)
    {
        FileStream f = File.OpenRead(fileName);
        return Open(f);
    }

    public static PdbReader Open(Stream stream)
    {
        var msf = MsfReader.Open(stream);
        return new PdbReader(msf);
    }

    PdbReader(MsfReader msf)
    {
        // Read the PDB Information Stream (stream 1). This stream is small and nearly always used.

        var info = PdbInfoStream.Read(msf);

        DbiStreamInfo dbiStreamInfo = new DbiStreamInfo(msf);

        // The Modules table is important for many PDB queries and is reasonably small.
        // Read it now.

        this._modules = DbiStreamInfo.ReadModules(msf, ref dbiStreamInfo.Header);
        this._moduleSymbols = new ModuleSymbolsEntry[this._modules.Length];
        this._msf = msf;
        this._info = info;
        this._dbiStreamInfo = dbiStreamInfo;
    }

    public MsfReader Msf
    {
        get { return this._msf; }
    }

    public Guid Guid
    {
        get { return _info.Guid; }
    }

    public uint Age
    {
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
    public int FindNamedStream(string name)
    {
        return _info.FindNamedStream(name);
    }

    public bool HasFeature(uint value)
    {
        return _info.HasFeature(value);
    }

    /// Indicates that this PDB is a "mini PDB", produced by using the `/DEBUG:FASTLINK` parameter.
    ///
    /// See: <https://learn.microsoft.com/en-us/cpp/build/reference/debug-generate-debug-info?view=msvc-170>
    const uint FEATURE_MINI_PDB = 0x494E494Du;

    /// Indicates that this PDB is a "mini PDB", produced by using the `/DEBUG:FASTLINK` parameter.
    ///
    /// See: <https://learn.microsoft.com/en-us/cpp/build/reference/debug-generate-debug-info?view=msvc-170>
    public bool IsFastLink
    {
        get { return _info.HasFeature(FEATURE_MINI_PDB); }
    }

    public DbiStreamInfo GetDbiStreamInfo()
    {
        return _dbiStreamInfo;
    }

    public ModuleInfo[] GetModules()
    {
        return _modules;
    }

    public int NumModules
    {
        get { return _modules.Length; }
    }

    #region Global Symbols

    byte[]? _globalSymbols;

    /// <summary>
    /// Gets access to the Global Symbol Stream (GSS). The GSS is loaded on demand and cached.
    /// </summary>
    public byte[] GetGlobalSymbols()
    {
        if (_globalSymbols != null)
        {
            return _globalSymbols;
        }

        var globalSymbols = ReadGlobalSymbols();
        _globalSymbols = globalSymbols;
        return globalSymbols;
    }

    /// <summary>
    /// Reads the Global Symbol Stream (GSS) from disk. This is uncached.
    /// </summary>
    private byte[] ReadGlobalSymbols()
    {
        var dbi = GetDbiStreamInfo();
        if (dbi.Header.GlobalSymbolStream == MsfDefs.NilStreamIndex16)
        {
            return Array.Empty<byte>();
        }

        var sr = Msf.GetStreamReader(dbi.Header.GlobalSymbolStream);
        return sr.ReadStreamToArray();
    }

    #endregion

    #region Section Contributions

    SectionContribs? _sectionContribs;

    public SectionContribs GetSectionContribs()
    {
        if (_sectionContribs != null)
        {
            return _sectionContribs;
        }

        var dbi = GetDbiStreamInfo();

        byte[] sectionContribsBytes;

        if (dbi.Header.section_contribution_size == 0)
        {
            sectionContribsBytes = Array.Empty<byte>();
        }
        else
        {
            sectionContribsBytes = new byte[dbi.Header.section_contribution_size];
            int sectionContribsOffset = DbiStreamHeader.DbiStreamHeaderSize + dbi.Header.mod_info_size;
            var sr = Msf.GetStreamReader(PdbDefs.DbiStream);
            sr.ReadAtExact(sectionContribsOffset, sectionContribsBytes);
        }

        var sectionContribs = new SectionContribs(sectionContribsBytes);

        _sectionContribs = sectionContribs;
        return sectionContribs;
    }

    #endregion

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
    public SectionMap GetSectionMap()
    {
        if (_sectionMap != null)
        {
            return _sectionMap;
        }

        var sectionMap = SectionMap.Read(_msf, ref _dbiStreamInfo.Header);
        _sectionMap = sectionMap;
        return sectionMap;
    }

    #region Source Files

    SourceFiles? _sourceFiles;

    public SourceFiles GetSourceFiles()
    {
        if (_sourceFiles != null)
        {
            return _sourceFiles;
        }

        var sourceFiles = ReadSourceFiles();
        _sourceFiles = sourceFiles;
        return sourceFiles;
    }

    private SourceFiles ReadSourceFiles()
    {
        uint sourceFilesOffset = (uint)DbiStreamHeader.DbiStreamHeaderSize
            + (uint)_dbiStreamInfo.Header.mod_info_size
            + (uint)_dbiStreamInfo.Header.section_contribution_size
            + (uint)_dbiStreamInfo.Header.section_map_size;

        return SourceFiles.Read(_msf, sourceFilesOffset, _dbiStreamInfo.Header.source_info_size);
    }

    #endregion

    #region Optional Debug Headers

    ushort[]? _optionalDebugStreams;

    public ushort[] GetOptionalDebugStreams()
    {
        if (_optionalDebugStreams != null)
        {
            return _optionalDebugStreams;
        }

        ushort[] optionalDebugStreams = ReadOptionalDebugStreams();
        _optionalDebugStreams = optionalDebugStreams;
        return optionalDebugStreams;
    }

    private ushort[] ReadOptionalDebugStreams()
    {
        int substreamSize = _dbiStreamInfo.Header.optional_dbg_header_size;
        if (substreamSize == 0)
        {
            return Array.Empty<ushort>();
        }

        int numEntries = substreamSize / sizeof(ushort);
        ushort[] streams = new ushort[numEntries];
        Span<byte> streamsAsBytes = MemoryMarshal.Cast<ushort, byte>(new Span<ushort>(streams));

        var sr = _msf.GetStreamReader(PdbDefs.DbiStream);

        uint substreamOffset = (uint)DbiStreamHeader.DbiStreamHeaderSize
            + (uint)_dbiStreamInfo.Header.mod_info_size
            + (uint)_dbiStreamInfo.Header.section_contribution_size
            + (uint)_dbiStreamInfo.Header.section_map_size
            + (uint)_dbiStreamInfo.Header.source_info_size
            + (uint)_dbiStreamInfo.Header.type_server_map_size
            + (uint)_dbiStreamInfo.Header.edit_and_continue_size;

        sr.ReadAtExact(substreamOffset, streamsAsBytes);

        return streams;
    }

    /// <summary>
    /// Gets the stream index for an optional debug stream.
    /// Returns -1 if this optional debug stream is not present.
    /// </summary>
    public int GetOptionalDebugStream(OptionalDebugStream kind)
    {
        ushort[] streams = GetOptionalDebugStreams();

        int i = (int)kind;
        if (i < 0 || i >= streams.Length)
        {
            return -1;
        }

        ushort stream = streams[i];
        if (stream == MsfDefs.NilStreamIndex16)
        {
            return -1;
        }

        return stream;
    }

    #endregion

    #region Types Database

    Types? _types;

    public Types GetTypes()
    {
        if (_types != null)
        {
            return _types;
        }

        var types = ReadTypes();
        _types = types;
        return types;
    }

    private Types ReadTypes()
    {
        var types = new Types(_msf, PdbDefs.TpiStream);
        return types;
    }

    #endregion


    #region Procedure Table

    ProcTable? _procTable;

    public ProcTable GetProcTable()
    {
        if (_procTable != null)
        {
            return _procTable;
        }

        var procTable = BuildProcTable();
        _procTable = procTable;
        return procTable;
    }

    private ProcTable BuildProcTable()
    {
        List<ProcEntry> entries = new List<ProcEntry>();

        var modules = GetModules();

        for (int module = 0; module < modules.Length; ++module)
        {
            byte[] moduleSymbols = GetModuleSymbols(module);
            int originalLength = moduleSymbols.Length;

            SymIter iter = SymIter.ForModuleSymbols(moduleSymbols);

            while (true)
            {
                int recordOffset = originalLength - iter._data.Length;
                if (!iter.Next(out var symbolKind, out var recordData))
                {
                    break;
                }

                switch (symbolKind)
                {
                    case SymKind.S_GPROC32:
                    case SymKind.S_LPROC32:
                        {
                            ProcEntry proc;
                            proc.Module = (ushort)module;
                            proc.OffsetSegment = default; // TODO: get this
                            proc.ProcSymbolOffset = (uint)recordOffset;
                            proc.SymKind = symbolKind;
                            entries.Add(proc);
                        }
                        break;

                    default:
                        break;
                }
            }
        }

        return new ProcTable(entries.ToArray());
    }

    #endregion

    SymbolNameTable _globalSymbolIndex;

    public SymbolNameTable GetGlobalSymbolNameTable()
    {
        if (_globalSymbolIndex != null)
        {
            return _globalSymbolIndex;
        }

        SymbolNameTable symbolIndex = ReadGlobalSymbolNameTable();
        _globalSymbolIndex = symbolIndex;
        return symbolIndex;
    }

    SymbolNameTable ReadGlobalSymbolNameTable()
    {
        if (_dbiStreamInfo.Header.GlobalSymbolIndexStream == MsfDefs.NilStreamIndex16)
        {
            return SymbolNameTable.MakeEmpty();
        }

        bool isFastLink = IsFastLink;
        return new SymbolNameTable(_msf, _dbiStreamInfo.Header.GlobalSymbolIndexStream, isFastLink);
    }

    public bool FindGlobalSymbolRefByName(string name, out SymKind kind, out int moduleIndex, out int symbolOffset)
    {
        Debug.WriteLine($"FindGlobalSymbolRefByName: {name}");

        // We need the Global Symbol Stream and the Global Symbol Index.

        byte[] globalSymbolsBytes = GetGlobalSymbols();
        SymbolNameTable globalNameTable = GetGlobalSymbolNameTable();

        Debug.WriteLine($"buckets.length = {globalNameTable.buckets.Length:x08}");

        ReadOnlySpan<uint> offsets = globalNameTable.GetOffsetsForName(name);

        foreach (uint offsetPlusOne in offsets)
        {
            int offset = (int)offsetPlusOne - 1;
            // TODO: range check
            ReadOnlySpan<byte> symbolsAtOffset = new ReadOnlySpan<byte>(globalSymbolsBytes).Slice(offset);
            if (SymIter.NextOne(symbolsAtOffset, out var k, out var d))
            {
                switch (k)
                {
                    case SymKind.S_PROCREF:
                    case SymKind.S_LPROCREF:
                    case SymKind.S_DATAREF:
                        {
                            // RefSym2 header
                            Bytes b = new Bytes(d);
                            uint nameChecksum = b.ReadUInt32();
                            uint thisSymbolOffset = b.ReadUInt32();
                            ushort thisModuleIndex = b.ReadUInt16();

                            ReadOnlySpan<byte> symbolNameBytes = b.ReadUtf8Bytes();
                            string symbolNameString = System.Text.Encoding.UTF8.GetString(symbolNameBytes); // TODO: remove!

                            int thisHash = (int)CVHash.hash_mod_u32(symbolNameBytes, (uint)globalNameTable.hashModulus);

                            Debug.WriteLine($"checking symbol: {symbolNameString} - hash {(uint)thisHash:x08}");

                            if (StringIsEqual(name, symbolNameBytes))
                            {
                                kind = k;
                                moduleIndex = thisModuleIndex;
                                symbolOffset = (int)thisSymbolOffset;
                                return true;
                            }
                        }

                        break;

                    default:
                        break;
                }
            }
        }

        kind = default;
        moduleIndex = default;
        symbolOffset = default;
        return false;
    }

    /// <summary>
    /// Compare two strings for equality. One is stored in UTF-16, the other in UTF-8. This ONLY works for ASCII.
    /// </summary>
    // a is ascii, but stored in UTF-16
    // b is ascii, but stored in UTF-8
    static bool StringIsEqual(ReadOnlySpan<char> a, ReadOnlySpan<byte> b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; ++i)
        {
            if ((uint)a[i] != (uint)b[i])
            {
                return false;
            }
        }

        return true;
    }
}
