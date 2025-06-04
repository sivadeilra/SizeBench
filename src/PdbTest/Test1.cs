using Microsoft.VisualStudio.TestPlatform;
using Pdb;
using Pdb.CodeView;

namespace PdbTest;

[TestClass]
public sealed class Test1 {
    const string TestPdbPath = "c:\\pdb\\pdbtool.pdb";

    [TestMethod]
    public void DumpNumStreams() {
        using var f = File.OpenRead(TestPdbPath);
        using var pdb = PdbReader.Open(f);
        Console.WriteLine($"Number of streams: {pdb.Msf.NumStreams}");
    }

    [TestMethod]
    public void DumpModules() {
        using var f = File.OpenRead(TestPdbPath);
        using var pdb = PdbReader.Open(f);
        Console.WriteLine($"Number of streams: {pdb.Msf.NumStreams}");

        var modules = pdb.GetModules();

        int moduleIndex = 0;
        foreach (var module in modules) {
            Console.WriteLine($"  Module #{moduleIndex}: {module.ModuleName}");
            Console.WriteLine($"      ObjectFile: {module.ObjectFile}");
            ++moduleIndex;
        }
    }

    [TestMethod]
    public void DumpModuleSymbols() {
        using var f = File.OpenRead(TestPdbPath);
        using var pdb = PdbReader.Open(f);

        // Read symbols for some module
        byte[] moduleSymbols = pdb.GetModuleSymbols(1);
        var iter = SymIter.ForModuleSymbols(moduleSymbols);
        while (iter.Next(out var kind, out var recordData)) {
            Console.WriteLine($"symbol: {kind} len {recordData.Length}");
        }
    }

    [TestMethod]
    public void DumpGlobalSymbols() {
        using var f = File.OpenRead(TestPdbPath);
        using var pdb = PdbReader.Open(f);

        byte[] globalSymbols = pdb.GetGlobalSymbols();
        var iter = SymIter.ForGlobalSymbols(globalSymbols);
        while (iter.Next(out var kind, out var recordData)) {
            Console.WriteLine($"symbol: {kind} len {recordData.Length}");
        }
    }

    [TestMethod]
    public void DumpSectionContribs() {
        using var pdb = PdbReader.Open(TestPdbPath);

        var contribs = pdb.GetSectionContribs();

        // Are the section contributions correctly ordered by [section:offset]?
        bool misordered = false;
        int totalGapSize = 0;
        for (int i = 1; i < contribs.Contribs.Length; ++i) {
            ref var prev = ref contribs.Contribs[i - 1];
            ref var next = ref contribs.Contribs[i];
            int prevEnd = prev.offset + prev.size;
            if (prevEnd > next.offset) {
                misordered = true;
                break;
            }
            int gapSize = next.offset - prevEnd;
            totalGapSize += gapSize;
        }

        if (misordered) {
            Console.WriteLine("Section contribs are MISORDERED");
        } else {
            Console.WriteLine("Section contribs are properly ordered");
            Console.WriteLine($"Total gap bytes: {totalGapSize}");
        }

        int count = Math.Min(16, contribs.Contribs.Length);
        for (int i = 0; i < count; ++i) {
            ref var contrib = ref contribs.Contribs[i];
            Console.WriteLine($"  {i} : {contrib.section:x04}:{contrib.offset:x08} mod {contrib.module_index}");
        }
    }

    [TestMethod]
    public void DumpSectionMap() {
        using var pdb = PdbReader.Open(TestPdbPath);
        var sectionMap = pdb.GetSectionMap();

        foreach (var entry in sectionMap.Entries) {
            Console.WriteLine($"  offset: 0x{entry.offset:x}");
        }
    }

    [TestMethod]
    public void DumpSources() {
        using var pdb = PdbReader.Open(TestPdbPath);
        var modules = pdb.GetModules();
        var sources = pdb.GetSourceFiles();

        for (int module = 0; module < sources.NumModules; ++module) {
            var moduleName = modules[module].ObjectFile;
            Console.WriteLine($"Module: {moduleName}");

            var moduleSources = sources.GetNameOffsetsForModule(module);
            foreach (var nameOffset in moduleSources) {
                var fileName = sources.GetFileNameString(nameOffset);
                Console.WriteLine($"    {fileName}");
            }

            Console.WriteLine();
        }
    }
    
}
