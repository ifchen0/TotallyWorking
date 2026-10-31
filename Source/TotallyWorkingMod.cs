using RimWorld;
using UnityEngine;
using Verse;

namespace TotallyWorking
{
    public enum SpotlightMode { Off, Mouse, Center }

    public class OverlaySettings : ModSettings
    {
        public float lightOpacity = 0.6f;
        public float heavyOpacity = 0.9f;
        public int linesPerSecond = 12;
        public int fontSize = 14;
        public bool showEvents = true;
        public bool showLogErrors = true;
        public bool showAlerts = true;
        public bool showPawnStates = true;
        public bool showJobs;
        public bool showSummary = true;
        public bool logOnly;
        public bool randomPauses = true;
        public bool pauseWithGame = true;
        public bool coverWindows = true;
        public bool disguiseWindow = true;
        public string windowTitle = "build.log - Notepad";
        public SpotlightMode spotlightMode = SpotlightMode.Mouse;
        public float spotlightRadius = 0.25f; // fraction of the screen height
        public float spotlightOpacity = 0.4f;
        public int level; // 0 off, 1 light, 2 heavy; kept across restarts

        public override void ExposeData()
        {
            Scribe_Values.Look(ref lightOpacity, "lightOpacity", 0.6f);
            Scribe_Values.Look(ref heavyOpacity, "heavyOpacity", 0.9f);
            Scribe_Values.Look(ref linesPerSecond, "linesPerSecond", 12);
            Scribe_Values.Look(ref fontSize, "fontSize", 14);
            Scribe_Values.Look(ref showEvents, "showEvents", true);
            Scribe_Values.Look(ref showLogErrors, "showLogErrors", true);
            Scribe_Values.Look(ref showAlerts, "showAlerts", true);
            Scribe_Values.Look(ref showPawnStates, "showPawnStates", true);
            Scribe_Values.Look(ref showJobs, "showJobs", false);
            Scribe_Values.Look(ref showSummary, "showSummary", true);
            Scribe_Values.Look(ref logOnly, "logOnly", false);
            Scribe_Values.Look(ref randomPauses, "randomPauses", true);
            Scribe_Values.Look(ref pauseWithGame, "pauseWithGame", true);
            Scribe_Values.Look(ref coverWindows, "coverWindows", true);
            Scribe_Values.Look(ref disguiseWindow, "disguiseWindow", true);
            Scribe_Values.Look(ref windowTitle, "windowTitle", "build.log - Notepad");
            Scribe_Values.Look(ref spotlightMode, "spotlightMode", SpotlightMode.Mouse);
            Scribe_Values.Look(ref spotlightRadius, "spotlightRadius", 0.25f);
            Scribe_Values.Look(ref spotlightOpacity, "spotlightOpacity", 0.4f);
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
                // On 1.7 the first OS font lookup reads every installed font file, which stalled the frame the
                // overlay first appeared in. Build that index in the background now, unless the overlay is
                // already on (then the first frame builds it anyway). Unity's native lookup is left alone.
                if (Settings.level == 0 && OverlayBehaviour.DeferredWindows)
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        try { Font.GetOSInstalledFontNames(); }
                        catch (System.Exception e) { Log.Warning("[TotallyWorking] Font index warm-up failed: " + e.Message); }
                    });
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
            if (listing.ButtonText("TotW_Spotlight".Translate(("TotW_Spotlight_" + Settings.spotlightMode).Translate())))
                Settings.spotlightMode = (SpotlightMode)(((int)Settings.spotlightMode + 1) % 3);
            if (Settings.spotlightMode != SpotlightMode.Off)
            {
                listing.Label("TotW_SpotlightRadius".Translate(Mathf.RoundToInt(Settings.spotlightRadius * 100)));
                Settings.spotlightRadius = Mathf.Round(listing.Slider(Settings.spotlightRadius, 0.1f, 0.5f) * 20f) / 20f;
                listing.Label("TotW_SpotlightOpacity".Translate(Mathf.RoundToInt(Settings.spotlightOpacity * 100)));
                Settings.spotlightOpacity = Mathf.Round(listing.Slider(Settings.spotlightOpacity, 0f, 0.9f) * 20f) / 20f;
            }
            listing.Label("TotW_LinesPerSecond".Translate(Settings.linesPerSecond));
            Settings.linesPerSecond = Mathf.RoundToInt(listing.Slider(Settings.linesPerSecond, 1f, 60f));
            listing.Label("TotW_FontSize".Translate(Settings.fontSize));
            Settings.fontSize = Mathf.RoundToInt(listing.Slider(Settings.fontSize, 8f, 32f));
            listing.CheckboxLabeled("TotW_RandomPauses".Translate(), ref Settings.randomPauses, "TotW_RandomPauses_Desc".Translate());
            listing.CheckboxLabeled("TotW_PauseWithGame".Translate(), ref Settings.pauseWithGame, "TotW_PauseWithGame_Desc".Translate());
            listing.CheckboxLabeled("TotW_CoverWindows".Translate(), ref Settings.coverWindows, "TotW_CoverWindows_Desc".Translate());
            listing.Gap();
            listing.CheckboxLabeled("TotW_ShowEvents".Translate(), ref Settings.showEvents, "TotW_ShowEvents_Desc".Translate());
            listing.CheckboxLabeled("TotW_ShowLogErrors".Translate(), ref Settings.showLogErrors, "TotW_ShowLogErrors_Desc".Translate());
            listing.CheckboxLabeled("TotW_ShowAlerts".Translate(), ref Settings.showAlerts, "TotW_ShowAlerts_Desc".Translate());
            listing.CheckboxLabeled("TotW_ShowPawnStates".Translate(), ref Settings.showPawnStates, "TotW_ShowPawnStates_Desc".Translate());
            listing.CheckboxLabeled("TotW_ShowJobs".Translate(), ref Settings.showJobs, "TotW_ShowJobs_Desc".Translate());
            listing.CheckboxLabeled("TotW_ShowSummary".Translate(), ref Settings.showSummary, "TotW_ShowSummary_Desc".Translate());
            listing.Gap();
            listing.CheckboxLabeled("TotW_LogOnly".Translate(), ref Settings.logOnly, "TotW_LogOnly_Desc".Translate());
            listing.Gap();
            listing.CheckboxLabeled("TotW_DisguiseWindow".Translate(), ref Settings.disguiseWindow, "TotW_DisguiseWindow_Desc".Translate());
            if (Settings.disguiseWindow)
                Settings.windowTitle = listing.TextEntryLabeled("TotW_WindowTitle".Translate(), Settings.windowTitle);
            listing.End();
        }
    }
}
