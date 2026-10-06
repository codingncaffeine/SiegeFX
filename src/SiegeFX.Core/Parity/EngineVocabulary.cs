using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace SiegeFX.Core.Parity;

/// <summary>Every name the built engine can use to read something by name:
/// the string literals in its assemblies (with the identifier tokens of the
/// ones that hold no whitespace, and the ones ending in '_' kept as prefixes),
/// and separately its type, method, field and property names. Read from the
/// compiled assemblies, not from a list someone keeps, so it cannot drift from
/// the code. An identifier the original game's data uses that the engine never
/// names cannot be read by name: it is not implemented, or it is read through
/// a generated name and needs a reviewer's note. Log text (literals with
/// whitespace) is left out so prose cannot pass for a reader, and member names
/// only answer <see cref="NamesMember"/>: GAS data is read by string, so a C#
/// property called Scale says nothing about a GAS field called scale.</summary>
public sealed class EngineVocabulary
{
    readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _members = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _prefixes = new(StringComparer.OrdinalIgnoreCase);
    readonly List<string> _assemblies = new();

    /// <summary>The assemblies the vocabulary was read from.</summary>
    public IReadOnlyList<string> Assemblies => _assemblies;

    /// <summary>Distinct literal names (literals and their tokens).</summary>
    public int Count => _names.Count;

    /// <summary>Distinct member names (normalized).</summary>
    public int MemberCount => _members.Count;

    /// <summary>Reads the given assemblies; files without .NET metadata are skipped.</summary>
    public static EngineVocabulary Load(IEnumerable<string> assemblyPaths)
    {
        var v = new EngineVocabulary();
        foreach (var path in assemblyPaths)
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) continue;
            var md = pe.GetMetadataReader();
            v._assemblies.Add(path);

            if (md.GetHeapSize(HeapIndex.UserString) > 1)
            {
                var h = MetadataTokens.UserStringHandle(1);
                while (!h.IsNil)
                {
                    v.AddLiteral(md.GetUserString(h));
                    h = md.GetNextHandle(h);
                }
            }
            foreach (var t in md.TypeDefinitions)
                v.AddMember(md.GetString(md.GetTypeDefinition(t).Name));
            foreach (var m in md.MethodDefinitions)
                v.AddMember(md.GetString(md.GetMethodDefinition(m).Name));
            foreach (var f in md.FieldDefinitions)
                v.AddMember(md.GetString(md.GetFieldDefinition(f).Name));
            foreach (var p in md.PropertyDefinitions)
                v.AddMember(md.GetString(md.GetPropertyDefinition(p).Name));
        }
        return v;
    }

    /// <summary>True when a string literal of the engine names
    /// <paramref name="identifier"/> exactly (any case), or names a prefix of
    /// it ending in '_' (e.g. "chore_" for chore_walk).</summary>
    public bool Names(string identifier)
    {
        if (string.IsNullOrEmpty(identifier)) return false;
        if (_names.Contains(identifier)) return true;
        for (int i = identifier.IndexOf('_'); i > 0; i = identifier.IndexOf('_', i + 1))
            if (_prefixes.Contains(identifier[..(i + 1)])) return true;
        return false;
    }

    /// <summary>True when a type or member of the engine carries this name,
    /// ignoring case and underscores (a script's GetGoid against a bridge's
    /// GetGoid, or get_goid).</summary>
    public bool NamesMember(string identifier) =>
        !string.IsNullOrEmpty(identifier) && _members.Contains(Normalize(identifier));

    void AddLiteral(string literal)
    {
        var s = literal.Trim();
        if (s.Length == 0 || s.Length > 200 || s.Any(char.IsWhiteSpace)) return;
        _names.Add(s);
        int start = -1;
        for (int i = 0; i <= s.Length; i++)
        {
            bool idChar = i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_');
            if (idChar && start < 0) start = i;
            else if (!idChar && start >= 0)
            {
                var token = s[start..i];
                _names.Add(token);
                if (token.Length > 1 && token.EndsWith('_')) _prefixes.Add(token);
                start = -1;
            }
        }
    }

    void AddMember(string name)
    {
        if (string.IsNullOrEmpty(name) || name.StartsWith('<')) return; // compiler-generated
        _members.Add(Normalize(name));
    }

    static string Normalize(string name) => name.Replace("_", "").ToLowerInvariant();
}
