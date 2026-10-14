using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace SiegeFX.Guards;

/// <summary>The string literals compiled into an assembly (its user-string
/// heap), read the way the parity ledger reads the engine's vocabulary:
/// from the built code, so nothing can be missed by a source parser.</summary>
public static class AssemblyLiterals
{
    public static IReadOnlyList<string> Read(string assemblyPath)
    {
        var list = new List<string>();
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata) return list;
        var md = pe.GetMetadataReader();
        if (md.GetHeapSize(HeapIndex.UserString) <= 1) return list;
        var h = MetadataTokens.UserStringHandle(1);
        while (!h.IsNil)
        {
            list.Add(md.GetUserString(h));
            h = md.GetNextHandle(h);
        }
        return list;
    }
}
