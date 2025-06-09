using Microsoft.VisualStudio.TestPlatform;
using Pdb;
using Pdb.CodeView;

namespace PdbTest;

[TestClass]
public sealed class NumberTests
{
    [TestMethod]
    public void IsImmediateNumeric()
    {
        Assert.IsTrue(((Leaf)0x1234).IsImmediateNumeric());
        Assert.IsFalse(Leaf.LF_CHAR.IsImmediateNumeric());
    }

    [TestMethod]
    public void DecodeImmediate()
    {
        Bytes b = new(new byte[] { 0xaa, 0x55, 0x01 });

        Assert.IsTrue(Number.Read(ref b, out var num));
        Assert.AreEqual(b.Length, 1);

        Assert.AreEqual(num.Kind, (Leaf)0x55aa);

        Assert.IsTrue(num.AsInt32(out var ivalue));
        Assert.AreEqual(ivalue, 0x55aa);

        Assert.IsTrue(num.AsUInt32(out var uvalue));
        Assert.AreEqual(uvalue, 0x55aau);

        Assert.IsFalse(Number.Read(ref b, out _));
    }

    [TestMethod]
    public void DecodeInt8()
    {
        Bytes b = new(new byte[] { 0, 0x80, 0x80, 0xaa });
        Assert.IsTrue(Number.Read(ref b, out var num);
        Assert.AreEqual(b.Length, 1);

        Assert.IsTrue(num.AsInt32(out var value));
        Assert.AreEqual<int>(value, -128);
    }
}
