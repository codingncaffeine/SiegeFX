using System.Numerics;
using SiegeFX.Core.Actors;
using SiegeFX.Core.Assets;

namespace SiegeFX.Tests;

public class AnimSfxTests
{
    const uint Sfx1 = 0x31584653, Sfx2 = 0x32584653, Fire = 0x45524946;
    // a_c_gbn_bs_fs2_dsf: 1.333 s, SFX1 at 0.625.
    static readonly PrsAnimation.NoteEvent[] Hammer = { new(0.625f, Sfx1) };
    const float Len = 1.3333f;

    static List<int> Crossed(IReadOnlyList<PrsAnimation.NoteEvent> notes, double a, double b)
    {
        var l = new List<int>();
        AnimNotes.SfxCrossed(notes, Len, a, b, l);
        return l;
    }

    [Fact]
    public void TheHammerNoteFiresOncePerLoopEveryLoop()
    {
        // Step a looping idle at 60 fps for 10 loops: one strike each.
        int strikes = 0;
        double t = 0;
        for (int i = 0; i < 800; i++)
        {
            double next = t + 1.0 / 60;
            strikes += Crossed(Hammer, t, next).Count;
            t = next;
        }
        Assert.Equal((int)Math.Floor((t - 0.625 * Len) / Len) + 1, strikes);
        Assert.True(strikes >= 9);
    }

    [Fact]
    public void OnlySfxNotesCountAndTheyKeepTheirNumbers()
    {
        var notes = new PrsAnimation.NoteEvent[] { new(0.8f, Sfx2), new(0.2f, Fire), new(0.1f, Sfx1) };
        Assert.Equal(new[] { 1, 2 }, Crossed(notes, 0.01, Len));
    }

    [Fact]
    public void NoTimePassingFiresNothing()
    {
        var atZero = new PrsAnimation.NoteEvent[] { new(0f, Sfx1) };
        Assert.Empty(Crossed(atZero, 0, 0));          // paused on a fresh clip
        Assert.Equal(new[] { 1 }, Crossed(atZero, 0, 0.02)); // the clip's first frame
        Assert.Empty(Crossed(Hammer, 0.9, 0.9));
    }

    [Fact]
    public void AHitchFiresAtMostOneLoopsWorth()
    {
        Assert.Single(Crossed(Hammer, 0.1, 0.1 + 20 * Len));
    }

    static (TriggerRuntime rt, TriggerInstance trig) BlacksmithLikeTrigger(string rows)
    {
        var doc = GasDocument.Parse("[common] { [template_triggers] { " + rows + " } }");
        var triggers = doc.Roots.Single().Children.Single();
        var matrix = new TriggerMatrix(triggers.Children
            .Select(r => TriggerRow.Parse(r, "test", null)).ToList());
        var trig = new TriggerInstance(0x03200c26, 0, Vector3.Zero, matrix, startActive: true);
        var rt = new TriggerRuntime();
        rt.Register(trig);
        return (rt, trig);
    }

    sealed class SfxCtx : TriggerContext
    {
        public readonly List<string> Calls = new();
        public override void CallSfxScript(string scriptName, IReadOnlyList<string>? args, Vector3 origin, uint ownerScid)
            => Calls.Add($"{scriptName}@{ownerScid:X8}");
    }

    [Fact]
    public void WeAnimSfxRunsTheTemplatesSfxScriptEveryTime()
    {
        var (rt, _) = BlacksmithLikeTrigger("""
            [*] { action* = call_sfx_script("blacksmith_hammer"); condition* = receive_world_message("we_anim_sfx",1); }
            """);
        var ctx = new SfxCtx();
        for (int loop = 0; loop < 8; loop++)
        {
            rt.PostInboundMessage(0x03200c26, "we_anim_sfx", 1);
            rt.Tick(0.7, ctx);
            rt.Tick(0.7, ctx);
        }
        Assert.Equal(8, ctx.Calls.Count);
        Assert.All(ctx.Calls, c => Assert.Equal("blacksmith_hammer@03200C26", c));
    }

    [Fact]
    public void TheNoteNumberPicksTheRowAndOtherNumbersDoNotClogIt()
    {
        // giant_spider-style: one row per foot note.
        var (rt, _) = BlacksmithLikeTrigger("""
            [*] { action* = call_sfx_script("spark_1"); condition* = receive_world_message("we_anim_sfx",1); }
            [*] { action* = call_sfx_script("spark_2"); condition* = receive_world_message("we_anim_sfx",2); }
            """);
        var ctx = new SfxCtx();
        for (int loop = 0; loop < 6; loop++)
        {
            rt.PostInboundMessage(0x03200c26, "we_anim_sfx", 2);
            rt.Tick(0.1, ctx);
            rt.Tick(0.1, ctx);
        }
        Assert.Equal(6, ctx.Calls.Count);
        Assert.All(ctx.Calls, c => Assert.StartsWith("spark_2@", c));
        rt.PostInboundMessage(0x03200c26, "we_anim_sfx", 1);
        rt.Tick(0.1, ctx);
        Assert.StartsWith("spark_1@", ctx.Calls[^1]);
    }

    [Fact]
    public void AMessageWithoutAQualifierStillMatchesByName()
    {
        var (rt, _) = BlacksmithLikeTrigger("""
            [*] { action* = call_sfx_script("x"); condition* = receive_world_message("we_req_activate",0); }
            """);
        var ctx = new SfxCtx();
        rt.PostInboundMessage(0x03200c26, "we_req_activate");
        rt.Tick(0.1, ctx);
        Assert.Single(ctx.Calls);
    }
}
