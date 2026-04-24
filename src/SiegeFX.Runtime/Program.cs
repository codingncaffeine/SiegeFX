using SiegeFX.Runtime.Render;

// Invocation shapes:
//   SiegeFX.Runtime [mesh.sno|mesh.asp] [texture.raw | tank.dsres]
//   SiegeFX.Runtime --region <map-tank> <terrain-tank> <region-path>
//   SiegeFX.Runtime --world  <map-tank> <terrain-tank> [root-region]
//   SiegeFX.Runtime --anim   <rigged.asp> <clip.prs> [texture.raw]
string? meshPath = null;
string? texturePath = null;
string? regionMap = null;
string? regionTerrain = null;
string? regionPath = null;
string? worldMap = null;
string? worldTerrain = null;
string? worldRoot = null;
string? animAsp = null;
string? animPrs = null;
string? animTexture = null;

if (args.Length >= 1 && args[0] == "--region")
{
    if (args.Length < 4)
    {
        Console.Error.WriteLine("usage: SiegeFX.Runtime --region <map-tank> <terrain-tank> <region-path>");
        return 1;
    }
    regionMap = args[1];
    regionTerrain = args[2];
    regionPath = args[3];
}
else if (args.Length >= 1 && args[0] == "--world")
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("usage: SiegeFX.Runtime --world <map-tank> <terrain-tank> [root-region]");
        return 1;
    }
    worldMap = args[1];
    worldTerrain = args[2];
    worldRoot = args.Length >= 4 ? args[3] : null;
}
else if (args.Length >= 1 && args[0] == "--anim")
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("usage: SiegeFX.Runtime --anim <rigged.asp> <clip.prs> [texture.raw]");
        return 1;
    }
    animAsp = args[1];
    animPrs = args[2];
    animTexture = args.Length >= 4 ? args[3] : null;
}
else
{
    meshPath    = args.Length > 0 ? args[0] : null;
    texturePath = args.Length > 1 ? args[1] : null;
}

using var host = new RenderHost(
    "SiegeFX  —  RMB+WASD to fly, Shift to sprint, Esc to quit",
    meshPath: meshPath,
    texturePath: texturePath,
    regionMapTankPath: regionMap,
    regionTerrainTankPath: regionTerrain,
    regionPath: regionPath,
    worldMapTankPath: worldMap,
    worldTerrainTankPath: worldTerrain,
    worldRootHint: worldRoot,
    animAspPath: animAsp,
    animPrsPath: animPrs,
    animTexturePath: animTexture);
host.Run();
return 0;
