using PdbNative;
using System.IO;

namespace PdbNativeTests;

[TestClass]
public class UnitTest1
{
    [TestMethod]
    public void TestMethod1()
    {
        using var fileStream = File.Open("d:\\SizeBench\\src\\TestPEs\\External\\x64\\ReactNativeXaml.pdb", FileMode.Open, FileAccess.Read, FileShare.Read);
        using var msf = MsfReader.Open(fileStream);


        {
            using var s = msf.OpenStream(1);
            long length = s.Length;
            byte[] streamData = new byte[length];
            s.Read(streamData, 0, streamData.Length);

        }

    }
}
