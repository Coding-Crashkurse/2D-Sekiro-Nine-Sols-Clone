using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using AshenSol.Core;
using AshenSol.Enemies;
using AshenSol.Level;
using AshenSol.VFX;

namespace AshenSol.Intro
{
    /// <summary>
    /// The opening cutscene: five illustrated panels with parallax camera moves, live puppet rigs and
    /// ElevenLabs narration. Built entirely from the existing sprite library so it matches the game.
    /// Runs on a stage parked far above the levels; skippable with any key.
    /// </summary>
    public class IntroSequence : MonoBehaviour
    {
        // the stage sits well above every level (levels use y -12..26)
        static readonly Vector2 Stage = new Vector2(0f, 600f);
        const float PanelSpacing = 120f;

        public static bool IsPlaying { get; private set; }

        class Panel
        {
            public Transform Root;
            public Vector2 CamFrom, CamTo;
            public float ZoomFrom = 6f, ZoomTo = 6f;
            public string Voice;
            public string Subtitle;
            public System.Func<Panel, float, IEnumerator> Beat;   // optional scripted action
        }

        readonly List<Panel> panels = new List<Panel>();
        bool skipped;
        Transform root;
        VolumeProfile introLook;

        void OnDestroy()
        {
            if (introLook != null) Destroy(introLook);
        }

        public static IntroSequence Create()
        {
            var go = new GameObject("IntroSequence");
            return go.AddComponent<IntroSequence>();
        }

        /// <summary>Play the whole sequence. Yields until it is done (or skipped).</summary>
        public IEnumerator Play()
        {
            IsPlaying = true;
            skipped = false;
            root = new GameObject("IntroStage").transform;
            root.position = Stage;
            introLook = IntroStage.CreateReadableLook(root);

            BuildPanels();

            // take over presentation
            Services.Ui.SetHudVisible(false);
            Services.Ui.HideBossBar();
            Services.Cam.SetManual(true);
            Services.Cam.SetBounds(new Rect(Stage.x - 5000f, Stage.y - 5000f, 10000f, 10000f));
            CameraController.SetAmbient(new Color(0.85f, 0.82f, 0.9f), 0.75f);
            Services.Audio.PlayAmbience("ambience_wind", 0.35f);
            Services.Audio.PlayMusic("music_intro", 2.5f);
            Services.Ui.SetLetterbox(1f, 0.8f);
            Services.Ui.ShowSkipHint(true);

            yield return new WaitForSecondsRealtime(0.2f);
            Services.Ui.Fade(0f, 1.4f);

            for (int i = 0; i < panels.Count && !skipped; i++)
                yield return PlayPanel(panels[i], i);

            if (!skipped) yield return new WaitForSecondsRealtime(0.6f);

            // hand back to the game
            Services.Ui.Fade(1f, skipped ? 0.35f : 1.0f);
            yield return new WaitForSecondsRealtime(skipped ? 0.4f : 1.05f);
            Services.Ui.ShowSubtitle(null);
            Services.Ui.ShowSkipHint(false);
            Services.Ui.SetLetterbox(0f, 0.4f);
            Services.Audio.StopVoice();
            Services.Cam.SetManual(false);
            if (root != null) Destroy(root.gameObject);
            IsPlaying = false;
            Destroy(gameObject);
        }

        IEnumerator PlayPanel(Panel p, int index)
        {
            Services.Cam.SetPosition(p.CamFrom);
            Services.Cam.SetZoom(p.ZoomFrom, 0f);
            SetPanelVisible(p, true);

            GameEvents.RaiseLog("intro panel " + (index + 1));
            float length = Services.Audio.PlayVoice(p.Voice, 1f);
            if (length <= 0.05f) length = 4.5f;              // missing clip: keep the pacing anyway
            float hold = length + 1.1f;
            Services.Ui.ShowSubtitle(p.Subtitle);

            Coroutine beat = null;
            if (p.Beat != null) beat = StartCoroutine(p.Beat(p, hold));

            float t = 0f;
            while (t < hold)
            {
                if (CheckSkip()) break;
                t += Time.unscaledDeltaTime;
                float e = Ease.InOutSine(Mathf.Clamp01(t / hold));
                Services.Cam.SetPosition(Vector2.Lerp(p.CamFrom, p.CamTo, e));
                Services.Cam.SetZoom(Mathf.Lerp(p.ZoomFrom, p.ZoomTo, e), 0.05f);
                yield return null;
            }
            if (beat != null) StopCoroutine(beat);

            if (!skipped && index < panels.Count - 1)
            {
                // ink wipe into the next panel: fully dark before the swap, then the new panel fades up
                Services.Ui.Fade(1f, 0.25f);
                yield return new WaitForSecondsRealtime(0.28f);
                Services.Ui.ShowSubtitle(null);
                SetPanelVisible(p, false);
                Services.Ui.Fade(0f, 0.45f);
                yield return new WaitForSecondsRealtime(0.15f);
            }
            else SetPanelVisible(p, false);
        }

        bool CheckSkip()
        {
            if (skipped) return true;
            var inp = Services.Input;
            if (inp != null && (inp.AnyPressed || inp.ConfirmPressed || inp.PausePressed || inp.QuitPressed || inp.InteractPressed))
            {
                skipped = true;
                Services.Audio.StopVoice();
                GameEvents.RaiseLog("intro skipped");
                return true;
            }
            return false;
        }

        void SetPanelVisible(Panel p, bool v)
        {
            if (p != null && p.Root != null) p.Root.gameObject.SetActive(v);
        }

        // ================================================================= panels
        void BuildPanels()
        {
            panels.Add(PanelNineSuns());
            panels.Add(PanelAshfall());
            panels.Add(PanelTraining());
            panels.Add(PanelGate());
            panels.Add(PanelWalk());
            foreach (var p in panels) SetPanelVisible(p, false);
        }

        Transform NewPanel(int index, out Vector2 origin)
        {
            origin = Stage + new Vector2(index * PanelSpacing, 0f);
            var t = new GameObject("Panel" + index).transform;
            t.SetParent(root, false);
            t.position = origin;
            return t;
        }

        // ---------------- 1: nine suns over the valley ----------------
        Panel PanelNineSuns()
        {
            Vector2 o;
            var t = NewPanel(0, out o);

            IntroStage.Layer(t, "bg_sky", new Vector2(0f, 2f), 46f, SortOrder.BgSky, new Color(0.62f, 0.5f, 0.55f));
            // the nine suns, the rightmost already dimming
            for (int i = 0; i < 9; i++)
            {
                float f = i / 8f;
                var pos = new Vector2(-10f + f * 20f, 4.4f + Mathf.Sin(f * Mathf.PI) * 2.2f);
                float scale = i == 8 ? 2.2f : Mathf.Lerp(3.4f, 2.6f, Mathf.Abs(f - 0.5f) * 2f);
                var c = i == 8 ? new Color(0.6f, 0.25f, 0.2f) : Color.Lerp(Palette.Gold, Palette.Amber, f);
                var glow = IntroStage.Layer(t, "fx_glow", pos, scale, SortOrder.BgSky + 2, c.WithAlpha(i == 8 ? 0.5f : 0.9f), true);
                var pulse = glow.gameObject.AddComponent<IntroPulse>();
                pulse.Init(glow, i * 0.7f, i == 8 ? 0.35f : 0.12f, i == 8 ? 0.5f : 0.9f);
            }
            IntroStage.Layer(t, "bg_far", new Vector2(0f, 1.5f), 34f, SortOrder.BgFar, new Color(0.5f, 0.42f, 0.5f));
            IntroStage.Layer(t, "bg_mid", new Vector2(1.5f, -0.6f), 32f, SortOrder.BgMid, new Color(0.26f, 0.2f, 0.26f));
            IntroStage.Layer(t, "bg_near", new Vector2(-1f, -3.2f), 30f, SortOrder.BgNear, new Color(0.09f, 0.08f, 0.12f));
            IntroStage.Mist(t, new Rect(-14f, -4f, 28f, 5f), new Color(1f, 0.85f, 0.75f), 10);

            return new Panel
            {
                Root = t,
                CamFrom = o + new Vector2(-1.5f, 2.6f), CamTo = o + new Vector2(1.2f, 1.6f),
                ZoomFrom = 7.6f, ZoomTo = 6.4f,
                Voice = "intro_1",
                Subtitle = "Nine suns burned over this valley.  We built them."
            };
        }

        // ---------------- 2: the ninth dies, the ash falls, the dead rise ----------------
        Panel PanelAshfall()
        {
            Vector2 o;
            var t = NewPanel(1, out o);

            IntroStage.Layer(t, "bg_boss_sky", new Vector2(0f, 3f), 46f, SortOrder.BgSky, new Color(0.75f, 0.6f, 0.6f));
            // warm haze low on the horizon so the husks read as silhouettes
            IntroStage.Layer(t, "fx_glow", new Vector2(0f, -3.4f), 34f, SortOrder.BgSky + 1, new Color(0.85f, 0.42f, 0.3f, 0.5f), true);
            IntroStage.Layer(t, "bg_mid", new Vector2(0f, -1f), 32f, SortOrder.BgMid, new Color(0.16f, 0.13f, 0.18f));
            IntroStage.Ground(t, new Vector2(0f, -4.2f), 32f);
            for (int i = -2; i <= 2; i++)
                IntroStage.Prop(t, "prop_pillar", new Vector2(i * 5.5f + 1.2f, -4.2f), 1f, SortOrder.BgProps, new Color(0.3f, 0.28f, 0.34f));
            IntroStage.Prop(t, "prop_torii", new Vector2(0f, -4.2f), 1f, SortOrder.BgProps + 1, new Color(0.35f, 0.2f, 0.22f));

            // husks: the shrine's own, standing back up
            var husks = new List<EnemyRig>();
            float[] xs = { -5.5f, -2.4f, 0.9f, 3.8f, 6.4f };
            for (int i = 0; i < xs.Length; i++)
            {
                var rig = IntroStage.Figure(t, IntroStage.HuskConfig(), new Vector2(xs[i], -4.2f), i % 2 == 0 ? 1 : -1,
                    new Color(0.17f, 0.16f, 0.21f), 0.95f + (i % 3) * 0.06f);
                rig.SetPose(true, 20f, 200f, 62f, 30f, 30f, -30f, -30f, 40f);        // slumped over on bent knees, feet on the ground
                husks.Add(rig);
            }
            IntroStage.Ash(t, new Rect(-16f, -5f, 32f, 16f), 90f);

            return new Panel
            {
                Root = t,
                CamFrom = o + new Vector2(-3.5f, 0.6f), CamTo = o + new Vector2(2.6f, -1.4f),
                ZoomFrom = 6.4f, ZoomTo = 5.2f,
                Voice = "intro_2",
                Subtitle = "When the ninth went out, its ash fell like snow —\nand the dead did not lie still.",
                Beat = (p, dur) => HusksRise(husks, dur)
            };
        }

        IEnumerator HusksRise(List<EnemyRig> husks, float duration)
        {
            yield return new WaitForSecondsRealtime(duration * 0.35f);
            for (int i = 0; i < husks.Count; i++)
            {
                var rig = husks[i];
                rig.SetPose(false);
                rig.Punch(0.9f, 1.12f);
                rig.Flash(Palette.Red, 0.5f);
                if (rig.Light != null) { rig.Light.color = Palette.Red; rig.Light.intensity = 1.6f; }
                Services.Vfx.DustPuff((Vector2)rig.Root.position, 1.1f);
                Services.Audio.PlaySfx("enemy_telegraph", 0.35f, 0.25f);
                yield return new WaitForSecondsRealtime(0.42f);
            }
        }

        // ---------------- 3: the master teaches the turning blade ----------------
        Panel PanelTraining()
        {
            Vector2 o;
            var t = NewPanel(2, out o);

            IntroStage.Layer(t, "bg_sky", new Vector2(0f, 2f), 46f, SortOrder.BgSky, new Color(0.9f, 0.6f, 0.5f));
            IntroStage.Layer(t, "bg_far", new Vector2(0f, 0.5f), 34f, SortOrder.BgFar, new Color(0.34f, 0.24f, 0.3f));
            IntroStage.Layer(t, "fx_glow", new Vector2(0.5f, -1.6f), 26f, SortOrder.BgFar + 1, new Color(1f, 0.5f, 0.35f, 0.55f), true);
            IntroStage.Layer(t, "bg_near", new Vector2(0f, -2.6f), 30f, SortOrder.BgNear, new Color(0.08f, 0.07f, 0.1f));
            IntroStage.Ground(t, new Vector2(0f, -3.4f), 26f);
            IntroStage.Prop(t, "prop_lantern", new Vector2(-4.5f, 0.4f), 1f, SortOrder.Props, Color.white);
            IntroStage.Prop(t, "prop_lantern", new Vector2(4.6f, 0.6f), 1f, SortOrder.Props, Color.white);

            var student = IntroStage.Figure(t, IntroStage.StudentConfig(), new Vector2(-1.5f, -3.4f), 1,
                new Color(0.14f, 0.14f, 0.19f), 1f);
            var master = IntroStage.Figure(t, IntroStage.MasterConfig(), new Vector2(1.8f, -3.4f), -1,
                new Color(0.11f, 0.11f, 0.15f), 1.12f);
            IntroStage.Mist(t, new Rect(-12f, -4f, 24f, 4f), new Color(1f, 0.8f, 0.7f), 8);

            return new Panel
            {
                Root = t,
                CamFrom = o + new Vector2(0.6f, -1.1f), CamTo = o + new Vector2(0.1f, -1.9f),
                ZoomFrom = 5.4f, ZoomTo = 4.2f,
                Voice = "intro_3",
                Subtitle = "My master sealed the sanctum with his own body.\nHe asked one thing of me:  learn the turning blade.",
                Beat = (p, dur) => TrainingBeat(student, master, dur)
            };
        }

        IEnumerator TrainingBeat(EnemyRig student, EnemyRig master, float duration)
        {
            yield return new WaitForSecondsRealtime(duration * 0.42f);
            for (int i = 0; i < 3; i++)
            {
                // master strikes
                master.Telegraph(AttackKind.Parryable, 0.5f);
                yield return new WaitForSecondsRealtime(0.5f);
                master.EndTelegraph();
                master.Strike(0.16f, -120f, 80f);
                Services.Audio.PlaySfx("grunt_swing", 0.5f, 0.1f);
                yield return new WaitForSecondsRealtime(0.13f);

                // the student turns it
                student.SetPose(true, 95f, 0f, 6f);
                Vector2 spark = (Vector2)student.Root.position + new Vector2(0.85f, 1.0f);
                Services.Vfx.ParrySpark(spark, true);
                Services.Vfx.SlashArc(spark, 20f, false, Palette.PlayerSlash, 0.9f);
                Services.Audio.PlaySfx("parry_perfect", 0.75f);
                Services.Cam.Shake(0.22f);
                yield return new WaitForSecondsRealtime(0.45f);
                student.SetPose(false);
                yield return new WaitForSecondsRealtime(0.55f);
            }
        }

        // ---------------- 4: the gate, and what he became ----------------
        Panel PanelGate()
        {
            Vector2 o;
            var t = NewPanel(3, out o);

            IntroStage.Layer(t, "bg_boss_sky", new Vector2(0f, 2.5f), 46f, SortOrder.BgSky, new Color(0.8f, 0.55f, 0.55f));
            IntroStage.Layer(t, "bg_boss_far", new Vector2(0f, 0f), 34f, SortOrder.BgFar, new Color(0.3f, 0.18f, 0.22f));
            IntroStage.Layer(t, "fx_glow", new Vector2(0f, -2.2f), 22f, SortOrder.BgFar + 1, new Color(1f, 0.35f, 0.28f, 0.5f), true);
            IntroStage.Ground(t, new Vector2(0f, -4f), 30f);
            IntroStage.Prop(t, "prop_torii", new Vector2(0f, -4f), 1.25f, SortOrder.BgProps, new Color(0.4f, 0.22f, 0.24f));
            IntroStage.Prop(t, "prop_gate_sealed", new Vector2(0f, -4f), 1.15f, SortOrder.Props, new Color(0.5f, 0.45f, 0.5f));
            var glyph = IntroStage.Prop(t, "prop_gate_glyph", new Vector2(0f, -0.6f), 1.3f, SortOrder.Props + 2, Palette.Red, true);
            IntroStage.Prop(t, "prop_chain", new Vector2(-3.4f, 6f), 1f, SortOrder.BgProps + 1, new Color(0.4f, 0.35f, 0.4f));
            IntroStage.Prop(t, "prop_chain", new Vector2(3.6f, 6f), 1f, SortOrder.BgProps + 1, new Color(0.4f, 0.35f, 0.4f));

            var warden = IntroStage.Figure(t, IntroStage.WardenConfig(), new Vector2(-2.6f, -4f), 1,
                new Color(0.12f, 0.11f, 0.15f), 1f);
            // standing watch before the gate, slumped over the glaive. Legs stay on the walk cycle: angling
            // them at the hip without lowering the root lifts the feet off the ground and he floats.
            warden.SetPose(true, 30f, 180f, -12f);
            IntroStage.Ash(t, new Rect(-14f, -5f, 28f, 14f), 55f);

            return new Panel
            {
                Root = t,
                CamFrom = o + new Vector2(-1.4f, -1.8f), CamTo = o + new Vector2(0.2f, -0.4f),
                ZoomFrom = 3.9f, ZoomTo = 5.8f,
                Voice = "intro_4",
                Subtitle = "He has held that gate for a hundred years.\nThe seal is failing — and what is left of him has forgotten my name.",
                Beat = (p, dur) => GateBeat(warden, glyph, dur)
            };
        }

        IEnumerator GateBeat(EnemyRig warden, SpriteRenderer glyph, float duration)
        {
            yield return new WaitForSecondsRealtime(duration * 0.3f);
            // the mask wakes up
            if (warden.Light != null) { warden.Light.color = Palette.Teal; warden.Light.intensity = 2.4f; }
            warden.Flash(Palette.Teal, 0.6f);
            Services.Vfx.FlashLight((Vector2)warden.Root.position + new Vector2(0f, 1.6f), Palette.Teal, 2.5f, 3.5f, 0.7f);
            Services.Audio.PlaySfx("boss_hurt", 0.5f);

            yield return new WaitForSecondsRealtime(duration * 0.3f);
            // the seal cracks
            for (int i = 0; i < 3; i++)
            {
                Vector2 at = (Vector2)glyph.transform.position;
                Services.Vfx.HitSpark(at, Random.insideUnitCircle.normalized, Palette.Red, 1.2f);
                Services.Vfx.FlashLight(at, Palette.Red, 3f, 4f, 0.3f);
                Services.Audio.PlaySfx("enemy_telegraph_red", 0.4f, 0.2f);
                Services.Cam.Shake(0.25f);
                glyph.color = Palette.Red.WithAlpha(1f);
                yield return new WaitForSecondsRealtime(0.5f);
                glyph.color = Palette.Red.WithAlpha(0.45f);
                yield return new WaitForSecondsRealtime(0.4f);
            }
        }

        // ---------------- 5: tonight ----------------
        Panel PanelWalk()
        {
            Vector2 o;
            var t = NewPanel(4, out o);

            IntroStage.Layer(t, "bg_sky", new Vector2(0f, 2f), 46f, SortOrder.BgSky, new Color(0.6f, 0.54f, 0.68f));
            IntroStage.Layer(t, "bg_far", new Vector2(0f, 0.8f), 34f, SortOrder.BgFar, new Color(0.36f, 0.36f, 0.48f));
            IntroStage.Layer(t, "bg_mid", new Vector2(2f, -0.8f), 32f, SortOrder.BgMid, new Color(0.2f, 0.2f, 0.28f));
            IntroStage.Layer(t, "fx_glow", new Vector2(9.5f, -2f), 18f, SortOrder.BgMid + 1, new Color(1f, 0.35f, 0.3f, 0.45f), true);
            IntroStage.Ground(t, new Vector2(0f, -3.6f), 40f);
            for (int i = 0; i < 5; i++)
                IntroStage.Prop(t, "prop_lantern", new Vector2(-8f + i * 4.2f, 0.2f + (i % 2) * 0.4f), 1f, SortOrder.Props, Color.white);
            IntroStage.Prop(t, "prop_torii", new Vector2(6.5f, -3.6f), 1.15f, SortOrder.BgProps, new Color(0.3f, 0.16f, 0.18f));
            IntroStage.Prop(t, "prop_gate_sealed", new Vector2(6.5f, -3.6f), 1.05f, SortOrder.Props, new Color(0.22f, 0.19f, 0.22f));
            IntroStage.Prop(t, "prop_gate_glyph", new Vector2(6.5f, 0.1f), 1.1f, SortOrder.Props + 2, Palette.Red, true);
            IntroStage.Mist(t, new Rect(-14f, -4f, 30f, 4f), Palette.Bone, 12);
            IntroStage.Ash(t, new Rect(-14f, -4f, 30f, 14f), 40f);

            var hero = IntroStage.Figure(t, IntroStage.StudentConfig(), new Vector2(-6.2f, -3.6f), 1,
                new Color(0.2f, 0.19f, 0.25f), 1f);
            // a lantern-warm pool travelling with him so he never dissolves into the background
            var heroGlow = IntroStage.Layer(t, "fx_glow", new Vector2(-6.2f, -2.6f), 7f, SortOrder.BgNear + 1,
                new Color(1f, 0.55f, 0.3f, 0.35f), true);

            return new Panel
            {
                Root = t,
                CamFrom = o + new Vector2(-5.2f, -2.3f), CamTo = o + new Vector2(2.2f, -1.7f),
                ZoomFrom = 4.8f, ZoomTo = 5.8f,
                Voice = "intro_5",
                Subtitle = "Tonight I go through the gate —\nand give him the only answer he ever wanted.",
                Beat = (p, dur) => WalkBeat(hero, heroGlow, dur)
            };
        }

        IEnumerator WalkBeat(EnemyRig hero, SpriteRenderer glow, float duration)
        {
            // Move the figure that OWNS the rig: EnemyRig.Apply() rewrites Root.localPosition every
            // frame for the walk bob, so writing to Root here is overwritten instantly and the hero
            // ends up walking on the spot.
            var body = hero.Root.parent;
            var figure = IntroFigure.Of(hero);
            if (figure != null) figure.VelocityX = 2.6f;      // the walk cycle; the figure itself is moved below
            float t = 0f;
            var start = body.position;
            var glowStart = glow.transform.position;
            float step = 0f;
            while (t < duration)
            {
                float dt = Time.unscaledDeltaTime;
                t += dt;
                body.position = start + new Vector3(t * 1.15f, 0f, 0f);
                glow.transform.position = glowStart + new Vector3(t * 1.15f, 0f, 0f);
                step -= dt;
                if (step <= 0f)
                {
                    step = 0.42f;
                    Services.Audio.PlaySfx("footstep", 0.3f, 0.2f);
                    Services.Vfx.DustPuff((Vector2)body.position, 0.45f);
                }
                yield return null;
            }
        }
    }

    /// <summary>Gentle breathing pulse for the intro's sun discs.</summary>
    public class IntroPulse : MonoBehaviour
    {
        SpriteRenderer sr; float phase, amp, baseAlpha;
        public void Init(SpriteRenderer r, float ph, float amplitude, float alpha)
        {
            sr = r; phase = ph; amp = amplitude; baseAlpha = alpha;
        }
        void Update()
        {
            if (sr == null) return;
            float f = 1f + amp * Mathf.Sin(Time.unscaledTime * 0.8f + phase);
            var c = sr.color;
            sr.color = new Color(c.r, c.g, c.b, Mathf.Clamp01(baseAlpha * f));
        }
    }
}
