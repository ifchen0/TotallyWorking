using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace TotallyWorking
{
    // Own MonoBehaviour with a low GUI.depth so the overlay draws above every RimWorld window,
    // without touching the game's GUI code. It only paints on Repaint, so input passes through.
    public class OverlayBehaviour : MonoBehaviour
    {
        private const int MaxLines = 300;
        private const float PollInterval = 0.25f;

        private readonly List<Line> lines = new List<Line>();
        private readonly Queue<string> pending = new Queue<string>();
        private readonly List<Line> feedLines = new List<Line>();
        private readonly CodeSource source = new CodeSource();
        private readonly GameFeed feed = new GameFeed();
        private float lineTimer;
        private float pollTimer;
        private bool wasOn;
        private GUIStyle style;
        private int styleSize;

        private void Update()
        {
            var key = TotallyWorkingDefOf.TotallyWorking_CycleOverlay;
            if (key != null && key.JustPressed)
            {
                var settings = TotallyWorkingMod.Settings;
                settings.level = (settings.level + 1) % 3;
                TotallyWorkingMod.Instance.WriteSettings();
            }
            if (TotallyWorkingMod.Settings.level == 0)
            {
                wasOn = false;
                return;
            }
            if (!wasOn)
            {
                wasOn = true;
                feed.Poll(false, feedLines); // start from now, no backlog of old events
            }

            pollTimer += Time.unscaledDeltaTime;
            if (pollTimer >= PollInterval)
            {
                pollTimer = 0f;
                feedLines.Clear();
                feed.Poll(true, feedLines);
                lines.AddRange(feedLines);
            }

            lineTimer += Time.unscaledDeltaTime * TotallyWorkingMod.Settings.linesPerSecond;
            // Cap the catch-up after a long frame so the screen does not jump by pages.
            if (lineTimer > 20f)
                lineTimer = 20f;
            while (lineTimer >= 1f)
            {
                lineTimer -= 1f;
                if (pending.Count == 0)
                    foreach (var l in source.NextChunk())
                        pending.Enqueue(l);
                lines.Add(new Line(pending.Dequeue()));
            }
            if (lines.Count > MaxLines)
                lines.RemoveRange(0, lines.Count - MaxLines);
        }

        private void OnGUI()
        {
            GUI.depth = -10000;
            var settings = TotallyWorkingMod.Settings;
            if (settings.level == 0 || Event.current.type != EventType.Repaint)
                return;

            float opacity = settings.level == 1 ? settings.lightOpacity : settings.heavyOpacity;
            var screen = new Rect(0f, 0f, Screen.width, Screen.height);
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, opacity);
            GUI.DrawTexture(screen, Texture2D.whiteTexture);

            EnsureStyle(Mathf.RoundToInt(settings.fontSize * Prefs.UIScale));
            float lineHeight = style.lineHeight > 0f ? style.lineHeight : style.fontSize * 1.2f;
            int count = Mathf.Max(1, Mathf.FloorToInt((Screen.height - 16f) / lineHeight));
            int start = Mathf.Max(0, lines.Count - count);

            // One label per line: a multi-line block drifts from lineHeight (e.g. with CJK fallback glyphs),
            // which would misplace the coloured lines.
            float width = Screen.width - 24f;
            var codeColor = new Color(0.85f, 0.85f, 0.85f, 0.95f);
            for (int i = start; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line.text.Length == 0)
                    continue;
                GUI.color = line.color ?? codeColor;
                GUI.Label(new Rect(12f, 8f + (i - start) * lineHeight, width, lineHeight), line.text, style);
            }
            GUI.color = old;
        }

        private void EnsureStyle(int size)
        {
            if (style != null && styleSize == size)
                return;
            styleSize = size;
            var font = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Microsoft JhengHei", "Courier New" }, size);
            style = new GUIStyle
            {
                font = font,
                fontSize = size,
                wordWrap = false,
                richText = false,
                clipping = TextClipping.Clip,
                alignment = TextAnchor.UpperLeft,
            };
            style.normal.textColor = Color.white;
        }
    }
}
