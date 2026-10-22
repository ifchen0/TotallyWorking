using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace TotallyWorking
{
    public struct Line
    {
        public string text;
        public Color? color; // null = regular code colour

        public Line(string text, Color? color = null)
        {
            this.text = text;
            this.color = color;
        }
    }

    // Polls letters, messages and the log, and turns new entries into code-comment / compiler-output lines.
    public class GameFeed
    {
        private const int MaxText = 150;
        private const int MaxLogLinesPerPoll = 3;

        private static readonly FieldInfo LiveMessagesField = typeof(Messages).GetField("liveMessages", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly string[] ErrorCodes = { "CS0103", "CS0246", "CS1061", "CS0029", "CS0120", "CS7036" };
        private static readonly string[] WarningCodes = { "CS0168", "CS0219", "CS0618", "CS0649", "CS8618", "CS0162" };

        private static readonly Color ThreatColor = new Color(0.9f, 0.35f, 0.35f);
        private static readonly Color NegativeColor = new Color(0.95f, 0.65f, 0.3f);
        private static readonly Color PositiveColor = new Color(0.5f, 0.85f, 0.5f);
        private static readonly Color NeutralColor = new Color(0.55f, 0.7f, 0.95f);

        private Game game;
        private readonly HashSet<int> seenLetters = new HashSet<int>();
        private readonly HashSet<Message> seenMessages = new HashSet<Message>();
        private LogMessage lastLog;
        private readonly ColonyFeed colony = new ColonyFeed();

        public void Poll(bool emit, List<Line> output)
        {
            var settings = TotallyWorkingMod.Settings;
            PollLog(emit && settings.showLogErrors, output);
            if (Current.ProgramState != ProgramState.Playing || Current.Game == null)
                return;
            if (game != Current.Game)
            {
                // New or loaded game: everything already there counts as seen.
                game = Current.Game;
                seenLetters.Clear();
                seenMessages.Clear();
                colony.Reset();
                emit = false;
            }
            PollLetters(emit && settings.showEvents, output);
            PollMessages(emit && settings.showEvents, output);
            colony.Poll(emit, output);
        }

        private void PollLetters(bool emit, List<Line> output)
        {
            var letters = Find.LetterStack?.LettersListForReading;
            if (letters == null)
                return;
            foreach (var letter in letters)
            {
                if (!seenLetters.Add(letter.ID) || !emit)
                    continue;
                string text = letter.Label.Resolve();
                if (letter is ChoiceLetter choice)
                    text += ": " + choice.Text.Resolve();
                output.Add(new Line("// TODO: " + Clean(text), letter.def?.color ?? Color.white));
            }
        }

        private void PollMessages(bool emit, List<Line> output)
        {
            if (!(LiveMessagesField?.GetValue(null) is List<Message> live))
                return;
            foreach (var msg in live)
                if (seenMessages.Add(msg) && emit)
                    output.Add(new Line("// NOTE: " + Clean(msg.text), MessageColor(msg.def)));
            // Expired messages leave the live list; forget them so the set stays small.
            seenMessages.IntersectWith(live);
        }

        private void PollLog(bool emit, List<Line> output)
        {
            var fresh = new List<LogMessage>();
            LogMessage newest = null;
            try
            {
                using (Log.LockMessages())
                {
                    foreach (var m in Log.Messages)
                    {
                        newest = m;
                        if (m == lastLog)
                        {
                            fresh.Clear();
                            continue;
                        }
                        if (m.type != LogMessageType.Message)
                            fresh.Add(m);
                    }
                }
            }
            catch
            {
                return;
            }
            bool first = lastLog == null;
            lastLog = newest;
            // Skip the backlog on the first poll; a burst (or an evicted last entry) shows only the newest few.
            if (!emit || first)
                return;
            for (int i = Mathf.Max(0, fresh.Count - MaxLogLinesPerPoll); i < fresh.Count; i++)
            {
                var m = fresh[i];
                bool error = m.type == LogMessageType.Error;
                var codes = error ? ErrorCodes : WarningCodes;
                string code = codes[((m.text?.GetHashCode() ?? 0) & 0x7FFFFFFF) % codes.Length];
                output.Add(new Line((error ? "error " : "warning ") + code + ": " + Clean(m.text), m.Color));
            }
        }

        private static Color MessageColor(MessageTypeDef def)
        {
            if (def == MessageTypeDefOf.ThreatBig || def == MessageTypeDefOf.ThreatSmall || def == MessageTypeDefOf.PawnDeath)
                return ThreatColor;
            if (def == MessageTypeDefOf.NegativeEvent || def == MessageTypeDefOf.NegativeHealthEvent || def == MessageTypeDefOf.RejectInput || def == MessageTypeDefOf.CautionInput)
                return NegativeColor;
            if (def == MessageTypeDefOf.PositiveEvent || def == MessageTypeDefOf.TaskCompletion || def == MessageTypeDefOf.SituationResolved)
                return PositiveColor;
            return NeutralColor;
        }

        private static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            text = text.StripTags().Replace("\r", "").Replace('\n', ' ').Trim();
            return text.Length > MaxText ? text.Substring(0, MaxText) + "..." : text;
        }
    }
}
