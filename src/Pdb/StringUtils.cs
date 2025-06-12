internal static class StringUtils
{
    /// <summary>
    /// Compare two strings for equality. One is stored in UTF-16, the other in UTF-8. This ONLY works for ASCII.
    /// </summary>
    // a is ascii, but stored in UTF-16
    // b is ascii, but stored in UTF-8
    internal static bool StringIsEqual(ReadOnlySpan<char> a, ReadOnlySpan<byte> b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; ++i)
        {
            if ((uint)a[i] != (uint)b[i])
            {
                return false;
            }
        }

        return true;
    }
}
