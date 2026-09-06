using UnityEngine;
using AshenSol.Core;

namespace AshenSol.VFX
{
    /// <summary>A pooled, tapered ribbon swept around the blade. Vertex feathering keeps the
    /// entire crescent soft, including its tips, without a cropped sprite or texture edge.</summary>
    public class SlashFx : MonoBehaviour
    {
        const int Segments = 96;
        const int Rows = 7;
        const float Lifetime = 0.24f;
        static readonly float[] Across = { -1f, -0.65f, -0.24f, 0f, 0.24f, 0.65f, 1f };
        static readonly float[] Opacity = { 0f, 0.12f, 0.65f, 1f, 0.65f, 0.12f, 0f };

        Mesh mesh;
        Vector3[] vertices;
        Color[] colors;
        Color tint;
        float elapsed, angle, direction;
        float span = 1f, aspect = 1f;
        bool mirrored;
        public bool IsActive { get; private set; }

        public static SlashFx Make(Transform parent)
        {
            var go = new GameObject("SwordRibbon");
            go.transform.SetParent(parent, false);
            var fx = go.AddComponent<SlashFx>();
            fx.mesh = new Mesh { name = "Soft sword crescent" };
            fx.mesh.MarkDynamic();
            fx.vertices = new Vector3[(Segments + 1) * Rows];
            fx.colors = new Color[fx.vertices.Length];
            var uv = new Vector2[fx.vertices.Length];
            for (int i = 0; i < uv.Length; i++) uv[i] = Vector2.one * 0.5f;
            var triangles = new int[Segments * (Rows - 1) * 6];
            int at = 0;
            for (int i = 0; i < Segments; i++)
                for (int row = 0; row < Rows - 1; row++)
                {
                    int a = i * Rows + row, b = a + Rows;
                    triangles[at++] = a; triangles[at++] = b; triangles[at++] = a + 1;
                    triangles[at++] = a + 1; triangles[at++] = b; triangles[at++] = b + 1;
                }
            fx.mesh.vertices = fx.vertices;
            fx.mesh.uv = uv;
            fx.mesh.triangles = triangles;
            fx.mesh.bounds = new Bounds(Vector3.zero, new Vector3(5f, 4f, 1f));
            go.AddComponent<MeshFilter>().sharedMesh = fx.mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = VfxManager.AdditiveFor(Res.White);
            renderer.sortingOrder = SortOrder.Fx + 8;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.SetActive(false);
            return fx;
        }

        /// <summary>arcSpan scales the crescent's angle (a thrust is a short streak), arcAspect flattens it (a
        /// horizontal slash is wide and low). Short arcs are centred on the position instead of hanging off it.</summary>
        public void Play(Vector2 position, float angleDeg, bool flipX, Color color, float scale, float arcSpan = 1f, float arcAspect = 1f)
        {
            tint = color;
            span = Mathf.Max(0.1f, arcSpan); aspect = Mathf.Max(0.15f, arcAspect);
            angle = angleDeg;
            direction = angleDeg < 0f ? -1f : 1f;
            mirrored = flipX;
            elapsed = 0f;
            transform.position = position;
            transform.localScale = new Vector3((flipX ? -1f : 1f) * scale, scale, 1f);
            IsActive = true;
            gameObject.SetActive(true);
            Apply(0f);
        }

        void Update()
        {
            elapsed += Time.deltaTime;
            float p = Mathf.Clamp01(elapsed / Lifetime);
            Apply(p);
            if (p >= 1f) { IsActive = false; gameObject.SetActive(false); }
        }

        void Apply(float p)
        {
            // The leading tip draws the arc quickly; its tail then catches up as it fades.
            float head = Mathf.Lerp(0.22f, 1f, Ease.OutCubic(Mathf.Clamp01(p / 0.5f)));
            float tail = 0.82f * Mathf.Pow(Mathf.Clamp01((p - 0.3f) / 0.7f), 2f);
            float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((p - 0.18f) / 0.82f));
            float rotation = angle + direction * Mathf.Lerp(-12f, 18f, Ease.OutCubic(p));
            transform.rotation = Quaternion.Euler(0f, 0f, mirrored ? -rotation : rotation);

            Vector2 shift = span < 0.6f ? new Vector2(-1.1f, 0f) : Vector2.zero;
            for (int i = 0; i <= Segments; i++)
            {
                float u = (float)i / Segments;
                float a = Mathf.Lerp(-1.95f * span, 1.95f * span, Mathf.Lerp(tail, head, u)) * direction;
                float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                Vector2 center = new Vector2(cos * 1.75f - 0.65f, sin * 1.05f * aspect) + shift;
                Vector2 normal = new Vector2(cos / 1.75f, sin / (1.05f * aspect)).normalized;
                float taper = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(u * Mathf.PI)), 0.7f);
                float width = 0.22f * taper * Mathf.Lerp(0.65f, 1f, u) * Mathf.Lerp(1f, 0.55f, p);
                for (int row = 0; row < Rows; row++)
                {
                    int index = i * Rows + row;
                    vertices[index] = center + normal * (Across[row] * width);
                    float core = 1f - Mathf.Abs(Across[row]);
                    Color c = Color.Lerp(tint, Color.white, core * core * 0.6f);
                    c.a = tint.a * fade * Opacity[row] * Mathf.Sqrt(taper);
                    colors[index] = c;
                }
            }
            mesh.vertices = vertices;
            mesh.colors = colors;
        }

        void OnDestroy() { if (mesh != null) Destroy(mesh); }
    }
}
