using SiegeFX.Runtime.Render;

// Invocation shapes:
//   SiegeFX.Runtime [mesh.sno|mesh.asp] [texture.raw | tank.dsres]
//   SiegeFX.Runtime --region <map-tank> <terrain-tank> <region-path>
//   SiegeFX.Runtime --world  <map-tank> <terrain-tank> [root-region]
//   SiegeFX.Runtime --anim   <rigged.asp> <clip.prs> [texture.raw]
//   SiegeFX.Runtime --skrit-anim <rigged.asp> <skrit> <clip0.prs> [clip1.prs ...] [--texture <raw>]
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
string? skritPath = null;
List<string>? skritClips = null;

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
else if (args.Length >= 1 && args[0] == "--skrit-anim")
{
    // Phase 9a. Rigged ASP + skrit that decides which clip plays. Optional trailing
    // `--texture <raw>` binds albedo (keeps the positional list of clips unambiguous).
    if (args.Length < 4)
    {
        Console.Error.WriteLine("usage: SiegeFX.Runtime --skrit-anim <rigged.asp> <skrit> <clip0.prs> [clip1.prs ...] [--texture <raw>]");
        return 1;
    }
    animAsp = args[1];
    skritPath = args[2];
    skritClips = new List<string>();
    for (int i = 3; i < args.Length; i++)
    {
        if (args[i] == "--texture")
        {
            if (i + 1 >= args.Length) { Console.Error.WriteLine("--texture needs a value"); return 1; }
            animTexture = args[i + 1];
            i++;
        }
        else skritClips.Add(args[i]);
    }
    if (skritClips.Count == 0) { Console.Error.WriteLine("--skrit-anim requires at least one clip"); return 1; }
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
    animTexturePath: animTexture,
    skritPath: skritPath,
    skritClipPaths: skritClips);
host.Run();
return 0;
