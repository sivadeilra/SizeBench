namespace Pdb;

#pragma warning disable 0660
#pragma warning disable 0661

public ref struct Utf8Span
{
    public ReadOnlySpan<byte> Bytes;

    public Utf8Span(ReadOnlySpan<byte> bytes)
    {
        this.Bytes = bytes;
    }

    public override string ToString()
    {
        return System.Text.Encoding.UTF8.GetString(this.Bytes);
    }

    public int Length => this.Bytes.Length;

    public static bool operator ==(Utf8Span a, string b)
    {
        return StringUtils.StringIsEqual(b, a.Bytes);
    }

    public static bool operator !=(Utf8Span a, string b)
    {
        return !StringUtils.StringIsEqual(b, a.Bytes);
    }

    public static bool operator ==(Utf8Span a, ReadOnlySpan<char> b)
    {
        return StringUtils.StringIsEqual(b, a.Bytes);
    }

    public static bool operator !=(Utf8Span a, ReadOnlySpan<char> b)
    {
        return !StringUtils.StringIsEqual(b, a.Bytes);
    }

    public static bool operator ==(string a, Utf8Span b)
    {
        return StringUtils.StringIsEqual(a, b.Bytes);
    }

    public static bool operator !=(string a, Utf8Span b)
    {
        return !StringUtils.StringIsEqual(a, b.Bytes);
    }

    public static bool operator ==(ReadOnlySpan<char> a, Utf8Span b)
    {
        return StringUtils.StringIsEqual(a, b.Bytes);
    }

    public static bool operator !=(ReadOnlySpan<char> a, Utf8Span b)
    {
        return !StringUtils.StringIsEqual(a, b.Bytes);
    }

}
