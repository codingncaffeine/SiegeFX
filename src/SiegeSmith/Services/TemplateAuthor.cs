using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace SiegeSmith.Services;

/// <summary>GAME-3 — writes custom ContentDB templates (items, monsters, NPCs)
/// for a new game. Each template SPECIALIZES a shipped base — the DS1 modding
/// idiom — so the inheritance chain supplies mesh, animation, sounds, and AI,
/// and the custom file overrides only identity and stats. Files land under
/// <c>world/contentdb/templates/custom/</c> in the assets folder; the map
/// packager bundles that tree into the map tank, and the SiegeFX engine merges
/// map-tank templates on load (the SS-CUSTOM path), so created content plays.</summary>
public static class TemplateAuthor
{
    public enum Kind { Weapon, Armor, Monster, Npc }

    public sealed class Spec
    {
        public Kind Kind;
        public string Name = "";        // template identifier (lowercase)
        public string Base = "";        // specializes target
        public string ScreenName = "";  // in-game display name
        public int DamageMin = 4;       // weapon / monster melee
        public int DamageMax = 9;
        public int Defense = 10;        // armor
        public int Life = 50;           // monster
    }

    /// <summary>Compose the template GAS text.</summary>
    public static string Compose(Spec s)
    {
        var sb = new StringBuilder();
        sb.Append($"[t:template,n:{s.Name}]\r\n{{\r\n");
        sb.Append($"\tspecializes = {s.Base};\r\n");
        if (!string.IsNullOrWhiteSpace(s.ScreenName))
        {
            sb.Append("\t[common]\r\n\t{\r\n");
            sb.Append($"\t\tscreen_name = \"{s.ScreenName.Replace("\"", "'")}\";\r\n");
            sb.Append("\t}\r\n");
        }
        switch (s.Kind)
        {
            case Kind.Weapon:
                sb.Append("\t[attack]\r\n\t{\r\n");
                sb.Append($"\t\tdamage_min = {s.DamageMin.ToString(CultureInfo.InvariantCulture)};\r\n");
                sb.Append($"\t\tdamage_max = {Math.Max(s.DamageMin, s.DamageMax).ToString(CultureInfo.InvariantCulture)};\r\n");
                sb.Append("\t}\r\n");
                break;
            case Kind.Armor:
                sb.Append("\t[defend]\r\n\t{\r\n");
                sb.Append($"\t\tdefense = {s.Defense.ToString(CultureInfo.InvariantCulture)};\r\n");
                sb.Append("\t}\r\n");
                break;
            case Kind.Monster:
                sb.Append("\t[aspect]\r\n\t{\r\n");
                sb.Append($"\t\tlife = {s.Life.ToString(CultureInfo.InvariantCulture)};\r\n");
                sb.Append($"\t\tmax_life = {s.Life.ToString(CultureInfo.InvariantCulture)};\r\n");
                sb.Append("\t}\r\n");
                sb.Append("\t[attack]\r\n\t{\r\n");
                sb.Append($"\t\tdamage_min = {s.DamageMin.ToString(CultureInfo.InvariantCulture)};\r\n");
                sb.Append($"\t\tdamage_max = {Math.Max(s.DamageMin, s.DamageMax).ToString(CultureInfo.InvariantCulture)};\r\n");
                sb.Append("\t}\r\n");
                break;
            case Kind.Npc:
                // Identity only — the base supplies everything; conversations
                // bind through the World Builder's Logic tools.
                break;
        }
        sb.Append("}\r\n");
        return sb.ToString();
    }

    /// <summary>Write the template into the assets tree (one file per template,
    /// hand-editable afterwards). Returns the file path.</summary>
    public static string Write(string assetsFolder, Spec s)
    {
        var dir = Path.Combine(assetsFolder, "world", "contentdb", "templates", "custom");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, s.Name + ".gas");
        File.WriteAllText(path, Compose(s));
        return path;
    }

    /// <summary>Lowercase identifier from arbitrary input.</summary>
    public static string SanitizeName(string raw)
    {
        var sb = new StringBuilder();
        foreach (var ch in raw.Trim().ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
        return sb.Length > 0 ? sb.ToString() : "custom_template";
    }
}
