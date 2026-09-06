using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using AshenSol.Core;
using AshenSol.Enemies;
using AshenSol.VFX;

namespace AshenSol.Intro
{
    /// <summary>Builders for the cutscene panels: flat sprite layers, props, ground strips, ash and
    /// stand-alone puppet figures. Everything reuses the in-game sprite library so the intro looks
    /// like the same world.</summary>
    public static class IntroStage
    {
        // The art already contains dark shadows. Heavy multiplicative tints were erasing
        // its detail; preserve the palette while bringing those multipliers toward neutral.
        static Color ReadableTint(Color tint, float lift)
        {
            Color color = Color.Lerp(tint, Color.white, lift);
            color.a = tint.a;
            return color;
        }

        public static VolumeProfile CreateReadableLook(Transform parent)
        {
            var go = new GameObject("Intro exposure");
            go.transform.SetParent(parent, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 20f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.5f);
            color.contrast.Override(-5f);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.16f);
            volume.sharedProfile = profile;
            return profile;
        }
        /// <summary>A full-width backdrop layer scaled to a given world width.</summary>
        public static SpriteRenderer Layer(Transform parent, string sprite, Vector2 localPos, float worldWidth,
                                           int sort, Color tint, bool additive = false)
        {
            var go = new GameObject("layer_" + sprite);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var sr = go.AddComponent<SpriteRenderer>();
            var sp = Res.Sprite(sprite);
            sr.sprite = sp;
            sr.sortingOrder = sort;
            sr.color = additive ? tint : ReadableTint(tint, 0.55f);
            sr.material = additive ? VfxManager.AdditiveFor(sp) : MaterialLibrary.SpriteUnlit;
            float w = sp.bounds.size.x;
            float s = w > 0.001f ? worldWidth / w : 1f;
            go.transform.localScale = Vector3.one * s;
            return sr;
        }

        /// <summary>A prop standing on the given base position (sprite bottom sits on y).</summary>
        public static SpriteRenderer Prop(Transform parent, string sprite, Vector2 basePos, float scale,
                                          int sort, Color tint, bool additive = false)
        {
            var go = new GameObject("prop_" + sprite);
            go.transform.SetParent(parent, false);
            var sp = Res.Sprite(sprite);
            float h = sp.bounds.size.y * scale;
            go.transform.localPosition = new Vector3(basePos.x, basePos.y + h * 0.5f, 0f);
            go.transform.localScale = Vector3.one * scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sp;
            sr.sortingOrder = sort;
            sr.color = additive ? tint : ReadableTint(tint, 0.6f);
            sr.material = additive ? VfxManager.AdditiveFor(sp) : MaterialLibrary.SpriteUnlit;
            return sr;
        }

        /// <summary>A tiled stone floor strip with the mossy top edge, purely decorative (no colliders).</summary>
        public static void Ground(Transform parent, Vector2 topCenter, float width)
        {
            var go = new GameObject("ground");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(topCenter.x, topCenter.y - 3f, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Res.Sprite("tile_stone");
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.tileMode = SpriteTileMode.Continuous;
            sr.size = new Vector2(width, 6f);
            sr.sortingOrder = SortOrder.Ground;
            sr.material = MaterialLibrary.SpriteUnlit;
            sr.color = new Color(0.48f, 0.49f, 0.58f);

            var top = new GameObject("moss");
            top.transform.SetParent(parent, false);
            top.transform.localPosition = new Vector3(topCenter.x, topCenter.y - 0.06f, 0f);
            var tr = top.AddComponent<SpriteRenderer>();
            tr.sprite = Res.Sprite("tile_stone_top");
            tr.drawMode = SpriteDrawMode.Tiled;
            tr.tileMode = SpriteTileMode.Continuous;
            tr.size = new Vector2(width, 0.24f);
            tr.sortingOrder = SortOrder.GroundDecor;
            tr.material = MaterialLibrary.SpriteUnlit;
            tr.color = new Color(0.65f, 0.72f, 0.78f);
        }

        public static void Mist(Transform parent, Rect area, Color tint, int count)
        {
            var root = VfxManager.CreateMist(area, tint, count, parent);
            root.transform.localPosition = Vector3.zero;
        }

        /// <summary>Falling ash: the visual signature of the whole backstory.</summary>
        public static ParticleSystem Ash(Transform parent, Rect area, float rate)
        {
            var go = new GameObject("ash");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(area.center.x, area.center.y, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true; main.playOnAwake = true; main.maxParticles = 900;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.85f, 0.82f, 0.8f, 0.75f), new Color(0.6f, 0.58f, 0.6f, 0.5f));
            main.gravityModifier = 0.035f;
            main.useUnscaledTime = true;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            var em = ps.emission; em.enabled = true; em.rateOverTime = rate;
            var sh = ps.shape;
            sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(area.width, 0.4f, 1f);
            sh.position = new Vector3(0f, area.height * 0.5f, 0f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(-0.5f, 0.2f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.9f, -0.35f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var noise = ps.noise;
            noise.enabled = true; noise.strength = 0.35f; noise.frequency = 0.3f; noise.scrollSpeed = 0.25f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = VfxManager.UnlitFor(Res.Sprite("fx_dust"));
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortingOrder = SortOrder.Fog;
            ps.Play();
            ps.Simulate(8f, true, false);
            ps.Play();
            return ps;
        }

        /// <summary>A stand-alone puppet (no enemy behind it) posed on the given ground position.</summary>
        public static EnemyRig Figure(Transform parent, EnemyRig.Config cfg, Vector2 groundPos, int facing,
                                      Color silhouette, float scale)
        {
            var go = new GameObject("figure");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(groundPos.x, groundPos.y, 0f);
            cfg.Scale = scale;
            var rig = EnemyRig.BuildHumanoid(go.transform, cfg);
            rig.SetFacing(facing);
            foreach (var r in rig.Renderers)
            {
                if (r == null) continue;
                r.color = ReadableTint(silhouette, 0.75f);
                r.material = MaterialLibrary.SpriteUnlit;   // unaffected by 2D lights: a clean silhouette
            }
            var tick = go.AddComponent<IntroFigure>();
            tick.Rig = rig;
            rig.Animate(0f, 0.016f);
            return rig;
        }

        // ---------------- figure profiles ----------------
        public static EnemyRig.Config StudentConfig()
        {
            return new EnemyRig.Config
            {
                Torso = "player_torso", Head = "player_head", Arm = "player_arm_front",
                Weapon = "player_sword", Leg = "player_leg_front",
                TwoSegment = true, UpperArm = "player_arm_front_upper", LowerArm = "player_arm_front_lower", UpperLeg = "player_leg_front_upper", LowerLeg = "player_leg_front_lower",
                UpperArmH = 0.26f, LowerArmH = 0.24f, UpperLegH = 0.30f, LowerLegH = 0.28f, UpperArmLen = 0.22f, LowerArmLen = 0.20f, UpperLegLen = 0.26f, LowerLegLen = 0.28f,
                HipY = 0.62f, LegOffset = 0.07f, TorsoH = 0.62f, HeadY = 0.60f,
                Shoulder = new Vector2(0.09f, 0.54f), ArmLen = 0.42f, ArmSpriteH = 0.46f,
                LegSpriteH = 0.54f, HeadSpriteH = 0.46f,
                WeaponOffset = new Vector2(0f, 0.30f), WeaponVerticalIdle = true, ArmIdle = 14f,
                SortBase = SortOrder.Player, LightColor = Palette.Teal, LightIntensity = 0.9f, LightRadius = 1.8f,
                WeaponTipDistance = 0.75f, TrailColor = Palette.PlayerSlash
            };
        }

        public static EnemyRig.Config MasterConfig()
        {
            return new EnemyRig.Config
            {
                Torso = "spear_torso", Head = "spear_head", Arm = "spear_arm",
                Weapon = "spear_spear", Leg = "spear_leg",
                TwoSegment = true, UpperArm = "spear_arm_upper", LowerArm = "spear_arm_lower", UpperLeg = "spear_leg_upper", LowerLeg = "spear_leg_lower",
                UpperArmH = 0.24f, LowerArmH = 0.22f, UpperLegH = 0.27f, LowerLegH = 0.25f, UpperArmLen = 0.20f, LowerArmLen = 0.20f, UpperLegLen = 0.23f, LowerLegLen = 0.25f,
                HipY = 0.48f, TorsoH = 0.66f, HeadY = 0.64f,
                Shoulder = new Vector2(0.09f, 0.55f), ArmLen = 0.40f, ArmSpriteH = 0.42f,
                LegSpriteH = 0.48f, HeadSpriteH = 0.42f,
                WeaponOffset = new Vector2(0f, 0.25f), WeaponVerticalIdle = true, ArmIdle = 12f,
                SortBase = SortOrder.Enemy, LightColor = Palette.TealDeep, LightIntensity = 0.7f, LightRadius = 2f,
                WeaponTipDistance = 1f
            };
        }

        public static EnemyRig.Config HuskConfig()
        {
            return new EnemyRig.Config
            {
                Torso = "grunt_torso", Head = "grunt_head", Arm = "grunt_arm",
                Weapon = "grunt_blade", Leg = "grunt_leg",
                TwoSegment = true, UpperArm = "grunt_arm_upper", LowerArm = "grunt_arm_lower", UpperLeg = "grunt_leg_upper", LowerLeg = "grunt_leg_lower",
                UpperArmH = 0.24f, LowerArmH = 0.22f, UpperLegH = 0.26f, LowerLegH = 0.24f, UpperArmLen = 0.20f, LowerArmLen = 0.20f, UpperLegLen = 0.22f, LowerLegLen = 0.24f,
                HipY = 0.46f, TorsoH = 0.62f, HeadY = 0.60f,
                Shoulder = new Vector2(0.09f, 0.52f), ArmLen = 0.40f, ArmSpriteH = 0.42f,
                LegSpriteH = 0.46f, HeadSpriteH = 0.38f,
                WeaponOffset = new Vector2(0f, 0.26f), WeaponIdleLocal = 200f, ArmIdle = 18f,
                SortBase = SortOrder.Enemy, LightColor = Palette.RedDeep, LightIntensity = 0.3f, LightRadius = 1.4f,
                WeaponTipDistance = 0.62f
            };
        }

        public static EnemyRig.Config WardenConfig()
        {
            return new EnemyRig.Config
            {
                Torso = "boss_torso", Head = "boss_head", Arm = "boss_arm",
                Weapon = "boss_glaive", Leg = "boss_leg",
                TwoSegment = true, UpperArm = "boss_arm_upper", LowerArm = "boss_arm_lower", UpperLeg = "boss_leg_upper", LowerLeg = "boss_leg_lower",
                UpperArmH = 0.48f, LowerArmH = 0.44f, UpperLegH = 0.62f, LowerLegH = 0.56f, UpperArmLen = 0.42f, LowerArmLen = 0.40f, UpperLegLen = 0.54f, LowerLegLen = 0.56f,
                HipY = 1.1f, LegOffset = 0.18f, TorsoH = 1.5f, HeadY = 1.42f,
                Shoulder = new Vector2(0.22f, 1.22f), ArmLen = 0.82f, ArmSpriteH = 0.92f,
                LegSpriteH = 1.1f, HeadSpriteH = 0.82f,
                WeaponOffset = new Vector2(0f, 0.55f), WeaponVerticalIdle = true, ArmIdle = 10f,
                SortBase = SortOrder.Boss, LightColor = Palette.Red, LightIntensity = 0.8f, LightRadius = 3f,
                WeaponTipDistance = 1.8f, TrailColor = Palette.BossSlash
            };
        }
    }

    /// <summary>Ticks a stand-alone puppet every frame on unscaled time, so poses, flashes, telegraphs and
    /// strikes set by the cutscene actually play. Settles the rig before it is first seen.</summary>
    public class IntroFigure : MonoBehaviour
    {
        public EnemyRig Rig;
        /// <summary>Horizontal speed fed to the walk cycle (the beat moves the transform itself).</summary>
        public float VelocityX;
        bool settled;

        public static IntroFigure Of(EnemyRig rig)
        {
            return rig != null && rig.Root != null && rig.Root.parent != null ? rig.Root.parent.GetComponent<IntroFigure>() : null;
        }

        void OnEnable() { settled = false; }

        void Update()
        {
            if (Rig == null) return;
            if (!settled)
            {
                settled = true;
                for (int i = 0; i < 40; i++) Rig.Animate(VelocityX, 1f / 30f);
            }
            Rig.Animate(VelocityX, Time.unscaledDeltaTime);
        }
    }
}
