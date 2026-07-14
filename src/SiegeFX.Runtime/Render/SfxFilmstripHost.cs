using System.Numerics;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SiegeFX.Core.Assets;
using SiegeFX.Core.Sfx;
using SiegeFX.Core.Tank;

namespace SiegeFX.Runtime.Render;

/// <summary>Phase 23c — offscreen spell-cast filmstrip renderer.
///
/// Renders each spell's cast through the REAL ParticleSystem (same code
/// path the game uses) into an offscreen FBO in a hidden window, sampling
/// frames into a horizontal strip PNG per spell plus one master contact
/// sheet of every spell. Replaces "cast all 61 spells in-game and
/// eyeball each" with a single page of filmstrips for human review.
/// Deterministic: fixed seed, fixed anchors, fixed 20 Hz dt — the same
/// constants as the Phase 23b timeline goldens.</summary>
public static class SfxFilmstripHost
{
    public static int Run(string logicTankPath, string objectsTankPath, string spellFilter,
                          string outDir, int frames, int stripCount, int seed, int size,
                          float targetDist = 4f, string? scriptName = null, string? effectsDir = null)
    {
        int exit = 0;
        var opts = WindowOptions.Default with
        {
            Title = "SiegeFX sfx filmstrip",
            Size = new Vector2D<int>(size, size),
            IsVisible = false,
            VSync = false,
        };
        var window = Window.Create(opts);
        window.Load += () =>
        {
            try { exit = RenderAll(window, logicTankPath, objectsTankPath, spellFilter, outDir, frames, stripCount, seed, size, targetDist, scriptName, effectsDir); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"filmstrip: {ex}");
                exit = 5;
            }
            finally { window.Close(); }
        };
        window.Run();
        return exit;
    }

    static int RenderAll(IWindow window, string logicTankPath, string objectsTankPath, string spellFilter,
                         string outDir, int frames, int stripCount, int seed, int size, float targetDist,
                         string? scriptName, string? effectsDir)
    {
        var gl = GL.GetApi(window);

        // --- offscreen target -------------------------------------------
        uint fbo = gl.GenFramebuffer();
        gl.BindFramebuffer(GLEnum.Framebuffer, fbo);
        uint colorTex = gl.GenTexture();
        gl.BindTexture(GLEnum.Texture2D, colorTex);
        unsafe
        {
            gl.TexImage2D(GLEnum.Texture2D, 0, (int)GLEnum.Rgba8,
                (uint)size, (uint)size, 0, GLEnum.Rgba, GLEnum.UnsignedByte, null);
        }
        gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Nearest);
        gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Nearest);
        gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.ColorAttachment0, GLEnum.Texture2D, colorTex, 0);
        uint depthRbo = gl.GenRenderbuffer();
        gl.BindRenderbuffer(GLEnum.Renderbuffer, depthRbo);
        gl.RenderbufferStorage(GLEnum.Renderbuffer, GLEnum.DepthComponent24, (uint)size, (uint)size);
        gl.FramebufferRenderbuffer(GLEnum.Framebuffer, GLEnum.DepthAttachment, GLEnum.Renderbuffer, depthRbo);
        if (gl.CheckFramebufferStatus(GLEnum.Framebuffer) != GLEnum.FramebufferComplete)
        {
            Console.Error.WriteLine("filmstrip: FBO incomplete");
            return 5;
        }

        // --- data + renderer --------------------------------------------
        using var logicTank = TankFile.Open(logicTankPath);
        var logicReader = new TankReader(logicTank);
        var (templates, _) = TemplateStore.LoadFromTank(logicReader);
        var spells = SpellCatalog.Build(templates);
        var sfxStore = SfxScriptStore.LoadFromTank(logicReader);

        // SS-FXLAB — loose-directory override: every .gas in the folder is
        // harvested OVER the stock store (same-name wins), so an in-progress
        // Effects Lab script renders through the real ParticleSystem without
        // repacking a tank.
        if (!string.IsNullOrEmpty(effectsDir) && System.IO.Directory.Exists(effectsDir))
        {
            int merged = 0;
            foreach (var f in System.IO.Directory.EnumerateFiles(effectsDir, "*.gas", System.IO.SearchOption.AllDirectories))
            {
                try { merged += sfxStore.AddFromGasText(System.IO.File.ReadAllText(f), f); }
                catch (Exception ex) { Console.Error.WriteLine($"filmstrip: skipped {f}: {ex.Message}"); }
            }
            Console.WriteLine($"filmstrip: merged {merged} loose script(s) from {effectsDir}");
        }

        using var objectsTank = TankFile.Open(objectsTankPath);
        var objectsReader = new TankReader(objectsTank);
        using var particles = new ParticleSystem(gl);
        particles.LoadTextures(objectsReader);

        // Caster feet at origin, target `targetDist` u east (default 4u,
        // matching the 23b timeline goldens; pass --target-dist to fly a
        // projectile a realistic distance and expose moving-fire trails),
        // weapon bone at hand height.
        var src    = new Vector3(0f, 0f, 0f);
        var tgt    = new Vector3(targetDist, 0f, 0f);
        var weapon = new Vector3(0.3f, 1.2f, 0f);
        var ctx = new SfxContext(src, tgt, weapon);

        // Side-on camera framing both endpoints; pull back proportional to the
        // flight length so a long shot still fits the frame.
        float mid = targetDist * 0.5f;
        var lookAt = new Vector3(mid, 0.9f, 0f);
        var camPos = new Vector3(mid, targetDist * 0.5f + 1.0f, targetDist * 1.6f + 2.5f);
        var view = Matrix4x4.CreateLookAt(camPos, lookAt, Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, 1f, 0.1f, 100f);

        const float dt = 1f / 20f;
        bool all = string.Equals(spellFilter, "--all", StringComparison.OrdinalIgnoreCase);
        System.IO.Directory.CreateDirectory(outDir);

        // Each job = one strip: normally every matching spell's cast script;
        // in script-direct mode (SS-FXLAB) exactly the named effect script,
        // no spell wrapper required.
        List<(string Name, string Script)> roster;
        if (!string.IsNullOrEmpty(scriptName))
        {
            if (!sfxStore.TryGet(scriptName, out _))
            {
                Console.Error.WriteLine($"filmstrip: effect script '{scriptName}' not found (store has {sfxStore.Count})");
                return 4;
            }
            roster = new List<(string, string)> { (scriptName!, scriptName!) };
        }
        else
        {
            roster = spells.All
                .Where(s => !string.IsNullOrEmpty(s.CastSfxScript) && sfxStore.TryGet(s.CastSfxScript!, out _))
                .Where(s => all || s.Name.IndexOf(spellFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(s => s.Name, StringComparer.Ordinal)
                .Select(s => (s.Name, s.CastSfxScript!))
                .ToList();
        }
        if (roster.Count == 0)
        {
            Console.Error.WriteLine($"filmstrip: no runnable spell matches '{spellFilter}'");
            return 4;
        }

        int stripEvery = Math.Max(1, frames / Math.Max(1, stripCount));
        int tiles = 0;
        for (int i = 0; i < frames; i++) if (i % stripEvery == 0) tiles++;

        var framePixels = new byte[size * size * 4];
        var stripPixels = new byte[tiles * size * size * 4]; // tiles horizontal
        int stripW = tiles * size;
        var masterRows = new List<(string Name, byte[] Strip)>();
        var index = new List<string>();

        foreach (var spell in roster)
        {
            particles.Clear();
            var rt = new SfxRuntime(sfxStore, particles);
            rt.SetDeterministicSeed(seed);
            rt.Spawn(spell.Script, ctx, null);

            Array.Clear(stripPixels);
            int tile = 0;
            for (int f = 0; f < frames; f++)
            {
                if (f > 0)
                {
                    rt.Tick(dt);
                    particles.Tick(dt);
                }

                gl.BindFramebuffer(GLEnum.Framebuffer, fbo);
                gl.Viewport(0, 0, (uint)size, (uint)size);
                gl.ClearColor(0f, 0f, 0f, 1f);
                gl.Enable(EnableCap.DepthTest);
                gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));
                particles.Draw(view, proj, camPos);
                gl.Finish();

                if (f % stripEvery != 0) continue;
                unsafe
                {
                    fixed (byte* p = framePixels)
                        gl.ReadPixels(0, 0, (uint)size, (uint)size, GLEnum.Rgba, GLEnum.UnsignedByte, p);
                }
                // GL reads bottom-up; copy row-flipped into the strip tile.
                int tileX = tile * size;
                for (int y = 0; y < size; y++)
                {
                    int srcOff = (size - 1 - y) * size * 4;
                    int dstOff = (y * stripW + tileX) * 4;
                    System.Buffer.BlockCopy(framePixels, srcOff, stripPixels, dstOff, size * 4);
                }
                tile++;
            }

            // Opaque alpha — additive blending leaves a<1, which renders the
            // sheet see-through in viewers; the black background is the look.
            for (int px = 3; px < stripPixels.Length; px += 4) stripPixels[px] = 255;

            var outPath = System.IO.Path.Combine(outDir, spell.Name + ".png");
            using (var fs = System.IO.File.Create(outPath))
                SiegeFX.Core.IO.Png.EncodeRgba(fs, stripPixels, stripW, size);
            masterRows.Add((spell.Name, (byte[])stripPixels.Clone()));
            index.Add(spell.Name);
            Console.WriteLine($"  filmstrip: {spell.Name} -> {outPath}");
        }

        // Master contact sheet — every spell's strip stacked vertically, in
        // the same order as index.txt.
        if (masterRows.Count > 1)
        {
            int masterH = masterRows.Count * size;
            var master = new byte[stripW * masterH * 4];
            for (int r = 0; r < masterRows.Count; r++)
                System.Buffer.BlockCopy(masterRows[r].Strip, 0, master, r * size * stripW * 4, size * stripW * 4);
            var sheetPath = System.IO.Path.Combine(outDir, "_contact_sheet.png");
            using (var fs = System.IO.File.Create(sheetPath))
                SiegeFX.Core.IO.Png.EncodeRgba(fs, master, stripW, masterH);
            System.IO.File.WriteAllLines(System.IO.Path.Combine(outDir, "_contact_sheet_index.txt"),
                index.Select((n, i) => $"row {i + 1,3}: {n}"));
            Console.WriteLine($"filmstrip: contact sheet ({masterRows.Count} rows) -> {sheetPath}");
        }

        gl.DeleteFramebuffer(fbo);
        gl.DeleteTexture(colorTex);
        gl.DeleteRenderbuffer(depthRbo);
        Console.WriteLine($"filmstrip: {masterRows.Count} spells rendered, {tiles} tiles each, {size}px, seed={seed}");
        return 0;
    }
}
