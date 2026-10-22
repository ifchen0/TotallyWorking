using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace TotallyWorking
{
    // Live colony status the game does not log: alerts, colonist state and job changes, and an hourly summary.
    public class ColonyFeed
    {
        private const float AlertRepeatSeconds = 30f;
        private const float NutritionPerColonistPerDay = 1.6f;

        private static readonly FieldInfo ActiveAlertsField = typeof(AlertsReadout).GetField("activeAlerts", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly Color CriticalColor = new Color(0.9f, 0.35f, 0.35f);
        private static readonly Color HighColor = new Color(0.95f, 0.65f, 0.3f);
        private static readonly Color MediumColor = new Color(0.9f, 0.85f, 0.45f);
        private static readonly Color ResolvedColor = new Color(0.5f, 0.85f, 0.5f);
        private static readonly Color DraftColor = new Color(0.45f, 0.8f, 0.9f);
        private static readonly Color JobColor = new Color(0.6f, 0.6f, 0.65f);
        private static readonly Color SummaryColor = new Color(0.55f, 0.7f, 0.95f);

        private class PawnState
        {
            public bool downed;
            public bool drafted;
            public MentalStateDef mental;
            public Job job;
            public HashSet<Hediff> hediffs = new HashSet<Hediff>();
        }

        // Collects same-kind events from one poll so a burst (e.g. drafting everyone) becomes one line.
        private class Groups
        {
            private const int MaxLength = 140;

            private class Group
            {
                public string level, header;
                public Color color;
                public string single;
                public List<string> items = new List<string>();
            }

            private readonly Dictionary<string, Group> byKey = new Dictionary<string, Group>();
            private readonly List<Group> order = new List<Group>();

            // single: the line text when the group ends up with one entry; item: its part of a merged line.
            public void Add(string key, string level, string header, Color color, string single, string item)
            {
                if (!byKey.TryGetValue(key, out var g))
                {
                    byKey[key] = g = new Group { level = level, header = header, color = color, single = single };
                    order.Add(g);
                }
                g.items.Add(item);
            }

            public void Flush(List<Line> output)
            {
                foreach (var g in order)
                {
                    if (g.items.Count == 1)
                    {
                        output.Add(new Line(g.level + g.single, g.color));
                        continue;
                    }
                    string text = g.level + g.header + ": ";
                    int shown = 0;
                    foreach (var item in g.items)
                    {
                        if (shown > 0 && text.Length + item.Length > MaxLength)
                            break;
                        text += (shown > 0 ? ", " : "") + item;
                        shown++;
                    }
                    if (shown < g.items.Count)
                        text += " +" + (g.items.Count - shown) + " more";
                    output.Add(new Line(text, g.color));
                }
                byKey.Clear();
                order.Clear();
            }
        }

        private readonly Groups groups = new Groups();
        private readonly HashSet<Alert> activeAlerts = new HashSet<Alert>();
        private readonly Dictionary<Alert, float> alertAnnounced = new Dictionary<Alert, float>();
        private readonly Dictionary<Pawn, PawnState> pawns = new Dictionary<Pawn, PawnState>();
        private int lastHour = -1;

        public void Reset()
        {
            activeAlerts.Clear();
            alertAnnounced.Clear();
            pawns.Clear();
            lastHour = -1;
        }

        public void Poll(bool emit, List<Line> output)
        {
            var settings = TotallyWorkingMod.Settings;
            PollAlerts(emit && settings.showAlerts, output);
            PollPawns(emit && settings.showPawnStates, emit && settings.showJobs, output);
            groups.Flush(output);
            PollSummary(emit && settings.showSummary, output);
        }

        private void PollAlerts(bool emit, List<Line> output)
        {
            if (!(Find.UIRoot is UIRoot_Play play) || !(ActiveAlertsField?.GetValue(play.alerts) is List<Alert> live))
                return;
            foreach (var alert in live)
            {
                if (!activeAlerts.Add(alert) || !emit)
                    continue;
                // Alerts can flicker on and off; announce each one at most every so often.
                if (alertAnnounced.TryGetValue(alert, out float last) && Time.unscaledTime - last < AlertRepeatSeconds)
                    continue;
                alertAnnounced[alert] = Time.unscaledTime;
                string label = SafeLabel(alert);
                var color = alert.Priority == AlertPriority.Critical ? CriticalColor : alert.Priority == AlertPriority.High ? HighColor : MediumColor;
                groups.Add("alert" + alert.Priority, "WARN  ", "alerts", color, "alert: " + label, label);
            }
            foreach (var gone in activeAlerts.Where(a => !live.Contains(a)).ToList())
            {
                activeAlerts.Remove(gone);
                if (emit && alertAnnounced.TryGetValue(gone, out float shown) && Time.unscaledTime - shown >= 2f)
                {
                    string label = SafeLabel(gone);
                    groups.Add("resolved", "INFO  ", "alerts resolved", ResolvedColor, "alert resolved: " + label, label);
                }
            }
        }

        private void PollPawns(bool emitState, bool emitJobs, List<Line> output)
        {
            var colonists = PawnsFinder.AllMaps_FreeColonistsSpawned;
            foreach (var pawn in colonists)
            {
                bool isNew = !pawns.TryGetValue(pawn, out var st);
                if (isNew)
                    pawns[pawn] = st = new PawnState();
                string name = pawn.LabelShort;

                bool downed = pawn.Downed;
                if (downed != st.downed && !isNew && emitState)
                {
                    if (downed)
                        groups.Add("downed", "ERROR ", "downed", CriticalColor, name + ": downed", name);
                    else
                        groups.Add("up", "INFO  ", "back on their feet", ResolvedColor, name + ": back on their feet", name);
                }
                st.downed = downed;

                var mental = pawn.MentalStateDef;
                if (mental != st.mental && !isNew && emitState)
                {
                    if (mental != null)
                        groups.Add("mental", "WARN  ", "mental break", HighColor, name + ": mental break: " + mental.label, name + " (" + mental.label + ")");
                    else
                        groups.Add("calm", "INFO  ", "mental state ended", ResolvedColor, name + ": mental state ended", name);
                }
                st.mental = mental;

                bool drafted = pawn.Drafted;
                if (drafted != st.drafted && !isNew && emitState)
                    groups.Add(drafted ? "drafted" : "undrafted", "INFO  ", drafted ? "drafted" : "undrafted", DraftColor, name + (drafted ? ": drafted" : ": undrafted"), name);
                st.drafted = drafted;

                if (pawn.health?.hediffSet?.hediffs is List<Hediff> hediffs)
                {
                    foreach (var h in hediffs)
                        if (h.Visible && h.def.isBad && st.hediffs.Add(h) && !isNew && emitState)
                        {
                            string what = name + ": " + h.LabelCap + (h.Part != null ? " (" + h.Part.Label + ")" : "");
                            groups.Add("hediff", "WARN  ", "injured", HighColor, what, what);
                        }
                    st.hediffs.RemoveWhere(h => !hediffs.Contains(h));
                }

                var job = pawn.CurJob;
                if (job != st.job && job != null && !isNew && emitJobs)
                {
                    string what = name + ": " + Report(pawn);
                    groups.Add("job", "DEBUG ", "jobs", JobColor, what, what);
                }
                st.job = job;
            }
            // Forget colonists that left, died or were despawned.
            foreach (var gone in pawns.Keys.Where(p => !colonists.Contains(p)).ToList())
                pawns.Remove(gone);
        }

        private void PollSummary(bool emit, List<Line> output)
        {
            var map = Find.CurrentMap;
            if (map == null)
                return;
            int hour = Find.TickManager.TicksGame / GenDate.TicksPerHour;
            if (hour == lastHour)
                return;
            bool first = lastHour < 0;
            lastHour = hour;
            if (!emit || first)
                return;

            var colonists = map.mapPawns.FreeColonists;
            int count = colonists.Count;
            var moods = colonists.Where(p => p.needs?.mood != null).Select(p => p.needs.mood.CurLevelPercentage).ToList();
            string mood = moods.Count > 0 ? Mathf.RoundToInt(moods.Average() * 100f) + "%" : "-";
            float foodDays = count > 0 ? map.resourceCounter.TotalHumanEdibleNutrition / (count * NutritionPerColonistPerDay) : 0f;
            string time = GenLocalDate.HourOfDay(map).ToString("00") + ":00";
            output.Add(new Line("INFO  [" + time + "] colonists=" + count + " mood=" + mood + " food=" + foodDays.ToString("0.0") + "d wealth=" + Mathf.RoundToInt(map.wealthWatcher.WealthTotal).ToString("N0"), SummaryColor));
        }

        private static string Report(Pawn pawn)
        {
            try
            {
                return pawn.jobs?.curDriver?.GetReport().StripTags() ?? pawn.CurJobDef?.label ?? "?";
            }
            catch
            {
                return pawn.CurJobDef?.label ?? "?";
            }
        }

        private static string SafeLabel(Alert alert)
        {
            try
            {
                return alert.GetLabel().StripTags().Replace('\n', ' ');
            }
            catch
            {
                return alert.GetType().Name;
            }
        }
    }
}
