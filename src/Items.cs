// The game's items: every class's gear in each rarity, and the rings, trinkets and amulets.
// assets/items.txt is read from the game itself (tools/game_items.py): the jewelry with its numbers, the gear's names,
// the drop chances. The gear's bonuses are not in the game's files (it works them out as it runs): assets/gear-stats.txt
// holds those, entered from the game's own item cards; a piece not in it has its bonuses "not known yet".
// Used by the Guide's "Items" topic and the Gear Planner.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AzerothLauncher
{
    // one bonus: a stat of the character sheet and its amount (a percent as its percent: 5.1 is 5.1%)
    public class ItemBonus
    {
        public string Stat = "";                             // damage, cooldown, health%, crit, ... (see Items.StatNames)
        public double Value;
        public string Text = "";                             // as the item card says it
    }

    public class GearPiece
    {
        public string Class = "", Rarity = "", Slot = "", Name = "", Kind = "";
        public double Chance;                                // 0.2 = 20%
        public List<string> Drops = new List<string>();      // "Kurinnaxx · Gates of Ahn'Qiraj · 4:00"
        public int Health;
        public List<ItemBonus> Bonuses;                      // null: not known yet
        public bool Known { get { return Bonuses != null; } }
    }

    public class Jewel
    {
        public string Key = "", Name = "", Icon = "", Kind = "";   // Kind: Ring, Trinket, Amulet
        public string Class = "", Spell = "";
        public bool IsCodex;
        public List<ItemBonus> Bonuses = new List<ItemBonus>();
        public List<string> Lines = new List<string>();      // every line, the special effects too
    }

    public static class Items
    {
        public static readonly string[] Classes = { "Warrior", "Paladin", "Hunter", "Rogue", "Priest", "Shaman", "Mage", "Warlock", "Druid" };
        public static readonly string[] Rarities = { "Common", "Uncommon", "Rare", "Epic", "PvP", "Legendary" };
        public static readonly string[] Slots = { "Head", "Shoulder", "Chest", "Waist", "Legs", "Feet", "Wrist", "Hands", "Main Hand" };
        static readonly string[] WithOffHand = { "Head", "Shoulder", "Chest", "Waist", "Legs", "Feet", "Wrist", "Hands", "Main Hand", "Off Hand" };

        // the zones of each rarity (the Guide's Zones & Bosses): their bosses in order, and the slot each boss drops.
        // The first zone of a pair drops Head, Shoulder, Chest and Waist, the second Legs, Feet, Wrist and Hands; both
        // final bosses drop the Main Hand.
        static readonly string[][] Zones =
        {
            new[] { "Common", "Elwynn Forest", "Hogger 3:00", "Mother Fang 6:00", "Rhahk'Zor 9:00", "Mr. Smite 12:00", "Edwin VanCleef 16:00" },
            new[] { "Common", "Duskwood", "Naraxis 3:00", "Mor'Ladim 6:00", "Stitches 9:00", "Lupos 12:00", "Morbent Fel 16:00" },
            new[] { "Uncommon", "Warsong Gulch", "Doomguard Commander", "Mannoroth's Herald", "Varimathras", "Lord Hel'nurath", "Lord Kazzak 16:00" },
            new[] { "Uncommon", "Un'Goro Crater", "Ravasaur Matriarch 3:00", "Uhk'loc 6:00", "Lar'korwi 9:00", "Tyrant Devilsaur 12:00", "King Mosh 16:00" },
            new[] { "Rare", "Arathi Basin", "Kovork 4:00", "Nimar the Slayer 7:00", "Marez Cowl 11:00", "Lord Falconcrest 14:00", "Lord Aliden Perenolde 18:00" },
            new[] { "Rare", "Feralas", "Zevrim Thornhoof 4:00", "Alzzin the Wildshaper 7:00", "Tendris Warpwood 11:00", "Immol'thar 14:00", "King Gordok 18:00" },
            new[] { "Epic", "Gurubashi Arena", "High Priestess Jeklik 4:00", "High Priest Venoxis 7:00", "Bloodlord Mandokir 11:00", "Jin'do the Hexxer 14:00", "Hakkar the Soulflayer 18:00" },
            new[] { "Epic", "Scarlet Monastery", "Houndmaster Loksey 4:00", "Arcanist Doan 7:00", "Herod 11:00", "Scarlet Commander Mograine 14:00", "High Inquisitor Whitemane 18:00" },
            new[] { "PvP", "Alterac Valley", "Korrak the Bloodrager 4:00", "Ivus the Forest Lord 8:00", "Lokholar the Ice Lord 12:00", "Glacius the Frozen Titan 16:00", "Azuregos 20:00" },
            new[] { "PvP", "Blackrock Mountain", "General Angerforge 4:00", "Warchief Rend Blackhand 8:00", "Emperor Dagran Thaurissan 12:00", "Majordomo Executus 16:00", "Nefarian 20:00" },
            new[] { "Legendary", "Gates of Ahn'Qiraj", "Kurinnaxx 4:00", "General Rajaxx 8:00", "Princess Huhuran 12:00", "Ossirian the Unscarred 16:00", "C'Thun 20:00" },
            new[] { "Legendary", "Naxxramas", "Anub'Rekhan 4:00", "Noth the Plaguebringer 8:00", "Patchwerk 12:00", "Sapphiron 16:00", "Kel'Thuzad 20:00" },
        };
        // every boss's name ("Mr. Smite", "High Priest Venoxis"): kept whole and plain in the Guide's text, so a spell or
        // class inside a boss's name is not taken for the spell or the class
        public static IEnumerable<string> BossNames()
        {
            foreach (string[] z in Zones)
                for (int i = 2; i < z.Length; i++)
                {
                    string n = z[i];
                    int sp = n.LastIndexOf(' ');
                    if (sp > 0 && n.IndexOf(':', sp) > 0) n = n.Substring(0, sp);
                    yield return n;
                }
        }

        static readonly string[][] ZoneSlots =
        {
            new[] { "Head", "Shoulder", "Chest", "Waist", "Main Hand" },
            new[] { "Legs", "Feet", "Wrist", "Hands", "Main Hand" },
        };

        // the character sheet's stats, as the game names them, and how a bonus to each is written
        public static readonly string[][] StatNames =
        {
            new[] { "damage", "Damage", "%" }, new[] { "cooldown", "Haste", "%" }, new[] { "health%", "Health", "%" },
            new[] { "crit", "Crit", "%" }, new[] { "critdmg", "Crit Dmg", "%" }, new[] { "armor", "Armor", "%" },
            new[] { "regen", "Regen", "" }, new[] { "speed", "Speed", "%" }, new[] { "area", "Area", "%" },
            new[] { "pickup", "Pickup", "%" }, new[] { "xp", "Experience", "%" }, new[] { "gold", "Gold", "%" },
            new[] { "luck", "Luck", "%" }, new[] { "echo", "Echo", "%" }, new[] { "lifesteal", "Lifesteal", "%" },
            new[] { "killheal", "Kill Heal", "" }, new[] { "targets", "Targets", "" }, new[] { "thorns", "Thorns", "%" },
            new[] { "buff", "Buff Power", "%" }, new[] { "lives", "Lives", "" }, new[] { "dothaste", "DoT Haste", "%" },
            new[] { "health", "Health", "" },
        };
        // the game's stat numbers (its own order, items.txt @stat) -> the sheet's stat
        static readonly string[] GameStat = { "damage", "cooldown", "health%", "crit", "critdmg", "armor", "regen", "speed", "area",
            "pickup", "xp", "gold", "luck", "echo", "lifesteal", "killheal", "", "", "", "", "", "", "thorns", "buff" };

        static List<GearPiece> gear;
        static List<Jewel> jewels;
        static readonly string[] CodexRows =
        {
            "Druid|Starfire|Eye of the Splitting Sky|Starfire: +1 target~Starfire: +20% damage~Starfire's hits leap to 2 more enemies for 30%",
            "Druid|Rejuvenation|The Eternal Talisman|Rejuvenation: +26% healing~Rejuvenation: -35% cooldown~While Rejuvenation heals you, 5% of the damage you deal heals you too",
            "Druid|Shred|Medallion of the Bladestorm|Shred: +4 targets~Shred: +50% damage~Shred's hits leap to 2 more enemies for 50%",
            "Warlock|Drain Life|Pendant of the Plague|Drain Life: +3 targets~Drain Life: +90% damage~Enemies that die with Drain Life on them explode for 20% of their max health, at most 2 times a second",
            "Warlock|Death Coil|Eye of the Splitting Sky|Death Coil: +1 target~Death Coil: +20% damage~Death Coil's hits leap to 2 more enemies for 30%",
            "Warlock|Hellfire|Heart of the Earthshaker|Hellfire: +60% area~Hellfire: +35% damage~Hellfire's hits stun for 1.5 sec",
            "Warlock|Immolate|Pendant of the Plague|Immolate: +3 targets~Immolate: +90% damage~Enemies that die with Immolate on them explode for 20% of their max health, at most 2 times a second",
            "Mage|Fireball|Eye of the Splitting Sky|Fireball: +1 target~Fireball: +20% damage~Fireball's hits leap to 2 more enemies for 30%",
            "Mage|Arcane Missiles|Stormcaller's Amulet|Arcane Missiles: +4 targets~Arcane Missiles: +20% damage~30% chance for Arcane Missiles's hits to strike again for 100%",
            "Mage|Flamestrike|Amulet of the Inferno|Flamestrike: +50% area~Flamestrike: +100% duration~Flamestrike: +50% damage~Enemies Flamestrike kills explode for 30% of their max health, at most 2 times a second",
            "Mage|Ice Barrier|The Eternal Talisman|Ice Barrier: +26% absorb~Ice Barrier: -35% cooldown~Ice Barrier: +100% absorb~A broken shield bursts for 50% of what it absorbed on enemies within 10 yd",
            "Shaman|Healing Wave|The Eternal Talisman|Healing Wave: +26% healing~Healing Wave: -35% cooldown~Healing Wave also shields you for 40% of your max health",
            "Shaman|Lightning Shield|The Eternal Talisman|Lightning Shield: +60% damage~25% chance to stun a melee attacker for 1.5 sec",
            "Shaman|Earth Shock|Eye of the Splitting Sky|Earth Shock: +1 target~Earth Shock: +20% damage~Earth Shock's hits leap to 2 more enemies for 30%",
            "Priest|Shadow Word: Pain|Pendant of the Plague|Shadow Word: Pain: +3 targets~Shadow Word: Pain: +90% damage~Enemies that die with Shadow Word: Pain on them explode for 20% of their max health, at most 2 times a second",
            "Priest|Mind Blast|Eye of the Splitting Sky|Mind Blast: +1 target~Mind Blast: +20% damage~Mind Blast's hits leap to 2 more enemies for 30%",
            "Priest|Holy Fire|Eye of the Splitting Sky|Holy Fire: +1 target~Holy Fire: +20% damage~Holy Fire's hits leap to 2 more enemies for 30%",
            "Priest|Psychic Scream|Heart of the Earthshaker|Psychic Scream: +60% area~Psychic Scream: +35% damage~Psychic Scream's hits stun for 1.5 sec",
            "Priest|Lightwell|Charm of the Fated Hour|Lightwell: +60% damage~Lightwell's hits leap to 3 more enemies for 30%",
            "Rogue|Seal Fate|Charm of the Fated Hour|Seal Fate: +60% damage~Seal Fate: +100% proc rate",
            "Rogue|Ghostly Strike|Medallion of the Bladestorm|Ghostly Strike: +4 targets~Ghostly Strike: +50% damage~Ghostly Strike: +100% absorb",
            "Rogue|Kidney Shot|Medallion of the Bladestorm|Kidney Shot: +4 targets~Kidney Shot: +50% damage~Kidney Shot: +100% stun duration",
            "Rogue|Sinister Strike|Medallion of the Bladestorm|Sinister Strike: +4 targets~Sinister Strike: +50% damage~Sinister Strike's hits burn and bleed for 80% more over 6 sec",
            "Hunter|Volley|Amulet of the Inferno|Volley: +50% area~Volley: +100% duration~Volley: +50% damage~Enemies Volley kills explode for 30% of their max health, at most 2 times a second",
            "Hunter|Concussive Shot|Eye of the Splitting Sky|Concussive Shot: +1 target~Concussive Shot: +20% damage~Concussive Shot's hits leap to 2 more enemies for 30%",
            "Hunter|Hunter's Mark|Charm of the Fated Hour|Hunter's Mark: +60% damage~Hunter's Mark: +40% area",
            "Paladin|Divine Shield|The Eternal Talisman|Divine Shield: +100% duration~Divine Shield: -35% cooldown~Enemies you kill during Divine Shield explode for 20% of their max health, at most 2 times a second",
            "Paladin|Holy Wrath|Heart of the Earthshaker|Holy Wrath: +60% area~Holy Wrath: +35% damage~Holy Wrath's hits stun for 1.5 sec",
            "Paladin|Exorcism|Eye of the Splitting Sky|Exorcism: +1 target~Exorcism: +20% damage~Exorcism's hits leap to 2 more enemies for 30%",
            "Warrior|Whirlwind|Heart of the Earthshaker|Whirlwind: +60% area~Whirlwind: +35% damage~Whirlwind's hits stun for 1.5 sec",
            "Warrior|Execute|The Reaper's Amulet|Execute: +45% damage~A kill with Execute readies it again at once~Execute's hits splash 50% on enemies within 6 yd",
            "Warrior|Bloodthirst|Medallion of the Bladestorm|Bloodthirst: +4 targets~Bloodthirst: +50% damage~Bloodthirst's hits burn and bleed for 80% more over 6 sec~Bloodthirst also shields you for 30% of your max health",
            "Warrior|Rend|Pendant of the Plague|Rend: +3 targets~Rend: +90% damage~Enemies that die with Rend on them explode for 20% of their max health, at most 2 times a second"
        };
        static Dictionary<string, string> chance;

        public static List<GearPiece> Gear { get { Load(); return gear; } }
        public static List<Jewel> Jewelry { get { Load(); return jewels; } }

        public static string Pct(double v)
        {
            return v.ToString(Math.Abs(v - Math.Round(v)) < 0.05 ? "0" : "0.#", CultureInfo.InvariantCulture);
        }

        static double Num(string s)
        {
            double v;
            return double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        static string Cap(string s) { return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1); }

        // a bonus as the item card says it ("Equip: Increases all damage you deal by 39%.")
        public static string BonusText(string stat, double v)
        {
            switch (stat)
            {
                case "damage": return "Increases all damage you deal by " + Pct(v) + "%.";
                case "cooldown": return "Reduces the cooldown of your spells by " + Pct(v) + "%.";
                case "health%": return "Increases your maximum health by " + Pct(v) + "%.";
                case "crit": return "Improves your chance to get a critical strike by " + Pct(v) + "%.";
                case "critdmg": return "Increases the damage of your critical strikes by " + Pct(v) + "%.";
                case "armor": return "Reduces all damage taken by " + Pct(v) + "%.";
                case "regen": return "Restores " + Pct(v) + " health per second.";
                case "speed": return "Increases your movement speed by " + Pct(v) + "%.";
                case "area": return "Increases the area of effect of your spells by " + Pct(v) + "%.";
                case "pickup": return "Increases the range you pick up loot from by " + Pct(v) + "%.";
                case "xp": return "Increases experience gained by " + Pct(v) + "%.";
                case "gold": return "Increases gold found by " + Pct(v) + "%.";
                case "luck": return "Increases your luck by " + Pct(v) + "%.";
                case "echo": return "Gives your spells a " + Pct(v) + "% chance to cast twice.";
                case "lifesteal": return "Heals you for " + Pct(v) + "% of all damage you deal.";
                case "killheal": return "Each enemy you kill restores " + Pct(v) + " health.";
                case "targets": return "Your spells strike " + Pct(v) + " more target" + (v == 1 ? "" : "s") + ".";
                case "thorns": return "Melee attackers take " + Pct(v) + "% of the damage they deal you.";
                case "buff": return "Your damage, haste, shield and thorns buffs are " + Pct(v) + "% stronger.";
                case "lives": return "+" + Pct(v) + " return to life at half health.";
                case "dothaste": return "Your damage over time spells tick " + Pct(v) + "% faster.";
            }
            return stat + " " + Pct(v);
        }

        // a ring's or trinket's text from the game -> the sheet's stat
        static string StatOfText(string t)
        {
            t = t.ToLowerInvariant();
            if (t.Contains("return to life")) return "lives";
            if (t.Contains("tick")) return "dothaste";
            if (t.Contains("heals you")) return "lifesteal";
            if (t.Contains("each kill heals")) return "killheal";
            if (t.Contains("buffs are")) return "buff";
            if (t.Contains("cast twice")) return "echo";
            if (t.Contains("critical strike damage")) return "critdmg";
            if (t.Contains("critical strike")) return "crit";
            if (t.StartsWith("+{v}% damage reduction")) return "armor";
            if (t.StartsWith("+{v}% max health")) return "health%";
            if (t.Contains("cooldown")) return "cooldown";
            if (t.Contains("health per second")) return "regen";
            if (t.Contains("movement speed")) return "speed";
            if (t.Contains("area of effect")) return "area";
            if (t.Contains("pickup")) return "pickup";
            if (t.Contains("experience")) return "xp";
            if (t.Contains("gold")) return "gold";
            if (t.Contains("luck")) return "luck";
            if (t.Contains("damage")) return "damage";
            return "";
        }

        static void Load()
        {
            if (gear != null) return;
            var g = new List<GearPiece>();
            var j = new List<Jewel>();
            chance = new Dictionary<string, string>();
            var sets = new Dictionary<string, string[]>();   // "priest|Legendary" -> { name, of }
            var nouns = new Dictionary<string, string>();    // "cloth|Head" -> Circlet
            var weapons = new Dictionary<string, string>();  // "priest|0|Legendary" -> Anathema
            var armor = new Dictionary<string, string>();
            var pvp = new Dictionary<string, string[]>();    // "priest" -> { Alliance, Horde, weapon (Alliance) ... }
            var statText = new List<string>();
            var amuletLines = new List<string[]>();          // read after the stats' texts (they come later in the file)
            string section = null;
            bool on = false;
            try
            {
                foreach (string raw in Util.ReadResource("AS.items.txt").Replace("\r\n", "\n").Split('\n'))
                {
                    string line = raw.Trim();
                    if (line == "===") { on = true; continue; }
                    if (!on || line.Length == 0) continue;
                    if (line.StartsWith("@")) { section = line.Substring(1).Trim(); continue; }
                    string[] f = line.Split('|');
                    for (int i = 0; i < f.Length; i++) f[i] = f[i].Trim();
                    switch (section)
                    {
                        case "jewelry":
                            if (f.Length < 7) break;
                            {
                                var jw = new Jewel { Key = f[0], Name = f[1], Icon = f[2], Kind = Cap(f[3]) };
                                double v = Num(f[5]);
                                string t = f[6];
                                bool pct = t.Contains("{v}%");
                                string shown = t.Replace("{v}", pct ? Pct(v * 100) : Pct(v)).Replace("{max}", "");
                                shown = System.Text.RegularExpressions.Regex.Replace(shown, @", \+\{h\}% ([a-z ]+)$", " and some $1");
                                jw.Lines.Add(shown);
                                string st = StatOfText(t);
                                if (st.Length > 0) jw.Bonuses.Add(new ItemBonus { Stat = st, Value = pct ? v * 100 : v, Text = shown });
                                j.Add(jw);
                            }
                            break;
                        case "stat": if (f.Length >= 2) statText.Add(f[1]); break;
                        case "amulet": if (f.Length >= 4) amuletLines.Add(f); break;
                        case "set": if (f.Length >= 4) sets[f[0] + "|" + f[1]] = new[] { f[2], f[3] }; break;
                        case "noun": if (f.Length >= 3) nouns[f[0] + "|" + f[1]] = f[2]; break;
                        case "weapon": if (f.Length >= 4) weapons[f[0] + "|" + f[1] + "|" + f[2]] = f[3]; break;
                        case "armor": if (f.Length >= 2) armor[f[0]] = f[1]; break;
                        case "chance": if (f.Length >= 2) chance[f[0]] = f[1]; break;
                        case "pvp":
                            if (f.Length >= 4)
                            {
                                var l = new List<string> { f[1], f[2] };
                                for (int i = 3; i < f.Length; i++) l.Add(f[i].Split('/')[0].Trim());
                                pvp[f[0]] = l.ToArray();
                            }
                            break;
                    }
                }
            }
            catch (Exception) { }
            foreach (string[] f in amuletLines)
            {
                var jw = new Jewel { Key = f[0], Name = f[1], Icon = f[2], Kind = "Amulet" };
                for (int i = 4; i < f.Length; i++)
                {
                    int eq = f[i].IndexOf('=');
                    if (eq < 0) { jw.Lines.Add("A special effect of its own (see it in game)."); continue; }
                    int n = (int)Num(f[i].Substring(0, eq));
                    double v = Num(f[i].Substring(eq + 1));
                    string st = n < GameStat.Length ? GameStat[n] : "";
                    string tpl = n < statText.Count ? statText[n] : "";
                    bool pct = tpl.Contains("{p}") || (tpl.Length == 0 && v < 1);
                    double shownV = pct ? v * 100 : v;
                    string shown = tpl.Length > 0 ? tpl.Replace("{p}", Pct(shownV) + "%").Replace("{v}", Pct(v)) : BonusText(st, shownV);
                    jw.Lines.Add(shown);
                    if (st.Length > 0) jw.Bonuses.Add(new ItemBonus { Stat = st, Value = shownV, Text = shown });
                }
                j.Add(jw);
            }
            // Codex neck pieces, transcribed from the supplied in-game video: one entry per class/spell pair.
            foreach (string row in CodexRows)
            {
                string[] f = row.Split('|');
                if (f.Length < 4) continue;
                var jw = new Jewel { Key = "codex_" + f[0].ToLowerInvariant() + "_" + f[1].ToLowerInvariant().Replace(" ", "_"),
                    Name = f[2], Icon = f[1], Kind = "Amulet", Class = f[0], Spell = f[1], IsCodex = true };
                jw.Lines.Add("Legendary amulet - remakes " + jw.Spell);
                foreach (string effect in f[3].Split('~')) if (effect.Length > 0) jw.Lines.Add(effect);
                jw.Lines.Add("Offered only once your hero knows the spell.");
                jw.Lines.Add("Offered again while worn, it is empowered.");
                j.Add(jw);
            }
            // the bonuses entered from the game's item cards
            var known = new Dictionary<string, GearPiece>(StringComparer.OrdinalIgnoreCase);
            on = false;
            try
            {
                foreach (string raw in Util.ReadResource("AS.gear-stats.txt").Replace("\r\n", "\n").Split('\n'))
                {
                    string line = raw.Trim();
                    if (line == "===") { on = true; continue; }
                    if (!on || line.Length == 0 || line.StartsWith("#")) continue;
                    string[] f = line.Split('|');
                    if (f.Length < 5) continue;
                    var p = new GearPiece { Health = (int)Num(f[3]), Bonuses = new List<ItemBonus>() };
                    foreach (string part in f[4].Split(','))
                    {
                        int eq = part.IndexOf('=');
                        if (eq < 0) continue;
                        string st = part.Substring(0, eq).Trim().ToLowerInvariant();
                        double v = Num(part.Substring(eq + 1));
                        p.Bonuses.Add(new ItemBonus { Stat = st, Value = v, Text = BonusText(st, v) });
                    }
                    known[f[0].Trim() + "|" + f[1].Trim() + "|" + f[2].Trim()] = p;
                }
            }
            catch (Exception) { }
            // every class's pieces
            foreach (string cls in Classes)
            {
                string k = cls.ToLowerInvariant(), arm;
                if (!armor.TryGetValue(k, out arm)) arm = "cloth";
                foreach (string rar in Rarities)
                    foreach (string slot in WithOffHand)
                    {
                        var p = new GearPiece { Class = cls, Rarity = rar, Slot = slot };
                        if (slot == "Main Hand" || slot == "Off Hand")
                        {
                            // the second weapon (a rogue's off-hand dagger, a shaman's shield): weapons "|1|", the PvP list's 4th
                            bool off = slot == "Off Hand";
                            string[] pv;
                            string w;
                            if (rar == "PvP") p.Name = pvp.TryGetValue(k, out pv) && pv.Length > (off ? 3 : 2) ? pv[off ? 3 : 2] : "";
                            else p.Name = weapons.TryGetValue(k + (off ? "|1|" : "|0|") + rar, out w) ? w : "";
                            p.Kind = "Weapon";
                        }
                        else
                        {
                            string noun;
                            nouns.TryGetValue(arm + "|" + slot, out noun);
                            string[] set, pv;
                            if (rar == "PvP") p.Name = pvp.TryGetValue(k, out pv) ? pv[0] + " " + noun : "";
                            else if (sets.TryGetValue(k + "|" + rar, out set))
                                p.Name = set[1] == "1" ? noun + " of " + set[0] : set[0] + " " + noun;
                            if (rar == "PvP" && pvp.TryGetValue(k, out pv)) setNames[cls + "|" + rar] = pv[0];
                            else if (sets.TryGetValue(k + "|" + rar, out set)) setNames[cls + "|" + rar] = set[0];
                            p.Kind = Cap(arm);
                        }
                        if (string.IsNullOrEmpty(p.Name)) continue;
                        string c;
                        if (chance.TryGetValue(rar, out c)) p.Chance = Num(c);
                        foreach (var z in Zones)
                        {
                            if (z[0] != rar) continue;
                            int pair = Array.IndexOf(Zones, z) % 2;
                            int at = Array.IndexOf(ZoneSlots[pair], slot == "Off Hand" ? "Main Hand" : slot);   // dropped where the main hand is
                            if (at < 0) continue;
                            string boss = z[2 + at], time = "";
                            int sp = boss.LastIndexOf(' ');
                            if (sp > 0 && boss.Substring(sp + 1).Contains(":")) { time = boss.Substring(sp + 1); boss = boss.Substring(0, sp); }
                            p.Drops.Add(boss + " · " + z[1] + (time.Length > 0 ? " · " + time : ""));
                        }
                        GearPiece kp;
                        if (slot == "Main Hand" && k == "hunter") p.Slot = "Ranged";   // a hunter's bow or gun: the Ranged slot, as in the game
                        if (known.TryGetValue(k + "|" + rar + "|" + p.Slot, out kp)) { p.Health = kp.Health; p.Bonuses = kp.Bonuses; }
                        g.Add(p);
                    }
            }
            gear = g;
            jewels = j;
            LoadSets();
        }

        // ---- the sets (0.121.0): 4-piece and 8-piece bonuses and what each star adds (assets/gear-sets.txt)
        public class SetBonus
        {
            public string Text = "";
            public List<ItemBonus> Bonuses = new List<ItemBonus>();   // what the planner can add (4-piece only)
        }
        static readonly Dictionary<string, string> setNames = new Dictionary<string, string>();       // "Priest|Legendary" -> Faith
        static readonly Dictionary<string, SetBonus> fourPiece = new Dictionary<string, SetBonus>();   // rarity -> bonus
        static readonly Dictionary<string, string> eightPiece = new Dictionary<string, string>();      // "Priest|Legendary" -> text

        static void LoadSets()
        {
            string section = null;
            bool on = false;
            try
            {
                foreach (string raw in Util.ReadResource("AS.gear-sets.txt").Replace("\r\n", "\n").Split('\n'))
                {
                    string line = raw.Trim();
                    if (line == "===") { on = true; continue; }
                    if (!on || line.Length == 0 || line.StartsWith("#")) continue;
                    if (line.StartsWith("@")) { section = line.Substring(1).Trim(); continue; }
                    string[] f = line.Split('|');
                    for (int i = 0; i < f.Length; i++) f[i] = f[i].Trim();
                    if (section == "four" && f.Length >= 2)
                    {
                        var sb = new SetBonus { Text = f[1] };
                        if (f.Length >= 3)
                            foreach (string part in f[2].Split(','))
                            {
                                int eq = part.IndexOf('=');
                                if (eq < 0) continue;
                                string st = part.Substring(0, eq).Trim().ToLowerInvariant();
                                double v = Num(part.Substring(eq + 1));
                                sb.Bonuses.Add(new ItemBonus { Stat = st, Value = v, Text = BonusText(st, v) });
                            }
                        fourPiece[f[0]] = sb;
                    }
                    else if (section == "eight" && f.Length >= 3) eightPiece[Cap(f[0]) + "|" + f[1]] = f[2];
                }
            }
            catch (Exception) { }
        }

        // the real icons of the gear that are real World of Warcraft items (assets\item-icons.txt: name -> icon name, shown
        // from Wowhead's image host); a piece without one shows "Image soon"
        static Dictionary<string, string> gearIcons;

        public static Dictionary<string, string> GearIcons
        {
            get
            {
                if (gearIcons != null) return gearIcons;
                var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (string raw in Util.ReadResource("AS.item-icons.txt").Replace("\r\n", "\n").Split('\n'))
                    {
                        if (raw.StartsWith("#")) continue;
                        string[] f = raw.Split('|');
                        if (f.Length >= 3 && f[2].Trim().Length > 0) d[f[0].Trim()] = f[2].Trim().ToLowerInvariant();
                    }
                }
                catch (Exception) { }
                return gearIcons = d;
            }
        }

        // an item's real icon name by its name: a gear piece from the list above, or a ring, trinket or amulet's own; else null
        public static string IconFor(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string ic;
            if (GearIcons.TryGetValue(name.Trim(), out ic)) return ic;
            foreach (var j in Jewelry) if (string.Equals(j.Name, name.Trim(), StringComparison.OrdinalIgnoreCase) && j.Icon.Length > 0) return j.Icon.ToLowerInvariant();
            return null;
        }

        public static bool IsArmor(GearPiece p) { return p != null && p.Kind != "Weapon"; }

        public static string SetName(string cls, string rarity)
        {
            Load();
            string n;
            return setNames.TryGetValue(cls + "|" + rarity, out n) ? n : rarity;
        }

        public static SetBonus FourPiece(string rarity)
        {
            Load();
            SetBonus b;
            return fourPiece.TryGetValue(rarity, out b) ? b : null;
        }

        public static string EightPiece(string cls, string rarity)
        {
            Load();
            string t;
            return eightPiece.TryGetValue(cls + "|" + rarity, out t) ? t : null;
        }

        // Stars (0 to 5, every piece, weapons too) decide which set bonuses a set reaches. What a star adds to a piece's
        // own numbers, and what it costs, is not known yet: nothing here counts it.
        public const string StarsNotCounted = "Stars decide which set bonuses you reach. How much each star adds to a piece is not known yet, so it is not counted here.";

        public static List<GearPiece> GearOf(string cls, string slot)
        {
            var l = new List<GearPiece>();
            foreach (var p in Gear) if (p.Class == cls && p.Slot == slot) l.Add(p);
            return l;
        }

        public static List<Jewel> JewelryOf(string kind)
        {
            var l = new List<Jewel>();
            foreach (var j in Jewelry) if (j.Kind == kind) l.Add(j);
            return l;
        }

        // the card of a piece, as plain lines
        public static string Card(GearPiece p) { return Card(p, 0); }

        // the card at a number of stars (0 to 5; its numbers stay as they are), and its set's bonuses
        public static string Card(GearPiece p, int stars)
        {
            var b = new StringBuilder();
            b.Append(p.Name).Append('\n').Append(p.Rarity).Append(' ').Append(p.Kind == "Weapon" ? "" : p.Kind + " · ").Append(p.Slot);
            if (stars > 0) b.Append("\n").Append(Lang.T("Stars")).Append(": ").Append(stars).Append("/5");
            if (p.Known)
            {
                if (p.Health > 0) b.Append("\n+").Append(p.Health).Append(" Health");
                foreach (var x in p.Bonuses) b.Append("\nEquip: ").Append(x.Text);
            }
            else b.Append("\n").Append(Lang.T("Its bonuses are not known yet."));
            if (IsArmor(p))
            {
                b.Append("\n").Append(SetName(p.Class, p.Rarity)).Append(':');
                var four = FourPiece(p.Rarity);
                b.Append("\n  (4) ").Append(four != null ? four.Text : Lang.T("not entered yet")).Append(" [1+ ").Append(Lang.T("stars")).Append(']');
                string eight = EightPiece(p.Class, p.Rarity);
                b.Append("\n  (8) ").Append(eight ?? Lang.T("not entered yet")).Append(" [3+ ").Append(Lang.T("stars")).Append(']');
            }
            if (p.Drops.Count > 0)
            {
                b.Append("\n").Append(Lang.T("Drops from")).Append(':');
                foreach (string d in p.Drops) b.Append("\n  ").Append(d).Append(" · ").Append(Pct(p.Chance * 100)).Append('%');
            }
            return b.ToString();
        }

        public static string Card(Jewel j)
        {
            var b = new StringBuilder();
            b.Append(j.Name).Append('\n').Append(j.IsCodex ? "Legendary amulet - remakes " + j.Spell : Lang.T(j.Kind));
            foreach (string l in j.Lines) if (!j.IsCodex || !l.StartsWith("Legendary amulet - remakes ")) b.Append("\n").Append(l);
            return b.ToString();
        }

        // ---- the Guide's "Items" topic, written out from the data (guide.txt's format)
        public static IEnumerable<string> GuideLines()
        {
            var l = new List<string>();
            if (Gear.Count == 0 && Jewelry.Count == 0) return l;
            l.Add("@topic Items | Every piece of gear and every ring, trinket and amulet, with what it does and where it drops");
            l.Add("Every class has its own gear: one set for each rarity, nine pieces each (Head, Shoulder, Chest, Waist, Legs, Feet, Wrist, Hands and the weapon). A hunter's weapon goes in the Ranged slot; rogues also wear an Off Hand dagger and shamans a shield. Each boss drops one piece on its own roll, likelier on each Heroic level and after each miss; a piece you already have pays marks instead. Marks raise a piece from 0 to 5 stars, and a set's pieces worn together give 4-piece and 8-piece bonuses (Gear & the Armory). Rings, trinkets and amulets are the same for every class: guardians' lairs and battleground caches offer them during a run.");
            l.Add("");
            l.Add("The bonuses below are those of a piece at 0 stars; each rarity ends with its set's 4-piece and 8-piece bonuses. Sets can be mixed: four pieces of one set and four of another give both 4-piece bonuses. Try it out in the Gear Planner (Guide > Theorycrafting): click a piece to add a star; stars decide which set bonuses you reach.");
            l.Add("@page Rings, Trinkets & Amulets | The jewelry every hero is offered, with its numbers");
            l.Add("Offered again while worn, a ring, trinket or ordinary amulet is empowered: its numbers grow toward three times their own. The numbers below are those of its first pick.");
            foreach (string kind in new[] { "Ring", "Trinket", "Amulet" })
            {
                l.Add("# " + kind + "s");
                foreach (var j in JewelryOf(kind))
                {
                    if (j.IsCodex) continue;
                    l.Add("- " + j.Name + ": " + string.Join(" ", j.Lines.ToArray()));
                }
            }
            foreach (string cls in Classes)
            {
                l.Add("@page " + cls + " Gear | Every " + cls + " piece in every rarity, and the bosses that drop it");
                foreach (string rar in Rarities)
                {
                    string c;
                    var four = FourPiece(rar);
                    string eight = EightPiece(cls, rar);
                    string fourText = four != null ? four.Text : "Not entered yet.";
                    string eightText = eight ?? "Not entered yet.";
                    l.Add("# " + rar + (chance.TryGetValue(rar, out c) ? " (" + Pct(Num(c) * 100) + "% each on normal)" : ""));
                    l.Add(SetName(cls, rar));
                    l.Add("");
                    l.Add("4p bonus (4 armor at 1+ stars): " + fourText);
                    l.Add("");
                    l.Add("8p bonus (8 armor at 3+ stars; empowered at 5): " + eightText);
                    l.Add("");
                    foreach (var p in Gear)
                    {
                        if (p.Class != cls || p.Rarity != rar) continue;
                        string bon = p.Known ? (p.Health > 0 ? "+" + p.Health + " Health. " : "") + JoinBonuses(p) : "Bonuses not known yet.";
                        l.Add("- " + p.Name + " (" + p.Slot + "): " + bon + " Drops from " + string.Join(" or ", p.Drops.ToArray()) + ".");
                    }
                }
            }
            l.Add("@topic Archive | Every class Codex neck piece, listed alphabetically");
            l.Add("A Codex neck piece remakes one spell. It is offered once your hero knows that spell; while worn, it can be offered again and empowered. The class guides also show each class's Codex pieces beside its gear.");
            var codex = new List<Jewel>();
            foreach (var j in Jewelry) if (j.IsCodex) codex.Add(j);
            codex.Sort(delegate(Jewel a, Jewel b)
            {
                int c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
                c = string.Compare(a.Class, b.Class, StringComparison.OrdinalIgnoreCase);
                return c != 0 ? c : string.Compare(a.Spell, b.Spell, StringComparison.OrdinalIgnoreCase);
            });
            foreach (var j in codex)
            {
                var effects = new List<string>();
                foreach (string effect in j.Lines) if (!effect.StartsWith("Legendary amulet - remakes ")) effects.Add(effect);
                l.Add("- " + j.Name + ": this " + j.Class.ToLowerInvariant() + " neck piece remakes " + j.Spell + ". " + string.Join(" ", effects.ToArray()));
            }
            return l;
        }

        static string JoinBonuses(GearPiece p)
        {
            var parts = new List<string>();
            foreach (var x in p.Bonuses) parts.Add(x.Text);
            return string.Join(" ", parts.ToArray());
        }
    }
}
