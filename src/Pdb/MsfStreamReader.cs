using Pdb;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Pdb;

public class MsfStreamReader : IMsfStreamReader {
    readonly MsfReader _msf;
    readonly int _stream;
    readonly int _firstPagePointer;
    readonly int _numPages;

    /// <summary>
    /// Size in bytes of the stream.  If the stream is a nil stream, this returns 0.
    /// </summary>
    readonly uint _streamSize;

    public ulong StreamSize {
        get {
            return _streamSize;
        }
    }

    internal MsfStreamReader(MsfReader msf, int stream) {
        _msf = msf;
        _stream = stream;

        uint streamSize = _msf._streamSizes[this._stream];
        if (streamSize == MsfDefs.NilStreamSize) {
            streamSize = 0;
        }
        _streamSize = streamSize;

        _firstPagePointer = _msf._streamPageStarts[stream];
        _numPages = _msf._streamPageStarts[stream + 1] - _firstPagePointer;
    }

    public void ReadAtExact(long streamOffset, in Span<byte> buffer) {
        int originalLen = buffer.Length;
        int n = ReadAt(streamOffset, buffer);
        Debug.Assert(buffer.Length == originalLen);
        if (n != originalLen) {
            throw new IOException("Not enough bytes read");
        }
    }

    public byte[] ReadStreamToArray() {
        if (_streamSize > (uint)int.MaxValue) {
            throw new Exception("Stream is too large to read into a byte array");
        }

        int streamSize = (int)_streamSize;
        byte[] bytes = new byte[streamSize];
        ReadAtExact(0, bytes);
        return bytes;
    }

    public int ReadAt(long streamOffset, in Span<byte> buffer) {
        // "rest" = "the rest of the transfer that has not yet been done"
        Span<byte> rest = buffer;

        int totalBytesRead = 0;

        int pageSizeShift = _msf._pageSizeShift;

        if (streamOffset >= _streamSize) {
            return 0;
        }

        // The subtraction cannot overflow because we just tested it, above.
        // The truncation from int64 to uint32 cannot overflow because stream sizes are constrained by uint32.
        uint streamBytesAvailable = (uint)(_streamSize - streamOffset);

        if ((uint)rest.Length > streamBytesAvailable) {
            // The caller is requesting data that goes beyond the length of the stream.
            // We truncate the Span so that we can use the remaining size of the span
            // as our single controlling variable for the transfer loop, below.
            //
            // This truncation from int32 to uint32 cannot overflow because we know that streamBytesAvailable
            // is less than rest.Length, which is a non-negative int32 value.
            rest = rest.Slice(0, (int)streamBytesAvailable);
        }

        // This test subsumes several special cases, which is why we do it after computing
        // streamBytesAvailable, etc.
        if (rest.Length == 0) {
            return 0;
        }

        // At this point, we know that rest.Length and streamOffset lie within the defined
        // size of the stream. We use rest.Length as our loop control variable.
        //
        // Because we have adjusted rest.Length (if necessary) to the defined size of the
        // stream, there is no need to test its length again, in the loop below.

        Span<uint> streamPages = new Span<uint>(_msf._allStreamPages, _firstPagePointer, _numPages);
        var mapper = new StreamPageMapper(streamPages, pageSizeShift, _streamSize);

        // This cannot overflow because we already tested it against the stream size.
        uint currentStreamOffset = (uint)streamOffset;

        while (rest.Length != 0) {

            (long fileOffset, uint transferLen) = mapper.Map(currentStreamOffset, (uint)rest.Length);

            if (transferLen == 0) {
                // This should never happen.
                break;
            }

            IOUtils.SeekRead(_msf._file, fileOffset, rest);

            rest = rest.Slice((int)transferLen);
            totalBytesRead += (int)transferLen;
        }

        return totalBytesRead;
    }
}
