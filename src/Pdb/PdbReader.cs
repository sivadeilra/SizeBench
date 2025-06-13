using Pdb.CodeView;
using System.Diagnostics;
using System;
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
    const uint FEATURE_MINI_PDB = 0x494E494Du;

    /// Indicates that this PDB is a "mini PDB", produced by using the `/DEBUG:FASTLINK` parameter.
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

    #region Section Headers

    [StructLayout(LayoutKind.Sequential)]
    public struct SectionHeader
    {
        public byte[] NameUtf8;
        public string Name;
        public uint VirtualSize;
        public uint VirtualAddress;
        public uint SizeOfRawData;
        public uint PointerToRawData;
        public uint PointerToRelocations;
        public uint PointerToLineNumbers;
        public uint NumberOfRelocations;
        public uint Characteristics;
    }


    [StructLayout(LayoutKind.Sequential)]
    private struct MostlySectionHeader
    {
        // public byte[] NameUtf8;
        public uint VirtualSize;
        public uint VirtualAddress;
        public uint SizeOfRawData;
        public uint PointerToRawData;
        public uint PointerToRelocations;
        public uint PointerToLineNumbers;
        public uint NumberOfRelocations;
        public uint Characteristics;
    }

    private SectionHeader[]? _sections;

    public SectionHeader[] GetSections()
    {
        if (_sections != null)
        {
            return _sections;
        }

        var sections = ReadSections();
        _sections = sections;
        return sections;
    }

    private SectionHeader[] ReadSections()
    {
        int stream = GetOptionalDebugStream(OptionalDebugStream.section_header_data);
        if (stream == -1)
        {
            return Array.Empty<SectionHeader>();
        }

        const int SectionHeaderSize = 40;

        byte[] sectionsStreamBytes = _msf.GetStreamReader(stream).ReadStreamToArray();
        int numSections = sectionsStreamBytes.Length / SectionHeaderSize;

        SectionHeader[] sections = new SectionHeader[numSections];
        for (int i = 0; i < numSections; ++i)
        {
            ReadOnlySpan<byte> sectionSpan = new ReadOnlySpan<byte>(sectionsStreamBytes, i * SectionHeaderSize, SectionHeaderSize);
            Bytes b = new Bytes(sectionSpan);
            var nameBytes = b.ReadN(8);
            var mostly = b.ReadT<MostlySectionHeader>();

            int nameLen = nameBytes.Length;
            for (int j = 0; j < nameBytes.Length; ++j)
            {
                nameLen = j;
                break;
            }
            var nameArray = nameBytes.Slice(0, nameLen).ToArray();

            SectionHeader section;

            section.NameUtf8 = nameArray;
            section.Name = System.Text.Encoding.UTF8.GetString(nameArray);
            section.VirtualSize = mostly.VirtualSize;
            section.VirtualAddress = mostly.VirtualAddress;
            section.SizeOfRawData = mostly.SizeOfRawData;
            section.PointerToRawData = mostly.PointerToRawData;
            section.PointerToRelocations = mostly.PointerToRelocations;
            section.PointerToLineNumbers = mostly.PointerToLineNumbers;
            section.NumberOfRelocations = mostly.NumberOfRelocations;
            section.Characteristics = mostly.Characteristics;
            sections[i] = section;
        }

        return sections;
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

    SymbolNameTable? _globalSymbolNameTable;

    /// <summary>
    /// Gets the Global Symbol Name Table, which can be used for searching for global symbols by name.
    /// </summary>
    public SymbolNameTable GetGlobalSymbolIndex()
    {
        if (_globalSymbolNameTable != null)
        {
            return _globalSymbolNameTable;
        }

        SymbolNameTable symbolIndex = ReadGlobalSymbolIndex();
        _globalSymbolNameTable = symbolIndex;
        return symbolIndex;
    }

    private SymbolNameTable ReadGlobalSymbolIndex()
    {
        if (_dbiStreamInfo.Header.GlobalSymbolIndexStream == MsfDefs.NilStreamIndex16)
        {
            return SymbolNameTable.MakeEmpty();
        }

        var sr = _msf.GetStreamReader(_dbiStreamInfo.Header.GlobalSymbolIndexStream);
        bool isFastLink = IsFastLink;
        return new SymbolNameTable(ref sr, 0, (int)sr.StreamSize, isFastLink);
    }

    PublicSymbolIndex? _publicSymbols;

    public PublicSymbolIndex GetPublicSymbolIndex()
    {
        if (_publicSymbols != null)
        {
            return _publicSymbols;
        }

        var nameTable = ReadPublicSymbols();
        _publicSymbols = nameTable;
        return nameTable;
    }

    // The PSI is similar to the GSI, but has several major differences:
    //
    // * The PSI has a header structure (not present in the GSI)
    // * After the PSI stream header, it contains the same name table that the GSI uses
    // * After the name table, the PSI contains an address-to-symbol mapping table.
    private PublicSymbolIndex ReadPublicSymbols()
    {
        if (_dbiStreamInfo.Header.PublicSymbolIndexStream == MsfDefs.NilStreamIndex16)
        {
            return PublicSymbolIndex.MakeEmpty();
        }

        // TODO: Do we actually want IsFastLink when loading the PSI, or only for the GSI?
        bool isFastLink = IsFastLink;
        return new PublicSymbolIndex(_msf, _dbiStreamInfo.Header.PublicSymbolIndexStream, isFastLink);
    }

    /// <summary>
    /// Given a symbol name, this searches the Global Symbol Index (GSI) for a "symbol reference" record with that name.
    /// </summary>
    /// <remarks>
    /// If a matching symbol is found, this returns true and returns the module index and the
    /// byte offset within that module's symbol stream for the symbol. However, this function
    /// does not look up the symbol data within the given module's stream; the caller must do that.
    /// </remarks>
    /// <param name="name"></param>
    /// <param name="kind"></param>
    /// <param name="moduleIndex"></param>
    /// <param name="symbolOffset"></param>
    /// <returns>True if a matching symbol was found.</returns>
    public bool FindGlobalSymbolRefByName(ReadOnlySpan<char> name, out SymKind kind, out int moduleIndex, out int symbolOffset)
    {
        // We need the Global Symbol Stream and the Global Symbol Index.
        ReadOnlySpan<byte> globalSymbolsBytes = GetGlobalSymbols();
        SymbolNameTable globalSymbolIndex = GetGlobalSymbolIndex();

        foreach (uint offsetPlusOne in globalSymbolIndex.GetOffsetsForName(name))
        {
            // This should not happen, but guard against it.
            if (offsetPlusOne == 0)
            {
                continue;
            }

            int offset = (int)offsetPlusOne - 1;

            if (offset > globalSymbolsBytes.Length)
            {
                // The offset is invalid. Ignore it.
                continue;
            }

            ReadOnlySpan<byte> symbolsAtOffset = globalSymbolsBytes.Slice(offset);
            if (SymIter.NextOne(symbolsAtOffset, out var k, out var d))
            {
                switch (k)
                {
                    case SymKind.S_PROCREF:
                    case SymKind.S_LPROCREF:
                    case SymKind.S_DATAREF:
                        {
                            // All of these symbol records have the same structure. They use a REFSYM2 header,
                            // followed by NUL-terminated symbol name.
                            Bytes b = new Bytes(d);
                            uint nameChecksum = b.ReadUInt32();
                            uint thisSymbolOffset = b.ReadUInt32();
                            int thisModuleIndexPlusOne = (int)b.ReadUInt16();
                            var thisName = b.ReadUtf8Bytes();

                            if (name == thisName)
                            {
                                // The module index has a bias of 1.
                                if (thisModuleIndexPlusOne == 0)
                                {
                                    break;
                                }

                                kind = k;
                                moduleIndex = thisModuleIndexPlusOne - 1;
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

    // Check for public symbols (S_PUB32).
    public bool FindPublicSymbolByName(ReadOnlySpan<char> name, out OffsetSegment offsetSegment)
    {
        // We need the Global Symbol Stream and the Public Symbol Index.
        ReadOnlySpan<byte> globalSymbolsBytes = GetGlobalSymbols();
        var publicSymbolIndex = GetPublicSymbolIndex();
        var publicSymbolNames = publicSymbolIndex.Names;

        foreach (uint offsetPlusOne in publicSymbolNames.GetOffsetsForName(name))
        {
            // This should not happen, but guard against it.
            if (offsetPlusOne == 0)
            {
                continue;
            }

            int offset = (int)offsetPlusOne - 1;

            if (offset > globalSymbolsBytes.Length)
            {
                // The offset is invalid. Ignore it.
                continue;
            }

            ReadOnlySpan<byte> symbolsAtOffset = globalSymbolsBytes.Slice(offset);
            if (SymIter.NextOne(symbolsAtOffset, out var k, out var d))
            {
                switch (k)
                {
                    case SymKind.S_PUB32:
                        {
                            // S_PUB32 symbols do not point to a module. The symbol record directly contains
                            // the OffsetSegment.
                            Bytes b = new Bytes(d);
                            uint thisFlags = b.ReadUInt32();
                            uint thisOffset = b.ReadUInt32();
                            ushort thisSegment = b.ReadUInt16();
                            var thisName = b.ReadUtf8Bytes();

                            if (name == thisName)
                            {
                                offsetSegment = new OffsetSegment(thisOffset, thisSegment);
                                return true;
                            }
                        }
                        break;

                    default:
                        break;
                }
            }
        }

        offsetSegment = default;
        return false;
    }

    #region Names Stream

    Names? _names;

    public Names GetNames()
    {
        if (_names != null)
        {
            return _names;
        }

        var names = ReadNames();
        _names = names;
        return names;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Names ReadNames()
    {
        int stream = FindNamedStream("/names");
        if (stream == MsfDefs.NilStreamIndex16)
        {
            return Names.MakeEmpty();
        }

        return new Names(_msf, stream);
    }

    #endregion

    /// <summary>
    /// Finds a global symbol and returns a pointer to the points at that symbol, within a module stream.
    /// </summary>
    /// <remarks>
    /// This function may load the symbols for a particular module on-demand.
    /// </remarks>
    /// <param name="name">The name of the global symbol to find</param>
    /// <param name="symbolRecords">
    /// On return, contains a SymIter pointing to the symbol record.
    /// This SymIter also includes symbol records that follow the initial symbol record. These symbol
    /// records may or may not be related.
    /// </param>
    /// <returns><c>true</c> if the symbol was found.</returns>
    public bool FindGlobalSymbolRecordByName(ReadOnlySpan<char> name, out SymIter symbolRecords)
    {
        if (!FindGlobalSymbolRefByName(name, out var symRefKind, out var module, out var symbolOffset))
        {
            symbolRecords = default;
            return false;
        }

        byte[] moduleSymbols = GetModuleSymbols(module);

        // symbolRecord points to the absolute position within the module symbols stream.
        if (symbolOffset > moduleSymbols.Length)
        {
            throw new InvalidPdbDataException("Found global symbol, but its record offset extends beyond the range of the containing module's symbol stream.");
        }

        symbolRecords = SymIter.ForRaw(new ReadOnlySpan<byte>(moduleSymbols).Slice(symbolOffset));
        return true;
    }

    public bool FindGlobalSymbolOffsetSegmentName(ReadOnlySpan<char> name, out OffsetSegment offsetSegment)
    {
        offsetSegment = default;
        if (FindGlobalSymbolRecordByName(name, out var symbolRecords))
        {
            if (!symbolRecords.Next(out var kind, out var recordBytes))
            {
                // If this fails, then the PDB has inconsistent data. This would happen if
                // a S_PROCREF in the GSS pointed to a bogus location in a module stream.
                return false;
            }

            switch (kind)
            {
                case SymKind.S_LPROC32:
                case SymKind.S_GPROC32:
                    {
                        var b = new Bytes(recordBytes);
                        var proc = b.ReadT<SymProcHeader>();
                        offsetSegment = proc.offset_segment;
                        return true;
                    }

                case SymKind.S_LDATA32:
                case SymKind.S_GDATA32:
                    {
                        var b = new Bytes(recordBytes);
                        var data = b.ReadT<SymDataHeader>();
                        offsetSegment = data.OffsetSegment;
                        return true;
                    }

                default:
                    // Uh oh, we don't know what kind of symbol this is.
                    return false;
            }
        }

        // Look for S_PUB32 symbols.
        if (FindPublicSymbolByName(name, out var offSeg))
        {
            offsetSegment = offSeg;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Given an <c>OffsetSegment</c>, translates it to an RVA.
    /// </summary>
    /// <remarks>
    /// If the segment or offset or invalid, this will throw an exception.
    /// </remarks>
    /// <param name="offsetSegment"></param>
    /// <returns>The RVA that it was translated to</returns>
    /// <exception cref="InvalidPdbDataException"></exception>
    public uint TranslateOffsetSegmentToRva(OffsetSegment offsetSegment)
    {
#if nah
        var sections = GetSectionMap();
        if (offsetSegment.segment > sections.Entries.Length)
        {
            throw new InvalidPdbDataException("Segment value in segment:offset is invalid");
        }

        ref var section = ref sections.Entries[offsetSegment.segment];
        uint rva = section.offset + offsetSegment.offset;
        return rva;
#else
        var sections = GetSections();

        // Segment numbers start at 1, not zero.
        if (offsetSegment.segment == 0 || offsetSegment.segment > sections.Length)
        {
            throw new InvalidPdbDataException("Segment value in segment:offset is invalid");
        }
        ref var section = ref sections[offsetSegment.segment - 1];

        uint rva = section.VirtualAddress + offsetSegment.offset;
        return rva;
#endif

    }
}

public sealed class InvalidPdbDataException : Exception
{
    public InvalidPdbDataException(string message) : base(message)
    {
    }
}