// The game's own settings, changed from the launcher: the [cvars] of benilla-config\config.toml, the file the game
// keeps the settings you changed in (it reads it when it starts, and keeps hand edits). The names, values and
// defaults below are the game's own (from its options window): gxWindow 0 is "Windowed (Fullscreen)", the
// borderless window over the whole screen (there is no exclusive fullscreen), 1 an ordinary window of gxResolution.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AzerothLauncher
{
    public class GameSetting
    {
        public string Cvar, Label, Note, Default, Group;
        public string[] Values, Names;                        // the choices: what is written, and what is shown
        public bool Check;                                    // on ("1") or off ("0")
    }

    public static class GameSettings
    {
        public static readonly List<GameSetting> All = new List<GameSetting>();
        static string group = "";
        static void Group(string name) { group = name; }

        static void Add(string cvar, string label, string def, string note, string values, string names)
        {
            var s = new GameSetting();
            s.Cvar = cvar; s.Label = label; s.Default = def; s.Note = note; s.Group = group;
            s.Values = values.Split('|');
            s.Names = names.Split('|');
            All.Add(s);
        }

        static void Check(string cvar, string label, string def, string note)
        {
            var s = new GameSetting();
            s.Cvar = cvar; s.Label = label; s.Default = def; s.Note = note; s.Check = true; s.Group = group;
            All.Add(s);
        }

        // in groups, with the game's own names and descriptions (from its options window)
        static GameSettings()
        {
            Group("DISPLAY");
            Add("gxWindow", "Display mode", null, "Windowed (Fullscreen) fills the screen with a borderless window; Windowed runs in an ordinary one.",
                "0|1", "WINDOWED (FULLSCREEN)|WINDOWED");
            Add("gxResolution", "Window size (Windowed)", "1600x900", "The size of the game's window when it runs Windowed.",
                "1280x720|1366x768|1600x900|1920x1080|2560x1440|3840x2160", "1280×720|1366×768|1600×900|1920×1080|2560×1440|3840×2160");
            Check("gxMaximize", "Maximize the game window", null, "Opens the window as big as the screen allows.");
            Check("gxVSync", "Vertical sync", "1", "Matches the frame rate to your monitor. Turn it on if you see the picture tearing.");
            Add("uiScale", "UI scale", "1.0", "The size of the game's interface.", "0.64|0.75|0.85|1.0", "64%|75%|85%|100%");
            Add("gamma", "Brightness", "1.0", "Lightens or darkens the whole picture: nights, caves and interiors are what it is for.",
                "0.8|1.0|1.2|1.4|1.6", "80%|100%|120%|140%|160%");

            Group("GRAPHICS");
            Add("renderScale", "Render scale", "1", "Below 100% trades sharpness for frame rate; above 100% smooths edges at the cost of frame rate.",
                "0.5|0.75|1|1.5|2", "50%|75%|100%|150%|200%");
            Add("gxMultisample", "Anti-aliasing (multisampling)", null, "Smooths jagged edges at some cost in frame rate. Takes effect the next time the game starts.",
                "1|2|4|8", "OFF|2x|4x|8x");
            Add("anisotropic", "Anisotropic filtering", null, "Keeps the ground and walls sharp when seen at a slant. Takes effect the next time the game starts.",
                "1|2|4|8|16", "OFF|2x|4x|8x|16x");
            Check("trilinear", "Trilinear filtering", null, "Smooths the steps between a texture's levels of detail. Takes effect the next time the game starts.");
            Add("farclip", "Terrain distance", "350", "How far you can see terrain. Lower it for a faster game.", "177|350|577|777", "177|350|577|777");
            Add("WorldDetail", "Environment detail", null, "How far you can see objects. Lower it for a faster game.", "0|1|2", "LOW|MEDIUM|HIGH");
            Add("spellEffectLevel", "Spell detail", null, "The detail of spell effects. Lower it for a faster game.", "0|1|2", "LOW|MEDIUM|HIGH");
            Add("weatherDensity", "Weather intensity", null, "How strong rain and snow are. Lower it for a faster game.", "0|1|2|3", "MINIMAL|LOW|HIGH|FULL");
            Check("noSpellEffects", "No spell effects", "0", "Draws no explosions, missiles, glows or beams. Spells still sound. For a slow computer.");
            Check("cardRays", "Level-Up Rays", null, "Turns the spinning rays behind the level-up and jewelry cards on or off.");
            Check("ffxGlow", "Full screen glow", "1", "A soft glow around the bright parts of the picture. Off for a sharper picture or a slower computer.");
            Check("ffxDeath", "Death effect", "1", "Turns the world grey while your hero is dead.");

            Group("SOUND");
            Check("MasterSoundEffects", "Enable all sound", "1", null);
            Add("MasterVolume", "Master volume", "1.0", null, "0|0.1|0.25|0.5|0.75|1.0", "0%|10%|25%|50%|75%|100%");
            Add("SoundVolume", "Sound effects volume", "1.0", null, "0|0.1|0.25|0.5|0.75|1.0", "0%|10%|25%|50%|75%|100%");
            Add("MusicVolume", "Music volume", null, null, "0|0.1|0.25|0.5|0.75|1.0", "0%|10%|25%|50%|75%|100%");
            Add("AmbienceVolume", "Ambience volume", null, null, "0|0.1|0.25|0.5|0.75|1.0", "0%|10%|25%|50%|75%|100%");
            Check("EnableMusic", "Music", "1", null);
            Check("SoundZoneMusicNoDelay", "Loop music", "0", "Starts the next track as soon as one ends, instead of after a stretch of silence.");
            Check("EnableAmbience", "Ambience", "1", null);
            Check("EnableErrorSpeech", "Error speech", "1", "Speech for errors, such as \"Target out of range\".");
            Check("EmoteSounds", "Emote sounds", "1", "The voices of emotes such as /cheer and /laugh.");
            Check("Sound_EnableSoundWhenGameIsInBG", "Sound when the game is in the background", "0", "Keeps the sound playing while you work in another window.");

            Group("GAMEPLAY");
            Check("storeLevels", "Store level-ups", "0", "Level-ups no longer stop the fight: a Level Up! button over your action bars opens them when you are ready.");
            Check("cacheOnClick", "Open caches by clicking", "0", "Caches are bought only when you right-click them, not when you walk over them.");
            Check("meterOnTop", "Damage meter on top", "0", "Keeps the damage meter over the level-up and jewelry cards.");
            Check("hudClock", "Show run clock", "1", "The run's clock and the time to the next boss, at the top of the screen.");
            Check("hudBosses", "Show boss bars", "1", "The health bars of the bosses and awake guardians you fight.");
            Check("hudTally", "Show kills and gold", "1", "The box with your kills and gold beside the minimap.");
            Check("hudBuild", "Show build", "1", "Your spells and jewelry with their cooldowns.");
            Check("hudBanners", "Show banners", "1", "The messages in the middle of the screen, such as a boss arriving.");
            Check("hudLoot", "Show loot notices", "1", "The notice of a piece of gear a boss dropped.");
            Check("hudMeter", "Show damage meter", "1", "The damage meter's minimap button and its window.");
        }

        // where the game keeps it: the game config folder (benilla-config)
        public static string FileIn(string configDir)
        {
            return string.IsNullOrEmpty(configDir) ? null : Path.Combine(configDir, "config.toml");
        }

        static readonly Regex Line = new Regex("^\\s*([A-Za-z0-9_]+)\\s*=\\s*\"([^\"]*)\"\\s*$");

        public static Dictionary<string, string> Read(string file)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (file == null || !File.Exists(file)) return d;
            bool cvars = false;
            foreach (string raw in File.ReadAllLines(file))
            {
                string line = raw.Trim();
                if (line.StartsWith("[")) { cvars = line == "[cvars]"; continue; }
                Match m = Line.Match(line);
                if (cvars && m.Success) d[m.Groups[1].Value] = m.Groups[2].Value;
            }
            return d;
        }

        // the value now: the file's, else the game's default (null: the game decides)
        public static string Value(Dictionary<string, string> d, GameSetting s)
        {
            string v;
            return d.TryGetValue(s.Cvar, out v) ? v : s.Default;
        }

        // one setting written into the file, everything else in it kept as it is
        public static void Set(string file, string cvar, string value)
        {
            if (file == null) throw new UserError("The game config folder is not set. Set it in Options > FOLDERS.");
            var lines = File.Exists(file) ? new List<string>(File.ReadAllLines(file)) : new List<string>();
            string entry = cvar + " = \"" + value.Replace("\"", "") + "\"";
            int section = -1, end = lines.Count;
            for (int i = 0; i < lines.Count; i++)
            {
                string t = lines[i].Trim();
                if (t.StartsWith("["))
                {
                    if (section >= 0) { end = i; break; }
                    if (t == "[cvars]") section = i;
                    continue;
                }
                Match m = Line.Match(t);
                if (section >= 0 && m.Success && string.Equals(m.Groups[1].Value, cvar, StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = entry;
                    Write(file, lines);
                    return;
                }
            }
            if (section < 0)
            {
                if (lines.Count == 0) lines.Add("# benilla local config — CVar values that moved off their defaults.");
                lines.Add("[cvars]");
                lines.Add(entry);
            }
            else
            {
                while (end > section + 1 && lines[end - 1].Trim().Length == 0) end--;   // before the blank lines at its end
                lines.Insert(end, entry);
            }
            Write(file, lines);
        }

        static void Write(string file, List<string> lines)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            string tmp = file + ".tmp";
            File.WriteAllText(tmp, string.Join("\n", lines.ToArray()) + "\n", new UTF8Encoding(false));
            Util.Replace(tmp, file);
        }
    }
}
