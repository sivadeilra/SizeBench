
namespace Pdb;

public interface IMsfReader {
    int NumStreams { get; }
    bool IsStreamValid(int stream);
    long StreamSize(int stream);

    /// <summary>
    /// Returns an object which can read the given stream.
    /// 
    /// If `stream` is out of range, this function nonetheless returns a valid reader. The reader
    /// will report a stream length of 0.
    /// </summary>
    IMsfStreamReader GetStreamReader(int stream);
}

