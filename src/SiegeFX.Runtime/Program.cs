using SiegeFX.Runtime.Render;

string? meshPath    = args.Length > 0 ? args[0] : null;
string? texturePath = args.Length > 1 ? args[1] : null;
using var host = new RenderHost(
    "SiegeFX  —  RMB+WASD to fly, Shift to sprint, Esc to quit",
    meshPath: meshPath,
    texturePath: texturePath);
host.Run();
