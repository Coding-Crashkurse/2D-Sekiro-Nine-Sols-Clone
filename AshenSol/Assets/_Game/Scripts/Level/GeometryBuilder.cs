using UnityEngine;
using UnityEngine.Rendering.Universal;
using AshenSol.Core;

namespace AshenSol.Level
{
    /// <summary>Solid ground, one-way platforms, spikes, kill zones — all tiled sprites + colliders.</summary>
    public static class GeometryBuilder
    {
        static SpriteRenderer Tiled(Transform parent, string name, string sprite, Vector2 center, Vector2 size, int sort, Color tint)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Res.Sprite(sprite);
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.tileMode = SpriteTileMode.Continuous;
            sr.size = size;
            sr.sortingOrder = sort;
            sr.color = tint;
            return sr;
        }

        public static GameObject Ground(Rect r, Transform parent, bool mossTop = true)
        {
            var sr = Tiled(parent, "Ground", "tile_stone", r.center, r.size, SortOrder.Ground, Color.white);
            var go = sr.gameObject;
            go.layer = Layers.Ground;
            var col = go.AddComponent<BoxCollider2D>();
            col.size = r.size;
            if (mossTop && r.width >= 0.5f)
                Tiled(go.transform, "moss", "tile_stone_top", new Vector2(r.center.x, r.yMax - 0.06f), new Vector2(r.width, 0.24f), SortOrder.GroundDecor, Color.white);
            return go;
        }

        public static GameObject Wall(Rect r, Transform parent) { return Ground(r, parent, false); }

        /// <summary>One-way platform; (x, y) = left end of the top surface.</summary>
        public static GameObject Platform(float x, float y, float width, Transform parent)
        {
            var sr = Tiled(parent, "Platform", "tile_platform", new Vector2(x + width * 0.5f, y - 0.15f), new Vector2(width, 0.3f), SortOrder.Ground + 1, Color.white);
            var go = sr.gameObject;
            go.layer = Layers.OneWay;
            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(width, 0.3f);
            col.usedByEffector = true;
            var eff = go.AddComponent<PlatformEffector2D>();
            eff.useOneWay = true;
            eff.surfaceArc = 170f;
            eff.useSideFriction = false;
            return go;
        }

        /// <summary>Spikes standing on a floor at y (their base).</summary>
        public static GameObject Spikes(float x, float y, float width, Transform parent)
        {
            var sr = Tiled(parent, "Spikes", "tile_spikes", new Vector2(x + width * 0.5f, y + 0.2f), new Vector2(width, 0.4f), SortOrder.GroundDecor + 1, Color.white);
            var go = sr.gameObject;
            go.layer = Layers.Hazard;
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(Mathf.Max(0.2f, width - 0.3f), 0.28f);
            col.offset = new Vector2(0f, -0.02f);
            go.AddComponent<SpikeHazard>();
            var l = go.AddComponent<Light2D>();
            l.lightType = Light2D.LightType.Point;
            l.color = Palette.Red;
            l.intensity = 0.6f;
            l.pointLightOuterRadius = Mathf.Max(1.5f, width * 0.8f);
            return go;
        }

        public static GameObject KillZone(Rect r, Transform parent)
        {
            var go = new GameObject("KillZone");
            go.layer = Layers.Hazard;
            go.transform.SetParent(parent, false);
            go.transform.position = r.center;
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = r.size;
            go.AddComponent<KillZone>();
            return go;
        }
    }
}
