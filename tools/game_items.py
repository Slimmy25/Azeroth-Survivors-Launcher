"""Reads the items out of the game itself, as wow.aethus.pl serves it, and writes assets/items.txt.

wow.aethus.pl runs Azeroth Survivors in the browser; its items (the rings, trinkets and amulets with their numbers, and
the names of every class's gear in each rarity) are inside its WebAssembly file. Nothing comes from any other site.
Run it after a game update:  python tools/game_items.py   (Actions > Game items does the same.)

The tables are found by their shape, not by fixed places, so a new game build is read the same way.
The gear's bonuses are NOT in the game's files (the game works them out as it runs): assets/gear-stats.txt holds
those, entered from the game's own item cards.
"""
import json, os, re, struct, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from game_talents import get, wasm_url, memory

CLASSES = ["warrior", "paladin", "hunter", "rogue", "priest", "shaman", "mage", "warlock", "druid"]
ARMOR = {"warrior": "plate", "paladin": "plate", "hunter": "mail", "rogue": "leather", "priest": "cloth",
         "shaman": "mail", "mage": "cloth", "warlock": "cloth", "druid": "leather"}
SLOTS = ["Head", "Shoulder", "Chest", "Waist", "Legs", "Feet", "Wrist", "Hands"]   # the order of the game's nouns
WOW_CLASS = {1: "warrior", 2: "paladin", 3: "hunter", 4: "rogue", 5: "priest", 7: "shaman", 8: "mage", 9: "warlock",
             11: "druid"}                                                      # the game's class numbers
RARITIES = ["Common", "Uncommon", "Rare", "Epic", "Legendary"]                     # the five class sets, in order


def main():
    root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
    data = open(sys.argv[1], "rb").read() if len(sys.argv) > 1 else get(wasm_url())
    if data[:4] != b"\0asm":
        sys.exit("::error::the download is not the game's WebAssembly file")
    mem = memory(data)
    text = data.decode("latin1")
    debug = []
    u = lambda i: struct.unpack_from("<I", mem, i)[0] if 0 <= i <= len(mem) - 4 else 0
    f = lambda i: struct.unpack_from("<f", mem, i)[0]

    def s(p, l):
        if p < 1048576 or l <= 0 or l > 400 or p + l > len(mem):
            return None
        try:
            return mem[p:p + l].decode("utf-8")
        except UnicodeDecodeError:
            return None

    # ---- the English texts (the first of each key in the game's translations)
    en = {}
    for m in re.finditer(r'"([a-z_]+\.[A-Za-z0-9_.]+)":\s*"((?:[^"\\]|\\.)*)"', text):
        if m.group(1) not in en:
            try:
                en[m.group(1)] = json.loads('"' + m.group(2) + '"').encode("latin1").decode("utf-8")
            except Exception:
                pass

    # ---- jewelry: key, name, icon, text (four strings), then its number and its kind
    jewels = []
    for r in range(1048576, len(mem) - 40, 4):
        k = s(u(r), u(r + 4))
        if not k or not re.fullmatch(r"[a-z][a-z0-9_]{1,30}", k):
            continue
        n = s(u(r + 8), u(r + 12))
        if not n or not re.fullmatch(r"[A-Z][A-Za-z' :\-]+", n):
            continue
        ic = s(u(r + 16), u(r + 20))
        if not ic or not re.fullmatch(r"(INV|Spell|Ability)_[A-Za-z0-9_]+", ic):
            continue
        t = s(u(r + 24), u(r + 28))
        if not t or "{v}" not in t:
            continue
        v, kind = f(r + 32), u(r + 36)
        if not (0 < v < 10000) or kind > 0x400:
            continue
        jewels.append((r, k, n, ic, t, v, kind))
    debug.append("jewelry records: %d" % len(jewels))
    for j in jewels:
        debug.append("  %x %s | %s | %s | %s | %g | %x" % j)

    # ---- the amulets: key, name, icon (three strings) and three small numbers, one after the other
    amulet_rows = []
    for r in range(1048576, len(mem) - 40, 4):
        k = s(u(r), u(r + 4))
        if not k or ("amulet." + k) not in en:
            continue
        n = s(u(r + 8), u(r + 12))
        ic = s(u(r + 16), u(r + 20))
        if n != en["amulet." + k] or not ic or not re.fullmatch(r"(INV|Spell|Ability)_[A-Za-z0-9_]+", ic):
            continue
        amulet_rows.append((r, k, n, ic, u(r + 24), u(r + 28), u(r + 32)))
    debug.append("amulet rows: %d" % len(amulet_rows))
    for x in amulet_rows:
        debug.append("  %x %s | %s | %s | %d %d %d" % x)
    # each amulet's lines: kind 0x30 (0x31 since 0.121.0, 0x3c in later builds) is a plain bonus (its number and which stat), any
    # other kind is one of the game's special effects (described in game). A line was 36 bytes; later builds use 40. The size
    # used is the one that reads more lines as plain bonuses (a whole amulet of "special" lines means the layout moved again).
    PLAIN = (0x30, 0x31, 0x3c)
    def read_lines(ptr, cnt, size):
        return [ptr + size * i for i in range(cnt)]
    amulet_lines = {}
    for x in amulet_rows:
        ptr, cnt = x[4], x[5]
        best = None
        for size in (40, 36):
            qs = read_lines(ptr, cnt, size)
            plain = sum(1 for q in qs if u(q) in PLAIN)
            if best is None or plain > best[0]:
                best = (plain, size, qs)
        lines = []
        for q in best[2]:
            if u(q) in PLAIN:
                lines.append("%d=%g" % (u(q + 8), f(q + 4)))
            else:
                lines.append("special")
                debug.append("  special %s (%d-byte lines): %s" % (x[1], best[1], " ".join("%x" % u(q + 4 * j) for j in range(9))))
        amulet_lines[x[1]] = (x[3], x[6], lines)
    if amulet_rows and all(all(l == "special" for l in v[2]) for v in amulet_lines.values()):
        raise SystemExit("every amulet line reads as special: the game's amulet layout changed; not writing items.txt")
    # the stats' texts, in the game's order (the order is the stat's number): "Increases all damage you deal by {p}."
    stats = []
    first = "Increases all damage you deal by {p}."
    runs = re.findall(r"Reduces the cooldown of your spells by \{p\}\.[^\x00-\x1f\"]*", text)
    if runs and first in text:
        stats = [first] + re.split(r"(?<=\.)[a-z]?(?=[A-Z])", max(runs, key=len))
        stats = [re.sub(r"(?<=\.)[a-z]$", "", x) for x in stats]      # the letter that ends the last text
    debug.append("stat texts: %d" % len(stats))
    amulets = sorted((k[7:], v) for k, v in en.items() if k.startswith("amulet."))

    # ---- the class sets: five (name, length, "of" flag) entries in a row, the first the class's common set
    flags = {}
    for cls in CLASSES:
        names = [en.get("set.%s.%d" % (cls, i)) for i in range(5)]
        if None in names:
            continue
        first = names[0].encode("utf-8")
        q = 0
        while cls not in flags:
            q = mem.find(first, q)
            if q < 0:
                break
            pb = struct.pack("<II", q, len(first))
            r = 0
            while True:
                r = mem.find(pb, r)
                if r < 0:
                    break
                if r % 4 == 0 and all(s(u(r + 12 * i), u(r + 12 * i + 4)) == names[i] for i in range(5)):
                    flags[cls] = [u(r + 12 * i + 8) & 1 for i in range(5)]
                    debug.append("sets %s at %x: %s" % (cls, r, flags[cls]))
                    break
                r += 1
            q += 1

    # ---- the PvP sets: the Alliance and Horde name, the weapons (Alliance and Horde), the class
    pvp = []
    fm = en.get("set.pvp.alliance") or "Field Marshal's"
    for r in range(1048576, len(mem) - 96, 4):
        if s(u(r), u(r + 4)) not in (fm, "Warlord's"):
            continue
        horde = s(u(r + 8), u(r + 12))
        wp, wn, cid = u(r + 16), u(r + 20), u(r + 88)
        if not horde or not 1 <= wn <= 4 or not 1 <= cid <= 11:
            continue
        weapons = []
        for i in range(wn):
            q = wp + 24 * i
            weapons.append((s(u(q + 4), u(q + 8)), s(u(q + 16), u(q + 20))))
        if all(a and h for a, h in weapons):
            pvp.append((cid, s(u(r), u(r + 4)), horde, weapons))
            debug.append("pvp row %x class %d: %s / %s %s" % (r, cid, fm, horde, weapons))

    # ---- drop chances: Common, Uncommon, Rare, Epic, Legendary, PvP (60% down to 20%)
    chances = None
    for r in range(1048576, len(mem) - 24, 4):
        v = [f(r + 4 * i) for i in range(6)]
        if abs(v[0] - 0.6) < 1e-6 and (abs(v[1] - 0.5) < 1e-6 or abs(v[5] - 0.2) < 1e-6 or abs(v[2] - 0.5) < 1e-6):
            debug.append("chances at %x: %s" % (r, v))
            # the drop table has six real shares (PvP last); a run of five followed by unrelated memory is another table
            if chances is None and abs(v[1] - 0.5) < 1e-6 and abs(v[4] - 0.2) < 1e-6 and 0 < v[5] <= 1:
                chances = v
    if chances is None:
        raise SystemExit("no drop chance table found: not writing items.txt")

    # ---- write assets/items.txt
    out = ["Items of Azeroth Survivors, read from the game itself at wow.aethus.pl by tools/game_items.py.",
           "Do not edit by hand: run the tool again after a game update. Gear bonuses are in gear-stats.txt.",
           "@jewelry  key | Name | Icon | kind | stat | value | text   (kind: ring, trinket; value: {v} at its first pick)",
           "@set  class | rarity | set name | of   (of = 1: \"Gloves of Faith\", 0: \"Devout Leggings\")",
           "@noun  armor | slot | noun      @weapon  class | weapon | rarity | name",
           "@amulet  key | Name | Icon | tier | lines (stat=value, or special)      @stat  number | text",
           "@chance  rarity | share      @pvp  class | Alliance | Horde | weapons (Alliance / Horde)", "==="]
    out.append("@jewelry")
    seen = set()
    for _, k, n, ic, t, v, kind in jewels:
        if k in seen:
            continue
        seen.add(k)
        # Creator patch 0.126.0 adds revival protection that older item text omits.
        # Keep this supplement when regenerating the shared Guide/Planner/Handbook data.
        if k == "soulstone" and not re.search(r"protect|immun|invulner", t, re.I):
            t += "; protects you from damage for 2 seconds after revival"
        out.append(" | ".join([k, n, ic, "ring" if kind < 0x100 else "trinket", str(kind), "%g" % v, t]))
    out.append("@amulet")
    for key, name in amulets:
        icon, tier, lines = amulet_lines.get(key, ("", 0, []))
        out.append(" | ".join([key, name, icon, str(tier)] + lines))
    out.append("@stat")
    for i, t in enumerate(stats):
        out.append("%d | %s" % (i, t))
    out.append("@set")
    for cls in CLASSES:
        for i in range(5):
            name = en.get("set.%s.%d" % (cls, i))
            if name:
                out.append(" | ".join([cls, RARITIES[i], name, str(flags.get(cls, [0] * 5)[i])]))
    out.append("@noun")
    for armor in ("cloth", "leather", "mail", "plate"):
        for i, slot in enumerate(SLOTS):
            n = en.get("noun.%s.%d" % (armor, i))
            if n:
                out.append(" | ".join([armor, slot, n]))
    out.append("@weapon")
    for cls in CLASSES:
        for w in range(3):
            for i in range(5):
                n = en.get("weapon.%s.%d.%d" % (cls, w, i))
                if n:
                    out.append(" | ".join([cls, str(w), RARITIES[i], n]))
    out.append("@chance")
    for name, v in zip(RARITIES + ["PvP"], chances or []):
        out.append("%s | %g" % (name, round(v, 4)))
    out.append("@pvp")
    for cid, a, h, weapons in sorted(pvp):
        cls = WOW_CLASS.get(cid, str(cid))
        out.append(" | ".join([cls, a, h] + ["%s / %s" % w for w in weapons]))
    out.append("@armor")
    for cls in CLASSES:
        out.append("%s | %s" % (cls, ARMOR[cls]))
    path = os.path.join(root, "assets", "items.txt")
    open(path, "w", encoding="utf-8", newline="\r\n").write("\n".join(out) + "\n")
    open(os.path.join(root, "items-debug.txt"), "w", encoding="utf-8").write("\n".join(debug) + "\n")
    print("%d jewelry, %d amulets, %d class sets -> %s" % (len(seen), len(amulets), len(flags), path))


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception as e:                                   # shown on the Actions run's page
        print("::error::%s: %s" % (type(e).__name__, e))
        raise
