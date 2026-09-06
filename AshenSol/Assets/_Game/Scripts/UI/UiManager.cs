using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using AshenSol.Core;

namespace AshenSol.UI
{
    /// <summary>All screen-space UI, built from code: HUD, boss bar, name card, prompt, fade, title/death/victory/pause.</summary>
    public class UiManager : MonoBehaviour, IUiService
    {
        public static UiManager Instance { get; private set; }

        Canvas canvas; RectTransform rootRt;
        // HUD
        CanvasGroup hudGroup; Image hpFill, hpTrail, hpFlash; Image[] pips; float hp01 = 1f, trail01 = 1f, trailDelay; int qi; float[] pipPop; float hpFlashT;
        // boss
        CanvasGroup bossGroup; RectTransform bossRt; Image bossFill, bossPosture, bossPostureBack; Text bossName, bossSub; float bossShown01, bossHp01 = 1f, bossPosture01; bool bossBroken; float bossTargetY; bool bossVisible;
        // name card / prompt
        CanvasGroup cardGroup; Text cardTitle, cardSub; Image cardSprite, cardLineL, cardLineR; Coroutine cardCo;
        CanvasGroup promptGroup; Text promptText; float promptTimer, promptFade;
        // fade
        Image fadeImg; Coroutine fadeCo;
        // screens
        CanvasGroup titleGroup, deathGroup, victoryGroup, pauseGroup;
        Text titlePress, deathPress, victoryPress, victoryStats; Image titleLogo, titleGlow; TitleMenu titleMenu;
        Action titleCb, deathCb, victoryCb; bool titleShown, deathShown, victoryShown, pausedShown; float screenArmTime;
        Text victoryLines; Text pauseDifficulty;
        // cutscene furniture
        CanvasGroup subtitleGroup; Text subtitleText;
        RectTransform barTop, barBottom; float letterbox, letterboxTarget, letterboxSpeed = 1f;
        CanvasGroup skipGroup;

        void Awake()
        {
            Instance = this;
            Services.Ui = this;
            GameEvents.PlayerHealthChanged += OnHealth;
            GameEvents.PlayerQiChanged += OnQi;
            GameEvents.BossHealthChanged += OnBossHealth;
            GameEvents.BossPhaseChanged += OnBossPhase;
            Build();
        }

        void OnDestroy()
        {
            GameEvents.PlayerHealthChanged -= OnHealth;
            GameEvents.PlayerQiChanged -= OnQi;
            GameEvents.BossHealthChanged -= OnBossHealth;
            GameEvents.BossPhaseChanged -= OnBossPhase;
        }

        // ------------------------------------------------------------ build
        void Build()
        {
            var go = new GameObject("Canvas", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            rootRt = go.GetComponent<RectTransform>();

            BuildHud();
            BuildBossBar();
            BuildNameCard();
            BuildPrompt();
            BuildTitle();
            BuildDeath();
            BuildVictory();
            BuildPause();
            BuildCutscene();
            var fadeRt = UiKit.Panel(rootRt, "Fade", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            fadeImg = fadeRt.gameObject.AddComponent<Image>();
            fadeImg.color = new Color(0f, 0f, 0f, 1f);
            fadeImg.raycastTarget = false;
        }

        void BuildHud()
        {
            var hud = UiKit.Panel(rootRt, "HUD", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            hudGroup = UiKit.Group(hud);
            var tl = new Vector2(0f, 1f);
            Vector2 barPos = new Vector2(48f, -48f), barSize = new Vector2(420f, 26f);
            UiKit.Image(hud, "hpBack", null, Palette.Ink.WithAlpha(0.75f), tl, barPos, barSize);
            hpTrail = UiKit.Image(hud, "hpTrail", null, Palette.Bone.WithAlpha(0.85f), tl, barPos + new Vector2(3f, -3f), barSize - new Vector2(6f, 6f));
            hpFill = UiKit.Image(hud, "hpFill", null, Palette.Teal, tl, barPos + new Vector2(3f, -3f), barSize - new Vector2(6f, 6f));
            hpFlash = UiKit.Image(hud, "hpFlash", null, Palette.Red.WithAlpha(0f), tl, barPos, barSize);
            UiKit.Image(hud, "hpFrame", Res.Sprite("ui_bar_frame"), Color.white, tl, barPos, barSize);
            pips = new Image[3]; pipPop = new float[3];
            for (int i = 0; i < 3; i++)
            {
                UiKit.Image(hud, "pipEmpty" + i, Res.Sprite("ui_pip"), Palette.Bone.WithAlpha(0.7f), tl, new Vector2(52f + i * 40f, -84f), new Vector2(30f, 30f));
                pips[i] = UiKit.Image(hud, "pipFull" + i, Res.Sprite("ui_pip_full"), Palette.Gold, tl, new Vector2(52f + i * 40f, -84f), new Vector2(30f, 30f));
                pips[i].color = Palette.Gold.WithAlpha(0f);
            }
            UiKit.Text(hud, "qiLabel", "QI", 16, Palette.Bone.WithAlpha(0.7f), tl, new Vector2(176f, -84f), new Vector2(60f, 30f), TextAnchor.MiddleLeft);
            hudGroup.alpha = 0f;
        }

        void BuildBossBar()
        {
            bossRt = UiKit.Anchored(rootRt, "BossBar", new Vector2(0.5f, 0f), new Vector2(0f, -80f), new Vector2(900f, 90f));
            bossGroup = UiKit.Group(bossRt);
            var c = new Vector2(0.5f, 0.5f);
            bossName = UiKit.Text(bossRt, "name", "", 28, Palette.Bone, new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(900f, 34f));
            bossSub = UiKit.Text(bossRt, "sub", "", 15, Palette.Bone.WithAlpha(0.6f), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(900f, 20f));
            UiKit.Image(bossRt, "back", null, Palette.Ink.WithAlpha(0.8f), new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(900f, 18f));
            bossFill = UiKit.Image(bossRt, "fill", null, Palette.Red, new Vector2(0f, 0f), new Vector2(3f, 3f), new Vector2(894f, 12f));
            bossFill.rectTransform.pivot = new Vector2(0f, 0f);
            bossPostureBack = UiKit.Image(bossRt, "postureBack", null, Palette.Ink.WithAlpha(0.75f), new Vector2(0.5f, 0f), new Vector2(0f, -14f), new Vector2(900f, 10f));
            bossPosture = UiKit.Image(bossRt, "posture", null, Palette.Posture, new Vector2(0f, 0f), new Vector2(3f, -12f), new Vector2(0f, 6f));
            bossPosture.rectTransform.pivot = new Vector2(0f, 0f);
            UiKit.Image(bossRt, "frame", Res.Sprite("ui_bar_frame"), Palette.Bone.WithAlpha(0.7f), new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(900f, 18f), false);
            bossGroup.alpha = 0f;
        }

        void BuildNameCard()
        {
            var rt = UiKit.Anchored(rootRt, "NameCard", new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(1400f, 200f));
            cardGroup = UiKit.Group(rt);
            var c = new Vector2(0.5f, 0.5f);
            cardLineL = UiKit.Image(rt, "lineL", null, Palette.Bone.WithAlpha(0.3f), c, new Vector2(-420f, 0f), new Vector2(360f, 2f));
            cardLineR = UiKit.Image(rt, "lineR", null, Palette.Bone.WithAlpha(0.3f), c, new Vector2(420f, 0f), new Vector2(360f, 2f));
            cardTitle = UiKit.Text(rt, "title", "", 62, Palette.Bone, c, new Vector2(0f, 6f), new Vector2(1400f, 80f));
            cardSub = UiKit.Text(rt, "sub", "", 22, Palette.Teal, c, new Vector2(0f, -52f), new Vector2(1400f, 30f));
            cardSprite = UiKit.Image(rt, "sprite", null, Color.white, c, new Vector2(0f, 0f), new Vector2(1000f, 140f));
            cardSprite.enabled = false;
            cardGroup.alpha = 0f;
        }

        void BuildPrompt()
        {
            var rt = UiKit.Anchored(rootRt, "Prompt", new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(1200f, 40f));
            promptGroup = UiKit.Group(rt);
            promptText = UiKit.Text(rt, "text", "", 24, Palette.Bone, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200f, 40f));
            promptGroup.alpha = 0f;
        }

        void BuildTitle()
        {
            var rt = UiKit.Panel(rootRt, "Title", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            titleGroup = UiKit.Group(rt);
            var bg = UiKit.Fill(rt, "bg", Palette.Ink.WithAlpha(0.55f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var skyImg = UiKit.Image(rt, "sky", Res.Sprite("bg_boss_sky"), new Color(0.6f, 0.55f, 0.6f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2200f, 2200f), false);
            skyImg.transform.SetAsFirstSibling();
            skyImg.preserveAspect = false;
            bg.transform.SetSiblingIndex(1);
            var c = new Vector2(0.5f, 0.5f);
            titleGlow = UiKit.Image(rt, "glow", Res.Sprite("fx_glow"), Palette.Teal.WithAlpha(0.35f), c, new Vector2(0f, 265f), new Vector2(1300f, 640f));
            titleGlow.material = MaterialLibrary.Additive;
            titleLogo = UiKit.Image(rt, "logo", Res.Sprite("ui_title"), Color.white, c, new Vector2(0f, 268f), new Vector2(820f, 236f));
            titleMenu = new TitleMenu(rt, new Vector2(0f, -10f));
            titlePress = UiKit.Text(rt, "hint", UiKit.Spaced("W / S  select      A / D  change      ENTER  confirm"), 16,
                Palette.Bone.WithAlpha(0.42f), c, new Vector2(0f, -330f), new Vector2(1200f, 30f));
            UiKit.Text(rt, "controls",
                "A / D  move     SPACE  jump     J  attack     K  parry     L  dash     I  Qi Blast     H  heal\n" +
                "<color=#ffffff>WHITE</color> flash: parry it.     <color=#ff3a3a>RED</color> flash: dash away.     Break the <color=#ffcc55>guard bar</color>, then  I  to execute.",
                18, Palette.Bone.WithAlpha(0.62f), c, new Vector2(0f, -392f), new Vector2(1500f, 70f));
            UiKit.Text(rt, "credit", "Ashen Sol  —  a Nine Sols-inspired prototype", 15, Palette.Bone.WithAlpha(0.35f), new Vector2(1f, 0f), new Vector2(-24f, 18f), new Vector2(700f, 24f), TextAnchor.MiddleRight);
            titleGroup.alpha = 0f;
            rt.gameObject.SetActive(false);
        }

        void BuildDeath()
        {
            var rt = UiKit.Panel(rootRt, "Death", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            deathGroup = UiKit.Group(rt);
            UiKit.Fill(rt, "bg", Palette.Ink.WithAlpha(0.78f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var c = new Vector2(0.5f, 0.5f);
            UiKit.Image(rt, "text", Res.Sprite("ui_text_fallen"), Color.white, c, new Vector2(0f, 60f), new Vector2(800f, 120f));
            deathPress = UiKit.Text(rt, "press", UiKit.Spaced("PRESS ENTER TO RISE AGAIN"), 22, Palette.Bone, c, new Vector2(0f, -60f), new Vector2(900f, 40f));
            deathGroup.alpha = 0f;
            rt.gameObject.SetActive(false);
        }

        void BuildVictory()
        {
            var rt = UiKit.Panel(rootRt, "Victory", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            victoryGroup = UiKit.Group(rt);
            UiKit.Fill(rt, "bg", Palette.Ink.WithAlpha(0.9f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var c = new Vector2(0.5f, 0.5f);
            var glow = UiKit.Image(rt, "glow", Res.Sprite("fx_glow"), Palette.Gold.WithAlpha(0.3f), c, new Vector2(0f, 150f), new Vector2(1200f, 600f));
            glow.material = MaterialLibrary.Additive;
            UiKit.Image(rt, "text", Res.Sprite("ui_text_vanquished"), Color.white, c, new Vector2(0f, 150f), new Vector2(900f, 120f));
            victoryStats = UiKit.Text(rt, "stats", "", 24, Palette.Bone, c, new Vector2(0f, -30f), new Vector2(900f, 220f));
            victoryStats.lineSpacing = 1.4f;
            victoryPress = UiKit.Text(rt, "press", UiKit.Spaced("PRESS ENTER"), 22, Palette.Bone.WithAlpha(0.8f), c, new Vector2(0f, -220f), new Vector2(800f, 40f));
            victoryGroup.alpha = 0f;
            rt.gameObject.SetActive(false);
        }

        void BuildPause()
        {
            var rt = UiKit.Panel(rootRt, "Pause", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            pauseGroup = UiKit.Group(rt);
            UiKit.Fill(rt, "bg", Palette.Ink.WithAlpha(0.7f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var c = new Vector2(0.5f, 0.5f);
            UiKit.Text(rt, "title", UiKit.Spaced("PAUSED", 2), 56, Palette.Bone, c, new Vector2(0f, 120f), new Vector2(900f, 80f));
            UiKit.Text(rt, "controls",
                "A / D  move          SPACE  jump          J  attack          K  parry\n" +
                "L / SHIFT  dash          I  Qi Blast          H  heal\n\n" +
                "Parry to fill the <color=#ffcc55>guard bar</color>. When it breaks the enemy is helpless for 3 s —\n" +
                "stand next to it and press  I  to spend 1 Qi on an execution.\n\n" +
                "Gamepad:  A jump   X attack   B parry   RB dash   Y Qi / execute   LB heal",
                20, Palette.Bone.WithAlpha(0.75f), c, new Vector2(0f, -20f), new Vector2(1400f, 140f));
            pauseDifficulty = UiKit.Text(rt, "diff", "", 18, Palette.Gold.WithAlpha(0.85f), c, new Vector2(0f, -120f), new Vector2(900f, 30f));
            UiKit.Text(rt, "resume", UiKit.Spaced("ESC / START  —  RESUME"), 20, Palette.Teal, c, new Vector2(0f, -172f), new Vector2(900f, 40f));
            UiKit.Text(rt, "quit", UiKit.Spaced("Q  —  ABANDON RUN (back to title)"), 18, Palette.Bone.WithAlpha(0.55f), c, new Vector2(0f, -212f), new Vector2(900f, 36f));
            pauseGroup.alpha = 0f;
            rt.gameObject.SetActive(false);
        }

        void BuildCutscene()
        {
            // letterbox bars sit above everything but the fade
            barTop = UiKit.Anchored(rootRt, "BarTop", new Vector2(0.5f, 1f), Vector2.zero, new Vector2(4000f, 0f));
            var it = barTop.gameObject.AddComponent<Image>();
            it.color = Color.black; it.raycastTarget = false;
            barBottom = UiKit.Anchored(rootRt, "BarBottom", new Vector2(0.5f, 0f), Vector2.zero, new Vector2(4000f, 0f));
            var ib = barBottom.gameObject.AddComponent<Image>();
            ib.color = Color.black; ib.raycastTarget = false;

            var st = UiKit.Anchored(rootRt, "Subtitle", new Vector2(0.5f, 0f), new Vector2(0f, 132f), new Vector2(1500f, 96f));
            subtitleGroup = UiKit.Group(st);
            subtitleText = UiKit.Text(st, "text", "", 30, Palette.Bone, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500f, 96f));
            subtitleText.lineSpacing = 1.25f;
            subtitleGroup.alpha = 0f;

            var sk = UiKit.Anchored(rootRt, "SkipHint", new Vector2(1f, 0f), new Vector2(-40f, 56f), new Vector2(600f, 30f));
            skipGroup = UiKit.Group(sk);
            UiKit.Text(sk, "text", UiKit.Spaced("PRESS ANY KEY TO SKIP"), 15, Palette.Bone.WithAlpha(0.45f),
                new Vector2(1f, 0.5f), Vector2.zero, new Vector2(600f, 30f), TextAnchor.MiddleRight);
            skipGroup.alpha = 0f;
        }

        // ------------------------------------------------------------ events
        void OnHealth(int hp, int max)
        {
            float v = max > 0 ? Mathf.Clamp01((float)hp / max) : 0f;
            if (v < hp01) { trailDelay = 0.5f; hpFlashT = 0.25f; }
            else if (v > hp01) trail01 = v;
            hp01 = v;
        }

        void OnQi(int q, int max)
        {
            if (q > qi) for (int i = qi; i < q && i < 3; i++) pipPop[i] = 1f;
            qi = q;
        }

        void OnBossHealth(float h, float p, bool broken) { bossHp01 = h; bossPosture01 = p; bossBroken = broken; }
        void OnBossPhase(int p) { if (bossName != null) StartCoroutine(PhaseColor()); }

        IEnumerator PhaseColor()
        {
            bossName.color = Palette.Amber;
            yield return new WaitForSecondsRealtime(2.5f);
            bossName.color = Palette.Bone;
        }

        // ------------------------------------------------------------ update
        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            // HUD
            float w = hpFill.rectTransform.parent.GetComponent<RectTransform>() != null ? 414f : 414f;
            hpFill.rectTransform.sizeDelta = new Vector2(Mathf.Lerp(hpFill.rectTransform.sizeDelta.x, w * hp01, 1f - Mathf.Exp(-18f * dt)), 20f);
            if (trailDelay > 0f) trailDelay -= dt; else trail01 = Mathf.MoveTowards(trail01, hp01, dt * 1.8f);
            hpTrail.rectTransform.sizeDelta = new Vector2(w * Mathf.Max(trail01, hp01), 20f);
            hpFill.color = Color.Lerp(Palette.Red, Palette.Teal, Mathf.Clamp01(hp01 * 2.5f));
            if (hpFlashT > 0f) { hpFlashT -= dt; hpFlash.color = Palette.Red.WithAlpha(Mathf.Clamp01(hpFlashT / 0.25f) * 0.6f); }
            for (int i = 0; i < 3; i++)
            {
                bool full = i < qi;
                float a = Mathf.MoveTowards(pips[i].color.a, full ? 1f : 0f, dt * 6f);
                float pulse = full ? 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 3f + i) : 1f;
                pips[i].color = Palette.Gold.WithAlpha(a * pulse);
                if (pipPop[i] > 0f) pipPop[i] -= dt * 3f;
                float s = 1f + 0.5f * Mathf.Clamp01(pipPop[i]);
                pips[i].rectTransform.localScale = Vector3.one * s;
            }
            // boss bar
            bossShown01 = Mathf.MoveTowards(bossShown01, bossVisible ? 1f : 0f, dt * 1.6f);
            float e = Ease.OutCubic(bossShown01);
            bossGroup.alpha = e;
            bossRt.anchoredPosition = new Vector2(0f, Mathf.Lerp(-40f, 80f, e));
            float bw = 894f;
            float shownHp = Mathf.Lerp(bossFill.rectTransform.sizeDelta.x / bw, bossHp01, 1f - Mathf.Exp(-14f * dt));
            bossFill.rectTransform.sizeDelta = new Vector2(bw * shownHp, 12f);
            bossPosture.rectTransform.sizeDelta = new Vector2(bw * bossPosture01, 6f);
            float hot = (bossBroken || bossPosture01 > 0.995f) ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 12f) : 0f;
            bossPosture.color = Color.Lerp(Palette.Posture, Color.white, hot);
            // prompt
            if (promptTimer > 0f) { promptTimer -= dt; promptGroup.alpha = Mathf.MoveTowards(promptGroup.alpha, 1f, dt * 5f); }
            else promptGroup.alpha = Mathf.MoveTowards(promptGroup.alpha, 0f, dt * 2.5f);
            // cutscene furniture
            letterbox = Mathf.MoveTowards(letterbox, letterboxTarget, letterboxSpeed * dt);
            float barH = 108f * Ease.OutCubic(letterbox);
            barTop.sizeDelta = new Vector2(4000f, barH);
            barBottom.sizeDelta = new Vector2(4000f, barH);
            subtitleGroup.alpha = Mathf.MoveTowards(subtitleGroup.alpha, string.IsNullOrEmpty(subtitleText.text) ? 0f : 1f, dt * 3.5f);
            skipGroup.alpha = Mathf.MoveTowards(skipGroup.alpha, skipWanted ? 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 2f) : 0f, dt * 2f);

            // screens
            if (titleShown)
            {
                titleGroup.alpha = Mathf.MoveTowards(titleGroup.alpha, 1f, dt * 1.5f);
                float br = 1f + 0.02f * Mathf.Sin(Time.unscaledTime * 1.2f);
                titleLogo.rectTransform.localScale = Vector3.one * br;
                titleGlow.color = Palette.Teal.WithAlpha(0.28f + 0.1f * Mathf.Sin(Time.unscaledTime * 0.9f));
                if (Time.unscaledTime > screenArmTime) titleMenu.Tick();
            }
            else if (titleGroup.gameObject.activeSelf)
            {
                titleGroup.alpha = Mathf.MoveTowards(titleGroup.alpha, 0f, dt * 3f);
                if (titleGroup.alpha <= 0f) titleGroup.gameObject.SetActive(false);
            }
            if (deathShown)
            {
                deathGroup.alpha = Mathf.MoveTowards(deathGroup.alpha, 1f, dt * 1.2f);
                deathPress.color = Palette.Bone.WithAlpha(0.5f + 0.5f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2f)));
                if (Time.unscaledTime > screenArmTime && Services.Input != null && Services.Input.ConfirmPressed)
                {
                    deathShown = false;
                    Services.Audio.PlaySfx("ui_confirm");
                    var cb = deathCb; deathCb = null;
                    if (cb != null) cb();
                }
            }
            else if (deathGroup.gameObject.activeSelf)
            {
                deathGroup.alpha = Mathf.MoveTowards(deathGroup.alpha, 0f, dt * 3f);
                if (deathGroup.alpha <= 0f) deathGroup.gameObject.SetActive(false);
            }
            if (victoryShown)
            {
                victoryGroup.alpha = Mathf.MoveTowards(victoryGroup.alpha, 1f, dt * 1f);
                victoryPress.color = Palette.Bone.WithAlpha(0.5f + 0.5f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2f)));
                if (Time.unscaledTime > screenArmTime && Services.Input != null && Services.Input.ConfirmPressed)
                {
                    victoryShown = false;
                    Services.Audio.PlaySfx("ui_confirm");
                    var cb = victoryCb; victoryCb = null;
                    if (cb != null) cb();
                }
            }
            else if (victoryGroup.gameObject.activeSelf)
            {
                victoryGroup.alpha = Mathf.MoveTowards(victoryGroup.alpha, 0f, dt * 3f);
                if (victoryGroup.alpha <= 0f) victoryGroup.gameObject.SetActive(false);
            }
            if (pauseGroup.gameObject.activeSelf)
            {
                pauseGroup.alpha = Mathf.MoveTowards(pauseGroup.alpha, pausedShown ? 1f : 0f, dt * 6f);
                if (!pausedShown && pauseGroup.alpha <= 0f) pauseGroup.gameObject.SetActive(false);
            }
        }

        // ------------------------------------------------------------ IUiService
        public void ShowBossBar(string name, string subtitle)
        {
            bossName.text = UiKit.Spaced(name);
            bossSub.text = subtitle;
            bossHp01 = 1f; bossPosture01 = 0f; bossBroken = false;
            bossFill.rectTransform.sizeDelta = new Vector2(894f, 12f);
            bossVisible = true;
        }

        public void HideBossBar() { bossVisible = false; }
        public void UpdateBossBar(float h, float p, bool broken) { bossHp01 = h; bossPosture01 = p; bossBroken = broken; }

        public void ShowNameCard(string title, string subtitle, float seconds)
        {
            if (cardCo != null) StopCoroutine(cardCo);
            cardCo = StartCoroutine(NameCardRoutine(title, subtitle, seconds));
        }

        IEnumerator NameCardRoutine(string title, string subtitle, float seconds)
        {
            bool bossCard = title == AshenSol.Boss.BossController.BossName;
            var sp = bossCard ? Res.Sprite("ui_bossname") : null;
            bool useSprite = bossCard && sp != null && sp != Res.White;
            cardSprite.enabled = useSprite;
            if (useSprite) cardSprite.sprite = sp;
            cardTitle.text = useSprite ? "" : UiKit.Spaced(title, 2);
            cardSub.text = useSprite ? "" : subtitle;
            cardTitle.color = bossCard ? Palette.Red : Palette.Bone;
            cardLineL.enabled = cardLineR.enabled = !useSprite;
            float t = 0f;
            while (t < 0.45f) { t += Time.unscaledDeltaTime; cardGroup.alpha = Ease.OutCubic(t / 0.45f); cardLineL.rectTransform.sizeDelta = new Vector2(360f * cardGroup.alpha, 2f); cardLineR.rectTransform.sizeDelta = cardLineL.rectTransform.sizeDelta; yield return null; }
            cardGroup.alpha = 1f;
            yield return new WaitForSecondsRealtime(seconds);
            t = 0f;
            while (t < 0.7f) { t += Time.unscaledDeltaTime; cardGroup.alpha = 1f - Ease.InCubic(t / 0.7f); yield return null; }
            cardGroup.alpha = 0f;
            cardCo = null;
        }

        public void ShowPrompt(string text, float seconds)
        {
            promptText.text = text;
            promptTimer = seconds;
        }

        public void Fade(float toAlpha, float seconds, Action onDone = null)
        {
            if (fadeCo != null) StopCoroutine(fadeCo);
            fadeCo = StartCoroutine(FadeRoutine(toAlpha, seconds, onDone));
        }

        IEnumerator FadeRoutine(float to, float seconds, Action onDone)
        {
            float from = fadeImg.color.a; float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                fadeImg.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, Ease.InOutSine(t / seconds)));
                yield return null;
            }
            fadeImg.color = new Color(0f, 0f, 0f, to);
            fadeCo = null;
            if (onDone != null) onDone();
        }

        public void ShowDeathScreen(Action onRespawn)
        {
            deathCb = onRespawn; deathShown = true; screenArmTime = Time.unscaledTime + 0.8f;
            deathGroup.gameObject.SetActive(true);
            deathGroup.alpha = 0f;
        }

        public void ShowVictoryScreen(GameStats s, Action onContinue)
        {
            victoryCb = onContinue; victoryShown = true; screenArmTime = Time.unscaledTime + 2.5f;
            victoryGroup.gameObject.SetActive(true);
            victoryGroup.alpha = 0f;
            int m = Mathf.FloorToInt(s.PlayTime / 60f), sec = Mathf.FloorToInt(s.PlayTime % 60f);
            victoryStats.text = string.Format("Difficulty  {7}\nTime  {0:D2}:{1:D2}\nParries  {2}   (perfect {3})\nDeaths  {4}\nDamage taken  {5}\nGuardians slain  {6}",
                m, sec, s.Parries, s.PerfectParries, s.Deaths, s.DamageTaken, s.Kills, Settings.DifficultyName);
        }

        public void ShowTitle(Action onStart)
        {
            titleCb = onStart; titleShown = true; screenArmTime = Time.unscaledTime + 0.8f;
            titleGroup.gameObject.SetActive(true);
            titleGroup.alpha = 0f;
            titleMenu.SetVisible(true);
            titleMenu.OnStart = () =>
            {
                if (!titleShown) return;
                titleShown = false;
                var cb = titleCb; titleCb = null;
                if (cb != null) cb();
            };
        }

        public void HideTitle() { titleShown = false; }

        public void SetPaused(bool paused)
        {
            pausedShown = paused;
            if (paused)
            {
                pauseGroup.gameObject.SetActive(true);
                if (pauseDifficulty != null) pauseDifficulty.text = UiKit.Spaced("DIFFICULTY:  " + Settings.DifficultyName);
            }
        }

        public void SetHudVisible(bool visible) { hudGroup.alpha = visible ? 1f : 0f; }

        // ------------------------------------------------------------ cutscene
        bool skipWanted;

        public void ShowSubtitle(string text)
        {
            subtitleText.text = text ?? "";
        }

        public void SetLetterbox(float amount, float seconds)
        {
            letterboxTarget = Mathf.Clamp01(amount);
            letterboxSpeed = Mathf.Abs(letterboxTarget - letterbox) / Mathf.Max(0.05f, seconds);
        }

        public void ShowSkipHint(bool visible) { skipWanted = visible; }
    }
}
