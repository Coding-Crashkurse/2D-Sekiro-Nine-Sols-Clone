using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AshenSol.Core
{
    /// <summary>Creates the camera, the global 2D light and the post-process volume; follows the player with
    /// look-ahead, bounds clamping, trauma shake, kicks, zoom and temporary focus.</summary>
    public class CameraController : MonoBehaviour, ICameraService
    {
        public static CameraController Instance { get; private set; }
        public static Volume GlobalVolume { get; private set; }
        public static Light2D GlobalLight { get; private set; }

        public Camera Camera { get; private set; }
        public Transform Transform { get { return Camera != null ? Camera.transform : null; } }

        Transform target;
        Rect bounds; bool hasBounds;
        Vector2 pos, vel;
        Vector2 lastTargetPos; float lookAhead;
        float trauma; Vector2 kick;
        float zoomTarget = 6f, zoomCur = 6f, zoomVel;
        Vector2 focusPos; float focusBlend, focusBlendTarget, focusSpeed = 1f;
        float noiseSeed;
        Light2D ambientLight;
        bool manual;

        void Awake()
        {
            Instance = this;
            Services.Cam = this;
            noiseSeed = 17.3f;
            CreateCamera();
            CreateGlobalLight();
            CreateVolume();
        }

        void CreateCamera()
        {
            var go = new GameObject("MainCamera");
            go.tag = "MainCamera";
            go.transform.SetParent(transform, false);
            Camera = go.AddComponent<Camera>();
            Camera.orthographic = true;
            Camera.orthographicSize = 6f;
            Camera.nearClipPlane = 0.1f;
            Camera.farClipPlane = 100f;
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = Palette.Night;
            Camera.allowHDR = true;
            Camera.allowMSAA = false;
            go.AddComponent<AudioListener>();
            var data = Camera.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.None;
                data.renderShadows = false;
            }
            go.transform.position = new Vector3(0f, 0f, -10f);
        }

        void CreateGlobalLight()
        {
            var go = new GameObject("GlobalLight2D");
            go.transform.SetParent(transform, false);
            ambientLight = go.AddComponent<Light2D>();
            ambientLight.lightType = Light2D.LightType.Global;
            ambientLight.intensity = 0.6f;
            ambientLight.color = new Color(0.75f, 0.85f, 1f);
            GlobalLight = ambientLight;
        }

        void CreateVolume()
        {
            var go = new GameObject("PostFX");
            go.transform.SetParent(transform, false);
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 10f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(1.15f);
            bloom.threshold.Override(0.82f);
            bloom.scatter.Override(0.72f);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.34f);
            vig.smoothness.Override(0.45f);
            vig.color.Override(Palette.Ink);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.contrast.Override(14f);
            ca.saturation.Override(6f);
            ca.postExposure.Override(0.12f);
            var chrom = profile.Add<ChromaticAberration>(true);
            chrom.intensity.Override(0.04f);
            var lens = profile.Add<LensDistortion>(true);
            lens.intensity.Override(0f);
            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.12f);
            vol.sharedProfile = profile;
            GlobalVolume = vol;
        }

        public static void SetAmbient(Color color, float intensity)
        {
            if (GlobalLight == null) return;
            GlobalLight.color = color;
            GlobalLight.intensity = intensity;
        }

        // ---- ICameraService ----
        public void Shake(float t) { trauma = Mathf.Clamp01(trauma + t * Settings.ShakeMul); }
        public void Kick(Vector2 dir, float amount) { kick += dir.normalized * amount; }
        public void SetTarget(Transform t)
        {
            target = t;
            if (t != null) lastTargetPos = t.position;
        }
        public void SetBounds(Rect worldBounds) { bounds = worldBounds; hasBounds = true; }
        /// <summary>Cutscenes drive the camera directly; follow, look-ahead and bounds are bypassed.</summary>
        public void SetManual(bool m)
        {
            manual = m;
            vel = Vector2.zero;
            lookAhead = 0f;
            if (m) { focusBlend = 0f; focusBlendTarget = 0f; }
        }

        public void SetPosition(Vector2 worldPos)
        {
            pos = worldPos;
            if (manual) ApplyTransform();
        }

        public void SetZoom(float orthoSize, float seconds)
        {
            zoomTarget = orthoSize;
            if (seconds <= 0.02f) { zoomCur = orthoSize; zoomVel = 0f; }
        }
        public void Focus(Vector2 worldPos, float seconds)
        {
            focusPos = worldPos; focusBlendTarget = 1f; focusSpeed = 1f / Mathf.Max(0.05f, seconds);
        }
        public void ReleaseFocus(float seconds)
        {
            focusBlendTarget = 0f; focusSpeed = 1f / Mathf.Max(0.05f, seconds);
        }
        public void SnapToTarget()
        {
            if (target == null) return;
            lastTargetPos = target.position;
            lookAhead = 0f;
            pos = Desired();
            vel = Vector2.zero;
            ApplyTransform();
        }

        Vector2 Desired()
        {
            Vector2 tp = target != null ? (Vector2)target.position : pos;
            Vector2 d = tp + new Vector2(lookAhead, 2.0f);   // frame the play space, not the floor slab
            if (focusBlend > 0f) d = Vector2.Lerp(d, focusPos, Ease.InOutSine(focusBlend));
            return Clamp(d);
        }

        Vector2 Clamp(Vector2 p)
        {
            if (!hasBounds || Camera == null) return p;
            float hh = zoomCur, hw = zoomCur * Camera.aspect;
            float minX = bounds.xMin + hw, maxX = bounds.xMax - hw;
            float minY = bounds.yMin + hh, maxY = bounds.yMax - hh;
            p.x = minX > maxX ? bounds.center.x : Mathf.Clamp(p.x, minX, maxX);
            p.y = minY > maxY ? bounds.center.y : Mathf.Clamp(p.y, minY, maxY);
            return p;
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;

            if (manual)
            {
                zoomCur = Mathf.SmoothDamp(zoomCur, zoomTarget, ref zoomVel, 0.35f, 100f, dt);
                Camera.orthographicSize = zoomCur;
                trauma = Mathf.Max(0f, trauma - 1.7f * dt);
                kick = Vector2.Lerp(kick, Vector2.zero, 1f - Mathf.Exp(-14f * dt));
                ApplyTransform();
                return;
            }

            // look-ahead from target motion
            if (target != null)
            {
                float dx = target.position.x - lastTargetPos.x;
                lastTargetPos = target.position;
                float want = Mathf.Abs(dx) > 0.001f ? Mathf.Sign(dx) * 1.6f : lookAhead;
                lookAhead = Mathf.Lerp(lookAhead, want, 1f - Mathf.Exp(-2.5f * dt));
            }

            focusBlend = Mathf.MoveTowards(focusBlend, focusBlendTarget, focusSpeed * dt);
            zoomCur = Mathf.SmoothDamp(zoomCur, zoomTarget, ref zoomVel, 0.35f, 100f, dt);
            Camera.orthographicSize = zoomCur;

            Vector2 desired = Desired();
            float smooth = focusBlend > 0f ? 0.25f : 0.13f;
            pos = Vector2.SmoothDamp(pos, desired, ref vel, smooth, 200f, dt);
            pos = Clamp(pos);

            trauma = Mathf.Max(0f, trauma - 1.7f * dt);
            kick = Vector2.Lerp(kick, Vector2.zero, 1f - Mathf.Exp(-14f * dt));
            ApplyTransform();
        }

        void ApplyTransform()
        {
            if (Camera == null) return;
            float mag = trauma * trauma;
            float t = Time.unscaledTime * 22f;
            Vector2 shake = new Vector2(
                (Mathf.PerlinNoise(noiseSeed, t) - 0.5f) * 2f,
                (Mathf.PerlinNoise(noiseSeed + 5.1f, t) - 0.5f) * 2f) * (0.55f * mag);
            float rot = (Mathf.PerlinNoise(noiseSeed + 9.7f, t) - 0.5f) * 2f * 2.2f * mag;
            Vector2 p = pos + shake + kick;
            Camera.transform.position = new Vector3(p.x, p.y, -10f);
            Camera.transform.rotation = Quaternion.Euler(0f, 0f, rot);
        }
    }
}
