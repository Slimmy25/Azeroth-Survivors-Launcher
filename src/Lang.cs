// Translations. Every text the launcher shows goes through Lang.T, which looks it up in the chosen language's
// file (assets\lang\<code>.txt, built into the exe). Keys are the English texts; {0}, {1} ... stand for parts
// that change (names, numbers, paths), {#0} for parts that are always numbers. Text without a translation stays
// English. Patch notes, the About page and addon names are not translated.
//
// File format:
//   @name Deutsch                       the language's own name, shown in Options
//   @font Palatino Linotype, Georgia    optional: fonts to use for this language (the first one installed wins)
//   # a comment
//   Press PLAY to enter Azeroth.        an English text ...
//   = Drücke PLAY, um Azeroth zu betreten.     ... and its translation on the next line
//   (\n in a text stands for a line break)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace AzerothLauncher
{
    public static class Lang
    {
        class Pattern
        {
            public Regex Re;
            public string Value;
            public int Literal;                      // length of the fixed text: the most specific pattern wins
        }

        public static string Code = "en";
        public static string FontOverride = "";
        static Dictionary<string, string> exact = new Dictionary<string, string>(StringComparer.Ordinal);
        static List<Pattern> patterns = new List<Pattern>();
        static readonly Dictionary<string, string> cache = new Dictionary<string, string>(StringComparer.Ordinal);
        static readonly Regex Slot = new Regex(@"\{(#?)(\d)\}");

        // code -> the language's own name, English first
        public static List<KeyValuePair<string, string>> Available()
        {
            var list = new List<KeyValuePair<string, string>>();
            list.Add(new KeyValuePair<string, string>("en", "English"));
            var asm = Assembly.GetExecutingAssembly();
            var others = new List<KeyValuePair<string, string>>();
            foreach (string res in asm.GetManifestResourceNames())
            {
                if (!res.StartsWith("AS.lang.", StringComparison.Ordinal) || !res.EndsWith(".txt", StringComparison.Ordinal)) continue;
                string code = res.Substring(8, res.Length - 12);
                string name = code;
                foreach (string line in Read(res).Split('\n'))
                    if (line.StartsWith("@name ")) { name = line.Substring(6).Trim(); break; }
                others.Add(new KeyValuePair<string, string>(code, name));
            }
            others.Sort(delegate(KeyValuePair<string, string> a, KeyValuePair<string, string> b)
            {
                return string.Compare(a.Value, b.Value, StringComparison.CurrentCultureIgnoreCase);
            });
            list.AddRange(others);
            return list;
        }

        static string Read(string resource)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (s == null) return "";
                using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd().Replace("\r\n", "\n");
            }
        }

        static bool Has(string code)
        {
            if (code == "en") return true;
            foreach (var kv in Available()) if (kv.Key == code) return true;
            return false;
        }

        // the saved choice, else English (setup asks for the language first)
        public static string Default(string saved)
        {
            if (!string.IsNullOrEmpty(saved) && Has(saved)) return saved;
            return "en";
        }

        public static void Load(string code)
        {
            exact = new Dictionary<string, string>(StringComparer.Ordinal);
            patterns = new List<Pattern>();
            cache.Clear();
            FontOverride = "";
            Code = string.IsNullOrEmpty(code) ? "en" : code;
            // Search terms and inline guide hyperlinks also contain translated aliases.
            GuideLinks.Reset();
            if (Code == "en") return;
            string key = null;
            foreach (string raw in Read("AS.lang." + Code + ".txt").Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.StartsWith("@font ")) { FontOverride = line.Substring(6).Trim(); continue; }
                if (line.StartsWith("@") || line.StartsWith("#") || line.Trim().Length == 0) { key = null; continue; }
                if (line.StartsWith("= ") || line == "=")
                {
                    if (key != null) Add(key, Unescape(line.Length > 2 ? line.Substring(2) : ""));
                    key = null;
                    continue;
                }
                key = Unescape(line.Trim());
            }
            patterns.Sort(delegate(Pattern a, Pattern b) { return b.Literal.CompareTo(a.Literal); });
        }

        static string Unescape(string s) { return s.Replace("\\n", "\n"); }

        static void Add(string key, string value)
        {
            if (value.Length == 0) return;
            if (!Slot.IsMatch(key))
            {
                exact[key] = value;
                return;
            }
            var sb = new StringBuilder("^");
            int pos = 0, literal = 0;
            foreach (Match m in Slot.Matches(key))
            {
                string lit = key.Substring(pos, m.Index - pos);
                literal += lit.Length;
                sb.Append(Regex.Escape(lit));
                sb.Append(m.Groups[1].Value == "#" ? "(?<g" + m.Groups[2].Value + @">\d[\d.,]*)" : "(?<g" + m.Groups[2].Value + ">.+?)");
                pos = m.Index + m.Length;
            }
            string tail = key.Substring(pos);
            literal += tail.Length;
            sb.Append(Regex.Escape(tail)).Append("$");
            if (literal < 3) return;                         // too general to be safe
            var p = new Pattern();
            p.Re = new Regex(sb.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant);
            p.Value = value;
            p.Literal = literal;
            patterns.Add(p);
        }

        // the text in the chosen language (or as it is, when there is no translation)
        public static string T(string s)
        {
            if (string.IsNullOrEmpty(s) || Code == "en") return s ?? "";
            string r;
            lock (cache)
                if (cache.TryGetValue(s, out r)) return r;
            r = Lookup(s, 0) ?? Lines(s);
            lock (cache)
            {
                if (cache.Count > 5000) cache.Clear();
                cache[s] = r;
            }
            return r;
        }

        // a text made of several lines: each line on its own (leading spaces kept)
        static string Lines(string s)
        {
            string[] lines = s.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string body = line.TrimStart(' ');
                if (body.Trim().Length == 0) continue;
                string t = Lookup(body.TrimEnd(' '), 0);
                if (t != null) lines[i] = line.Substring(0, line.Length - body.Length) + t;
            }
            return string.Join("\n", lines);
        }

        static string Lookup(string s, int depth)
        {
            string v;
            if (exact.TryGetValue(s, out v)) return v;
            string trimmed = s.Trim();
            if (trimmed != s && trimmed.Length > 0)
            {
                // spaces around a text (indented lines, pieces joined together) are kept around its translation
                int at = s.IndexOf(trimmed, StringComparison.Ordinal);
                string inner = Lookup(trimmed, depth);
                return inner == null ? null : s.Substring(0, at) + inner + s.Substring(at + trimmed.Length);
            }
            if (depth > 3) return null;
            foreach (Pattern p in patterns)
            {
                Match m = p.Re.Match(s);
                if (!m.Success) continue;
                return Slot.Replace(p.Value, delegate(Match slot)
                {
                    Group g = m.Groups["g" + slot.Groups[2].Value];
                    if (!g.Success) return slot.Value;
                    string part = g.Value;
                    string inner = Lookup(part, depth + 1);      // a part may be a translatable text itself
                    return inner ?? (part.IndexOf('\n') >= 0 ? Lines(part) : part);
                });
            }
            return null;
        }

        // T for texts with {0} ... filled in afterwards
        public static string F(string template, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, T(template), args);
        }
    }

    // Messages and questions with translated text and title: inside the launcher (MsgDialog) when it is on screen,
    // otherwise a plain message box
    public static class Msg
    {
        static System.Windows.Forms.DialogResult Inside(string text, string caption, System.Windows.Forms.MessageBoxButtons buttons)
        {
            using (var d = new MsgDialog(text, caption, buttons)) return d.ShowModal(WowDialog.Host);
        }

        static bool CanInside()
        {
            var h = WowDialog.Host;
            return h != null && !h.IsDisposed && h.Visible && h.WindowState != System.Windows.Forms.FormWindowState.Minimized && !h.InvokeRequired;
        }

        public static System.Windows.Forms.DialogResult Show(string text, string caption)
        {
            if (CanInside()) return Inside(text, caption, System.Windows.Forms.MessageBoxButtons.OK);
            return System.Windows.Forms.MessageBox.Show(Lang.T(text), Lang.T(caption));
        }

        public static System.Windows.Forms.DialogResult Show(System.Windows.Forms.IWin32Window owner, string text, string caption)
        {
            if (CanInside()) return Inside(text, caption, System.Windows.Forms.MessageBoxButtons.OK);
            return System.Windows.Forms.MessageBox.Show(owner, Lang.T(text), Lang.T(caption));
        }

        public static System.Windows.Forms.DialogResult Show(System.Windows.Forms.IWin32Window owner, string text, string caption,
                                                             System.Windows.Forms.MessageBoxButtons buttons)
        {
            if (CanInside()) return Inside(text, caption, buttons);
            return System.Windows.Forms.MessageBox.Show(owner, Lang.T(text), Lang.T(caption), buttons);
        }

        public static System.Windows.Forms.DialogResult Show(System.Windows.Forms.IWin32Window owner, string text, string caption,
                                                             System.Windows.Forms.MessageBoxButtons buttons,
                                                             System.Windows.Forms.MessageBoxIcon icon)
        {
            if (CanInside()) return Inside(text, caption, buttons);
            return System.Windows.Forms.MessageBox.Show(owner, Lang.T(text), Lang.T(caption), buttons, icon);
        }
    }
}
