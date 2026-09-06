// ASHEN SOL — shared contracts. THIS FILE IS THE CROSS-MODULE API.
// Every module compiles against these types. Do NOT change signatures here without
// updating SPEC.md; adding new members is allowed only by the integration/compile stage.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AshenSol.Core
{
    public enum Team { Player = 0, Enemy = 1 }
    public enum AttackKind { Parryable = 0, Unblockable = 1 }
    public enum HitOutcome { Ignored = 0, Dodged = 1, Parried = 2, Blocked = 3, Hit = 4 }
    public enum GameState { Boot = 0, Title = 1, Level1 = 2, BossArena = 3, Victory = 4, Intro = 5, Works = 6 }
    public enum LevelId { None = 0, Level1 = 1, Works = 2, BossArena = 3 }
    public enum EnemyType { Grunt = 0, SpearSentinel = 1, WatcherDrone = 2, Boss = 100 }

    /// <summary>Describes one attack instance delivered to an IDamageable.</summary>
    public struct AttackInfo
    {
        public GameObject Source;     // attacker root object (may be a projectile)
        public Team Team;             // team of the attacker
        public int Damage;
        public Vector2 Origin;        // where the attack comes from (knockback direction, spark placement)
        public Vector2 HitPoint;      // estimated contact point (world)
        public AttackKind Kind;       // Parryable (white flash) or Unblockable (red flash)
        public float Knockback;       // impulse magnitude applied to the defender (0 = none)
        public bool IsProjectile;
        public Component Payload;     // e.g. the Projectile component (implements IReflectable) — may be null
        public string Tag;            // free-form id: "grunt_slash", "boss_slam", "player_combo2", ...
    }

    public interface IDamageable
    {
        bool IsAlive { get; }
        Team Team { get; }
        Transform Transform { get; }
        Vector2 Center { get; }
        /// <summary>Resolve an incoming attack. The defender decides the outcome.</summary>
        HitOutcome ReceiveAttack(in AttackInfo info);
    }

    /// <summary>Projectiles that can be sent back by a perfect parry.</summary>
    public interface IReflectable
    {
        void Reflect(Vector2 newDirection, Team newTeam, int newDamage);
    }

    /// <summary>Frame-polled input. Implemented by KeyboardGamepadInput (Input System) and ScriptedInput (AutoPilot).</summary>
    public interface IInputProvider
    {
        float Horizontal { get; }     // -1..1
        float Vertical { get; }       // -1..1
        bool JumpPressed { get; }     // true only on the frame the button went down
        bool JumpHeld { get; }
        bool AttackPressed { get; }
        bool ParryPressed { get; }
        bool ParryHeld { get; }
        bool DashPressed { get; }
        bool QiBlastPressed { get; }
        bool HealPressed { get; }
        bool PausePressed { get; }
        bool ConfirmPressed { get; }  // Enter / gamepad South — menus
        bool QuitPressed { get; }     // Q / gamepad North while paused — abandon the run
        bool AnyPressed { get; }
        /// <summary>Called once per frame by InputRouter (execution order -1000) before gameplay reads input.</summary>
        void Tick();
    }

    public interface IAudioService
    {
        /// <summary>Play "Resources/Audio/SFX/{name}". Missing clips log a warning once and are ignored.</summary>
        void PlaySfx(string name, float volume = 1f, float pitchVariance = 0.06f);
        /// <summary>Same as PlaySfx but attenuated by distance to the player (>= 18 units = silent).</summary>
        void PlaySfxAt(string name, Vector2 worldPos, float volume = 1f, float pitchVariance = 0.06f);
        /// <summary>Crossfade to "Resources/Audio/Music/{name}" (looping with seamless crossfade-loop). Same name = no-op.</summary>
        void PlayMusic(string name, float fadeSeconds = 1.5f);
        void StopMusic(float fadeSeconds = 1f);
        /// <summary>Loop an ambience bed from SFX folder (wind, arena hum). null/empty stops it.</summary>
        void PlayAmbience(string name, float volume = 0.5f);
        /// <summary>0..1 music volume multiplier (ducking during death screen etc.).</summary>
        void SetMusicDuck(float multiplier, float seconds);
        /// <summary>Play a narration line from "Resources/Audio/Voice/{name}". Returns its length in
        /// seconds so a cutscene can time itself to the delivery (0 when the clip is missing).</summary>
        float PlayVoice(string name, float volume = 1f);
        void StopVoice();
    }

    public interface ICameraService
    {
        Camera Camera { get; }
        Transform Transform { get; }
        /// <summary>Add trauma (0..1). Shake magnitude = trauma^2, decays ~1.6/s.</summary>
        void Shake(float trauma);
        void SetTarget(Transform target);
        /// <summary>Clamp the camera view inside these world bounds.</summary>
        void SetBounds(Rect worldBounds);
        void SetZoom(float orthoSize, float seconds);
        /// <summary>Cutscene control: stop following the target and drive the position directly.</summary>
        void SetManual(bool manual);
        void SetPosition(Vector2 worldPos);
        /// <summary>Temporarily look at a world position (boss intro). ReleaseFocus returns to target.</summary>
        void Focus(Vector2 worldPos, float seconds);
        void ReleaseFocus(float seconds);
        void SnapToTarget();
        /// <summary>Kick the camera in a direction (hit feedback), decays quickly.</summary>
        void Kick(Vector2 dir, float amount);
    }

    public interface IVfxService
    {
        void SlashArc(Vector2 pos, float angleDeg, bool flipX, Color color, float scale = 1f);
        void ParrySpark(Vector2 pos, bool perfect);
        void HitSpark(Vector2 pos, Vector2 dir, Color color, float scale = 1f);
        void InkSplatter(Vector2 pos, Vector2 dir, Color color, int count = 12);
        void DustPuff(Vector2 pos, float scale = 1f);
        /// <summary>Death dissolve: clones the renderers, fades/scatters them, bursts particles.</summary>
        void Dissolve(SpriteRenderer[] renderers, Vector2 pos, Color tint);
        void Shockwave(Vector2 pos, float radius, Color color);
        void Afterimage(SpriteRenderer[] renderers, Color tint, float lifeSeconds = 0.3f);
        void FlashLight(Vector2 pos, Color color, float intensity, float radius, float seconds);
        void ScreenFlash(Color color, float seconds);
        /// <summary>Post-process pulse (chromatic aberration + slight lens distortion), strength 0..1.</summary>
        void ChromaticPulse(float strength, float seconds);
        void FloatingText(Vector2 worldPos, string text, Color color, float scale = 1f);
        void Embers(Vector2 pos, int count, Color color);
        void ProjectileTrail(Transform follow, Color color);
    }

    public interface IUiService
    {
        void ShowBossBar(string name, string subtitle);
        void HideBossBar();
        void UpdateBossBar(float hp01, float posture01, bool postureBroken);
        /// <summary>Big centered name card (boss intro / level title). Auto hides after seconds.</summary>
        void ShowNameCard(string title, string subtitle, float seconds);
        /// <summary>Small hint text near the top center (tutorial, "Gate sealed").</summary>
        void ShowPrompt(string text, float seconds);
        /// <summary>Full screen black fade. toAlpha 1 = black.</summary>
        void Fade(float toAlpha, float seconds, Action onDone = null);
        void ShowDeathScreen(Action onRespawn);
        void ShowVictoryScreen(GameStats stats, Action onContinue);
        void ShowTitle(Action onStart);
        void HideTitle();
        void SetPaused(bool paused);
        void SetHudVisible(bool visible);
        /// <summary>Cutscene caption at the bottom of the screen. null or empty hides it.</summary>
        void ShowSubtitle(string text);
        /// <summary>Cinematic bars: 0 = none, 1 = full letterbox.</summary>
        void SetLetterbox(float amount, float seconds);
        void ShowSkipHint(bool visible);
    }

    /// <summary>Service locator. Each manager registers itself in Awake (GameBootstrap creates them in order).</summary>
    public static class Services
    {
        public static IAudioService Audio;
        public static ICameraService Cam;
        public static IVfxService Vfx;
        public static IUiService Ui;
        public static IInputProvider Input;

        // Null-safe accessors (return no-op stubs before managers exist, so tests/early calls never NRE)
        public static IAudioService AudioOrNull => Audio;
        public static bool Ready => Audio != null && Cam != null && Vfx != null && Ui != null && Input != null;
    }

    /// <summary>Global gameplay events. Use Raise* helpers; subscribers must unsubscribe in OnDestroy.</summary>
    public static class GameEvents
    {
        public static event Action<Vector2, bool> PlayerParried;          // position, perfect
        public static event Action<AttackInfo, HitOutcome> PlayerAttacked; // any attack that reached the player and its outcome
        public static event Action<int, int> PlayerHealthChanged;         // hp, max
        public static event Action<int, int> PlayerQiChanged;             // qi, max
        public static event Action PlayerDied;
        public static event Action PlayerRespawned;
        public static event Action<IDamageable, int> EnemyDamaged;        // enemy, damage applied
        public static event Action<IDamageable> EnemyKilled;
        public static event Action<string> CheckpointActivated;           // checkpoint id
        public static event Action<string, string> BossFightStarted;      // name, subtitle
        public static event Action<int> BossPhaseChanged;                 // 1-based phase
        public static event Action<float, float, bool> BossHealthChanged;  // hp01, posture01, guard broken
        public static event Action BossDefeated;
        public static event Action<LevelId> LevelBuilt;
        public static event Action<GameState> StateChanged;
        public static event Action GateOpened;
        public static event Action<string> Log;                           // free-form gameplay log line (AutoPilot writes these to file)

        public static void RaisePlayerParried(Vector2 pos, bool perfect) { PlayerParried?.Invoke(pos, perfect); }
        public static void RaisePlayerAttacked(in AttackInfo info, HitOutcome outcome) { PlayerAttacked?.Invoke(info, outcome); }
        public static void RaisePlayerHealthChanged(int hp, int max) { PlayerHealthChanged?.Invoke(hp, max); }
        public static void RaisePlayerQiChanged(int qi, int max) { PlayerQiChanged?.Invoke(qi, max); }
        public static void RaisePlayerDied() { PlayerDied?.Invoke(); }
        public static void RaisePlayerRespawned() { PlayerRespawned?.Invoke(); }
        public static void RaiseEnemyDamaged(IDamageable e, int dmg) { EnemyDamaged?.Invoke(e, dmg); }
        public static void RaiseEnemyKilled(IDamageable e) { EnemyKilled?.Invoke(e); }
        public static void RaiseCheckpointActivated(string id) { CheckpointActivated?.Invoke(id); }
        public static void RaiseBossFightStarted(string name, string subtitle) { BossFightStarted?.Invoke(name, subtitle); }
        public static void RaiseBossPhaseChanged(int phase) { BossPhaseChanged?.Invoke(phase); }
        public static void RaiseBossHealthChanged(float hp01, float posture01, bool broken) { BossHealthChanged?.Invoke(hp01, posture01, broken); }
        public static void RaiseBossDefeated() { BossDefeated?.Invoke(); }
        public static void RaiseLevelBuilt(LevelId id) { LevelBuilt?.Invoke(id); }
        public static void RaiseStateChanged(GameState s) { StateChanged?.Invoke(s); }
        public static void RaiseGateOpened() { GateOpened?.Invoke(); }
        public static void RaiseLog(string line) { Log?.Invoke(line); }

        /// <summary>Drop every subscriber (used only by tests / hard resets).</summary>
        public static void ClearAll()
        {
            PlayerParried = null; PlayerAttacked = null; PlayerHealthChanged = null; PlayerQiChanged = null;
            PlayerDied = null; PlayerRespawned = null; EnemyDamaged = null; EnemyKilled = null;
            CheckpointActivated = null; BossFightStarted = null; BossPhaseChanged = null; BossHealthChanged = null;
            BossDefeated = null; LevelBuilt = null; StateChanged = null; GateOpened = null; Log = null;
        }
    }

    /// <summary>Physics layers (numeric — names are set by the editor bootstrap but code must use these constants).</summary>
    public static class Layers
    {
        public const int Default = 0;
        public const int Ground = 6;       // solid world geometry
        public const int Player = 7;       // player body collider
        public const int Enemy = 8;        // enemy/boss body colliders
        public const int Hazard = 9;       // spikes, kill zones (trigger colliders)
        public const int Projectile = 10;  // projectiles (trigger colliders)
        public const int Trigger = 11;     // gates, checkpoints, encounter zones (trigger colliders)
        public const int OneWay = 12;      // one-way platforms (PlatformEffector2D)

        public const int GroundMask = (1 << Ground) | (1 << OneWay);
        public const int PlayerMask = 1 << Player;
        public const int EnemyMask = 1 << Enemy;
        public const int ProjectileMask = 1 << Projectile;
    }

    /// <summary>Sprite sorting orders (single sorting layer "Default").</summary>
    public static class SortOrder
    {
        public const int BgSky = -400;
        public const int BgFar = -300;
        public const int BgMid = -250;
        public const int BgNear = -200;
        public const int BgWall = -150;     // wall panels / lattice behind gameplay
        public const int BgProps = -100;    // pillars, banners, statues behind the play plane
        public const int Ground = 0;
        public const int GroundDecor = 5;   // moss strips, spikes
        public const int Props = 10;        // lanterns, shrines, gate
        public const int EnemyBack = 20;
        public const int Enemy = 30;
        public const int Boss = 35;
        public const int PlayerBack = 40;
        public const int Player = 50;
        public const int PlayerFront = 55;
        public const int Projectile = 60;
        public const int Fx = 80;
        public const int FxFront = 90;
        public const int Fog = 120;
        public const int Foreground = 150;
    }

    /// <summary>Taopunk palette (Nine Sols inspired). Use these — do not invent new base colours.</summary>
    public static class Palette
    {
        public static readonly Color Ink = Hex("#07090f");
        public static readonly Color Night = Hex("#0d1526");
        public static readonly Color Deep = Hex("#132238");
        public static readonly Color Slate = Hex("#243b52");
        public static readonly Color Stone = Hex("#5a6b7a");
        public static readonly Color Bone = Hex("#e8e0d0");
        public static readonly Color Teal = Hex("#4fe3d0");
        public static readonly Color TealDeep = Hex("#1a8f86");
        public static readonly Color Red = Hex("#ff3a3a");
        public static readonly Color RedDeep = Hex("#a3121f");
        public static readonly Color Gold = Hex("#ffcc55");
        public static readonly Color Amber = Hex("#ff9a3c");
        public static readonly Color Violet = Hex("#7b5cff");
        public static readonly Color White = Color.white;

        public static readonly Color ParryFlash = Hex("#ffffff");
        public static readonly Color TelegraphWhite = Hex("#ffffff");
        public static readonly Color TelegraphRed = Hex("#ff2a2a");
        public static readonly Color PlayerSlash = Hex("#9ffff2");
        public static readonly Color EnemySlash = Hex("#ffb37a");
        public static readonly Color BossSlash = Hex("#ff5a5a");
        public static readonly Color Posture = Hex("#ffcc55");   // yellow guard bar

        public static Color Hex(string hex)
        {
            Color c;
            return ColorUtility.TryParseHtmlString(hex, out c) ? c : Color.magenta;
        }

        public static Color WithAlpha(this Color c, float a) { c.a = a; return c; }
        /// <summary>HDR-boost a colour so bloom picks it up.</summary>
        public static Color Glow(this Color c, float intensity) { return new Color(c.r * intensity, c.g * intensity, c.b * intensity, c.a); }
    }

    /// <summary>Hit-stop request. TimeController (Core) applies it to Time.timeScale.</summary>
    public static class HitStop
    {
        public static float Remaining;
        public static void Request(float seconds) { Remaining = Mathf.Max(Remaining, seconds); }
    }

    [Serializable]
    public class GameStats
    {
        public float PlayTime;
        public int Parries;
        public int PerfectParries;
        public int Deaths;
        public int Kills;
        public int DamageTaken;
        public int DamageDealt;
        public bool BossDefeated;
    }

    /// <summary>Resource loading helpers with caching and one-time warnings.</summary>
    public static class Res
    {
        public const string SpritesPath = "Sprites/";
        public const string SfxPath = "Audio/SFX/";
        public const string VoicePath = "Audio/Voice/";
        public const string MusicPath = "Audio/Music/";

        static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();
        static readonly Dictionary<string, AudioClip> clipCache = new Dictionary<string, AudioClip>();
        static readonly HashSet<string> warned = new HashSet<string>();
        static Sprite white;

        public static Sprite Sprite(string name)
        {
            if (string.IsNullOrEmpty(name)) return White;
            Sprite s;
            if (spriteCache.TryGetValue(name, out s)) return s;
            s = Resources.Load<Sprite>(SpritesPath + name);
            if (s == null)
            {
                if (warned.Add("sprite:" + name)) Debug.LogWarning("[Res] Missing sprite: " + name + " (using white placeholder)");
                s = White;
            }
            spriteCache[name] = s;
            return s;
        }

        public static AudioClip Clip(string path)
        {
            AudioClip c;
            if (clipCache.TryGetValue(path, out c)) return c;
            c = Resources.Load<AudioClip>(path);
            if (c == null && warned.Add("clip:" + path)) Debug.LogWarning("[Res] Missing audio clip: " + path);
            clipCache[path] = c;
            return c;
        }

        /// <summary>4x4 white sprite (100 PPU) — generated at runtime, never missing.</summary>
        public static Sprite White
        {
            get
            {
                if (white == null)
                {
                    var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                    var px = new Color[16];
                    for (int i = 0; i < 16; i++) px[i] = Color.white;
                    tex.SetPixels(px); tex.Apply();
                    tex.filterMode = FilterMode.Bilinear;
                    white = UnityEngine.Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
                    white.name = "white";
                }
                return white;
            }
        }
    }

    /// <summary>Command line helpers: -autopilot, -screenshotDir <dir>, -quitAfter <sec>, -skipTitle, -startLevel boss</summary>
    public static class CmdArgs
    {
        static string[] args;
        static string[] Args { get { if (args == null) { try { args = Environment.GetCommandLineArgs(); } catch { args = new string[0]; } } return args; } }
        public static bool Has(string flag)
        {
            foreach (var a in Args) if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        public static string Get(string flag, string fallback = null)
        {
            var a = Args;
            for (int i = 0; i < a.Length - 1; i++) if (string.Equals(a[i], flag, StringComparison.OrdinalIgnoreCase)) return a[i + 1];
            return fallback;
        }
        public static float GetFloat(string flag, float fallback)
        {
            float f; var s = Get(flag);
            return (s != null && float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f)) ? f : fallback;
        }
    }

    /// <summary>Easing helpers shared by VFX/UI/animation code.</summary>
    public static class Ease
    {
        public static float OutCubic(float t) { t = Mathf.Clamp01(t); return 1f - Mathf.Pow(1f - t, 3f); }
        public static float InCubic(float t) { t = Mathf.Clamp01(t); return t * t * t; }
        public static float InOutSine(float t) { t = Mathf.Clamp01(t); return -(Mathf.Cos(Mathf.PI * t) - 1f) / 2f; }
        public static float OutBack(float t) { t = Mathf.Clamp01(t); const float c1 = 1.70158f, c3 = c1 + 1f; return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f); }
        public static float OutElastic(float t) { t = Mathf.Clamp01(t); if (t == 0f || t == 1f) return t; const float c4 = (2f * Mathf.PI) / 3f; return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * c4) + 1f; }
        public static float OutQuad(float t) { t = Mathf.Clamp01(t); return 1f - (1f - t) * (1f - t); }
        public static float Punch(float t) { t = Mathf.Clamp01(t); return Mathf.Sin(t * Mathf.PI) * (1f - t); }
    }

    /// <summary>Material access that survives builds (no Shader.Find dependence on stripped shaders).</summary>
    public static class MaterialLibrary
    {
        static Material lit, unlit, additive, ui;

        /// <summary>URP 2D default lit sprite material (taken from a throw-away SpriteRenderer, always included in builds).</summary>
        public static Material SpriteLit
        {
            get
            {
                if (lit == null)
                {
                    var go = new GameObject("_matprobe"); go.hideFlags = HideFlags.HideAndDontSave;
                    lit = go.AddComponent<SpriteRenderer>().sharedMaterial;
                    UnityEngine.Object.Destroy(go);
                }
                return lit;
            }
        }

        public static Material SpriteUnlit
        {
            get
            {
                if (unlit == null)
                {
                    var sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                    unlit = sh != null ? new Material(sh) : SpriteLit;
                }
                return unlit;
            }
        }

        /// <summary>Solid silhouette (vertex colour × texture alpha) — used for white/red telegraph flashes over sprites.</summary>
        public static Material Silhouette
        {
            get
            {
                if (silhouette == null)
                {
                    var sh = Shader.Find("AshenSol/Silhouette");
                    silhouette = sh != null ? new Material(sh) : new Material(SpriteUnlit);
                    silhouette.renderQueue = 3000;
                }
                return silhouette;
            }
        }
        static Material silhouette;

        /// <summary>Additive sprite material. Uses our own shader so SpriteRenderers keep their sprite texture
        /// (URP's particle shader ignores it and paints solid quads).</summary>
        public static Material Additive
        {
            get
            {
                if (additive == null)
                {
                    var sh = Shader.Find("AshenSol/SpriteAdditive");
                    additive = sh != null ? new Material(sh) : new Material(SpriteUnlit);
                    additive.renderQueue = 3000;
                }
                return additive;
            }
        }
    }
}
