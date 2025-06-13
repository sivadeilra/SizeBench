using Microsoft.VisualStudio.TestPlatform;
using Pdb;
using Pdb.CodeView;

namespace PdbTest;

[TestClass]
public sealed class Test1
{
    const string TestPdbPath = "c:\\pdb\\pdbtool.pdb";

    [TestMethod]
    public void DumpNumStreams()
    {
        using var f = File.OpenRead(TestPdbPath);
        using var pdb = PdbReader.Open(f);
        Console.WriteLine($"Number of streams: {pdb.Msf.NumStreams}");
    }

    [TestMethod]
    public void DumpModules()
    {
        using var f = File.OpenRead(TestPdbPath);
        using var pdb = PdbReader.Open(f);
        Console.WriteLine($"Number of streams: {pdb.Msf.NumStreams}");

        var modules = pdb.GetModules();

        int moduleIndex = 0;
        foreach (var module in modules)
        {
            Console.WriteLine($"  Module #{moduleIndex}: {module.ModuleName}");
            Console.WriteLine($"      ObjectFile: {module.ObjectFile}");
            ++moduleIndex;
        }
    }

    [TestMethod]
    public void DumpModuleSymbols()
    {
        using var f = File.OpenRead(TestPdbPath);
        using var pdb = PdbReader.Open(f);

        // Read symbols for some module
        byte[] moduleSymbols = pdb.GetModuleSymbols(1);
        var iter = SymIter.ForModuleSymbols(moduleSymbols);
        while (iter.Next(out var kind, out var recordData))
        {
            Console.WriteLine($"symbol: {kind} len {recordData.Length}");
        }
    }

    [TestMethod]
    public void DumpGlobalSymbols()
    {
        using var f = File.OpenRead(TestPdbPath);
        using var pdb = PdbReader.Open(f);

        byte[] globalSymbols = pdb.GetGlobalSymbols();
        var iter = SymIter.ForGlobalSymbols(globalSymbols);
        while (iter.Next(out var kind, out var recordData))
        {
            Console.WriteLine($"symbol: {kind} len {recordData.Length}");
        }
    }

    [TestMethod]
    public void DumpSectionContribs()
    {
        using var pdb = PdbReader.Open(TestPdbPath);

        var contribs = pdb.GetSectionContribs();

        // Are the section contributions correctly ordered by [section:offset]?
        bool misordered = false;
        int totalGapSize = 0;
        for (int i = 1; i < contribs.Contribs.Length; ++i)
        {
            ref var prev = ref contribs.Contribs[i - 1];
            ref var next = ref contribs.Contribs[i];
            int prevEnd = prev.offset + prev.size;
            if (prevEnd > next.offset)
            {
                misordered = true;
                break;
            }
            int gapSize = next.offset - prevEnd;
            totalGapSize += gapSize;
        }

        if (misordered)
        {
            Console.WriteLine("Section contribs are MISORDERED");
        }
        else
        {
            Console.WriteLine("Section contribs are properly ordered");
            Console.WriteLine($"Total gap bytes: {totalGapSize}");
        }

        int count = Math.Min(16, contribs.Contribs.Length);
        for (int i = 0; i < count; ++i)
        {
            ref var contrib = ref contribs.Contribs[i];
            Console.WriteLine($"  {i} : {contrib.section:x04}:{contrib.offset:x08} mod {contrib.module_index}");
        }
    }

    [TestMethod]
    public void DumpSectionMap()
    {
        using var pdb = PdbReader.Open(TestPdbPath);
        var sectionMap = pdb.GetSectionMap();

        foreach (var entry in sectionMap.Entries)
        {
            Console.WriteLine($"  offset: 0x{entry.offset:x}");
        }
    }

    [TestMethod]
    public void DumpSources()
    {
        using var pdb = PdbReader.Open(TestPdbPath);
        var modules = pdb.GetModules();
        var sources = pdb.GetSourceFiles();

        for (int module = 0; module < sources.NumModules; ++module)
        {
            var moduleName = modules[module].ObjectFile;
            Console.WriteLine($"Module: {moduleName}");

            var moduleSources = sources.GetNameOffsetsForModule(module);
            foreach (var nameOffset in moduleSources)
            {
                var fileName = sources.GetFileNameString(nameOffset);
                Console.WriteLine($"    {fileName}");
            }

            Console.WriteLine();
        }
    }

    [TestMethod]
    public void DumpOptionalDebugStreams()
    {
        using var pdb = PdbReader.Open(TestPdbPath);

        ushort[] streams = pdb.GetOptionalDebugStreams();

        for (int i = 0; i < streams.Length; ++i)
        {
            ushort stream = streams[i];
            if (stream != MsfDefs.NilStreamIndex16)
            {
                OptionalDebugStream streamId = (OptionalDebugStream)i;
                Console.WriteLine($"Stream: {stream} : {streamId}");
            }
        }

        int sectionHeadersStream = pdb.GetOptionalDebugStream(OptionalDebugStream.section_header_data);
        Console.WriteLine($"Section headers stream: {sectionHeadersStream}");
    }

    [TestMethod]
    public void DumpSections()
    {
        using var pdb = PdbReader.Open(TestPdbPath);

        var sections = pdb.GetSections();

        foreach (var section in sections)
        {
            Console.WriteLine($"section: va {section.VirtualAddress:x08} + {section.VirtualSize:x08} : {section.Name}");
        }

    }

    [TestMethod]
    public void DumpTypes()
    {
        using var pdb = PdbReader.Open(TestPdbPath);

        var types = pdb.GetTypes();

        TypesIter iter = types.Iter();

        int ii = 0;
        while (iter.Next(out var kind, out var recordData))
        {
            Console.WriteLine($"# {ii:4} : [{(ushort)kind:x04}] : {kind}");
            ++ii;
            if (ii == 20)
            {
                break;
            }
        }
    }

    [TestMethod]
    public void LoadGlobalSymbolsNameTable()
    {
        using var pdb = PdbReader.Open(TestPdbPath);
        var names = pdb.GetGlobalSymbolIndex();
    }

    [TestMethod]
    public void FindGlobalSymbolRef()
    {
        using var pdb = PdbReader.Open(TestPdbPath);

        string[] names = {
            "tracing_subscriber::registry::sharded::impl$2::record_follows_from",
            "ZSTD_compressBlock_doubleFast_noDict_7",
            "main",
            "memset",
            "_imp_CloseHandle",
            "pdbtool::main",
            // we do not expect to find these:
            "Hello!",
        };

        foreach (string name in names)
        {
            Console.WriteLine($"symbol: {name}");
            if (pdb.FindGlobalSymbolRefByName(name, out var kind, out var module, out var symbolOffset))
            {
                Console.WriteLine($"    found: module: {module}, kind: {kind}, symbolOffset: {symbolOffset}");
            }
            else
            {
                Console.WriteLine("    not found");
            }
        }
    }

    [TestMethod]
    public void DumpNames()
    {
        using var pdb = PdbReader.Open(TestPdbPath);

        var names = pdb.GetNames();


        uint[] nameIndexes = { 0, 10, 20, 30, 40 };

        foreach (uint nameIndex in nameIndexes)
        {
            var name = names.GetStringUtf8Bytes((NameIndex)nameIndex);
            string nameString = name.ToString();
            Console.WriteLine($"0x{(uint)nameIndex:x08} - {nameString}");
        }
    }

    [TestMethod]
    public void GetRvaForSymbol()
    {
        string[] names = {
            "pdbtool::main"
        };

        using var pdb = PdbReader.Open(TestPdbPath);

        foreach (string name in names) {
            if (!pdb.FindGlobalSymbolOffsetSegmentName(name, out var offsetSegment))
            {
                Console.WriteLine($"symbol not found: {name}");
                continue;
            }

            uint rva = pdb.TranslateOffsetSegmentToRva(offsetSegment);

            Console.WriteLine($"[{offsetSegment.segment:x04}:{offsetSegment.offset:x08}] - {name}");
        }
    }
}
