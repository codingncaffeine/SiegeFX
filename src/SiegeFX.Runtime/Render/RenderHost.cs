using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace SiegeFX.Runtime.Render;

/// <summary>
/// Owns the window, GL context, input binding, camera, and render loop.
/// Phase 3 just draws a reference grid so WASD + mouse-look can be verified;
/// later phases will hang real scene objects off this.
/// </summary>
public sealed class RenderHost : IDisposable
{
    private readonly IWindow _window;
    private GL? _gl;
    private IInputContext? _input;
    private Shader? _shader;
    private GridMesh? _grid;
    private readonly Camera _camera = new();
    private bool _mouseLookActive;
    private Vector2? _lastMousePos;

    private const string VertexSource = @"#version 330 core
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aColor;
uniform mat4 uViewProj;
out vec3 vColor;
void main()
{
    gl_Position = uViewProj * vec4(aPos, 1.0);
    vColor = aColor;
}";

    private const string FragmentSource = @"#version 330 core
in  vec3 vColor;
out vec4 FragColor;
void main() { FragColor = vec4(vColor, 1.0); }";

    public RenderHost(string title = "SiegeFX", int width = 1280, int height = 720)
    {
        var opts = WindowOptions.Default with
        {
            Title = title,
            Size = new Vector2D<int>(width, height),
            VSync = true,
        };
        _window = Window.Create(opts);
        _window.Load   += OnLoad;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.Resize += OnResize;
    }

    public void Run() => _window.Run();

    private void OnLoad()
    {
        _gl = GL.GetApi(_window);
        _input = _window.CreateInput();

        foreach (var kb in _input.Keyboards)
            kb.KeyDown += (_, key, _) => { if (key == Key.Escape) _window.Close(); };

        foreach (var mouse in _input.Mice)
        {
            mouse.MouseDown += (m, btn) =>
            {
                if (btn == MouseButton.Right)
                {
                    _mouseLookActive = true;
                    _lastMousePos = null;
                    m.Cursor.CursorMode = CursorMode.Raw;
                }
            };
            mouse.MouseUp += (m, btn) =>
            {
                if (btn == MouseButton.Right)
                {
                    _mouseLookActive = false;
                    _lastMousePos = null;
                    m.Cursor.CursorMode = CursorMode.Normal;
                }
            };
            mouse.MouseMove += (_, pos) =>
            {
                if (!_mouseLookActive) return;
                if (_lastMousePos is { } last)
                    _camera.LookDelta(pos.X - last.X, pos.Y - last.Y);
                _lastMousePos = pos;
            };
        }

        _gl.Enable(GLEnum.DepthTest);
        _gl.ClearColor(0.08f, 0.09f, 0.11f, 1f);
        _shader = new Shader(_gl, VertexSource, FragmentSource);
        _grid   = new GridMesh(_gl);
    }

    private void OnUpdate(double dt)
    {
        if (_input is null) return;
        var forward = 0f;
        var strafe  = 0f;
        var vert    = 0f;
        var sprint  = false;

        foreach (var kb in _input.Keyboards)
        {
            if (kb.IsKeyPressed(Key.W)) forward += 1f;
            if (kb.IsKeyPressed(Key.S)) forward -= 1f;
            if (kb.IsKeyPressed(Key.D)) strafe  += 1f;
            if (kb.IsKeyPressed(Key.A)) strafe  -= 1f;
            if (kb.IsKeyPressed(Key.E) || kb.IsKeyPressed(Key.Space))      vert += 1f;
            if (kb.IsKeyPressed(Key.Q) || kb.IsKeyPressed(Key.ControlLeft)) vert -= 1f;
            if (kb.IsKeyPressed(Key.ShiftLeft)) sprint = true;
        }
        _camera.Move(forward, strafe, vert, (float)dt, sprint);
    }

    private void OnRender(double _)
    {
        if (_gl is null || _shader is null || _grid is null) return;
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        var size = _window.FramebufferSize;
        var aspect = size.Y == 0 ? 1f : (float)size.X / size.Y;
        var vp = _camera.GetViewProjection(aspect);

        _shader.Use();
        _shader.SetMatrix4("uViewProj", vp);
        _grid.Draw();
    }

    private void OnResize(Vector2D<int> size) => _gl?.Viewport(size);

    public void Dispose()
    {
        _grid?.Dispose();
        _shader?.Dispose();
        _input?.Dispose();
        _window.Dispose();
    }
}
