using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace AzerothLauncher
{
    public partial class LauncherApp
    {
        // Exercises the actual selector hit rectangles and mouse-up path. The control
        // stays unparented/hidden and uses no icon URLs or player settings.
        void RunScalingGearRows(Action<bool, string> check)
        {
            const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
            const BindingFlags shared = BindingFlags.Static | BindingFlags.NonPublic;
            Type type = typeof(GearSheet);
            FieldInfo chosen = type.GetField("chosenSlot", shared), tier = type.GetField("tierMode", shared);
            object oldChosen = chosen.GetValue(null), oldTier = tier.GetValue(null);
            var oldStars = new Dictionary<string, int>(GearSheet.Stars);
            var oldTotals = PlannerTotals;
            float oldZoom = Theme.Zoom, oldScale = Theme.DisplayScale;
            int oldDpi = Theme.DisplayDpi;
            try
            {
                Theme.DisplayScale = 2f;
                Theme.DisplayDpi = 192;
                Theme.Zoom = 1.44f;
                chosen.SetValue(null, "Head");
                tier.SetValue(null, false);
                var slots = new Dictionary<string, object>();
                var picker = new Rectangle(0, 0, 210, 140);
                using (var sheet = new GearSheet("Warrior", slots, new Dictionary<string, string>()) { SelectorOnly = true })
                using (var bitmap = new Bitmap(picker.Width, picker.Height))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    sheet.Size = new Size(picker.Width + 1, picker.Height + GearSheet.BuildControlsHeight + 7);
                    type.GetField("scrollSlot", instance).SetValue(sheet, "Head");
                    var choices = (List<object>)type.GetMethod("Choices", instance).Invoke(sheet, new object[] { "Head" });
                    check(choices.Count > 4, "compact gear picker has first, wrapped and last item fixtures");
                    foreach (int requested in new[] { 0, 4, choices.Count - 1 })
                    {
                        slots.Clear();
                        type.GetField("itemOffset", instance).SetValue(sheet, requested);
                        type.GetMethod("Selector", instance).Invoke(sheet, new object[] { graphics, picker });
                        int offset = (int)type.GetField("itemOffset", instance).GetValue(sheet);
                        var rows = (List<KeyValuePair<Rectangle, object>>)type.GetField("rows", instance).GetValue(sheet);
                        string label = "compact gear row " + requested;
                        check(offset == requested && rows.Count > 0, label + " remains reachable after scrolling");
                        Rectangle hit = rows[0].Key;
                        check(!hit.IsEmpty && hit.Width > 0 && hit.Height > 0 && picker.Contains(hit), label + " has a visible contained hit target");
                        var point = new Point(hit.Left + hit.Width / 2, hit.Top + hit.Height / 2);
                        check((int)type.GetMethod("RowAt", instance).Invoke(sheet, new object[] { point }) == 0, label + " resolves pointer coordinates to the visible item");
                        type.GetMethod("OnMouseUp", instance).Invoke(sheet, new object[] { new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0) });
                        object selected;
                        check(slots.TryGetValue("Head", out selected) && ReferenceEquals(selected, choices[requested]), label + " mouse-up selects the expected item");
                    }
                }
            }
            finally
            {
                chosen.SetValue(null, oldChosen);
                tier.SetValue(null, oldTier);
                GearSheet.Stars.Clear();
                foreach (var pair in oldStars) GearSheet.Stars[pair.Key] = pair.Value;
                PlannerTotals = oldTotals;
                Theme.Zoom = oldZoom;
                Theme.DisplayScale = oldScale;
                Theme.DisplayDpi = oldDpi;
            }
        }
    }
}
