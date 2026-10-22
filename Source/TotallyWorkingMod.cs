using RimWorld;
using UnityEngine;
using Verse;

namespace TotallyWorking
{
    public class OverlaySettings : ModSettings
    {
        public float lightOpacity = 0.6f;
        public float heavyOpacity = 0.9f;
        public int linesPerSecond = 12;
        public int fontSize = 14;
        public bool showEvents = true;
        public bool showLogErrors = true;
        public int level; // 0 off, 1 light, 2 heavy; kept across restarts

        public override void ExposeData()
        {
            Scribe_Values.Look(ref lightOpacity, "lightOpacity", 0.6f);
            Scribe_Values.Look(ref heavyOpacity, "heavyOpacity", 0.9f);
            Scribe_Values.Look(ref linesPerSecond, "linesPerSecond", 12);
            Scribe_Values.Look(ref fontSize, "fontSize", 14);
            Scribe_Values.Look(ref showEvents, "showEvents", true);
            Scribe_Values.Look(ref showLogErrors, "showLogErrors", true);
            Scribe_Values.Look(ref level, "level", 0);
        }
    }

    [DefOf]
    public static class TotallyWorkingDefOf
    {
        public static KeyBindingDef TotallyWorking_CycleOverlay;

        static TotallyWorkingDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(TotallyWorkingDefOf));
    }

    public class TotallyWorkingMod : Mod
    {
        public static OverlaySettings Settings;
        public static TotallyWorkingMod Instance;

        public TotallyWorkingMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<OverlaySettings>();
            // Mod constructors may run off the main thread; Unity objects must be created on it.
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                var go = new GameObject("TotallyWorkingOverlay");
                Object.DontDestroyOnLoad(go);
                go.AddComponent<OverlayBehaviour>();
            });
        }

        public override string SettingsCategory() => "Totally Working";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.Label("TotW_KeyHint".Translate(TotallyWorkingDefOf.TotallyWorking_CycleOverlay.MainKeyLabel));
            listing.Gap();
            listing.Label("TotW_LightOpacity".Translate(Mathf.RoundToInt(Settings.lightOpacity * 100)));
            Settings.lightOpacity = Mathf.Round(listing.Slider(Settings.lightOpacity, 0.1f, 1f) * 20f) / 20f;
            listing.Label("TotW_HeavyOpacity".Translate(Mathf.RoundToInt(Settings.heavyOpacity * 100)));
            Settings.heavyOpacity = Mathf.Round(listing.Slider(Settings.heavyOpacity, 0.1f, 1f) * 20f) / 20f;
            listing.Label("TotW_LinesPerSecond".Translate(Settings.linesPerSecond));
            Settings.linesPerSecond = Mathf.RoundToInt(listing.Slider(Settings.linesPerSecond, 1f, 60f));
            listing.Label("TotW_FontSize".Translate(Settings.fontSize));
            Settings.fontSize = Mathf.RoundToInt(listing.Slider(Settings.fontSize, 8f, 32f));
            listing.Gap();
            listing.CheckboxLabeled("TotW_ShowEvents".Translate(), ref Settings.showEvents, "TotW_ShowEvents_Desc".Translate());
            listing.CheckboxLabeled("TotW_ShowLogErrors".Translate(), ref Settings.showLogErrors, "TotW_ShowLogErrors_Desc".Translate());
            listing.End();
        }
    }
}
