using System;
using UnityEngine;

namespace AshenSol.Boss
{
    /// <summary>What the arena director needs from a boss, so the same intro/reset/victory plumbing
    /// works for every one of them.</summary>
    public interface IBossFight
    {
        string BossName { get; }
        string BossSubtitle { get; }
        Vector2 Center { get; }
        bool IsAlive { get; }
        /// <summary>Music track for this fight.</summary>
        string FightMusic { get; }
        /// <summary>Stand up / power on during the intro.</summary>
        void Rise();
        void BeginFight();
        void ResetFight();
        event Action Defeated;
    }

    public enum BossKind { Warden = 0, Artisan = 1 }
}
