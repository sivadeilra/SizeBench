
namespace Pdb;

internal static class PdbInfoStream
{
    public static PdbInfo Read(MsfReader msf)
    {
        var r = msf.GetStreamReader(PdbDefs.PdbiStream);

        byte[] bytes = r.ReadStreamToArray();

        Bytes b = new Bytes(bytes);
        uint version = b.ReadUInt32();
        uint signature = b.ReadUInt32();
        uint age = b.ReadUInt32();

        Guid guid = b.ReadGuid();

        // Read the Named Streams Table.

        uint namesSize = b.ReadUInt32();
        ReadOnlySpan<byte> namesData = b.ReadN((int)namesSize);
        uint nameCount = b.ReadUInt32();
        uint namesHashSize = b.ReadUInt32();

        uint presentUInt32Count = b.ReadUInt32();
        ReadOnlySpan<byte> presentMask = b.ReadN((int)presentUInt32Count * 4);

        uint deletedUInt32Count = b.ReadUInt32();
        ReadOnlySpan<byte> deletedMask = b.ReadN((int)deletedUInt32Count * 4);

        // ignore the masks for now

        NamedStream[] namedStreams = new NamedStream[nameCount];

        for (int i = 0; i < nameCount; ++i)
        {
            // Read each hash entry.
            uint key = b.ReadUInt32();
            uint value = b.ReadUInt32();
            // Key is a byte offset into namesData
            // Value is a stream index.

            var nb = new Bytes(namesData.Slice((int)key)); // todo: bounds check
            string name = nb.ReadUtf8String();

            namedStreams[i] = new NamedStream
            {
                Name = name,
                Stream = (int)value,
            };
        }

        // After the Named Streams table, there is a list of uint32 values that are "feature codes".
        int numFeatures = b.Length / 4;
        uint[] features = new uint[numFeatures];
        for (int i = 0; i < numFeatures; ++i)
        {
            features[i] = b.ReadUInt32();
        }

        return new PdbInfo(guid, age, namedStreams, features);
    }
}

internal sealed class PdbInfo
{
    public Guid Guid;
    public uint Age;
    public NamedStream[] NamedStreams;
    public uint[] Features;

    public PdbInfo(Guid guid, uint age, NamedStream[] namedStreams, uint[] features)
    {
        this.Guid = guid;
        this.Age = age;
        this.NamedStreams = namedStreams;
        this.Features = features;
    }

    public int FindNamedStream(string name)
    {
        foreach (NamedStream ns in this.NamedStreams)
        {
            if (ns.Name == name)
            {
                return ns.Stream;
            }
        }

        return -1;
    }

    public bool HasFeature(uint f)
    {
        foreach (uint ff in this.Features)
        {
            if (f == ff)
            {
                return true;
            }
        }
        return false;
    }
}

internal struct NamedStream
{
    public string Name;
    public int Stream;
}

