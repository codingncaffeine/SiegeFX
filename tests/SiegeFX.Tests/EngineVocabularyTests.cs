using SiegeFX.Core.Parity;

namespace SiegeFX.Tests;

/// <summary>Reads this test assembly as if it were the engine. The planted
/// literals below are what the vocabulary must find; every probe is joined
/// at run time from parts, so a probe is never itself a literal of this
/// assembly and cannot pass a check by its own presence.</summary>
public class EngineVocabularyTests
{
    // The "engine code": literals in IL.
    static string FieldLookup() => "zzprobe_field_name";
    static string PrefixLookup() => "zzprobe_family_";
    static string PathLookup() => "/zzprobe/folder/zzprobe_interface.gas";
    static string LogLine() => "zzprobe_in_prose is only mentioned in a sentence";

    static readonly EngineVocabulary Vocab = EngineVocabulary.Load(new[] { typeof(EngineVocabularyTests).Assembly.Location });

    static string J(params string[] parts) => string.Join("_", parts);

    [Fact]
    public void A_literal_the_code_looks_up_is_named_in_any_case()
    {
        Assert.NotEmpty(FieldLookup());
        Assert.True(Vocab.Names(J("zzprobe", "field", "name")));
        Assert.True(Vocab.Names(J("ZZPROBE", "FIELD", "NAME")));
    }

    [Fact]
    public void A_name_the_code_never_uses_is_not_named()
    {
        Assert.False(Vocab.Names(J("zzprobe", "absent", "field")));
    }

    [Fact]
    public void A_literal_ending_in_underscore_names_its_family()
    {
        Assert.NotEmpty(PrefixLookup());
        Assert.True(Vocab.Names(J("zzprobe", "family", "walk")));
        Assert.False(Vocab.Names(J("zzprobe", "familywalk")));
    }

    [Fact]
    public void Path_literals_name_their_parts()
    {
        Assert.NotEmpty(PathLookup());
        Assert.True(Vocab.Names(J("zzprobe", "interface")));
    }

    [Fact]
    public void Prose_does_not_count_as_a_reader()
    {
        Assert.NotEmpty(LogLine());
        Assert.False(Vocab.Names(J("zzprobe", "in", "prose")));
    }

    [Fact]
    public void Member_names_answer_only_the_member_question()
    {
        // The name of a test method: a member of this assembly, never a literal.
        var method = J("Prose", "does", "not", "count", "as", "a", "reader");
        Assert.True(Vocab.NamesMember(method));
        Assert.True(Vocab.NamesMember(method.ToLowerInvariant()));
        Assert.False(Vocab.Names(method));
    }
}
