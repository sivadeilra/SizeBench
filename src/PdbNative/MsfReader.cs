using System;
using System.Collections.Generic;
using System.IO;

namespace PdbNative;

/// <summary>
/// Can read Multi-Stream File (MSF) files, which are the container format for PDBs.
/// </summary>
/// <remarks>
/// This class is thread-safe. All data that it stores is thread-safe and the only functions that this class provides
/// are those that read thread-safe data and issue reads to the lower storage layer.
/// 
/// BIG READ WARNING: Actually, the underlying FileStream object is NOT thread-safe because we have to call Seek() on it.
/// So until that is fixed, this class can't be used in multi-threaded environments.
/// </remarks>
public sealed class MsfReader : IDisposable
{
    bool m_disposed;

    readonly Stream m_stream;
    readonly int m_pageSizeShift;

    readonly uint m_numStreams;

    /// <summary>
    /// Size of each stream, in bytes.
    /// </summary>
    readonly uint[] m_streamSizes;

    // m_streamBases points into m_streamPages
    // m_streamBases.Length = m_numStreams + 1
    readonly uint[] m_streamBases;

    /// <summary>
    /// Contains page numbers for all streams
    /// </summary>
    readonly uint[] m_streamPages;

    private MsfReader(Stream stream, int pageSizeShift, uint numStreams, uint[] streamSizes, uint[] streamBases, uint[] streamPages)
    {
        this.m_stream = stream;
        this.m_pageSizeShift = pageSizeShift;
        this.m_numStreams = numStreams;
        this.m_streamSizes = streamSizes;
        this.m_streamBases = streamBases;
        this.m_streamPages = streamPages;
        this.m_disposed = false;
    }

    public static MsfReader Open(Stream stream)
    {
        var fileHeaderBytes = new byte[MsfDefs.MsfFileHeaderSize];
        stream.Seek(0, SeekOrigin.Begin);
        stream.ReadExactly(fileHeaderBytes, 0, MsfDefs.MsfFileHeaderSize);

        var actualMagic = new Span<byte>(fileHeaderBytes, 0, 32);
        if (!actualMagic.SequenceEqual(new Span<byte>(MsfDefs.FileHeaderMagic)))
        {
            throw new MsfException("File does not have a PDB/MSF signature");
        }

        uint pageSize = BitConverter.ToUInt32(fileHeaderBytes, 32);
        uint activeFpm = BitConverter.ToUInt32(fileHeaderBytes, 36);
        uint numPages = BitConverter.ToUInt32(fileHeaderBytes, 40);
        uint streamDirSizeBytes = BitConverter.ToUInt32(fileHeaderBytes, 44);

        if (!MsfDefs.IsAllowedPageSize(pageSize))
        {
            throw new MsfException("File uses a page size that is not allowed");
        }
        int pageSizeShift = (int)Math.Log2(pageSize);

        if (activeFpm != 1 && activeFpm != 2)
        {
            throw new MsfException("File header has an invalid value for the 'active FPM' field.");
        }

        // L1: 
        // L2: PageMap
        // L3: StreamDirectory

        // streamDirSizeBytes tells us the size, in bytes, of the Stream Directory.
        // Determine the size of the Stream Directory in pages, taking care to round up.
        uint streamDirSizePages = (streamDirSizeBytes + pageSize - 1) >> pageSizeShift; // size of L3
        uint pageMapSizePages = (streamDirSizePages * 4 + pageSize - 1) >> pageSizeShift;

        // Reads the root pointers (L1)
        var pageMapPointersBytes = new byte[pageMapSizePages * 4]; // really should be uint[]
        stream.Seek(MsfDefs.BigStreamPagesMapOffset, SeekOrigin.Begin);
        stream.ReadExactly(pageMapPointersBytes);

        // Read the Page Map (L2)
        var pageMapBytes = new byte[pageMapSizePages * pageSize]; // really should be uint[]
        for (int i = 0; i < pageMapSizePages; ++i)
        {
            uint pageMapPagePointer = BitConverter.ToUInt32(pageMapPointersBytes, i * 4);
            long pageMapPageOffset = ((long)pageMapPagePointer) << pageSizeShift;
            stream.Seek(pageMapPageOffset, SeekOrigin.Begin);
            stream.ReadExactly(pageMapBytes, i * (int)pageSize, (int)pageSize);
        }

        // Read the Stream Directory (L3)
        var streamDirBytes = new byte[streamDirSizeBytes]; // really should be uint[]
        for (int i = 0; i < streamDirSizePages; ++i)
        {
            uint page = BitConverter.ToUInt32(pageMapBytes, i * 4);
            // The last page may be a partial page.
            int streamDirOffset = i * (int)pageSize;
            int readLen = Math.Min((int)streamDirSizeBytes - streamDirOffset, (int)pageSize);
            stream.Seek(((long)page) << pageSizeShift, SeekOrigin.Begin);
            stream.ReadExactly(streamDirBytes, streamDirOffset, readLen);
        }

        // The Stream Directory contains:
        //      uint32_t num_streams;
        //      uint32_t stream_sizes[num_streams];
        //      uint32_t stream_pages[num_pages_in_all_streams];  // num_pages_in_all_streams is defined below

        // We are going to build two arrays. One contains stream_pages[], with its contents moved down so that
        // the values begin at 0, not at the index found in the Stream Directory. The other is stream_bases[],
        // which is one value per stream and points into the stream_pages[] array. stream_bases[] also contains
        // one extra value, to make bounds math easy.

        uint numStreams = BitConverter.ToUInt32(streamDirBytes, 0);
        if (numStreams == 0)
        {
            throw new MsfException("Stream directory is invalid; it contains no streams at all.");
        }

        if (streamDirBytes.Length < (numStreams + 1) * 4)
        {
            throw new MsfException("Stream directory is invalid; too small to contain valid data.");
        }

        // Stream entry 0 is special; it is the "Old Stream Directory".
        uint[] streamSizes = new uint[numStreams]; // stream sizes, in bytes

        uint totalPagesInAllStreams = 0;
        for (uint i = 0; i < numStreams; ++i)
        {
            // 4+ to step over num_streams;
            // 4* to scale up to sizeof(uint)
            uint streamSize = BitConverter.ToUInt32(streamDirBytes, 4 + (int)i * 4);
            streamSizes[i] = streamSize;

            if (streamSize != MsfDefs.NIL_STREAM_SIZE)
            {
                totalPagesInAllStreams += MsfDefs.GetNumPages(streamSize, pageSize);
            }
        }

        if (streamDirBytes.Length < (numStreams + 1 + totalPagesInAllStreams) * 4)
        {
            throw new MsfException("Stream directory is invalid; too small to contain valid data.");
        }

        uint r = (numStreams + 1) * 4; // r is base for offset into streamDirBytes

        // Convert streamPages table to uint.
        uint[] streamPages = new uint[totalPagesInAllStreams];
        for (uint i = 0; i < totalPagesInAllStreams; ++i)
        {
            uint streamPage = BitConverter.ToUInt32(streamDirBytes, (int)(r + i * 4));
            streamPages[i] = streamPage;
        }

        uint[] streamBases = new uint[numStreams + 1];

        uint p = 0; // index into streamPages _and_ into streamSizes (with a bias of r)
        for (int i = 0; i < numStreams; ++i)
        {
            streamBases[i] = p;
            uint streamSize = streamSizes[i];
            uint numPagesInStream = MsfDefs.GetNumPagesInStream(streamSize, pageSize);
            p += numPagesInStream;
        }
        streamBases[numStreams] = p;

        // Stream 0 is special; it is the Old Stream Directory. We intentionally do not
        // allow reading from it, so we will set its size to zero.
        streamSizes[0] = 0;
        streamBases[0] = streamBases[1];

        return new MsfReader(stream, pageSizeShift, numStreams, streamSizes, streamBases, streamPages);
    }

    public int NumStreams => (int)this.m_numStreams;

    public uint StreamSize(int streamIndex)
    {
        uint streamSize = this.m_streamSizes[streamIndex];
        if (streamSize == MsfDefs.NIL_STREAM_SIZE)
        {
            return 0;
        }
        else
        {
            return streamSize;
        }
    }

    internal Stream GetUnderlyingStream()
    {
        return this.m_stream;
    }

    public void Dispose()
    {
        if (this.m_disposed)
        {
            return;
        }

        this.m_disposed = true;
        this.m_stream.Dispose();
    }

    public uint PageSize => 1u << this.m_pageSizeShift;
    public int PageSizeShift => this.m_pageSizeShift;

    public MsfStream OpenStream(int streamIndex)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(streamIndex, this.m_streamSizes.Length);
        var streamSize = StreamSize(streamIndex);

        var streamPages = new Memory<uint>(
            this.m_streamPages,
            (int)this.m_streamBases[streamIndex],
            (int)(this.m_streamBases[streamIndex + 1] - this.m_streamBases[streamIndex]));
        return new MsfStream(this, streamIndex, streamSize, streamPages);
    }
}
