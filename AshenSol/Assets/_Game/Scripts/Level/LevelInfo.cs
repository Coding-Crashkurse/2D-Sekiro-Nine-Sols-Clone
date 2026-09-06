using System.Collections.Generic;
using UnityEngine;
using AshenSol.Core;
using AshenSol.Enemies;

namespace AshenSol.Level
{
    /// <summary>Everything GameFlow needs to know about a built level.</summary>
    public class LevelInfo
    {
        public LevelId Id;
        public string Title = "";
        public string Subtitle = "";
        public Rect Bounds;
        public Vector2 PlayerSpawn;
        public string MusicTrack = "music_level1";
        public string Ambience;
        public Color AmbientColor = Color.white;
        public float AmbientIntensity = 0.6f;
        public List<Checkpoint> Checkpoints = new List<Checkpoint>();
        public Gate ExitGate;
        public List<EnemyBase> Enemies = new List<EnemyBase>();
        public List<EncounterZone> Zones = new List<EncounterZone>();
        public AshenSol.Boss.BossArenaDirector Arena;

        /// <summary>Respawn every enemy and re-arm zones that were not cleared (the exit gate stays open once opened).</summary>
        public void ResetEnemies()
        {
            Projectile.ClearAll();
            AshenSol.Boss.GroundShockwave.ClearAll();
            for (int i = 0; i < Enemies.Count; i++)
                if (Enemies[i] != null) Enemies[i].ResetToSpawn();
            for (int i = 0; i < Zones.Count; i++)
                if (Zones[i] != null && !Zones[i].Cleared) Zones[i].Rearm();
        }
    }
}
