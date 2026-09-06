using UnityEngine;
using UnityEngine.Rendering.Universal;
using AshenSol.Core;

namespace AshenSol.Player
{
    /// <summary>Procedural puppet: joint pivots + sprites, posed every frame from the controller state.</summary>
    public class PlayerRig
    {
        enum Pose { None, Idle, Run, Jump, Fall, Dash, Parry, ParrySuccess, Hurt, Heal, QiBlast, Climb }

        public SpriteRenderer[] Renderers { get; private set; }
        public Transform Root { get; private set; }

        Transform torso, head, armBack, armFront, sword, legBack, legFront;
        SpriteRenderer[] overlays;
        SpriteRenderer glyph;
        Light2D swordLight;
        SashChain sash;
        Transform sashAnchor;

        // current joint state
        float torsoA, headA, armBackA, armFrontA, bladeA, legBackA, legFrontA, bobY, torsoY;
        Vector2 scale = Vector2.one, scaleVel;
        float runPhase;
        int facing = 1;

        // overrides
        bool attackActive; int attackIdx; float attackT, attackDur;
        Pose forced = Pose.None; float forcedTimer;
        bool visible = true;
        float flashT, flashDur; Color flashColor;
        const float LightBase = 0.7f; float lightPulse, lightPulseT, lightPulseDur;
        float glyphPulse;

        public static PlayerRig Build(PlayerController c)
        {
            var rig = new PlayerRig();
            var root = new GameObject("Rig").transform;
            root.SetParent(c.transform, false);
            rig.Root = root;

            var renderers = new SpriteRenderer[7];
            var overlays = new SpriteRenderer[7];
            int n = 0;

            rig.legBack = Pivot(root, "legBack", new Vector2(-0.07f, 0.62f));
            renderers[n] = Part(rig.legBack, "player_leg_back", new Vector2(0f, -0.27f), SortOrder.PlayerBack, out overlays[n]); n++;
            rig.legFront = Pivot(root, "legFront", new Vector2(0.07f, 0.62f));
            renderers[n] = Part(rig.legFront, "player_leg_front", new Vector2(0f, -0.27f), SortOrder.Player + 2, out overlays[n]); n++;
            rig.torso = Pivot(root, "torso", new Vector2(0f, 0.60f));
            renderers[n] = Part(rig.torso, "player_torso", new Vector2(0f, 0.31f), SortOrder.Player, out overlays[n]); n++;
            rig.head = Pivot(rig.torso, "head", new Vector2(0.02f, 0.60f));
            renderers[n] = Part(rig.head, "player_head", new Vector2(0f, 0.20f), SortOrder.Player + 1, out overlays[n]); n++;
            rig.armBack = Pivot(rig.torso, "armBack", new Vector2(-0.08f, 0.54f));
            renderers[n] = Part(rig.armBack, "player_arm_back", new Vector2(0f, -0.22f), SortOrder.PlayerBack + 2, out overlays[n]); n++;
            rig.armFront = Pivot(rig.torso, "armFront", new Vector2(0.09f, 0.54f));
            renderers[n] = Part(rig.armFront, "player_arm_front", new Vector2(0f, -0.22f), SortOrder.PlayerFront, out overlays[n]); n++;
            rig.sword = Pivot(rig.armFront, "sword", new Vector2(0f, -0.42f));
            renderers[n] = Part(rig.sword, "player_sword", new Vector2(0f, 0.30f), SortOrder.PlayerFront + 1, out overlays[n]); n++;

            var lightGo = new GameObject("SwordLight");
            lightGo.transform.SetParent(rig.sword, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            rig.swordLight = lightGo.AddComponent<Light2D>();
            rig.swordLight.lightType = Light2D.LightType.Point;
            rig.swordLight.pointLightOuterRadius = 1.7f;
            rig.swordLight.pointLightInnerRadius = 0.15f;
            rig.swordLight.intensity = LightBase;
            rig.swordLight.color = Palette.Teal;
            rig.swordLight.falloffIntensity = 0.6f;

            var glyphGo = new GameObject("ParryGlyph");
            glyphGo.transform.SetParent(root, false);
            glyphGo.transform.localPosition = new Vector3(0.95f, 0.95f, 0f);
            rig.glyph = glyphGo.AddComponent<SpriteRenderer>();
            rig.glyph.sprite = Res.Sprite("fx_parry_glyph");
            rig.glyph.sortingOrder = SortOrder.PlayerFront + 3;
            rig.glyph.material = MaterialLibrary.Additive;
            rig.glyph.color = Palette.Teal.WithAlpha(0f);
            rig.glyph.enabled = false;

            rig.sashAnchor = Pivot(rig.torso, "sashAnchor", new Vector2(-0.12f, 0.50f));
            rig.sash = SashChain.Build(c.transform, rig.sashAnchor, 7, Palette.Red, "player_sash_seg", 0.13f, SortOrder.PlayerBack - 1, 1f, 0.45f);

            rig.Renderers = renderers;
            rig.overlays = overlays;
            rig.ResetPose();
            return rig;
        }

        static Transform Pivot(Transform parent, string name, Vector2 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }

        static SpriteRenderer Part(Transform pivot, string sprite, Vector2 offset, int sort, out SpriteRenderer overlay)
        {
            var go = new GameObject(sprite);
            go.transform.SetParent(pivot, false);
            go.transform.localPosition = offset;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Res.Sprite(sprite);
            sr.sortingOrder = sort;
            var og = new GameObject("flash");
            og.transform.SetParent(go.transform, false);
            overlay = og.AddComponent<SpriteRenderer>();
            overlay.sprite = sr.sprite;
            overlay.sortingOrder = sort + 1;
            overlay.material = MaterialLibrary.Silhouette;
            overlay.color = new Color(1f, 1f, 1f, 0f);
            overlay.enabled = false;
            return sr;
        }

        // ---------------- public controls ----------------
        public void ResetPose()
        {
            torsoA = headA = armBackA = legBackA = legFrontA = 0f; armFrontA = 20f; bladeA = -160f; bobY = torsoY = 0f;
            scale = Vector2.one; scaleVel = Vector2.zero;
            attackActive = false; forced = Pose.None; forcedTimer = 0f; flashT = 0f;
            lightPulse = 0f;
            if (sash != null) sash.Reset();
            HideParryGlyph();
            Apply();
        }

        public void SetFacing(int f) { facing = f; }

        public void SetVisible(bool v)
        {
            if (visible == v) return;
            visible = v;
            for (int i = 0; i < Renderers.Length; i++) { Renderers[i].enabled = v; if (!v) overlays[i].enabled = false; }
            if (sash != null) sash.SetVisible(v);
            if (!v) glyph.enabled = false;
            swordLight.enabled = v;
        }

        public void Squash() { scale = new Vector2(1.18f, 0.82f); }
        public void Stretch() { scale = new Vector2(0.86f, 1.16f); }
        public void Dash() { scale = new Vector2(1.25f, 0.8f); }
        public void Hurt() { forced = Pose.Hurt; forcedTimer = 0.22f; scale = new Vector2(0.9f, 1.1f); }

        public void PlayAttack(int idx, float duration)
        {
            attackActive = true; attackIdx = idx; attackT = 0f; attackDur = Mathf.Max(0.01f, duration);
            forced = Pose.None;
        }
        public void EndAttack() { attackActive = false; }

        public void PlayParry() { forced = Pose.Parry; forcedTimer = 99f; }
        public void ParrySuccessPose(bool perfect)
        {
            forced = Pose.ParrySuccess; forcedTimer = 0.12f;
            scale = new Vector2(1.08f, 0.94f);
            glyph.enabled = true;
            glyph.color = (perfect ? Color.white : Palette.Amber).WithAlpha(0.9f);
            glyph.transform.localScale = Vector3.one * (perfect ? 1.5f : 1.15f);
            glyphPulse = 0.12f;
        }
        public void EndParry() { if (forced == Pose.Parry || forced == Pose.ParrySuccess) forced = Pose.None; }

        public void ShowParryGlyph(bool blockPhase)
        {
            glyph.enabled = true;
            glyph.transform.localScale = Vector3.one;
            glyph.color = blockPhase ? Palette.Amber.WithAlpha(0.4f) : Palette.Teal.WithAlpha(0.6f);
            glyphPulse = 0f;
        }
        public void HideParryGlyph() { if (glyph != null) glyph.enabled = false; }

        public void PlayHeal() { forced = Pose.Heal; forcedTimer = 99f; }
        public void EndHeal() { if (forced == Pose.Heal) forced = Pose.None; }
        public void PlayQiBlast() { forced = Pose.QiBlast; forcedTimer = 99f; scale = new Vector2(1.1f, 0.95f); }
        public void EndQiBlast() { if (forced == Pose.QiBlast) forced = Pose.None; }

        public void Flash(Color color, float seconds)
        {
            flashColor = color; flashDur = Mathf.Max(0.01f, seconds); flashT = flashDur;
        }

        public void PulseSwordLight(float intensity, float seconds)
        {
            lightPulse = intensity; lightPulseDur = Mathf.Max(0.01f, seconds); lightPulseT = lightPulseDur;
        }

        // ---------------- per-frame ----------------
        public void Animate(PlayerController c, float dt)
        {
            float udt = Time.unscaledDeltaTime;
            bool grounded = c.IsGrounded;
            Vector2 v = c.Velocity;
            float speed01 = Mathf.Clamp01(Mathf.Abs(v.x) / PlayerTuning.MaxSpeed);

            if (forcedTimer < 90f && forced != Pose.None) { forcedTimer -= dt; if (forcedTimer <= 0f) forced = Pose.None; }

            Pose pose;
            if (c.IsDead) pose = Pose.Idle;
            else if (forced != Pose.None) pose = forced;
            else if (c.IsClimbing) pose = Pose.Climb;
            else if (c.IsDashing) pose = Pose.Dash;
            else if (!grounded) pose = v.y > 0.5f ? Pose.Jump : Pose.Fall;
            else if (Mathf.Abs(v.x) > 0.6f && c.HorizontalInput != 0f) pose = Pose.Run;
            else pose = Pose.Idle;

            float t = Time.time;
            float tTorso = 0f, tHead = 0f, tArmB = -8f, tArmF = 20f, tBlade = -160f, tLegB = 0f, tLegF = 0f, tBob = 0f, tTorsoY = 0f;
            Vector2 tScale = Vector2.one;
            float rate = 18f;

            switch (pose)
            {
                case Pose.Idle:
                    tBob = Mathf.Sin(t * 1.2f * Mathf.PI * 2f) * 0.025f;
                    tArmF = 20f + Mathf.Sin(t * 1.2f * Mathf.PI * 2f) * 4f;
                    tArmB = -8f - Mathf.Sin(t * 1.2f * Mathf.PI * 2f) * 3f;
                    tHead = Mathf.Sin(t * 0.7f) * 2f;
                    break;
                case Pose.Run:
                    runPhase += dt * 9f * Mathf.Max(speed01, 0.5f);
                    tLegF = Mathf.Sin(runPhase) * 38f;
                    tLegB = -tLegF;
                    tArmF = -Mathf.Sin(runPhase) * 22f + 18f;
                    tArmB = Mathf.Sin(runPhase) * 26f - 6f;
                    tTorso = -8f;
                    tBob = Mathf.Abs(Mathf.Sin(runPhase)) * 0.05f;
                    tHead = 3f;
                    rate = 30f;
                    break;
                case Pose.Jump:
                    tLegF = -30f; tLegB = 22f; tArmF = 55f; tArmB = -55f; tTorso = -6f; tBlade = -150f;
                    break;
                case Pose.Fall:
                    tLegF = 28f; tLegB = -22f; tArmF = 75f; tArmB = -65f; tTorso = 5f; tBlade = -140f;
                    break;
                case Pose.Dash:
                    tTorso = -28f; tLegF = 42f; tLegB = -42f; tArmF = 95f; tArmB = -40f; tBlade = -90f; tHead = 6f;
                    rate = 40f;
                    break;
                case Pose.Parry:
                    tTorso = 6f; tArmF = 95f; tBlade = 0f; tArmB = -30f; tLegF = 14f; tLegB = -10f; tTorsoY = -0.04f; tHead = -4f;
                    rate = 40f;
                    break;
                case Pose.ParrySuccess:
                    tTorso = 10f; tArmF = 60f; tBlade = 30f; tArmB = -40f; tLegF = 18f; tLegB = -14f;
                    rate = 40f;
                    break;
                case Pose.Hurt:
                    tTorso = 16f; tArmF = -45f; tArmB = 55f; tLegF = -10f; tLegB = 15f; tHead = 12f; tBlade = -200f;
                    rate = 30f;
                    break;
                case Pose.Heal:
                    tTorso = -12f; tLegF = 62f; tLegB = -74f; tTorsoY = -0.28f; tArmF = 42f; tArmB = 30f; tBlade = -175f; tHead = 10f;
                    rate = 14f;
                    break;
                case Pose.Climb:
                    // hanging on the wall: arms up, legs tucked, sword held close
                    tArmF = 150f; tArmB = 120f; tLegF = 30f; tLegB = -22f; tTorso = 4f; tBlade = -30f; tHead = -6f;
                    tBob = Mathf.Sin(t * 5f) * 0.03f;
                    rate = 16f;
                    break;
                case Pose.QiBlast:
                    tTorso = -12f; tArmF = 88f; tArmB = 82f; tBlade = -95f; tLegF = 24f; tLegB = -18f; tHead = -6f;
                    rate = 40f;
                    break;
            }

            // attack override drives the sword arm directly
            if (attackActive)
            {
                attackT += dt;
                float p = Ease.OutCubic(attackT / attackDur);
                float from, to;
                switch (attackIdx)
                {
                    case 0: from = -110f; to = 70f; break;
                    case 1: from = 95f; to = -70f; break;
                    default: from = -160f; to = 200f; break;
                }
                float a = Mathf.Lerp(from, to, p);
                armFrontA = a;
                bladeA = a + 180f;
                tTorso = -16f + (attackIdx == 2 ? -6f : 0f);
                tLegF = 20f; tLegB = -16f; tArmB = -30f;
                tBob = 0f;
                if (attackT > attackDur + 0.35f) attackActive = false;
            }

            float k = 1f - Mathf.Exp(-rate * dt);
            torsoA = Mathf.LerpAngle(torsoA, tTorso, k);
            headA = Mathf.LerpAngle(headA, tHead, k);
            armBackA = Mathf.LerpAngle(armBackA, tArmB, k);
            if (!attackActive)
            {
                armFrontA = Mathf.LerpAngle(armFrontA, tArmF, k);
                bladeA = Mathf.LerpAngle(bladeA, tBlade, k);
            }
            legBackA = Mathf.LerpAngle(legBackA, tLegB, pose == Pose.Run ? 1f : k);
            legFrontA = Mathf.LerpAngle(legFrontA, tLegF, pose == Pose.Run ? 1f : k);
            bobY = Mathf.Lerp(bobY, tBob, k);
            torsoY = Mathf.Lerp(torsoY, tTorsoY, k);
            scale = Vector2.SmoothDamp(scale, tScale, ref scaleVel, 0.08f, 100f, dt);

            // flash overlay
            if (flashT > 0f)
            {
                flashT -= udt;
                float a = Mathf.Clamp01(flashT / flashDur) * flashColor.a;
                SetOverlay(flashColor, a);
            }
            else SetOverlay(flashColor, 0f);

            // sword light
            float li = LightBase;
            if (lightPulseT > 0f) { lightPulseT -= udt; li = Mathf.Lerp(LightBase, lightPulse, Ease.OutCubic(lightPulseT / lightPulseDur)); }
            if (pose == Pose.Parry) li = Mathf.Max(li, 1.4f);
            if (pose == Pose.Heal) li = Mathf.Max(li, 1.6f);
            swordLight.intensity = li;

            // glyph pulse
            if (glyph.enabled)
            {
                if (glyphPulse > 0f)
                {
                    glyphPulse -= udt;
                    glyph.transform.localScale = Vector3.one * (1.1f + 0.5f * Mathf.Clamp01(glyphPulse / 0.12f));
                }
                else glyph.transform.localRotation = Quaternion.Euler(0f, 0f, t * 40f);
            }

            Apply();
            if (sash != null) sash.Tick(dt, facing, v);
        }

        void SetOverlay(Color color, float alpha)
        {
            bool on = alpha > 0.005f && visible;
            for (int i = 0; i < overlays.Length; i++)
            {
                overlays[i].enabled = on;
                if (on) overlays[i].color = new Color(color.r, color.g, color.b, alpha);
            }
        }

        void Apply()
        {
            Root.localScale = new Vector3(facing * scale.x, scale.y, 1f);
            Root.localPosition = new Vector3(0f, bobY, 0f);
            torso.localPosition = new Vector3(0f, 0.60f + torsoY, 0f);
            torso.localRotation = Quaternion.Euler(0f, 0f, torsoA);
            head.localRotation = Quaternion.Euler(0f, 0f, headA);
            armBack.localRotation = Quaternion.Euler(0f, 0f, armBackA);
            armFront.localRotation = Quaternion.Euler(0f, 0f, armFrontA);
            sword.localRotation = Quaternion.Euler(0f, 0f, bladeA - armFrontA);
            legBack.localRotation = Quaternion.Euler(0f, 0f, legBackA);
            legFront.localRotation = Quaternion.Euler(0f, 0f, legFrontA);
        }
    }
}
