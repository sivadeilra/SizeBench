namespace PdbNative;

public sealed class PdbReader : IDisposable
{
    readonly MsfReader _msf;

    private PdbReader(MsfReader msf)
    {
        this._msf = msf;
    }

    public static PdbReader FromMsfReader(MsfReader msf)
    {
        return new PdbReader(msf);
    }

    // public static PdbReader FromMsfzReader(MsfzReader msf)


    public void Dispose()
    {
        this._msf.Dispose();
    }

    public FindTypeRecord(TypeIndex typeIndex)
    {
    }
}


