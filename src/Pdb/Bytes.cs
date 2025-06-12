
using System.Runtime.InteropServices;

namespace Pdb;

public ref struct Bytes
{
    public Bytes(ReadOnlySpan<byte> data)
    {
        _data = data;
    }

    public ReadOnlySpan<byte> _data;

    public bool IsEmpty
    {
        get { return _data.IsEmpty; }
    }

    public int Length
    {
        get { return _data.Length; }
    }

    public bool HasN(int n)
    {
        return _data.Length >= n;
    }

    public ReadOnlySpan<byte> ReadN(int n)
    {
        if (_data.Length < n)
        {
            throw new Exception("Not enough data");
        }
        ReadOnlySpan<byte> s = _data.Slice(0, n);
        _data = _data.Slice(n);
        return s;
    }

    public byte ReadUInt8()
    {
        var data = ReadN(1);
        return data[0];
    }

    public sbyte ReadInt8()
    {
        var data = ReadN(1);
        return (sbyte)data[0];
    }

    public ushort ReadUInt16()
    {
        var data = ReadN(2);
        uint b0 = data[0];
        uint b1 = data[1];
        return (ushort)(b0 | (b1 << 8));
    }

    public short ReadInt16()
    {
        return (short)ReadUInt16();
    }

    public uint ReadUInt32()
    {
        var data = ReadN(4);
        uint b0 = data[0];
        uint b1 = data[1];
        uint b2 = data[2];
        uint b3 = data[3];
        return b0 | (b1 << 8) | (b2 << 16) | (b3 << 24);
    }

    public int ReadInt32()
    {
        return (int)ReadUInt32();
    }

    public ulong ReadUInt64()
    {
        var data = ReadN(8);
        ulong b0 = data[0];
        ulong b1 = data[1];
        ulong b2 = data[2];
        ulong b3 = data[3];
        ulong b4 = data[4];
        ulong b5 = data[5];
        ulong b6 = data[6];
        ulong b7 = data[7];
        return b0 | (b1 << 8) | (b2 << 16) | (b3 << 24)
            | (b4 << 32) | (b5 << 40) | (b6 << 48) | (b7 << 56);
    }

    public int ReadInt64()
    {
        return (int)ReadUInt64();
    }

    public Guid ReadGuid()
    {
        var b = ReadN(16);
        return new Guid(b);
    }

    public Utf8Span ReadUtf8Bytes()
    {
        // Find the NUL location.
        for (int i = 0; i < _data.Length; ++i)
        {
            if (_data[i] == 0)
            {
                ReadOnlySpan<byte> s = _data.Slice(0, i);
                _data = _data.Slice(i + 1);
                return new Utf8Span(s);
            }
        }

        throw new Exception("Did not find NUL terminator for string");
    }

    public string ReadUtf8String()
    {
        // Find the NUL location.
        for (int i = 0; i < _data.Length; ++i)
        {
            if (_data[i] == 0)
            {
                var s = System.Text.Encoding.UTF8.GetString(_data.Slice(0, i));
                _data = _data.Slice(i + 1);
                return s;
            }
        }

        throw new Exception("Did not find NUL terminator for string");
    }

    public T ReadT<T>()
        where T : struct
    {
        int n = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
        ReadOnlySpan<byte> tbytes = ReadN(n);
        return MemoryMarshal.AsRef<T>(tbytes);
    }

    public void Skip(int n)
    {
        if (_data.Length < n)
        {
            throw new Exception("Not enough data");
        }
        _data = _data.Slice(n);
    }

    // Get* methods read at an absolute index and do not move the read cursor

    void NeedsBytes(int n)
    {
        if (_data.Length < n)
        {
            throw new Exception("Not enough data");
        }
    }

    public ReadOnlySpan<byte> GetN(int offset, int n)
    {
        if (offset > _data.Length)
        {
            throw new Exception("Not enough data");
        }

        int avail = _data.Length - offset;
        if (avail < n)
        {
            throw new Exception("Not enough data");
        }

        return _data.Slice(offset, n);
    }

    public byte GetByte(int offset)
    {
        var data = GetN(offset, 1);
        return data[0];
    }


    public ushort GetUInt16(int start)
    {
        var data = GetN(start, 2);
        uint b0 = data[0];
        uint b1 = data[1];
        return (ushort)(b0 | (b1 << 8));
    }

    public short GetInt16(int start)
    {
        return (short)GetUInt16(start);
    }

    public uint GetUInt32(int start)
    {
        var data = GetN(start, 4);
        uint b0 = data[0];
        uint b1 = data[1];
        uint b2 = data[2];
        uint b3 = data[3];
        return b0 | (b1 << 8) | (b2 << 16) | (b3 << 24);
    }

    public int GetInt32(int start)
    {
        return (int)GetUInt32(start);
    }

    public uint GetUInt64(int start)
    {
        var data = GetN(start, 8);
        uint b0 = data[0];
        uint b1 = data[1];
        uint b2 = data[2];
        uint b3 = data[3];
        uint b4 = data[4];
        uint b5 = data[5];
        uint b6 = data[6];
        uint b7 = data[7];
        return b0 | (b1 << 8) | (b2 << 16) | (b3 << 24)
            | (b4 << 32) | (b5 << 40) | (b6 << 48) | (b7 << 56);
    }

    public int GetInt64(int start)
    {
        return (int)GetUInt64(start);
    }
}
