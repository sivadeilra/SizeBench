namespace Pdb.CodeView;

/// <summary>
/// Represents an encoded CodeView "number".
/// </summary>
/// 
/// <remarks>
/// This type stores the number in its encoded form, rather than "parsing" it, because
/// there are a lot of different Number variants (integer, float, string, etc.). It is
/// easiest to provide a way to decode those on-demand and to provide conversion to
/// specific types (with ranges) than it is to parse everything up front.
/// </remarks>
public ref struct Number
{
    public ReadOnlySpan<byte> Data;

    public Leaf Kind
    {
        get
        {
            Bytes b = new Bytes(this.Data);
            return (Leaf)b.ReadUInt16();
        }
    }

    public Number(ReadOnlySpan<byte> data)
    {
        this.Data = data;
    }

    public static bool Skip(ref Bytes b)
    {
        return Number.Read(ref b, out _);
    }

    public static bool Read(ref Bytes b, out Number number)
    {
        if (b.Length < 2)
        {
            number = default;
            return false;
        }

        ReadOnlySpan<byte> originalBytes = b._data;
        Leaf kind = (Leaf)b.ReadUInt16();

        if (kind.IsImmediateNumeric())
        {
            // Nothing more to do; the first two bytes are the entire value.
        }
        else
        {
            int n = kind switch
            {
                Leaf.LF_CHAR => 1,
                Leaf.LF_SHORT => 2,
                Leaf.LF_USHORT => 2,
                Leaf.LF_LONG => 4,
                Leaf.LF_ULONG => 4,
                Leaf.LF_REAL32 => 4,
                Leaf.LF_REAL64 => 8,
                Leaf.LF_REAL80 => 10,
                Leaf.LF_REAL128 => 16,
                Leaf.LF_QUADWORD => 8,
                Leaf.LF_UQUADWORD => 8,
                Leaf.LF_REAL48 => 6,
                Leaf.LF_COMPLEX32 => 8,
                Leaf.LF_COMPLEX64 => 16,
                Leaf.LF_COMPLEX128 => 32,
                Leaf.LF_VARSTRING => b.ReadUInt16(),
                Leaf.LF_OCTWORD => 16,
                Leaf.LF_UOCTWORD => 16,
                Leaf.LF_DECIMAL => 16,
                Leaf.LF_DATE => 8,
                Leaf.LF_UTF8STRING => b.ReadUtf8Bytes().Length != 0 ? 0 : 0,
                Leaf.LF_REAL16 => 2,
                _ => throw new Exception("CodeView Number kind is not recognized"),
            };
            b.Skip(n);
        }

        int totalLen = originalBytes.Length - b._data.Length;
        number = new Number(originalBytes.Slice(0, totalLen));
        return true;
    }

    public bool AsInt32(out int value)
    {
        Bytes b = new Bytes(this.Data);

        Leaf kind = (Leaf)b.ReadUInt16();
        if (kind.IsImmediateNumeric())
        {
            value = (int)(ushort)kind;
            return true;
        }

        switch (kind)
        {
            case Leaf.LF_CHAR:
                value = b.ReadInt8();
                return true;

            case Leaf.LF_SHORT:
                value = b.ReadInt16();
                return true;

            case Leaf.LF_USHORT:
                value = b.ReadUInt16();
                return true;

            case Leaf.LF_LONG:
                value = b.ReadInt32();
                return true;

            case Leaf.LF_ULONG:
                {
                    uint uvalue = b.ReadUInt32();
                    if (uvalue > (uint)int.MaxValue)
                    {
                        value = 0;
                        return false;
                    }
                    value = (int)uvalue;
                    return true;
                }

            case Leaf.LF_QUADWORD:
                {
                    long value64 = b.ReadInt64();
                    if (value64 < int.MinValue || value64 > int.MaxValue)
                    {
                        value = 0;
                        return false;
                    }
                    value = (int)value64;
                    return true;
                }

            case Leaf.LF_UQUADWORD:
                {
                    ulong uvalue64 = b.ReadUInt64();
                    if (uvalue64 > (ulong)int.MaxValue)
                    {
                        value = 0;
                        return false;
                    }
                    value = (int)uvalue64;
                    return true;
                }

            // TODO: LF_OCTWORD, LF_UOCTWORD

            default:
                value = 0;
                return false;
        }
    }

    public int AsInt32()
    {
        if (AsInt32(out int value))
        {
            return value;
        }
        else
        {
            throw new Exception("Incorrect type");
        }
    }

    public bool AsUInt32(out uint value)
    {
        Bytes b = new Bytes(this.Data);

        Leaf kind = (Leaf)b.ReadUInt16();
        if (kind.IsImmediateNumeric())
        {
            value = (uint)(ushort)kind;
            return true;
        }

        switch (kind)
        {
            case Leaf.LF_CHAR:
                {
                    sbyte ivalue = b.ReadInt8();
                    if (ivalue < 0)
                    {
                        value = 0;
                        return false;
                    }
                    value = (uint)ivalue;
                    return true;
                }

            case Leaf.LF_SHORT:
                {
                    short ivalue = b.ReadInt16();
                    if (ivalue < 0)
                    {
                        value = 0;
                        return false;
                    }
                    value = (uint)ivalue;
                    return true;
                }

            case Leaf.LF_USHORT:
                value = b.ReadUInt16();
                return true;

            case Leaf.LF_LONG:
                {
                    int ivalue = b.ReadInt32();
                    if (ivalue < 0)
                    {
                        value = 0;
                        return false;
                    }
                    value = (uint)ivalue;
                    return true;
                }

            case Leaf.LF_ULONG:
                {
                    value = b.ReadUInt32();
                    return true;
                }

            case Leaf.LF_QUADWORD:
                {
                    long value64 = b.ReadInt64();
                    if (value64 < 0 || value64 > (long)uint.MaxValue)
                    {
                        value = 0;
                        return false;
                    }
                    value = (uint)value64;
                    return true;
                }

            case Leaf.LF_UQUADWORD:
                {
                    ulong uvalue64 = b.ReadUInt64();
                    if (uvalue64 > (ulong)uint.MaxValue)
                    {
                        value = 0;
                        return false;
                    }
                    value = (uint)uvalue64;
                    return true;
                }

            // TODO: LF_OCTWORD, LF_UOCTWORD

            default:
                value = 0;
                return false;
        }
    }

    public uint AsUInt32()
    {
        if (AsUInt32(out var value))
        {
            return value;
        }
        else
        {
            throw new Exception("Incorrect type");
        }
    }

    public bool AsStringUtf8Bytes(out ReadOnlySpan<byte> value)
    {
        Bytes b = new Bytes(this.Data);

        Leaf kind = (Leaf)b.ReadUInt16();
        if (kind.IsImmediateNumeric())
        {
            value = default;
            return false;
        }

        switch (kind)
        {
            case Leaf.LF_VARSTRING:
                {
                    ushort len = b.ReadUInt16();
                    value = b.ReadN(len);
                    return true;
                }

            case Leaf.LF_UTF8STRING:
                {
                    value = b.ReadUtf8Bytes();
                    return true;
                }

            default:
                value = default;
                return false;
        }
    }

    public bool AsString(out string s)
    {
        if (AsStringUtf8Bytes(out var bytes))
        {
            s = System.Text.Encoding.UTF8.GetString(bytes);
            return true;
        }
        else
        {
            s = "";
            return false;
        }
    }

    public string AsString()
    {
        if (AsString(out var s))
        {
            return s;
        }
        else
        {
            throw new Exception("Incorrect type");
        }
    }
}
