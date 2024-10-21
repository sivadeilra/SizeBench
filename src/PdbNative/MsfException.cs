using System;

namespace PdbNative;

public sealed class MsfException : Exception
{
    public MsfException(string message) : base(message) { }
}
