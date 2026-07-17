using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SiegeFX.Core.Assets;
using SiegeFX.Core.Tank;
using SiegeFX.Runtime.Render.Hud;

namespace SiegeFX.Runtime.Render;

/// <summary>Offscreen frontend-chrome still renderer. Renders the
/// FrontendScene at a named ScreenState into a hidden-window FBO and
/// writes a PNG — the frontend counterpart of SfxFilmstripHost. Turns
/// "what does the SP screen actually draw" from an eyeball question
/// into a diffable receipt, without the user having to boot + click
/// through the menu flow. Settled states only (transitions render at
/// their hold pose 0, matching SetState + no Tick).</summary>
public static class FrontendShotHost
{
    public static int Run(string logicTankPath, string objectsTankPath, string stateName,
                          string outPath, int width, int height)
    {
        int exit = 0;
        var opts = WindowOptions.Default with
        {
            Title = "SiegeFX frontend shot",
            Size = new Vector2D<int>(Math.Min(width, 1024), Math.Min(height, 768)),
            IsVisible = false,
            VSync = false,
        };
        var window = Window.Create(opts);
        window.Load += () =>
        {
            try { exit = RenderShot(window, logicTankPath, objectsTankPath, stateName, outPath, width, height); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"frontend-shot: {ex}");
                exit = 5;
            }
            finally { window.Close(); }
        };
        window.Run();
        return exit;
    }

    static int RenderShot(IWindow window, string logicTankPath, string objectsTankPath,
                          string stateName, string outPath, int width, int height)
    {
        if (!Enum.TryParse<FrontendScene.ScreenState>(stateName, ignoreCase: true, out var state))
        {
            Console.Error.WriteLine($"frontend-shot: unknown state '{stateName}'. Valid states:");
            foreach (var n in Enum.GetNames<FrontendScene.ScreenState>())
                Console.Error.WriteLine($"  {n}");
            return 4;
        }

        var gl = GL.GetApi(window);

        uint fbo = gl.GenFramebuffer();
        gl.BindFramebuffer(GLEnum.Framebuffer, fbo);
        uint colorTex = gl.GenTexture();
        gl.BindTexture(GLEnum.Texture2D, colorTex);
        unsafe
        {
            gl.TexImage2D(GLEnum.Texture2D, 0, (int)GLEnum.Rgba8,
                (uint)width, (uint)height, 0, GLEnum.Rgba, GLEnum.UnsignedByte, null);
        }
        gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Nearest);
        gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Nearest);
        gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.ColorAttachment0, GLEnum.Texture2D, colorTex, 0);
        if (gl.CheckFramebufferStatus(GLEnum.Framebuffer) != GLEnum.FramebufferComplete)
        {
            Console.Error.WriteLine("frontend-shot: FBO incomplete");
            return 5;
        }

        using var logicTank   = TankFile.Open(logicTankPath);
        using var objectsTank = TankFile.Open(objectsTankPath);
        var resolver = new AssetResolver();
        resolver.Add(new TankReader(objectsTank), "Objects.dsres");
        resolver.Add(new TankReader(logicTank),   "Logic.dsres");

        using var scene = new FrontendScene(gl, resolver);
        scene.SetState(state);

        // Same GL state as RenderHost's HUD pass around _frontendScene.Draw:
        // depth off, alpha blend on, back-face culling ON (culling is what
        // makes the drum/flap "flip" mechanisms show the right face), and
        // the chrome letterbox scissor (aspect 1.32, matching RenderHost).
        gl.Viewport(0, 0, (uint)width, (uint)height);
        gl.Disable(EnableCap.DepthTest);
        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        gl.Enable(EnableCap.CullFace);
        gl.CullFace(GLEnum.Back);
        gl.FrontFace(FrontFaceDirection.Ccw);
        gl.ClearColor(0f, 0f, 0f, 1f);
        gl.Clear(ClearBufferMask.ColorBufferBit);

        const float chromeAspect = 1.32f;
        float vpAspect = width / (float)height;
        int boxW, boxH;
        if (vpAspect > chromeAspect) { boxH = height; boxW = (int)(height * chromeAspect); }
        else                         { boxW = width;  boxH = (int)(width / chromeAspect); }
        int boxX = (width - boxW) / 2;
        int boxYTop = (height - boxH) / 2;
        int boxYGlBottom = height - boxYTop - boxH;
        gl.Enable(EnableCap.ScissorTest);
        gl.Scissor(boxX, boxYGlBottom, (uint)boxW, (uint)boxH);

        scene.Draw(width, height);

        gl.Disable(EnableCap.ScissorTest);

        var pixels = new byte[width * height * 4];
        unsafe
        {
            fixed (byte* p = pixels)
                gl.ReadPixels(0, 0, (uint)width, (uint)height, GLEnum.Rgba, GLEnum.UnsignedByte, p);
        }
        // GL reads bottom-up; flip to top-down and force opaque alpha (the
        // HUD blend pass leaves destination alpha in a meaningless state).
        var flipped = new byte[pixels.Length];
        int stride = width * 4;
        for (int y = 0; y < height; y++)
            System.Buffer.BlockCopy(pixels, (height - 1 - y) * stride, flipped, y * stride, stride);
        for (int i = 3; i < flipped.Length; i += 4) flipped[i] = 255;

        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
        using (var fs = System.IO.File.Create(outPath))
            SiegeFX.Core.IO.Png.EncodeRgba(fs, flipped, width, height);
        Console.WriteLine($"frontend-shot: {state} -> {outPath} ({width}x{height})");
        return 0;
    }
}
