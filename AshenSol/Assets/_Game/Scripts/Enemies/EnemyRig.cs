using UnityEngine;
using UnityEngine.Rendering.Universal;
using AshenSol.Core;

namespace AshenSol.Enemies
{
    /// <summary>Configurable puppet for humanoid enemies (and the boss) plus a drone variant. Handles telegraph
    /// flashes (silhouette overlays), weapon swings, recoil, stagger and walk cycles.
    /// Limb ownership, highest first: stagger, held pose, telegraph wind-up, walk/idle. The telegraph tint is
    /// presentation and runs on top of whichever of those owns the limbs.</summary>
    public class EnemyRig
    {
        public class Config
        {
            public string Prefix = "grunt";
            public string Torso = "grunt_torso", Head = "grunt_head", Arm = "grunt_arm", Weapon = "grunt_blade", Leg = "grunt_leg";
            public float Scale = 1f;
            public float HipY = 0.44f;          // hip height (leg pivot) in units before scale
            public float LegOffset = 0.07f;
            public float TorsoH = 0.62f;        // torso sprite height
            public float HeadY = 0.60f;         // neck height relative to torso pivot
            public Vector2 Shoulder = new Vector2(0.08f, 0.52f);
            public float ArmLen = 0.40f;        // hand distance from shoulder
            public float ArmSpriteH = 0.42f;
            public float LegSpriteH = 0.46f;
            public float HeadSpriteH = 0.38f;
            public Vector2 WeaponOffset = new Vector2(0f, 0.28f); // weapon sprite offset from hand
            public float WeaponIdleLocal = 180f;  // 180 = blade continues the arm outward
            public bool WeaponVerticalIdle = false; // spears: keep vertical at rest
            public float ArmIdle = 15f;
            public int SortBase = SortOrder.Enemy;
            public Color LightColor = Palette.Amber;
            public float LightIntensity = 0.4f;
            public float LightRadius = 1.5f;
            public bool SecondArm = true;
            public float WeaponTipDistance = 0.9f;
            /// <summary>Colour of the streak the weapon tip leaves during a swing.</summary>
            public Color TrailColor = Palette.EnemySlash;
        }

        public enum Style { Humanoid, Drone }

        // The body tint is the parry cue: it peaks a fixed lead before the strike however long the wind-up
        // is, and the first active frame pops. Leads are real seconds; the difficulty multiplier is already
        // folded into the telegraph length by the time it reaches the rig.
        const float BlinkSeconds = 0.12f;      // the "look here" at the start of every wind-up
        const float RampStartLead = 0.55f;     // seconds before the strike the ramp begins
        const float RampPeakLead = 0.14f;      // seconds before the strike it reaches full
        const float FloorAlpha = 0.18f;
        const float PeakAlpha = 0.9f;
        const float StrikePopSeconds = 0.09f;

        const float Stride = 2.7f;             // ground covered per walk cycle (stylised: slides a little)
        const float LungeSpeed = 8f;           // faster than this the legs hold one long stride
        const float TurnSeconds = 0.08f;       // the mirror blends through thin instead of snapping

        public SpriteRenderer[] Renderers { get; private set; }
        public Transform Root { get; private set; }
        public Transform Torso, Head, ArmFront, ArmBack, Weapon, LegBack, LegFront, WeaponTip;
        public Light2D Light { get; private set; }
        public Config Cfg { get; private set; }
        public Style RigStyle { get; private set; }
        /// <summary>Drone only: roll into the flight direction, degrees.</summary>
        public float Bank;

        SpriteRenderer[] overlays;
        SpriteRenderer flare;
        TrailRenderer trail;
        // drone
        Transform ring, eye;

        float torsoA, headA, armA, armBackA, weaponLocal, legBA, legFA, bob, rootX;
        Vector2 scale = Vector2.one, scaleVel;
        float runPhase; int facing = 1; float facingBlend = 1f; bool visible = true, everAnimated;
        float flashT, flashDur; Color flashColor;
        float telegraphT, telegraphDur; Color telegraphColor; bool telegraphing;
        float strikeT, strikeDur; bool striking; float strikeFrom = -120f, strikeTo = 80f; bool thrust;
        float recoilT, recoilDur, recoilFrom, recoilTo;
        float spinT, spinSpeed;
        float flinchT;
        float staggerT;
        float poseArm, poseWeapon, poseTorso, poseLegF = float.NaN, poseLegB = float.NaN; bool posed;
        float ringSpin;

        // ---------------- builders ----------------
        public static EnemyRig BuildHumanoid(Transform parent, Config cfg)
        {
            var rig = new EnemyRig { Cfg = cfg, RigStyle = Style.Humanoid };
            var root = new GameObject("Rig").transform;
            root.SetParent(parent, false);
            root.localScale = Vector3.one * cfg.Scale;
            rig.Root = root;
            var list = new System.Collections.Generic.List<SpriteRenderer>();
            var ov = new System.Collections.Generic.List<SpriteRenderer>();
            int sb = cfg.SortBase;

            rig.LegBack = Pivot(root, "legBack", new Vector2(-cfg.LegOffset, cfg.HipY));
            list.Add(Part(rig.LegBack, cfg.Leg, new Vector2(0f, -cfg.LegSpriteH * 0.5f), sb - 3, ov));
            rig.LegFront = Pivot(root, "legFront", new Vector2(cfg.LegOffset, cfg.HipY));
            list.Add(Part(rig.LegFront, cfg.Leg, new Vector2(0f, -cfg.LegSpriteH * 0.5f), sb + 2, ov));
            rig.Torso = Pivot(root, "torso", new Vector2(0f, cfg.HipY - 0.02f));
            list.Add(Part(rig.Torso, cfg.Torso, new Vector2(0f, cfg.TorsoH * 0.5f), sb, ov));
            rig.Head = Pivot(rig.Torso, "head", new Vector2(0.01f, cfg.HeadY));
            list.Add(Part(rig.Head, cfg.Head, new Vector2(0f, cfg.HeadSpriteH * 0.5f), sb + 1, ov));
            if (cfg.SecondArm)
            {
                rig.ArmBack = Pivot(rig.Torso, "armBack", new Vector2(-cfg.Shoulder.x, cfg.Shoulder.y));
                list.Add(Part(rig.ArmBack, cfg.Arm, new Vector2(0f, -cfg.ArmSpriteH * 0.5f), sb - 2, ov));
            }
            rig.ArmFront = Pivot(rig.Torso, "armFront", cfg.Shoulder);
            list.Add(Part(rig.ArmFront, cfg.Arm, new Vector2(0f, -cfg.ArmSpriteH * 0.5f), sb + 4, ov));
            rig.Weapon = Pivot(rig.ArmFront, "weapon", new Vector2(0f, -cfg.ArmLen));
            list.Add(Part(rig.Weapon, cfg.Weapon, cfg.WeaponOffset, sb + 5, ov));
            rig.WeaponTip = Pivot(rig.Weapon, "tip", new Vector2(0f, cfg.WeaponTipDistance));

            rig.flare = MakeFlare(rig.WeaponTip, sb + 6);
            rig.trail = MakeTrail(rig.WeaponTip, cfg.TrailColor);
            rig.Light = MakeLight(rig.Torso, new Vector2(0f, cfg.TorsoH * 0.5f), cfg.LightColor, cfg.LightIntensity, cfg.LightRadius);

            rig.Renderers = list.ToArray();
            rig.overlays = ov.ToArray();
            rig.Reset();
            return rig;
        }

        public static EnemyRig BuildDrone(Transform parent, int sortBase)
        {
            var rig = new EnemyRig { Cfg = new Config { SortBase = sortBase, LightColor = Palette.Amber, LightIntensity = 0.8f, LightRadius = 2f }, RigStyle = Style.Drone };
            var root = new GameObject("Rig").transform;
            root.SetParent(parent, false);
            rig.Root = root;
            var list = new System.Collections.Generic.List<SpriteRenderer>();
            var ov = new System.Collections.Generic.List<SpriteRenderer>();
            rig.Torso = Pivot(root, "body", Vector2.zero);
            rig.ring = Pivot(rig.Torso, "ring", Vector2.zero);
            list.Add(Part(rig.ring, "drone_ring", Vector2.zero, sortBase - 1, ov));
            list.Add(Part(rig.Torso, "drone_body", Vector2.zero, sortBase, ov));
            rig.eye = Pivot(rig.Torso, "eye", new Vector2(0.08f, -0.02f));
            list.Add(Part(rig.eye, "drone_eye", Vector2.zero, sortBase + 1, ov));
            rig.WeaponTip = Pivot(rig.Torso, "tip", new Vector2(0.25f, -0.05f));
            rig.flare = MakeFlare(rig.WeaponTip, sortBase + 2);
            rig.Light = MakeLight(rig.Torso, new Vector2(0.05f, 0f), Palette.Amber, 0.8f, 2f);
            rig.Renderers = list.ToArray();
            rig.overlays = ov.ToArray();
            rig.Reset();
            return rig;
        }

        static Transform Pivot(Transform parent, string name, Vector2 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }

        static SpriteRenderer Part(Transform pivot, string sprite, Vector2 offset, int sort, System.Collections.Generic.List<SpriteRenderer> overlays)
        {
            var go = new GameObject(sprite);
            go.transform.SetParent(pivot, false);
            go.transform.localPosition = offset;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Res.Sprite(sprite);
            sr.sortingOrder = sort;
            var og = new GameObject("flash");
            og.transform.SetParent(go.transform, false);
            var o = og.AddComponent<SpriteRenderer>();
            o.sprite = sr.sprite;
            o.sortingOrder = sort + 1;
            o.material = MaterialLibrary.Silhouette;
            o.color = new Color(1f, 1f, 1f, 0f);
            o.enabled = false;
            overlays.Add(o);
            return sr;
        }

        static SpriteRenderer MakeFlare(Transform parent, int sort)
        {
            var go = new GameObject("flare");
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Res.Sprite("fx_flare");
            sr.material = MaterialLibrary.Additive;
            sr.sortingOrder = sort;
            sr.color = new Color(1f, 1f, 1f, 0f);
            sr.transform.localScale = Vector3.one * 0.35f;
            sr.enabled = false;
            return sr;
        }

        /// <summary>A short streak behind the weapon tip, on only while the weapon actually swings. Ties the
        /// slash effect to the real motion of the blade.</summary>
        static TrailRenderer MakeTrail(Transform tip, Color color)
        {
            var go = new GameObject("tipTrail");
            go.transform.SetParent(tip, false);
            var tr = go.AddComponent<TrailRenderer>();
            tr.sharedMaterial = AshenSol.VFX.VfxManager.AdditiveFor(Res.Sprite("fx_glow"));
            tr.time = 0.11f;
            tr.minVertexDistance = 0.03f;
            tr.widthMultiplier = 0.15f;
            tr.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            tr.numCornerVertices = 5;
            tr.numCapVertices = 5;
            tr.sortingOrder = SortOrder.Fx + 7;
            tr.startColor = color.WithAlpha(0.85f);
            tr.endColor = color.WithAlpha(0f);
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.receiveShadows = false;
            tr.autodestruct = false;
            tr.emitting = false;
            return tr;
        }

        static Light2D MakeLight(Transform parent, Vector2 pos, Color color, float intensity, float radius)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var l = go.AddComponent<Light2D>();
            l.lightType = Light2D.LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.pointLightOuterRadius = radius;
            l.pointLightInnerRadius = 0.1f;
            l.falloffIntensity = 0.7f;
            return l;
        }

        // ---------------- controls ----------------
        public void Reset()
        {
            torsoA = headA = legBA = legFA = bob = rootX = 0f; armBackA = -10f;
            armA = Cfg.ArmIdle; weaponLocal = Cfg.WeaponVerticalIdle ? -armA : Cfg.WeaponIdleLocal;
            scale = Vector2.one; scaleVel = Vector2.zero;
            flashT = 0f; telegraphing = false; striking = false; staggerT = 0f; posed = false;
            recoilT = 0f; spinT = 0f; flinchT = 0f; Bank = 0f;
            facingBlend = facing;
            if (Light != null) { Light.intensity = Cfg.LightIntensity; Light.color = Cfg.LightColor; }
            if (flare != null) flare.enabled = false;
            TrailOff(true);
            SetOverlay(Color.white, 0f);
            Apply();
        }

        public void SetFacing(int f)
        {
            facing = f == 0 ? 1 : f;
            if (!everAnimated) facingBlend = facing;
        }

        public void SetVisible(bool v)
        {
            visible = v;
            for (int i = 0; i < Renderers.Length; i++) { Renderers[i].enabled = v; if (!v) overlays[i].enabled = false; }
            if (Light != null) Light.enabled = v;
            if (!v && flare != null) flare.enabled = false;
            if (!v) TrailOff(true);
        }

        public void Flash(Color color, float seconds) { flashColor = color; flashDur = Mathf.Max(0.01f, seconds); flashT = flashDur; }

        public void Telegraph(AttackKind kind, float seconds)
        {
            telegraphing = true; telegraphDur = Mathf.Max(0.05f, seconds); telegraphT = 0f;
            telegraphColor = kind == AttackKind.Parryable ? Palette.TelegraphWhite : Palette.TelegraphRed;
            // a new wind-up always wins over the last follow-through
            striking = false; recoilT = 0f; spinT = 0f;
            TrailOff(false);
            if (flare != null) { flare.enabled = visible; flare.color = telegraphColor.WithAlpha(0f); }
        }

        /// <summary>End of the wind-up. With pop, the body flashes hard for a few frames: that IS the strike.
        /// Pass false when the wind-up is merely aborted.</summary>
        public void EndTelegraph(bool pop = true)
        {
            bool was = telegraphing;
            telegraphing = false;
            if (flare != null) flare.enabled = false;
            if (was && pop) Flash(telegraphColor.WithAlpha(1f), StrikePopSeconds);
            else SetOverlay(telegraphColor, 0f);
        }

        /// <summary>Weapon arm sweep from a raised backswing to a forward-down finish.</summary>
        public void Strike(float seconds, float from = -120f, float to = 80f)
        {
            striking = true; strikeT = 0f; strikeDur = Mathf.Max(0.02f, seconds); strikeFrom = from; strikeTo = to; thrust = false;
            recoilT = 0f; spinT = 0f; flinchT = 0f;
            TrailOn();
        }

        /// <summary>Spear thrust: arm snaps to horizontal, rig lunges forward.</summary>
        public void Thrust(float seconds)
        {
            striking = true; strikeT = 0f; strikeDur = Mathf.Max(0.02f, seconds); strikeFrom = 60f; strikeTo = 92f; thrust = true;
            recoilT = 0f; spinT = 0f; flinchT = 0f;
            TrailOn();
        }

        /// <summary>A continuous turn of the weapon arm (the whirl), degrees per second.</summary>
        public void Spin(float seconds, float degreesPerSecond)
        {
            spinT = Mathf.Max(0.02f, seconds); spinSpeed = degreesPerSecond;
            striking = false; recoilT = 0f; flinchT = 0f;
            TrailOn();
        }

        /// <summary>The blow was turned: the weapon arm bounces back along its own swing and the body rocks.</summary>
        public void Recoil(float seconds)
        {
            if (RigStyle == Style.Drone) return;
            float dir = strikeTo >= strikeFrom ? 1f : -1f;
            recoilFrom = armA; recoilTo = armA - dir * 55f;
            recoilDur = Mathf.Max(0.05f, seconds); recoilT = recoilDur;
            striking = false; spinT = 0f;
            TrailOff(false);
        }

        /// <summary>A short hit reaction while not attacking; attacks have armour and ignore it.</summary>
        public void Flinch(float seconds) { flinchT = Mathf.Max(flinchT, seconds); }

        /// <summary>Hold a fixed pose (boss crouch / leap / kneel). Pass posed=false to release.
        /// Leg angles are optional: NaN leaves the legs to the walk cycle.</summary>
        public void SetPose(bool on, float arm = 0f, float weapon = 180f, float torso = 0f, float legFront = float.NaN, float legBack = float.NaN)
        {
            posed = on; poseArm = arm; poseWeapon = weapon; poseTorso = torso; poseLegF = legFront; poseLegB = legBack;
            if (on) { striking = false; spinT = 0f; TrailOff(false); }
        }

        public void Stagger(float seconds)
        {
            staggerT = seconds; scale = new Vector2(1.15f, 0.85f);
            striking = false; recoilT = 0f; spinT = 0f;
            TrailOff(false);
        }
        public void Punch(float sx, float sy) { scale = new Vector2(sx, sy); }

        void TrailOn() { if (trail != null) { trail.Clear(); trail.emitting = visible; } }
        void TrailOff(bool clear) { if (trail != null) { trail.emitting = false; if (clear) trail.Clear(); } }

        // ---------------- per frame ----------------
        public void Animate(EnemyBase e, float dt)
        {
            Animate(e != null && e.Body != null ? e.Body.linearVelocity.x : 0f, dt);
        }

        /// <summary>Pose the rig from a bare horizontal speed, which lets cutscenes drive a rig with no enemy behind it.</summary>
        public void Animate(float velocityX, float dt)
        {
            everAnimated = true;
            float udt = Time.unscaledDeltaTime;
            float t = Time.time;
            facingBlend = Mathf.MoveTowards(facingBlend, facing, dt / TurnSeconds);
            if (RigStyle == Style.Drone) { AnimateDrone(dt); return; }

            float speed = Mathf.Abs(velocityX);
            bool walking = speed > 0.4f;
            bool lunging = speed > LungeSpeed;
            float tTorso = 0f, tHead = 0f, tArm = Cfg.ArmIdle, tArmB = -10f, tLegB = 0f, tLegF = 0f, tBob = 0f, tRootX = 0f;
            float tWeapon = Cfg.WeaponVerticalIdle ? -tArm : Cfg.WeaponIdleLocal;
            float rate = 16f;
            bool snapLegs = false;

            if (lunging)
            {
                // a dash or a charge: one long stride held, body low and forward
                tLegF = 44f; tLegB = -40f; tTorso = -14f; tBob = 0.02f;
                rate = 24f;
            }
            else if (walking)
            {
                // the cycle advances with the ground covered, so the feet stop sliding when he speeds up or brakes
                runPhase += speed * dt * (Mathf.PI * 2f / Stride);
                tLegF = Mathf.Sin(runPhase) * 30f; tLegB = -tLegF;
                tArmB = Mathf.Sin(runPhase) * 20f - 10f;
                tTorso = -5f; tBob = Mathf.Abs(Mathf.Sin(runPhase)) * 0.04f;
                rate = 26f;
                snapLegs = true;
            }
            else
            {
                tBob = Mathf.Sin(t * 1.4f * Mathf.PI * 2f) * 0.02f;
                tArm = Cfg.ArmIdle + Mathf.Sin(t * 1.4f * Mathf.PI * 2f) * 3f;
            }

            if (staggerT > 0f)
            {
                staggerT -= dt;
                // reeling: the body sways instead of freezing, and straightens up over the last half second
                // so the player can read that the opening is closing
                float sway = Mathf.Sin(t * 3.2f), sway2 = Mathf.Sin(t * 2.3f + 1.1f);
                float up = 1f - Mathf.Clamp01(staggerT / 0.5f);
                tTorso = Mathf.Lerp(22f + 5f * sway, 4f, up);
                tHead = Mathf.Lerp(14f + 4f * sway2, 2f, up);
                tArm = Mathf.Lerp(-20f + 6f * sway2, Cfg.ArmIdle, up);
                tArmB = Mathf.Lerp(30f - 5f * sway, -10f, up);
                tLegF = Mathf.Lerp(-10f + 3f * sway, 0f, up);
                tLegB = Mathf.Lerp(12f - 3f * sway, 0f, up);
                tWeapon = Mathf.Lerp(Cfg.WeaponVerticalIdle ? 40f : 200f, Cfg.WeaponVerticalIdle ? -tArm : Cfg.WeaponIdleLocal, up);
                tBob = 0f; snapLegs = false;
                rate = 14f;
            }
            else if (posed)
            {
                tArm = poseArm; tWeapon = poseWeapon; tTorso = poseTorso;
                if (!float.IsNaN(poseLegF)) { tLegF = poseLegF; snapLegs = false; }
                if (!float.IsNaN(poseLegB)) { tLegB = poseLegB; snapLegs = false; }
                rate = 20f;
            }
            else if (telegraphing)
            {
                float p = Mathf.Clamp01(telegraphT / telegraphDur);
                float wind = Ease.OutBack(Mathf.Min(1f, p * 1.3f));
                if (Cfg.WeaponVerticalIdle) { tArm = Mathf.Lerp(Cfg.ArmIdle, -35f, wind); tWeapon = 180f + Mathf.Lerp(0f, 10f, wind); tTorso = 8f * wind; tRootX = -0.15f * wind; }
                else { tArm = Mathf.Lerp(Cfg.ArmIdle, -125f, wind); tWeapon = 180f; tTorso = 10f * wind; }
                tHead = -6f * wind;
                rate = 30f;
            }

            if (flinchT > 0f)
            {
                flinchT -= dt;
                if (!telegraphing && !striking && staggerT <= 0f && spinT <= 0f && recoilT <= 0f)
                {
                    float f = Mathf.Clamp01(flinchT / 0.12f);
                    tTorso += 14f * f; tHead += 8f * f; tArmB += 18f * f; tRootX -= 0.06f * f;
                    rate = Mathf.Max(rate, 26f);
                }
            }

            // telegraph presentation, whichever pose owns the limbs
            if (telegraphing)
            {
                telegraphT += dt;
                float pulse;
                float a = TelegraphAlpha(telegraphT, telegraphDur, out pulse);
                SetOverlay(telegraphColor, a);
                if (Light != null) { Light.color = telegraphColor; Light.intensity = Cfg.LightIntensity + 1.8f * a; }
                if (flare != null) { flare.color = telegraphColor.WithAlpha(a); flare.transform.localScale = Vector3.one * (0.25f + 0.4f * a); flare.transform.localRotation = Quaternion.Euler(0f, 0f, t * 180f); }
            }
            else if (Light != null)
            {
                Light.color = Color.Lerp(Light.color, Cfg.LightColor, 1f - Mathf.Exp(-8f * udt));
                Light.intensity = Mathf.Lerp(Light.intensity, Cfg.LightIntensity, 1f - Mathf.Exp(-8f * udt));
            }

            float k = 1f - Mathf.Exp(-rate * dt);
            bool armDriven = false;
            if (spinT > 0f)
            {
                spinT -= dt;
                armA += spinSpeed * dt;
                weaponLocal = 180f;
                torsoA = Mathf.LerpAngle(torsoA, -10f, 1f - Mathf.Exp(-20f * dt));
                armDriven = true;
                if (spinT <= 0f) TrailOff(false);
            }
            else if (recoilT > 0f)
            {
                recoilT -= dt;
                float p = Ease.OutCubic(1f - Mathf.Clamp01(recoilT / recoilDur));
                armA = Mathf.Lerp(recoilFrom, recoilTo, p);
                weaponLocal = Mathf.LerpAngle(weaponLocal, 180f, 1f - Mathf.Exp(-24f * dt));
                torsoA = Mathf.LerpAngle(torsoA, 12f, 1f - Mathf.Exp(-24f * dt));
                rootX = Mathf.Lerp(rootX, -0.12f, 1f - Mathf.Exp(-24f * dt));
                armDriven = true;
            }
            else if (striking)
            {
                strikeT += dt;
                // OutQuad rather than OutCubic: the blade is still travelling through the middle of the
                // active window instead of having finished before it opened
                float p = Ease.OutQuad(strikeT / strikeDur);
                armA = Mathf.Lerp(strikeFrom, strikeTo, p);
                weaponLocal = 180f;
                if (thrust) { rootX = Mathf.Lerp(0f, 0.35f, p); torsoA = -12f; }
                else torsoA = -14f;
                if (strikeT > strikeDur + 0.06f) TrailOff(false);
                if (strikeT > strikeDur + 0.25f) striking = false;
                armDriven = true;
            }

            if (!armDriven)
            {
                armA = Mathf.LerpAngle(armA, tArm, k);
                weaponLocal = Mathf.LerpAngle(weaponLocal, tWeapon, k);
                torsoA = Mathf.LerpAngle(torsoA, tTorso, k);
                rootX = Mathf.Lerp(rootX, tRootX, k);
            }
            headA = Mathf.LerpAngle(headA, tHead, k);
            armBackA = Mathf.LerpAngle(armBackA, tArmB, k);
            legBA = Mathf.LerpAngle(legBA, tLegB, snapLegs ? 1f : k);
            legFA = Mathf.LerpAngle(legFA, tLegF, snapLegs ? 1f : k);
            bob = Mathf.Lerp(bob, tBob, k);
            scale = Vector2.SmoothDamp(scale, Vector2.one, ref scaleVel, 0.09f, 100f, dt);

            TickFlash(udt);
            Apply();
        }

        /// <summary>Tint strength over a wind-up: a blink at the start, a floor while it holds, a ramp that
        /// arrives hard a fixed lead before the strike.</summary>
        static float TelegraphAlpha(float elapsed, float duration, out float pulse)
        {
            float remaining = Mathf.Max(0f, duration - elapsed);
            float blink = 1f - Mathf.Clamp01(elapsed / BlinkSeconds);
            float ramp = 1f - Mathf.Clamp01((remaining - RampPeakLead) / (RampStartLead - RampPeakLead));
            ramp *= ramp;
            pulse = Mathf.Max(blink, ramp);
            return Mathf.Lerp(FloorAlpha, PeakAlpha, pulse);
        }

        void AnimateDrone(float dt)
        {
            float udt = Time.unscaledDeltaTime;
            float t = Time.time;
            float spin = 60f;
            if (telegraphing)
            {
                telegraphT += dt;
                float p = Mathf.Clamp01(telegraphT / telegraphDur);
                spin = 60f + 500f * p;
                float pulse;
                float a = TelegraphAlpha(telegraphT, telegraphDur, out pulse);
                SetOverlay(telegraphColor, a);
                if (Light != null) { Light.color = telegraphColor; Light.intensity = 0.8f + 1.8f * a; }
                if (flare != null) { flare.color = telegraphColor.WithAlpha(a); flare.transform.localScale = Vector3.one * (0.2f + 0.35f * a); flare.transform.localRotation = Quaternion.Euler(0f, 0f, t * 240f); }
                eye.localScale = Vector3.one * (1f + 0.45f * pulse);
            }
            else
            {
                if (Light != null) { Light.color = Color.Lerp(Light.color, Cfg.LightColor, 1f - Mathf.Exp(-8f * udt)); Light.intensity = Mathf.Lerp(Light.intensity, Cfg.LightIntensity, 1f - Mathf.Exp(-8f * udt)); }
                eye.localScale = Vector3.one * (1f + 0.08f * Mathf.Sin(t * 6f));
            }
            if (striking)
            {
                strikeT += dt;
                if (strikeT > strikeDur) striking = false;
            }
            if (flinchT > 0f) flinchT -= dt;
            ringSpin += spin * dt;
            ring.localRotation = Quaternion.Euler(0f, 0f, ringSpin);
            headA = Mathf.Sin(t * 1.3f) * 6f;
            Torso.localRotation = Quaternion.Euler(0f, 0f, headA * 0.5f + Bank);
            scale = Vector2.SmoothDamp(scale, Vector2.one, ref scaleVel, 0.09f, 100f, dt);
            Root.localScale = new Vector3(FacingScale() * scale.x, scale.y, 1f);
            TickFlash(udt);
        }

        void TickFlash(float udt)
        {
            if (flashT > 0f)
            {
                flashT -= udt;
                SetOverlay(flashColor, Mathf.Clamp01(flashT / flashDur) * flashColor.a);
            }
            else if (!telegraphing) SetOverlay(flashColor, 0f);
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
            Root.localScale = new Vector3(FacingScale() * scale.x * Cfg.Scale, scale.y * Cfg.Scale, 1f);
            Root.localPosition = new Vector3(rootX * facing, bob, 0f);
            if (Torso != null) Torso.localRotation = Quaternion.Euler(0f, 0f, torsoA);
            if (Head != null) Head.localRotation = Quaternion.Euler(0f, 0f, headA);
            if (ArmFront != null) ArmFront.localRotation = Quaternion.Euler(0f, 0f, armA);
            if (ArmBack != null) ArmBack.localRotation = Quaternion.Euler(0f, 0f, armBackA);
            if (Weapon != null) Weapon.localRotation = Quaternion.Euler(0f, 0f, weaponLocal);
            if (LegBack != null) LegBack.localRotation = Quaternion.Euler(0f, 0f, legBA);
            if (LegFront != null) LegFront.localRotation = Quaternion.Euler(0f, 0f, legFA);
        }
    }
}
