namespace Pdb;

public interface IMsfStreamReader {
    ulong StreamSize { get; }

    int ReadAt(long streamOffset, in Span<byte> buffer);
    void ReadAtExact(long streamOffset, in Span<byte> buffer);
    byte[] ReadStreamToArray();
}
