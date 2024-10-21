using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PdbNative;

public sealed class MsfStream : System.IO.Stream
{
    readonly MsfReader m_msf;

    readonly int m_stream;

    /// <summary>
    /// If the stream is a nil stream, this value is 0.
    /// </summary>
    readonly uint m_streamSize;

    readonly Memory<uint> m_pages;

    long m_readPos;

    internal MsfStream(MsfReader msf, int stream, uint streamSize, Memory<uint> pages)
    {
        this.m_msf = msf;
        this.m_stream = stream;
        this.m_streamSize = streamSize;
        this.m_pages = pages;
        this.m_readPos = 0;
    }

    public override bool CanWrite => false;
    public override bool CanRead => true;
    public override bool CanSeek => true;

    public override long Position
    {
        get { return this.m_readPos; }
        set
        {
            this.m_readPos = value;
            this.m_readPos = Math.Max(this.m_readPos, 0);
            this.m_readPos = Math.Min(this.m_readPos, this.m_streamSize);
        }
    }

    public override void Flush()
    {
    }

    public override long Length
    {
        get { return m_streamSize; }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        switch (origin)
        {
            case SeekOrigin.Begin:
                this.m_readPos = offset;
                return this.m_readPos;

            case SeekOrigin.End:
                this.m_readPos = this.m_streamSize + offset;
                break;

            case SeekOrigin.Current:
                this.m_readPos = this.m_readPos + offset;
                break;
        }

        this.m_readPos = Math.Max(this.m_readPos, 0);
        this.m_readPos = Math.Min(this.m_readPos, this.m_streamSize);

        return this.m_readPos;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }



    public override int Read(byte[] buffer, int offset, int count)
    {
        Debug.Assert(this.m_readPos >= 0);

        long streamOffset = this.m_readPos;
        int bytesTransferred = this.ReadAt(streamOffset, buffer, offset, count);
        this.m_readPos += bytesTransferred;
        return bytesTransferred;
    }

    public int ReadAt(long streamOffset, byte[] buffer, int offset, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(streamOffset);

        var pageSizeShift = this.m_msf.PageSizeShift;
        var msfStream = this.m_msf.GetUnderlyingStream();
        var streamPages = this.m_pages.Span;

        var totalBytesTransferred = 0;

        // We attempt to coalesce reads into a smaller number of reads.
        long previousReadFileOffset = 0;
        int previousReadCount = 0;

        while (count > 0)
        {
            if (streamOffset >= this.m_streamSize)
            {
                break;
            }

            long bytesAvailable = this.m_streamSize - streamOffset;
            int transferLen = Math.Min(count, (int)bytesAvailable);

            int startingStreamPage = (int)(streamOffset >> pageSizeShift);
            long startingByteOffsetWithinPage = (streamOffset & ((1u << pageSizeShift) - 1));
            uint startingFilePage = streamPages[startingStreamPage];
            long startingFileOffset = (((long)startingFilePage) << pageSizeShift) + startingByteOffsetWithinPage;

            count -= transferLen;
            streamOffset += transferLen;
            totalBytesTransferred += transferLen;

            // Can this read be coalesced with the previous read, if any?
            if (previousReadCount > 0)
            {
                if (previousReadFileOffset + previousReadCount == startingFileOffset)
                {
                    // Yes, the previous read can be coalesced with this read.
                    previousReadCount += transferLen;
                    continue;
                }
                else
                {
                    // No, the previous read cannot be coalesced with this read.
                    // Perform the previous read and clear the coalescing request.
                    msfStream.Seek(previousReadFileOffset, SeekOrigin.Begin);
                    msfStream.ReadExactly(new Span<byte>(buffer, offset, previousReadCount));
                    offset += previousReadCount;
                    // previousReadCount = 0;
                    // previousReadFileOffset = 0;
                }
            }

            previousReadCount = transferLen;
            previousReadFileOffset = startingFileOffset;
        }

        // If there is an outstanding read, then do the read.
        if (previousReadCount != 0)
        {
            msfStream.Seek(previousReadFileOffset, SeekOrigin.Begin);
            msfStream.ReadExactly(new Span<byte>(buffer, offset, previousReadCount));
            // offset += previousReadCount; // Accurate, but not needed.
        }

        return totalBytesTransferred;
    }
}
