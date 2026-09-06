using System;
using System.Collections;
using UnityEngine;
using AshenSol.Level;
using AshenSol.Player;
using AshenSol.Enemies;
using AshenSol.Boss;

namespace AshenSol.Core
{
    /// <summary>Top-level state machine: Title → Level1 → BossArena → Victory → Title, plus death/respawn and pause.</summary>
    public class GameFlow : MonoBehaviour
    {
        public static GameFlow Instance { get; private set; }

        public GameState State { get; private set; } = GameState.Boot;
        public LevelId CurrentLevel { get; private set; }
        public Transform LevelRoot { get; private set; }
        public LevelInfo CurrentLevelInfo { get; private set; }
        public GameStats Stats { get; private set; } = new GameStats();
        public Vector2 RespawnPoint { get; private set; }
        public bool IsPaused { get { return TimeController.Instance != null && TimeController.Instance.IsPaused; } }
        public bool Busy { get { return busy; } }
        public bool DeathScreenShowing { get { return deathScreenShowing; } }
        public PlayerController Player { get { return player; } }
        public bool InLevel { get { return State == GameState.Level1 || State == GameState.Works || State == GameState.Stair || State == GameState.BossArena; } }

        PlayerController player;
        bool busy, deathScreenShowing, victoryPending;
        GameObject titleFx;

        void Awake()
        {
            Instance = this;
            Settings.Load();
            GameEvents.PlayerParried += OnParried;
            GameEvents.PlayerAttacked += OnPlayerAttacked;
            GameEvents.EnemyDamaged += OnEnemyDamaged;
            GameEvents.EnemyKilled += OnEnemyKilled;
            GameEvents.PlayerDied += OnPlayerDied;
            GameEvents.BossDefeated += OnBossDefeated;
        }

        void OnDestroy()
        {
            GameEvents.PlayerParried -= OnParried;
            GameEvents.PlayerAttacked -= OnPlayerAttacked;
            GameEvents.EnemyDamaged -= OnEnemyDamaged;
            GameEvents.EnemyKilled -= OnEnemyKilled;
            GameEvents.PlayerDied -= OnPlayerDied;
            GameEvents.BossDefeated -= OnBossDefeated;
        }

        void Start() { StartCoroutine(Boot()); }

        IEnumerator Boot()
        {
            yield return null;
            if (CmdArgs.Has("-skipTitle"))
            {
                string lvl = (CmdArgs.Get("-startLevel", "level1") ?? "level1").ToLowerInvariant();
                Stats = new GameStats();
                LoadLevel(lvl == "boss" ? LevelId.BossArena : lvl == "works" ? LevelId.Works
                        : lvl == "stair" ? LevelId.Stair : LevelId.Level1);
            }
            else GoToTitle();
        }

        void SetState(GameState s)
        {
            State = s;
            GameEvents.RaiseStateChanged(s);
            GameEvents.RaiseLog("state " + s);
        }

        void Update()
        {
            if (busy || !InLevel) return;
            if (!IsPaused && !deathScreenShowing && !victoryPending) Stats.PlayTime += Time.unscaledDeltaTime;
            if (Services.Input == null) return;
            if (Services.Ui.UpgradePanelOpen || Services.Ui.CreditsRolling) return;   // these own input while they are up
            if (Services.Input.PausePressed && !deathScreenShowing && !victoryPending) TogglePause();
            else if (IsPaused && Services.Input.QuitPressed) { Unpause(); GoToTitle(); }
        }

        void TogglePause()
        {
            bool p = !IsPaused;
            TimeController.Instance.SetPaused(p);
            Services.Ui.SetPaused(p);
            Services.Audio.PlaySfx(p ? "ui_move" : "ui_confirm", 0.8f);
            Services.Audio.SetMusicDuck(p ? 0.4f : 1f, 0.3f);
        }

        void Unpause()
        {
            if (!IsPaused) return;
            TimeController.Instance.SetPaused(false);
            Services.Ui.SetPaused(false);
            Services.Audio.SetMusicDuck(1f, 0.3f);
        }

        // ---------------- Title ----------------
        public void GoToTitle()
        {
            if (busy) return;
            StartCoroutine(TitleRoutine());
        }

        IEnumerator TitleRoutine()
        {
            busy = true;
            Unpause();
            Services.Ui.Fade(1f, 0.5f);
            yield return new WaitForSecondsRealtime(0.55f);
            DestroyLevel();
            victoryPending = false; deathScreenShowing = false;
            if (player != null) player.gameObject.SetActive(false);
            Services.Ui.SetHudVisible(false);
            Services.Ui.HideBossBar();
            Services.Cam.SetTarget(null);
            Services.Cam.SetZoom(6f, 0f);
            Services.Cam.SetBounds(new Rect(-100f, -100f, 200f, 200f));
            CameraController.SetAmbient(new Color(0.85f, 0.7f, 0.7f), 0.5f);
            Services.Audio.PlayAmbience(null);
            Services.Audio.PlayMusic("music_title", 2f);
            Services.Audio.SetMusicDuck(1f, 0.5f);
            if (titleFx == null)
            {
                titleFx = new GameObject("TitleFx");
                AshenSol.VFX.VfxManager.CreateAmbientEmbers(new Rect(-14f, -9f, 28f, 18f), Palette.Amber, 9f, titleFx.transform);
            }
            SetState(GameState.Title);
            Services.Ui.ShowTitle(StartGame);
            Services.Ui.Fade(0f, 1.2f);
            busy = false;
        }

        public void StartGame()
        {
            if (State != GameState.Title || busy) return;
            Stats = new GameStats();
            Progression.Reset();
            Services.Ui.HideTitle();
            Services.Audio.PlaySfx("ui_confirm");
            StartCoroutine(IntroThenLevel());
        }

        /// <summary>The opening cutscene, then level 1. -skipIntro jumps straight in.</summary>
        IEnumerator IntroThenLevel()
        {
            if (!CmdArgs.Has("-skipIntro"))
            {
                busy = true;
                SetState(GameState.Intro);
                Services.Ui.Fade(1f, 0.6f);
                yield return new WaitForSecondsRealtime(0.65f);
                if (titleFx != null) { Destroy(titleFx); titleFx = null; }
                var intro = AshenSol.Intro.IntroSequence.Create();
                yield return intro.Play();
                busy = false;
            }
            LoadLevel(LevelId.Level1);
        }

        // ---------------- Levels ----------------
        public void LoadLevel(LevelId id)
        {
            if (busy) return;
            StartCoroutine(LoadLevelRoutine(id));
        }

        IEnumerator LoadLevelRoutine(LevelId id)
        {
            busy = true;
            Unpause();
            deathScreenShowing = false; victoryPending = false;
            Services.Ui.Fade(1f, 0.5f);
            yield return new WaitForSecondsRealtime(0.55f);

            DestroyLevel();
            if (titleFx != null) { Destroy(titleFx); titleFx = null; }
            Projectile.ClearAll();
            HitStop.Remaining = 0f;

            LevelRoot = new GameObject("Level_" + id).transform;
            LevelInfo info = null;
            try
            {
                info = id == LevelId.Level1 ? LevelBuilder.BuildLevel1(LevelRoot)
                     : id == LevelId.Works ? LevelBuilder.BuildWorks(LevelRoot)
                     : id == LevelId.Stair ? LevelBuilder.BuildStair(LevelRoot)
                     : LevelBuilder.BuildBossArena(LevelRoot);
            }
            catch (Exception e) { Debug.LogException(e); }
            if (info == null)
            {
                info = new LevelInfo { Id = id, Title = id.ToString(), Subtitle = "", Bounds = new Rect(-50f, -50f, 100f, 100f), PlayerSpawn = Vector2.zero, MusicTrack = "music_level1", AmbientColor = Color.white, AmbientIntensity = 0.6f };
            }
            CurrentLevelInfo = info;
            CurrentLevel = id;
            RespawnPoint = info.PlayerSpawn;

            EnsurePlayer();
            player.gameObject.SetActive(true);
            player.Respawn(info.PlayerSpawn);
            player.SetControlEnabled(true);

            Services.Cam.SetTarget(player.transform);
            Services.Cam.SetBounds(info.Bounds);
            Services.Cam.SetZoom(6f, 0f);
            Services.Cam.SnapToTarget();
            CameraController.SetAmbient(info.AmbientColor, info.AmbientIntensity);

            Services.Audio.PlayMusic(info.MusicTrack, 1.5f);
            Services.Audio.PlayAmbience(info.Ambience, 0.5f);
            Services.Audio.SetMusicDuck(1f, 0.5f);
            Services.Ui.SetHudVisible(true);
            Services.Ui.HideBossBar();

            if (info.ExitGate != null) info.ExitGate.PlayerEntered += OnGateEntered;
            SpawnAshPile();
            SetState(id == LevelId.Level1 ? GameState.Level1
                   : id == LevelId.Works ? GameState.Works
                   : id == LevelId.Stair ? GameState.Stair
                   : GameState.BossArena);
            GameEvents.RaiseLevelBuilt(id);
            yield return null;
            Services.Ui.Fade(0f, 0.9f);
            Services.Ui.ShowNameCard(info.Title, info.Subtitle, 3.2f);
            busy = false;
        }

        void EnsurePlayer()
        {
            if (player != null) return;
            Transform parent = GameBootstrap.Root != null ? GameBootstrap.Root.transform : null;
            player = PlayerController.Create(RespawnPoint, parent);
        }

        void DestroyLevel()
        {
            if (CurrentLevelInfo != null && CurrentLevelInfo.ExitGate != null)
                CurrentLevelInfo.ExitGate.PlayerEntered -= OnGateEntered;
            if (LevelRoot != null)
            {
                DestroyImmediate(LevelRoot.gameObject);
                LevelRoot = null;
            }
            CurrentLevelInfo = null;
        }

        public void SetRespawnPoint(Vector2 p) { RespawnPoint = p; }

        /// <summary>Put the dropped ash back into the world if it belongs to this level.</summary>
        void SpawnAshPile()
        {
            if (LevelRoot != null)
                foreach (var pile in LevelRoot.GetComponentsInChildren<AshPile>())
                {
                    pile.gameObject.SetActive(false);
                    Destroy(pile.gameObject);
                }
            if (Progression.DroppedAsh <= 0 || Progression.DropLevel != CurrentLevel || LevelRoot == null) return;
            AshPile.Create(Progression.DropPoint, LevelRoot);
        }

        void OnGateEntered()
        {
            if (busy || !InLevel) return;
            LevelId next = State == GameState.Level1 ? LevelId.Works
                         : State == GameState.Works ? LevelId.Stair
                         : State == GameState.Stair ? LevelId.BossArena
                         : LevelId.None;
            if (next == LevelId.None) return;
            Services.Audio.PlaySfx("level_start");
            GameEvents.RaiseLog("gate entered -> " + next);
            LoadLevel(next);
        }

        // ---------------- Death / respawn ----------------
        void OnPlayerDied()
        {
            if (!InLevel || deathScreenShowing || victoryPending) return;
            Stats.Deaths++;
            // souls rule: everything carried stays where you fell
            if (player != null) Progression.DropOnDeath(player.LastSafeGroundPosition, CurrentLevel);
            GameEvents.RaiseLog("player died (deaths=" + Stats.Deaths + ")");
            StartCoroutine(DeathRoutine());
        }

        IEnumerator DeathRoutine()
        {
            deathScreenShowing = true;
            Unpause();
            TimeController.Instance.SlowMo(0.35f, 1.2f);
            Services.Audio.SetMusicDuck(0.3f, 1f);
            yield return new WaitForSecondsRealtime(1.4f);
            if (!InLevel) { deathScreenShowing = false; yield break; }
            Services.Ui.ShowDeathScreen(Respawn);
        }

        public void Respawn()
        {
            if (busy || !InLevel) return;
            StartCoroutine(RespawnRoutine());
        }

        IEnumerator RespawnRoutine()
        {
            busy = true;
            Services.Ui.Fade(1f, 0.5f);
            yield return new WaitForSecondsRealtime(0.55f);
            Projectile.ClearAll();
            HitStop.Remaining = 0f;
            if (CurrentLevelInfo != null) CurrentLevelInfo.ResetEnemies();
            player.Respawn(RespawnPoint, true);   // you come back with the sash fully charged
            player.SetControlEnabled(true);
            Services.Cam.SetZoom(6f, 0.3f);
            Services.Cam.SnapToTarget();
            GameEvents.RaisePlayerRespawned();
            Services.Audio.SetMusicDuck(1f, 1f);
            deathScreenShowing = false;
            SpawnAshPile();
            yield return null;
            Services.Ui.Fade(0f, 0.8f);
            busy = false;
        }

        // ---------------- Victory ----------------
        void OnBossDefeated()
        {
            if (State != GameState.BossArena || victoryPending) return;
            victoryPending = true;
            Stats.BossDefeated = true;
            StartCoroutine(VictoryRoutine());
        }

        IEnumerator VictoryRoutine()
        {
            TimeController.Instance.SlowMo(0.25f, 1.5f);
            yield return new WaitForSecondsRealtime(3f);
            if (player != null) player.SetControlEnabled(false);
            SetState(GameState.Victory);
            Services.Ui.SetHudVisible(false);
            Services.Ui.HideBossBar();
            Services.Audio.PlayMusic("music_victory", 1f);
            Services.Ui.ShowVictoryScreen(Stats, RollCredits);
        }

        /// <summary>The run is over: the credits roll, and the title comes back after them.</summary>
        void RollCredits()
        {
            if (player != null) player.SetControlEnabled(false);
            Services.Ui.ShowCredits(GoToTitle);
        }

        // ---------------- Stats ----------------
        void OnParried(Vector2 pos, bool perfect) { Stats.Parries++; if (perfect) Stats.PerfectParries++; }
        void OnPlayerAttacked(AttackInfo info, HitOutcome outcome)
        {
            if (outcome == HitOutcome.Hit) Stats.DamageTaken += info.Damage;
            else if (outcome == HitOutcome.Blocked) Stats.DamageTaken += Mathf.CeilToInt(info.Damage * 0.3f);
        }
        void OnEnemyDamaged(IDamageable e, int dmg) { Stats.DamageDealt += dmg; }
        void OnEnemyKilled(IDamageable e) { if (!(e is BossController)) Stats.Kills++; }
    }
}
