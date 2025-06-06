using System.Runtime.CompilerServices;

namespace Pdb;

static class IOUtils {
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void SeekRead(System.IO.Stream stream, long fileOffset, in Span<byte> buffer) {
        stream.Seek(fileOffset, SeekOrigin.Begin);
        int n = stream.Read(buffer);
        if (n != buffer.Length) {
            throw new IOException("Partial read");
        }
    }
}
