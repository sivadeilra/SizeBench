using Microsoft.VisualStudio.TestPlatform;
using Pdb;
using Pdb.CodeView;

namespace PdbTest;

[TestClass]
public sealed class CVHashTests
{
    [TestMethod]
    public void TestHash()
    {
        (uint, string)[] stringInputs =  {
            (0x00000c09, ""),
            (0x00000c09, " "),
            (0x00000c09, "  "),
            (0x00000c09, "   "),
            (0x00000c09, "    "),
            (0x00019fe2, "hello"),
            (0x00019fe2, "HELLO"),
            (0x0003c00b, "Hello, World"),
            (0x0003c00b, "hello, world"),
            (0x000068e2, "hello_world::main"),
            (0x0000b441, "std::vector<std::basic_string<wchar_t>>"),
            (0x000372ae, "__chkstk"),
            (0x0001143b, "WelsEmms"),
        };

        foreach (var (expected_output, input) in stringInputs)
        {
            uint m = 0x3_ffff;

            byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(input);

            uint actual_output_bytes = CVHash.hash_mod_u32(inputBytes, m);
            uint actual_output_ascii = CVHash.hash_mod_u32_ascii(input, m);

            // "expected: 0x{expected_output:08x}, actual: 0x{actual_output:08x}, input: {input:02x?}"
            Assert.AreEqual(expected_output, actual_output_bytes);

            // "expected: 0x{expected_output:08x}, actual: 0x{actual_output:08x}, input: {input:02x?}"
            Assert.AreEqual(expected_output, actual_output_ascii);
        }

        (uint, byte[])[] bytesInputs = {
            (0x00000c0a, new byte[] { 1 }),
            (0x00000e0a, new byte[] { 1, 2 }),
            (0x00000e0b, new byte[] { 1, 2, 3 }),
            (0x00038b6b, new byte[] { 1, 2, 3, 4 }),
            (0x00038b70, new byte[] { 1, 2, 3, 4, 5 }),
            (0x00038d70, new byte[] { 1, 2, 3, 4, 5, 6 }),
            (0x00038d69, new byte[] { 1, 2, 3, 4, 5, 6, 7 }),
            (0x00019789, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }),
            (0x00019790, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }),
            (0x00019191, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }),
            (0x0001918a, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }),
            (0x000313ed, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }),
            (0x000313f8, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 }),
            (0x000214eb, new byte[] { 5, 6, 7, 8 }),
        };

        foreach (var (expected_output, input) in bytesInputs)
        {
            uint m = 0x3_ffff;
            uint actual_output = CVHash.hash_mod_u32(input, m);

            // "expected: 0x{expected_output:08x}, actual: 0x{actual_output:08x}, input: {input:02x?}"
            Assert.AreEqual(expected_output, actual_output);
        }
    }
}
