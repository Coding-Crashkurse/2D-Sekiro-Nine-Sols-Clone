using System;
using UnityEngine;

namespace AshenSol.Core
{
    public enum UpgradeKind { Vigor = 0, Edge = 1, Focus = 2 }

    /// <summary>
    /// Souls-like progression. Killing things drops ASH, which is carried, not banked: die and you
    /// drop the lot where you fell. Reach the pile again to pick it back up — die on the way and it
    /// is gone for good. Ash is spent at shrines to buy levels, and each level tempers one stat.
    /// </summary>
    public static class Progression
    {
        public const int MaxFocus = 3;          // at most +3 Qi from upgrades
        public const int VigorHp = 20;
        public const float EdgeDamage = 0.12f;
        public const int PlayerBaseHp = 100;
        public const int PlayerBaseQi = 3;

        public static int Level { get; private set; } = 1;
        /// <summary>Ash currently carried. Lost on death.</summary>
        public static int Ash { get; private set; }
        public static int BonusHp { get; private set; }
        public static float DamageMul { get; private set; } = 1f;
        public static int BonusQi { get; private set; }

        /// <summary>Ash lying on the ground waiting to be recovered (0 = nothing dropped).</summary>
        public static int DroppedAsh { get; private set; }
        public static Vector2 DropPoint { get; private set; }
        public static LevelId DropLevel { get; private set; }

        public static event Action Changed;

        /// <summary>Ash needed to buy the next level.</summary>
        public static int LevelCost { get { return 60 + (Level - 1) * 55; } }
        public static bool CanAffordLevel { get { return Ash >= LevelCost; } }
        public static float Progress01 { get { return Mathf.Clamp01((float)Ash / Mathf.Max(1, LevelCost)); } }

        static void Raise() { var e = Changed; if (e != null) e(); }

        public static void Reset()
        {
            Level = 1; Ash = 0;
            BonusHp = 0; DamageMul = 1f; BonusQi = 0;
            DroppedAsh = 0; DropLevel = LevelId.None;
            Raise();
        }

        public static void AddAsh(int amount)
        {
            if (amount <= 0) return;
            Ash += amount;
            Raise();
        }

        /// <summary>Death: everything carried falls where you did, replacing any older pile.</summary>
        public static void DropOnDeath(Vector2 where, LevelId level)
        {
            DroppedAsh = Ash;
            DropPoint = where;
            DropLevel = level;
            Ash = 0;
            if (DroppedAsh > 0) GameEvents.RaiseLog("dropped " + DroppedAsh + " ash at " + where);
            Raise();
        }

        public static int Recover()
        {
            int a = DroppedAsh;
            DroppedAsh = 0;
            DropLevel = LevelId.None;
            Ash += a;
            if (a > 0) GameEvents.RaiseLog("recovered " + a + " ash");
            Raise();
            return a;
        }

        public static bool Buy(UpgradeKind kind)
        {
            if (!CanAffordLevel) return false;
            if (kind == UpgradeKind.Focus && BonusQi >= MaxFocus) return false;
            Ash -= LevelCost;
            Level++;
            switch (kind)
            {
                case UpgradeKind.Vigor: BonusHp += VigorHp; break;
                case UpgradeKind.Edge: DamageMul += EdgeDamage; break;
                case UpgradeKind.Focus: BonusQi++; break;
            }
            GameEvents.RaiseLog("level " + Level + " via " + kind + " (hp+" + BonusHp + " dmg x" + DamageMul.ToString("F2") + " qi+" + BonusQi + ")");
            Raise();
            return true;
        }

        public static string Title(UpgradeKind kind)
        {
            switch (kind)
            {
                case UpgradeKind.Vigor: return "VIGOR";
                case UpgradeKind.Edge: return "EDGE";
                default: return "FOCUS";
            }
        }

        public static string Detail(UpgradeKind kind)
        {
            switch (kind)
            {
                case UpgradeKind.Vigor: return "+" + VigorHp + " max health\n" + (PlayerBaseHp + BonusHp) + "  →  " + (PlayerBaseHp + BonusHp + VigorHp);
                case UpgradeKind.Edge: return "+" + Mathf.RoundToInt(EdgeDamage * 100f) + "% sword damage\n"
                        + Mathf.RoundToInt(DamageMul * 100f) + "%  →  " + Mathf.RoundToInt((DamageMul + EdgeDamage) * 100f) + "%";
                default:
                    return BonusQi >= MaxFocus
                        ? "Qi capacity is maxed"
                        : "+1 max Qi\n" + (PlayerBaseQi + BonusQi) + "  →  " + (PlayerBaseQi + BonusQi + 1);
            }
        }

        public static bool Available(UpgradeKind kind)
        {
            return kind != UpgradeKind.Focus || BonusQi < MaxFocus;
        }
    }
}
