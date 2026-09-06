using System.Collections;
using UnityEngine;
using AshenSol.Core;
using AshenSol.Level;
using AshenSol.Player;

namespace AshenSol.Boss
{
    /// <summary>Owns the boss encounter: entry trigger, intro cinematic, music, reset on player death, victory hooks.</summary>
    public class BossArenaDirector : MonoBehaviour
    {
        public static BossArenaDirector Create(Rect arenaBounds, Vector2 bossSpawn, Gate entranceGate, Transform parent)
        {
            var go = new GameObject("BossArenaDirector");
            go.layer = Layers.Trigger;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(arenaBounds.xMin + 1f, arenaBounds.center.y, 0f);
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(0.6f, Mathf.Max(4f, arenaBounds.height));
            var d = go.AddComponent<BossArenaDirector>();
            d.arena = arenaBounds;
            d.gate = entranceGate;
            d.Boss = BossController.Create(bossSpawn, parent);
            d.Boss.Defeated += d.OnDefeated;
            return d;
        }

        public BossController Boss { get; private set; }
        public bool IntroPlayed { get; private set; }
        public bool FightActive { get; private set; }

        Rect arena; Gate gate; bool subscribed;

        void Awake()
        {
            GameEvents.PlayerRespawned += OnPlayerRespawned;
            subscribed = true;
        }

        void OnDestroy()
        {
            if (subscribed) GameEvents.PlayerRespawned -= OnPlayerRespawned;
            if (Boss != null) Boss.Defeated -= OnDefeated;
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (other.gameObject.layer != Layers.Player) return;
            if (IntroPlayed || FightActive) return;
            var p = other.GetComponentInParent<PlayerController>();
            if (p == null || !p.IsAlive) return;
            StartIntro();
        }

        public void StartIntro()
        {
            if (IntroPlayed) return;
            IntroPlayed = true;
            StartCoroutine(IntroRoutine());
        }

        IEnumerator IntroRoutine()
        {
            var player = PlayerController.Instance;
            if (player != null) player.SetControlEnabled(false);
            if (gate != null) gate.Close();
            Services.Cam.Focus(Boss.Center + new Vector2(0f, 1f), 0.9f);
            Services.Cam.SetZoom(7.2f, 1f);
            Services.Audio.SetMusicDuck(0.2f, 0.6f);
            yield return new WaitForSecondsRealtime(0.7f);

            Boss.Rise();
            Services.Audio.PlaySfx("boss_roar");
            Services.Cam.Shake(0.6f);
            Services.Vfx.ChromaticPulse(0.5f, 0.6f);
            yield return new WaitForSecondsRealtime(0.8f);

            Services.Ui.ShowNameCard(BossController.BossName, BossController.BossSubtitle, 3f);
            Services.Ui.ShowBossBar(BossController.BossName, BossController.BossSubtitle);
            Services.Audio.PlayMusic("music_boss", 1f);
            Services.Audio.SetMusicDuck(1f, 0.5f);
            GameEvents.RaiseBossFightStarted(BossController.BossName, BossController.BossSubtitle);
            yield return new WaitForSecondsRealtime(1.3f);

            Services.Cam.ReleaseFocus(0.8f);
            Services.Cam.SetZoom(6.8f, 1f);
            if (player != null && player.IsAlive) player.SetControlEnabled(true);
            FightActive = true;
            Boss.BeginFight();
        }

        void OnPlayerRespawned()
        {
            if (Boss == null) return;
            StopAllCoroutines();
            Boss.ResetFight();
            FightActive = false;
            IntroPlayed = false;
            Services.Ui.HideBossBar();
            if (gate != null) gate.Open(true);
            Services.Audio.PlayMusic("music_title", 1.5f);
            Services.Cam.ReleaseFocus(0.1f);
            Services.Cam.SetZoom(6f, 0.5f);
        }

        void OnDefeated()
        {
            FightActive = false;
            Services.Ui.HideBossBar();
            Services.Audio.StopMusic(2f);
            Services.Audio.PlaySfx("victory_sting");
            if (gate != null) gate.Open(true);
        }
    }
}
