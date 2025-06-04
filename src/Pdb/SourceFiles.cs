using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Pdb;

public sealed class SourceFiles
{
    /// <summary>
    /// The location within _fileNameOffsets where the files for a given module start. Each value
    /// is an index into _fileNameOffsets.
    /// </summary>
    readonly ushort[] _moduleFileStarts;
    /// <summary>
    /// The number of entries within _fileNameOffsets for a given module.
    /// </summary>
    readonly ushort[] _moduleFileCounts;

    /// <summary>
    /// Contains indices that point into _fileNameData.
    /// </summary>
    readonly uint[] _fileNameOffsets;

    /// <summary>
    /// Contains the NUL-terminated strings for the source files.
    /// </summary>
    readonly byte[] _fileNameData;

    /// <summary>
    /// Contains strings generated for values in _fileNameOffsets
    /// </summary>
    Dictionary<int, string>? _fileNameStrings;

    internal static SourceFiles Read(MsfReader msf, uint streamOffset, int length)
    {
        byte[] sourcesBytes = new byte[length];

        var sr = msf.GetStreamReader(PdbDefs.DbiStream);
        sr.ReadAtExact(streamOffset, sourcesBytes);

        return new SourceFiles(sourcesBytes);
    }

    /// <summary>
    /// Parse `sourcesBytes`
    /// </summary>
    SourceFiles(ReadOnlySpan<byte> sourcesBytes) {
        Bytes b = new Bytes(sourcesBytes);

        int numModules = b.ReadUInt16();

        // The next field is obsolete. It was originally the number of source files.
        _ = b.ReadUInt16();

        // TODO: bounds checks
        _moduleFileStarts = MemoryMarshal.Cast<byte, ushort>(b.ReadN(numModules * 2)).ToArray();
        _moduleFileCounts = MemoryMarshal.Cast<byte, ushort>(b.ReadN(numModules * 2)).ToArray();

        Debug.Assert(_moduleFileStarts.Length == numModules);
        Debug.Assert(_moduleFileCounts.Length == numModules);

        int numFileNameOffsets = 0;
        foreach (ushort count in _moduleFileCounts) {
            numFileNameOffsets += (int)count;
        }

        // TODO: bounds check
        _fileNameOffsets = MemoryMarshal.Cast<byte, uint>(b.ReadN(numFileNameOffsets * 4)).ToArray();

        // The rest contains file name data.
        _fileNameData = b._data.ToArray();
    }

    public int NumModules { get { return _moduleFileStarts.Length; } }

    /// <summary>
    /// Gets the name offsets for the source files for a given module.
    /// </summary>
    public ReadOnlySpan<uint> GetNameOffsetsForModule(int module) {
        int start = _moduleFileStarts[module];
        int count = _moduleFileCounts[module];
        return new ReadOnlySpan<uint>(_fileNameOffsets, start, count);
    }

    /// <summary>
    /// Gets the name of a source file, given its name offset. The name is returned as a slice over bytes, encoded in UTF-8.
    /// </summary>
    public ReadOnlySpan<byte> GetFileNameUtf8(uint name) {
        // TODO: bounds check
        var nameBytes = new ReadOnlySpan<byte>(_fileNameData).Slice((int)name);

        for (int i = 0; i < nameBytes.Length; ++i) {
            if (nameBytes[i] == 0) {
                return nameBytes.Slice(0, i);
            }
        }

        // Uh oh!  Name is not NUL-terminated.
        // TODO: Throw exception?
        return ReadOnlySpan<byte>.Empty;
    }

    /// <summary>
    /// Gets the name of a source file, given its name offset.
    /// </summary>
    /// <remarks>
    /// This allocates String objects on-demand, so it may be moderately expensive.
    /// However, it does cache them.
    /// </remarks>
    public string GetFileNameString(uint name) {
        if (_fileNameStrings != null) {
            if (_fileNameStrings.TryGetValue((int)name, out var existing)) {
                return existing;
            }
        } else {
            _fileNameStrings = new();
        }

        var nameBytes = GetFileNameUtf8(name);
        string s = System.Text.Encoding.UTF8.GetString(nameBytes);
        _fileNameStrings.Add((int)name, s);
        return s;
    }
}
