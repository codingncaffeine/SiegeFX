using System.Text.RegularExpressions;
using SiegeFX.Core.Assets;

namespace SiegeFX.Core.Tests;

/// <summary>Keeps <see cref="AiCommandCoverage"/> honest. The runtime project
/// is Windows-only, so rather than referencing it the tests read
/// RenderHost.cs and compare the dispatcher's case labels with the table.</summary>
public class AiCommandCoverageTests
{
    static readonly string RenderHostSource = File.ReadAllText(Path.Combine(
        RepoRoot(), "src", "SiegeFX.Runtime", "Render", "RenderHost.cs"));

    [Fact]
    public void DispatchedMatchesActivateAiCommandCaseLabels()
    {
        var body = Slice(RenderHostSource,
            "private void ActivateAiCommand(", "            default:");
        var labels = Regex.Matches(body, @"^\s*case ""([a-z0-9_]+)"":", RegexOptions.Multiline)
                          .Select(m => m.Groups[1].Value)
                          .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.NotEmpty(labels);
        Assert.Empty(labels.Except(AiCommandCoverage.Dispatched, StringComparer.OrdinalIgnoreCase)
                           .Select(v => $"{v}: handled by the dispatcher but missing from AiCommandCoverage.Dispatched"));
        Assert.Empty(AiCommandCoverage.Dispatched.Except(labels, StringComparer.OrdinalIgnoreCase)
                           .Select(v => $"{v}: listed as Dispatched but has no case in ActivateAiCommand"));
    }

    [Fact]
    public void NisVerbsAreIndexedForTheNisEngine()
    {
        var line = RenderHostSource.Split('\n')
            .First(l => l.Contains("if (tnLower is \"cmd_enter_nis\""));
        foreach (var verb in AiCommandCoverage.Nis)
            Assert.Contains($"\"{verb}\"", line);
    }

    [Fact]
    public void IndexedTemplatesAreRegisteredAtLoad()
    {
        foreach (var template in AiCommandCoverage.Indexed)
            Assert.Contains($"\"{template}\"", RenderHostSource);
    }

    [Fact]
    public void CategoriesAreDisjoint()
    {
        var all = new[] { AiCommandCoverage.Dispatched, AiCommandCoverage.Nis,
                          AiCommandCoverage.Indexed, AiCommandCoverage.Route }
            .SelectMany(s => s).ToList();
        Assert.Equal(all.Count, all.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("cmd_ai_t_move", AiCommandCoverage.Kind.Dispatched)]
    [InlineData("CMD_AI_T_MOVE", AiCommandCoverage.Kind.Dispatched)]
    [InlineData("cmd_camera_waypoint", AiCommandCoverage.Kind.Nis)]
    [InlineData("cmd_ai_t_attack_object", AiCommandCoverage.Kind.Indexed)]
    [InlineData("cmd_ai_c_patrol", AiCommandCoverage.Kind.Route)]
    [InlineData("cmd_not_a_real_verb", AiCommandCoverage.Kind.Stub)]
    public void Classify(string template, AiCommandCoverage.Kind expected)
    {
        Assert.Equal(expected, AiCommandCoverage.Classify(template));
    }

    static string Slice(string text, string startMarker, string endMarker)
    {
        int start = text.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{startMarker}' not found in RenderHost.cs");
        int end = text.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"'{endMarker}' not found after '{startMarker}'");
        return text[start..end];
    }

    static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SiegeFX.sln")))
                return dir.FullName;
        throw new InvalidOperationException("SiegeFX.sln not found above " + AppContext.BaseDirectory);
    }
}
