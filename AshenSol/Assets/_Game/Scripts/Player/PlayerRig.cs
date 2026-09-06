using UnityEngine;
using UnityEngine.Rendering.Universal;
using AshenSol.Core;

namespace AshenSol.Player
{
    /// <summary>Procedural puppet with knees and elbows: joint pivots + sprites, posed every frame from the
    /// controller state. Locomotion and stances are pose targets the joints ease toward; attacks are keyframed
    /// clips (anticipation, contact, follow-through, settle) that start from whatever the body is doing.
    /// Knees only bend backwards (negative), elbows only forwards (positive).</summary>
    public class PlayerRig
    {
        enum Pose { None, Idle, Run, Jump, Fall, Dash, Parry, Guard, ParrySuccess, Hurt, HurtBack, Heal, QiBlast, Climb, Charge }

        /// <summary>Joint targets for one moment. Angles in degrees, Bob/TorsoY in units.
        /// NaN on a leg or knee means "leave the legs to locomotion" (air attacks).</summary>
        struct JointPose
        {
            public float Torso, Head, ArmBack, ArmFront, ElbowBack, ElbowFront, Blade, LegBack, LegFront, KneeBack, KneeFront, Bob, TorsoY;

            /// <summary>Arm and blade lerp linearly so a clip can spin the sword all the way round.</summary>
            public static JointPose Lerp(in JointPose a, in JointPose b, float t)
            {
                JointPose r;
                r.Torso = Mathf.LerpAngle(a.Torso, b.Torso, t);
                r.Head = Mathf.LerpAngle(a.Head, b.Head, t);
                r.ArmBack = Mathf.LerpAngle(a.ArmBack, b.ArmBack, t);
                r.ArmFront = Mathf.Lerp(a.ArmFront, b.ArmFront, t);
                r.ElbowBack = Mathf.LerpAngle(a.ElbowBack, b.ElbowBack, t);
                r.ElbowFront = Mathf.LerpAngle(a.ElbowFront, b.ElbowFront, t);
                r.Blade = Mathf.Lerp(a.Blade, b.Blade, t);
                r.LegBack = LerpLeg(a.LegBack, b.LegBack, t);
                r.LegFront = LerpLeg(a.LegFront, b.LegFront, t);
                r.KneeBack = LerpLeg(a.KneeBack, b.KneeBack, t);
                r.KneeFront = LerpLeg(a.KneeFront, b.KneeFront, t);
                r.Bob = Mathf.Lerp(a.Bob, b.Bob, t);
                r.TorsoY = Mathf.Lerp(a.TorsoY, b.TorsoY, t);
                return r;
            }

            static float LerpLeg(float a, float b, float t)
            {
                if (float.IsNaN(b)) return float.NaN;
                if (float.IsNaN(a)) return b;
                return Mathf.LerpAngle(a, b, t);
            }
        }

        struct ClipKey
        {
            public float Time; public JointPose Pose; public System.Func<float, float> Ease;
            public ClipKey(float time, JointPose pose, System.Func<float, float> ease) { Time = time; Pose = pose; Ease = ease; }
        }

        static readonly System.Func<float, float> EaseOutQuad = Ease.OutQuad;
        static readonly System.Func<float, float> EaseOutCubic = Ease.OutCubic;
        static readonly System.Func<float, float> EaseInOutSine = Ease.InOutSine;

        public SpriteRenderer[] Renderers { get; private set; }
        public Transform Root { get; private set; }

        Transform torso, head, armBack, armFront, elbowBack, elbowFront, sword, legBack, legFront, kneeBack, kneeFront;
        SpriteRenderer[] overlays;
        SpriteRenderer glyph;
        Light2D swordLight;
        TrailRenderer trail;
        SashChain sash;
        Transform sashAnchor;

        // current joint state
        float torsoA, headA, armBackA, armFrontA, elbowBackA, elbowFrontA, bladeA, legBackA, legFrontA, kneeBackA, kneeFrontA, bobY, torsoY, plant;

        /// <summary>Resting blade angle. The sprite points up at 0, so this aims the tip a little under
        /// the horizon and forward: carried at a low guard rather than hanging off the wrist.</summary>
        const float BladeCarry = -112f;
        const float Stride = 4.6f;            // ground covered per run cycle (stylised: slides a little)
        const float TurnSeconds = 0.08f;      // the mirror blends through thin instead of snapping
        const float LowerZ = -0.002f;         // lower segments sit a hair closer to the camera: they win sorting ties at the joint
        // limb geometry (units): joint distances, and the sprite heights that overlap the joints a little
        const float UpperLegLen = 0.26f, LowerLegLen = 0.28f, UpperArmLen = 0.22f, LowerArmLen = 0.20f;
        const float UpperLegH = 0.30f, LowerLegH = 0.28f, UpperArmH = 0.26f, LowerArmH = 0.24f;
        Vector2 scale = Vector2.one, scaleVel;
        float runPhase, climbPhase;
        int facing = 1; float facingBlend = 1f; bool everAnimated;

        // attack clip
        readonly ClipKey[] clip = new ClipKey[4];
        int clipCount; float clipT; JointPose clipStart; bool clipActive; float clipTrailFrom, clipTrailUntil;

        // overrides
        Pose forced = Pose.None; float forcedTimer;
        bool visible = true; float dim = 1f;
        float flashT, flashDur; Color flashColor;
        const float LightBase = 0.7f; float lightPulse, lightPulseT, lightPulseDur;
        float glyphPulse;

        public static PlayerRig Build(PlayerController c)
        {
            var rig = new PlayerRig();
            var root = new GameObject("Rig").transform;
            root.SetParent(c.transform, false);
            rig.Root = root;

            var renderers = new SpriteRenderer[11];
            var overlays = new SpriteRenderer[11];
            int n = 0;

            rig.legBack = Pivot(root, "legBack", new Vector2(-0.07f, 0.62f));
            renderers[n] = Part(rig.legBack, "player_leg_back_upper", new Vector2(0f, -UpperLegH * 0.5f), SortOrder.PlayerBack, out overlays[n]); n++;
            rig.kneeBack = Pivot(rig.legBack, "knee", new Vector2(0f, -UpperLegLen));
            renderers[n] = Part(rig.kneeBack, "player_leg_back_lower", new Vector2(0f, -LowerLegH * 0.5f), SortOrder.PlayerBack, out overlays[n], LowerZ); n++;
            rig.legFront = Pivot(root, "legFront", new Vector2(0.07f, 0.62f));
            renderers[n] = Part(rig.legFront, "player_leg_front_upper", new Vector2(0f, -UpperLegH * 0.5f), SortOrder.Player + 2, out overlays[n]); n++;
            rig.kneeFront = Pivot(rig.legFront, "knee", new Vector2(0f, -UpperLegLen));
            renderers[n] = Part(rig.kneeFront, "player_leg_front_lower", new Vector2(0f, -LowerLegH * 0.5f), SortOrder.Player + 2, out overlays[n], LowerZ); n++;
            rig.torso = Pivot(root, "torso", new Vector2(0f, 0.60f));
            renderers[n] = Part(rig.torso, "player_torso", new Vector2(0f, 0.31f), SortOrder.Player, out overlays[n]); n++;
            rig.head = Pivot(rig.torso, "head", new Vector2(0.02f, 0.60f));
            renderers[n] = Part(rig.head, "player_head", new Vector2(0f, 0.20f), SortOrder.Player + 1, out overlays[n]); n++;
            rig.armBack = Pivot(rig.torso, "armBack", new Vector2(-0.08f, 0.54f));
            renderers[n] = Part(rig.armBack, "player_arm_back_upper", new Vector2(0f, -UpperArmH * 0.5f), SortOrder.PlayerBack + 2, out overlays[n]); n++;
            rig.elbowBack = Pivot(rig.armBack, "elbow", new Vector2(0f, -UpperArmLen));
            renderers[n] = Part(rig.elbowBack, "player_arm_back_lower", new Vector2(0f, -LowerArmH * 0.5f), SortOrder.PlayerBack + 2, out overlays[n], LowerZ); n++;
            rig.armFront = Pivot(rig.torso, "armFront", new Vector2(0.09f, 0.54f));
            renderers[n] = Part(rig.armFront, "player_arm_front_upper", new Vector2(0f, -UpperArmH * 0.5f), SortOrder.PlayerFront, out overlays[n]); n++;
            rig.elbowFront = Pivot(rig.armFront, "elbow", new Vector2(0f, -UpperArmLen));
            renderers[n] = Part(rig.elbowFront, "player_arm_front_lower", new Vector2(0f, -LowerArmH * 0.5f), SortOrder.PlayerFront, out overlays[n], LowerZ); n++;
            rig.sword = Pivot(rig.elbowFront, "sword", new Vector2(0f, -LowerArmLen));
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

            // the streak behind the tip: on only while a swing is actually travelling
            var tipGo = new GameObject("tip");
            tipGo.transform.SetParent(rig.sword, false);
            tipGo.transform.localPosition = new Vector3(0f, 0.76f, 0f);
            rig.trail = tipGo.AddComponent<TrailRenderer>();
            rig.trail.sharedMaterial = AshenSol.VFX.VfxManager.AdditiveFor(Res.Sprite("fx_glow"));
            rig.trail.time = 0.1f;
            rig.trail.minVertexDistance = 0.03f;
            rig.trail.widthMultiplier = 0.16f;
            rig.trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            rig.trail.numCornerVertices = 5;
            rig.trail.numCapVertices = 5;
            rig.trail.sortingOrder = SortOrder.Fx + 7;
            rig.trail.startColor = Palette.PlayerSlash.WithAlpha(0.9f);
            rig.trail.endColor = Palette.PlayerSlash.WithAlpha(0f);
            rig.trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rig.trail.receiveShadows = false;
            rig.trail.autodestruct = false;
            rig.trail.emitting = false;

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

        static SpriteRenderer Part(Transform pivot, string sprite, Vector2 offset, int sort, out SpriteRenderer overlay, float z = 0f)
        {
            var go = new GameObject(sprite);
            go.transform.SetParent(pivot, false);
            go.transform.localPosition = new Vector3(offset.x, offset.y, z);
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
            torsoA = headA = armBackA = legBackA = legFrontA = 0f; armFrontA = 20f; bladeA = BladeCarry; bobY = torsoY = 0f;
            kneeBackA = kneeFrontA = -3f; elbowFrontA = 16f; elbowBackA = 10f;
            plant = PlantDrop(legFrontA, kneeFrontA, legBackA, kneeBackA);
            scale = Vector2.one; scaleVel = Vector2.zero;
            clipActive = false; forced = Pose.None; forcedTimer = 0f; flashT = 0f;
            lightPulse = 0f;
            facingBlend = facing;
            if (trail != null) { trail.emitting = false; trail.Clear(); }
            if (sash != null) sash.Reset();
            HideParryGlyph();
            Apply();
        }

        public void SetFacing(int f)
        {
            facing = f;
            if (!everAnimated) facingBlend = facing;
        }

        public void SetVisible(bool v)
        {
            if (visible == v) return;
            visible = v;
            for (int i = 0; i < Renderers.Length; i++) { Renderers[i].enabled = v; if (!v) overlays[i].enabled = false; }
            if (sash != null) sash.SetVisible(v);
            if (!v) glyph.enabled = false;
            if (!v && trail != null) { trail.emitting = false; trail.Clear(); }
            swordLight.enabled = v;
        }

        /// <summary>Body opacity for the i-frame pulse. The sword light stays on, so the bloom does not strobe.</summary>
        public void SetDim(float a)
        {
            a = Mathf.Clamp01(a);
            if (Mathf.Abs(a - dim) < 0.001f) return;
            dim = a;
            var c = new Color(1f, 1f, 1f, a);
            for (int i = 0; i < Renderers.Length; i++) Renderers[i].color = c;
            if (sash != null) sash.SetAlpha(a);
        }

        public void Squash() { scale = new Vector2(1.18f, 0.82f); }
        public void Stretch() { scale = new Vector2(0.86f, 1.16f); }
        public void Dash() { scale = new Vector2(1.25f, 0.8f); }
        /// <summary>fromFront: the blow came from where he is facing, so he recoils backwards; otherwise he is thrown forward.</summary>
        public void Hurt(bool fromFront = true) { forced = fromFront ? Pose.Hurt : Pose.HurtBack; forcedTimer = 0.22f; scale = new Vector2(0.9f, 1.1f); }

        /// <summary>Keyframed swing. idx 0-2 = the combo, 3 = the charged overhead. The clip starts from the
        /// current pose: anticipation over the first part of the startup, the sweep arriving in the target
        /// once the hitbox is open, follow-through and settle across the recovery. The elbow folds in the
        /// wind-up and snaps straight through the contact, which is what makes a cut read as a cut.</summary>
        public void PlayAttack(int idx, float startup, float active, float recovery, bool grounded)
        {
            forced = Pose.None;
            startup = Mathf.Max(0.02f, startup); active = Mathf.Max(0.02f, active); recovery = Mathf.Max(0.05f, recovery);
            float from, to;
            switch (idx)
            {
                case 0: from = -110f; to = 70f; break;
                case 1: from = 95f; to = -70f; break;
                case 2: from = -160f; to = 200f; break;
                default: from = 190f; to = 60f; break;      // the heavy: coiled over the shoulder, down through the front
            }
            float dir = to >= from ? 1f : -1f;
            bool heavy = idx == 3, big = idx >= 2;
            float legF = grounded ? 20f : float.NaN, legB = grounded ? -16f : float.NaN;
            float kneeF = grounded ? -18f : float.NaN, kneeB = grounded ? -6f : float.NaN;
            // the heavy is hit-stopped long and hard, so its blade must already be deep in the target on
            // the first active frame; the light hits cross it during the window
            float tWind = startup * 0.45f;
            float tContact = startup + active * (heavy ? 0.25f : 0.5f);
            float tFollow = startup + active + recovery * 0.4f;
            float tSettle = startup + active + recovery;
            float wArm = from - dir * 12f, fArm = to + dir * 14f, sArm = to - dir * 25f;
            float wElbow = heavy ? 80f : 70f, cElbow = heavy ? 4f : 8f;
            clip[0] = new ClipKey(tWind, new JointPose { Torso = heavy ? 14f : 8f, Head = -4f, ArmBack = heavy ? 190f : -30f, ElbowBack = heavy ? 25f : 20f, ArmFront = wArm, ElbowFront = wElbow, Blade = wArm + wElbow + 180f, LegBack = legB, LegFront = legF, KneeBack = kneeB, KneeFront = kneeF, Bob = 0f, TorsoY = heavy ? -0.05f : 0f }, EaseOutQuad);
            clip[1] = new ClipKey(tContact, new JointPose { Torso = big ? -22f : -16f, Head = 4f, ArmBack = heavy ? 60f : -30f, ElbowBack = heavy ? 10f : 20f, ArmFront = to, ElbowFront = cElbow, Blade = to + cElbow + 180f, LegBack = legB, LegFront = legF, KneeBack = kneeB, KneeFront = kneeF, Bob = 0f, TorsoY = heavy ? -0.08f : -0.02f }, heavy ? EaseOutCubic : EaseOutQuad);
            clip[2] = new ClipKey(tFollow, new JointPose { Torso = big ? -26f : -20f, Head = 6f, ArmBack = heavy ? 50f : -34f, ElbowBack = 22f, ArmFront = fArm, ElbowFront = 24f, Blade = fArm + 24f + 180f, LegBack = legB, LegFront = legF, KneeBack = kneeB, KneeFront = kneeF, Bob = 0f, TorsoY = heavy ? -0.06f : -0.03f }, EaseOutCubic);
            clip[3] = new ClipKey(tSettle, new JointPose { Torso = -8f, Head = 2f, ArmBack = -14f, ElbowBack = 12f, ArmFront = sArm, ElbowFront = 18f, Blade = sArm + 18f + 180f, LegBack = legB, LegFront = legF, KneeBack = kneeB, KneeFront = kneeF, Bob = 0f, TorsoY = 0f }, EaseInOutSine);
            clipCount = 4;
            clipStart = CurrentPose();
            // take the shortest way into the anticipation from wherever the arm happens to be
            clipStart.ArmFront = wArm + Mathf.DeltaAngle(wArm, clipStart.ArmFront);
            clipStart.Blade = clip[0].Pose.Blade + Mathf.DeltaAngle(clip[0].Pose.Blade, clipStart.Blade);
            clipT = 0f; clipActive = true;
            clipTrailFrom = tWind; clipTrailUntil = tContact + 0.08f;
            if (trail != null) trail.Clear();
        }

        public void EndAttack()
        {
            if (forced == Pose.Charge) forced = Pose.None;
            if (!clipActive) return;
            clipActive = false;
            if (trail != null) trail.emitting = false;
            armFrontA = Mathf.DeltaAngle(0f, armFrontA);
            bladeA = Mathf.DeltaAngle(0f, bladeA);
        }

        public void PlayParry() { forced = Pose.Parry; forcedTimer = 99f; }
        /// <summary>Held wind-up for the charged strike: blade drawn back over the shoulder.</summary>
        public void PlayCharge() { EndAttack(); forced = Pose.Charge; forcedTimer = 99f; }
        public void ParrySuccessPose(bool perfect)
        {
            forced = Pose.ParrySuccess; forcedTimer = 0.12f;
            scale = new Vector2(1.08f, 0.94f);
            glyph.enabled = true;
            glyph.color = (perfect ? Color.white : Palette.Amber).WithAlpha(0.9f);
            glyph.transform.localScale = Vector3.one * (perfect ? 1.5f : 1.15f);
            glyphPulse = 0.12f;
        }
        public void EndParry() { if (forced == Pose.Parry || forced == Pose.Guard || forced == Pose.ParrySuccess) forced = Pose.None; }

        /// <summary>The stance has two halves and the body shows which one it is in: blade high and bright for the
        /// perfect window, sunk into a lower guard for the block window.</summary>
        public void ShowParryGlyph(bool blockPhase)
        {
            glyph.enabled = true;
            glyph.transform.localScale = Vector3.one;
            glyph.color = blockPhase ? Palette.Amber.WithAlpha(0.4f) : Palette.Teal.WithAlpha(0.6f);
            glyphPulse = 0f;
            if (forced == Pose.Parry || forced == Pose.Guard) forced = blockPhase ? Pose.Guard : Pose.Parry;
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
            everAnimated = true;
            float udt = Time.unscaledDeltaTime;
            bool grounded = c.IsGrounded;
            Vector2 v = c.Velocity;
            facingBlend = Mathf.MoveTowards(facingBlend, facing, dt / TurnSeconds);

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
            float tTorso = 0f, tHead = 0f, tArmB = -8f, tArmF = 20f, tBlade = BladeCarry, tLegB = 0f, tLegF = 0f, tBob = 0f, tTorsoY = 0f;
            float tKneeB = -3f, tKneeF = -3f, tElbowB = 10f, tElbowF = 16f;
            float rate = 18f;
            bool snapLegs = false;

            switch (pose)
            {
                case Pose.Idle:
                    tBob = Mathf.Sin(t * 1.2f * Mathf.PI * 2f) * 0.025f;
                    tArmF = 20f + Mathf.Sin(t * 1.2f * Mathf.PI * 2f) * 4f;
                    tElbowF = 16f + Mathf.Sin(t * 1.2f * Mathf.PI * 2f) * 3f;
                    tArmB = -8f - Mathf.Sin(t * 1.2f * Mathf.PI * 2f) * 3f;
                    tBlade = BladeCarry - Mathf.Sin(t * 1.2f * Mathf.PI * 2f) * 3f;   // the tip breathes with him
                    tHead = Mathf.Sin(t * 0.7f) * 2f;
                    break;
                case Pose.Run:
                {
                    // the cycle advances with the ground covered, not with time: no sliding while he accelerates or brakes
                    runPhase += Mathf.Abs(v.x) * dt * (Mathf.PI * 2f / Stride);
                    float s = Mathf.Sin(runPhase), cs = Mathf.Cos(runPhase);
                    tLegF = s * 38f;
                    tLegB = -tLegF;
                    // the swinging leg folds at the knee to clear the ground, the planted leg drives straight
                    tKneeF = -72f * Mathf.Max(0f, cs);
                    tKneeB = -72f * Mathf.Max(0f, -cs);
                    tArmF = -s * 22f + 18f;
                    tArmB = s * 26f - 6f;
                    tElbowF = 55f + 15f * s;
                    tElbowB = 55f - 15f * s;
                    tTorso = -8f;                    // the bob comes from the planted feet below
                    tHead = 3f;
                    rate = 30f;
                    snapLegs = true;
                    break;
                }
                case Pose.Jump:
                    tLegF = -30f; tLegB = 22f; tKneeF = -35f; tKneeB = -60f; tArmF = 55f; tArmB = -55f; tElbowF = 40f; tElbowB = 50f; tTorso = -6f; tBlade = -104f;
                    break;
                case Pose.Fall:
                    tLegF = 28f; tLegB = -22f; tKneeF = -25f; tKneeB = -15f; tArmF = 75f; tArmB = -65f; tElbowF = 30f; tElbowB = 35f; tTorso = 5f; tBlade = -98f;
                    break;
                case Pose.Dash:
                    tTorso = -28f; tLegF = 42f; tLegB = -42f; tKneeF = -30f; tKneeB = -8f; tArmF = 95f; tArmB = -40f; tElbowF = 20f; tElbowB = 40f; tBlade = -90f; tHead = 6f;
                    rate = 40f;
                    break;
                case Pose.Parry:
                    // the perfect window: a high guard, forearm up in front of the face, blade vertical and bright
                    tTorso = 6f; tArmF = 80f; tElbowF = 70f; tBlade = -8f; tArmB = -30f; tElbowB = 30f; tLegF = 14f; tLegB = -10f; tKneeF = -12f; tKneeB = -8f; tTorsoY = -0.02f; tHead = -4f;
                    rate = 40f;
                    break;
                case Pose.Guard:
                    // the block window: the guard has sunk and the blade leans; still covered, no longer sharp
                    tTorso = 10f; tArmF = 60f; tElbowF = 60f; tBlade = 18f; tArmB = -34f; tElbowB = 30f; tLegF = 18f; tLegB = -14f; tKneeF = -22f; tKneeB = -14f; tTorsoY = -0.07f; tHead = -2f;
                    rate = 22f;
                    break;
                case Pose.ParrySuccess:
                    tTorso = 10f; tArmF = 60f; tElbowF = 30f; tBlade = 30f; tArmB = -40f; tElbowB = 30f; tLegF = 18f; tLegB = -14f; tKneeF = -20f; tKneeB = -10f;
                    rate = 40f;
                    break;
                case Pose.Hurt:
                    tTorso = 16f; tArmF = -45f; tElbowF = 30f; tArmB = 55f; tElbowB = 20f; tLegF = -10f; tLegB = 15f; tKneeF = -20f; tKneeB = -30f; tHead = 12f; tBlade = -200f;
                    rate = 30f;
                    break;
                case Pose.HurtBack:
                    // hit from behind: thrown forward, head down, arms flung out ahead
                    tTorso = -20f; tArmF = 70f; tElbowF = 20f; tArmB = 50f; tElbowB = 20f; tLegF = 24f; tLegB = -18f; tKneeF = -30f; tKneeB = -10f; tHead = -12f; tBlade = -60f;
                    rate = 30f;
                    break;
                case Pose.Heal:
                    // a real kneel: the front foot tucked under, the back knee on the ground with the shin folded
                    // back; the hips come down through the planted contacts below
                    tTorso = -12f; tLegF = 60f; tKneeF = -130f; tLegB = -30f; tKneeB = -60f; tTorsoY = -0.06f;
                    tArmF = 42f; tElbowF = 60f; tArmB = 30f; tElbowB = 60f; tBlade = -175f; tHead = 10f;
                    rate = 14f;
                    break;
                case Pose.Climb:
                {
                    // hand over hand up the rungs. One arm reaches high and straight while the other, bent,
                    // pulls at chest height; the leg opposite the reaching arm lifts with the knee folded and
                    // plants, the other pushes down almost straight. The root mirrors the angles, so positive
                    // is toward the wall.
                    climbPhase += dt * 6.5f * Mathf.Clamp(Mathf.Abs(v.y) / 3.5f, 0f, 1.4f);
                    float s = Mathf.Sin(climbPhase), s2 = -s;
                    tArmF = 150f + 40f * s; tElbowF = 45f - 35f * s;
                    tArmB = 150f + 40f * s2; tElbowB = 45f - 35f * s2;
                    tLegF = 35f + 35f * s2; tKneeF = -30f - 60f * Mathf.Max(0f, s2);
                    tLegB = 35f + 35f * s; tKneeB = -30f - 60f * Mathf.Max(0f, s);
                    tTorso = -6f;                        // leaning into the wall
                    tBlade = 186f;                       // slung from the hand, hanging past the hip
                    tHead = -14f;                        // looking up the shaft
                    tBob = Mathf.Abs(s) * 0.03f;
                    tTorsoY = -0.02f;
                    rate = 20f;
                    break;
                }
                case Pose.Charge:
                    // coiled: both hands take the hilt back over the shoulder, tip up and behind, ready to come over.
                    // This is the pose the heavy clip starts from, so its wind-up key sits right next to it.
                    tTorso = 14f; tHead = -6f;
                    tArmF = 200f; tArmB = 196f; tElbowF = 20f; tElbowB = 25f;
                    tBlade = 40f;
                    tLegF = -16f; tLegB = 14f; tKneeF = -10f; tKneeB = -8f;
                    tTorsoY = -0.05f;
                    tBob = Mathf.Sin(t * 38f) * 0.012f;  // the strain of holding it
                    rate = 16f;
                    break;
                case Pose.QiBlast:
                    tTorso = -12f; tArmF = 88f; tArmB = 82f; tElbowF = 5f; tElbowB = 5f; tBlade = -95f; tLegF = 24f; tLegB = -18f; tKneeF = -20f; tKneeB = -8f; tHead = -6f;
                    rate = 40f;
                    break;
            }

            // The legs rotate at the hip and the knee, so any bend would lift the feet off the floor. The hips
            // drop by what the legs lose in height, but that is done below from the angles actually applied.
            // Taking it from these targets missed two cases: the limbs ease into a new pose over several
            // frames and the drop would run ahead of them, and an attack clip overwrites the bob outright
            // (every ClipKey authors Bob = 0), which dropped the planting for the whole swing.

            float k = 1f - Mathf.Exp(-rate * dt);
            if (clipActive)
            {
                clipT += dt;
                var jp = EvaluateClip(clipT);
                torsoA = jp.Torso; headA = jp.Head; armBackA = jp.ArmBack; armFrontA = jp.ArmFront; bladeA = jp.Blade;
                elbowBackA = jp.ElbowBack; elbowFrontA = jp.ElbowFront;
                bobY = jp.Bob; torsoY = jp.TorsoY;
                legBackA = float.IsNaN(jp.LegBack) ? Mathf.LerpAngle(legBackA, tLegB, snapLegs ? 1f : k) : jp.LegBack;
                legFrontA = float.IsNaN(jp.LegFront) ? Mathf.LerpAngle(legFrontA, tLegF, snapLegs ? 1f : k) : jp.LegFront;
                kneeBackA = float.IsNaN(jp.KneeBack) ? Mathf.LerpAngle(kneeBackA, tKneeB, snapLegs ? 1f : k) : jp.KneeBack;
                kneeFrontA = float.IsNaN(jp.KneeFront) ? Mathf.LerpAngle(kneeFrontA, tKneeF, snapLegs ? 1f : k) : jp.KneeFront;
                if (trail != null) trail.emitting = visible && clipT >= clipTrailFrom && clipT <= clipTrailUntil;
                if (clipT > clip[clipCount - 1].Time + 0.5f) EndAttack();
            }
            else
            {
                torsoA = Mathf.LerpAngle(torsoA, tTorso, k);
                headA = Mathf.LerpAngle(headA, tHead, k);
                armBackA = Mathf.LerpAngle(armBackA, tArmB, k);
                armFrontA = Mathf.LerpAngle(armFrontA, tArmF, k);
                elbowBackA = Mathf.LerpAngle(elbowBackA, tElbowB, k);
                elbowFrontA = Mathf.LerpAngle(elbowFrontA, tElbowF, k);
                bladeA = Mathf.LerpAngle(bladeA, tBlade, k);
                legBackA = Mathf.LerpAngle(legBackA, tLegB, snapLegs ? 1f : k);
                legFrontA = Mathf.LerpAngle(legFrontA, tLegF, snapLegs ? 1f : k);
                kneeBackA = Mathf.LerpAngle(kneeBackA, tKneeB, snapLegs ? 1f : k);
                kneeFrontA = Mathf.LerpAngle(kneeFrontA, tKneeF, snapLegs ? 1f : k);
                bobY = Mathf.Lerp(bobY, tBob, k);
                torsoY = Mathf.Lerp(torsoY, tTorsoY, k);
            }
            // taken from the applied angles, so the lowest foot sits on the floor in every frame, clip or not
            plant = grounded && pose != Pose.Climb ? PlantDrop(legFrontA, kneeFrontA, legBackA, kneeBackA) : 0f;
            scale = Vector2.SmoothDamp(scale, Vector2.one, ref scaleVel, 0.08f, 100f, dt);

            // flash overlay
            if (flashT > 0f)
            {
                flashT -= udt;
                float a = Mathf.Clamp01(flashT / flashDur) * flashColor.a;
                SetOverlay(flashColor, a);
            }
            else SetOverlay(flashColor, 0f);

            // sword light: the blade is the second channel for the parry timing
            float li = LightBase;
            if (lightPulseT > 0f) { lightPulseT -= udt; li = Mathf.Lerp(LightBase, lightPulse, Ease.OutCubic(lightPulseT / lightPulseDur)); }
            if (pose == Pose.Parry) li = Mathf.Max(li, 1.8f);
            if (pose == Pose.Guard) li = Mathf.Max(li, 0.95f);
            if (pose == Pose.Heal) li = Mathf.Max(li, 1.6f);
            if (pose == Pose.Charge) li = Mathf.Max(li, 1.3f + 0.5f * Mathf.Sin(t * 28f));
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

        /// <summary>How far the hips must drop so the lowest contact point (a foot, or a knee when the shin is
        /// folded back) sits on the floor.</summary>
        static float PlantDrop(float thighF, float kneeF, float thighB, float kneeB)
        {
            float lowest = Mathf.Max(LegExtent(thighF, kneeF), LegExtent(thighB, kneeB));
            return Mathf.Max(0f, UpperLegLen + LowerLegLen - lowest);
        }

        static float LegExtent(float thigh, float knee)
        {
            float kneeY = UpperLegLen * Mathf.Cos(thigh * Mathf.Deg2Rad);
            float footY = kneeY + LowerLegLen * Mathf.Cos((thigh + knee) * Mathf.Deg2Rad);
            return Mathf.Max(kneeY, footY);
        }

        JointPose CurrentPose()
        {
            return new JointPose
            {
                Torso = torsoA, Head = headA, ArmBack = armBackA, ArmFront = armFrontA, ElbowBack = elbowBackA, ElbowFront = elbowFrontA, Blade = bladeA,
                LegBack = legBackA, LegFront = legFrontA, KneeBack = kneeBackA, KneeFront = kneeFrontA, Bob = bobY, TorsoY = torsoY
            };
        }

        JointPose EvaluateClip(float time)
        {
            JointPose prev = clipStart; float t0 = 0f;
            for (int i = 0; i < clipCount; i++)
            {
                var key = clip[i];
                if (time < key.Time || i == clipCount - 1)
                {
                    float u = key.Time > t0 ? Mathf.Clamp01((time - t0) / (key.Time - t0)) : 1f;
                    return JointPose.Lerp(prev, key.Pose, key.Ease(u));
                }
                prev = key.Pose; t0 = key.Time;
            }
            return prev;
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

        /// <summary>Signed mirror factor; never quite zero so the turn passes through thin, not through nothing.</summary>
        float FacingScale()
        {
            float f = facingBlend;
            if (Mathf.Abs(f) < 0.08f) f = 0.08f * (f < 0f ? -1f : f > 0f ? 1f : facing);
            return f;
        }

        void Apply()
        {
            Root.localScale = new Vector3(FacingScale() * scale.x, scale.y, 1f);
            Root.localPosition = new Vector3(0f, bobY - plant, 0f);
            torso.localPosition = new Vector3(0f, 0.60f + torsoY, 0f);
            torso.localRotation = Quaternion.Euler(0f, 0f, torsoA);
            head.localRotation = Quaternion.Euler(0f, 0f, headA);
            armBack.localRotation = Quaternion.Euler(0f, 0f, armBackA);
            elbowBack.localRotation = Quaternion.Euler(0f, 0f, elbowBackA);
            armFront.localRotation = Quaternion.Euler(0f, 0f, armFrontA);
            elbowFront.localRotation = Quaternion.Euler(0f, 0f, elbowFrontA);
            // the blade angle is absolute in torso space, so undo the shoulder and the elbow above it
            sword.localRotation = Quaternion.Euler(0f, 0f, bladeA - armFrontA - elbowFrontA);
            legBack.localRotation = Quaternion.Euler(0f, 0f, legBackA);
            kneeBack.localRotation = Quaternion.Euler(0f, 0f, kneeBackA);
            legFront.localRotation = Quaternion.Euler(0f, 0f, legFrontA);
            kneeFront.localRotation = Quaternion.Euler(0f, 0f, kneeFrontA);
        }
    }
}
