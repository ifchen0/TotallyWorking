using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace TotallyWorking
{
    // Faint map of what blocks movement, one pixel per cell, drawn under the code so the base layout can be followed.
    // Only the edges of impassable areas are marked, so a mountain is an outline rather than a solid block.
    public class ObstacleMap
    {
        private const float RebuildSeconds = 2f;
        private const float Strength = 0.15f; // relative to the text alpha

        private static readonly Color32 WallColor = new Color32(140, 140, 140, 255);

        private Texture2D texture;
        private byte[] data;
        private Map builtFor;
        private float nextRebuild;

        public void Draw(float textAlpha)
        {
            if (Current.ProgramState != ProgramState.Playing || !WorldRendererUtility.DrawingMap)
                return;
            var map = Find.CurrentMap;
            var camera = Find.Camera;
            if (map == null || camera == null)
                return;
            if (map != builtFor || Time.unscaledTime >= nextRebuild)
                Rebuild(map);

            // The camera looks straight down, so the map's two corners give the whole screen rect.
            var size = map.Size;
            Vector3 a = camera.WorldToScreenPoint(Vector3.zero);
            Vector3 b = camera.WorldToScreenPoint(new Vector3(size.x, 0f, size.z));
            var rect = new Rect(a.x, Screen.height - b.y, b.x - a.x, b.y - a.y);
            GUI.color = new Color(1f, 1f, 1f, textAlpha * Strength);
            GUI.DrawTexture(rect, texture);
        }

        private void Rebuild(Map map)
        {
            builtFor = map;
            nextRebuild = Time.unscaledTime + RebuildSeconds;
            int w = map.Size.x, h = map.Size.z;
            if (texture == null || texture.width != w || texture.height != h)
            {
                texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                data = new byte[w * h * 4];
            }

            var walkable = map.pathing.Normal.pathGrid;
            var fog = map.fogGrid;
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    int i = z * w + x;
                    Color32 c = default;
                    var cell = new IntVec3(x, 0, z);
                    // Doors are walkable, so they stay a gap in the wall.
                    if (!fog.IsFogged(i) && !walkable.Walkable(cell) && NextToWalkable(walkable, x, z, w, h))
                        c = WallColor;
                    // Row 0 of the texture is its bottom, like z = 0 on the map.
                    int p = i * 4;
                    data[p] = c.r;
                    data[p + 1] = c.g;
                    data[p + 2] = c.b;
                    data[p + 3] = c.a;
                }
            // Raw bytes: the 1.7 texture stub has no SetPixels.
            texture.LoadRawTextureData(data);
            texture.Apply();
        }

        private static bool NextToWalkable(PathGrid grid, int x, int z, int w, int h)
        {
            return x > 0 && grid.Walkable(new IntVec3(x - 1, 0, z))
                || x < w - 1 && grid.Walkable(new IntVec3(x + 1, 0, z))
                || z > 0 && grid.Walkable(new IntVec3(x, 0, z - 1))
                || z < h - 1 && grid.Walkable(new IntVec3(x, 0, z + 1));
        }
    }
}
