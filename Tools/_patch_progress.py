"""Wire progression into the player, enemies, checkpoints and the HUD."""
import os
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
A = os.path.join(ROOT, "AshenSol", "Assets", "_Game", "Scripts")

def rw(rel, fn):
    p = os.path.join(A, rel)
    with open(p, encoding="utf8", newline="") as f: s = f.read()
    out = fn(s)
    with open(p, "w", encoding="utf8", newline="") as f: f.write(out)

def sub(s, old, new):
    o, n = old.replace("\n", "\r\n"), new.replace("\n", "\r\n")
    if o in s: return s.replace(o, n, 1)
    if old in s: return s.replace(old, new, 1)
    raise SystemExit("MISS: %r" % old[:130])

# ------------------------------------------------------------------ contracts: upgrade panel
def contracts(s):
    return sub(s, """        void ShowSkipHint(bool visible);""",
"""        void ShowSkipHint(bool visible);
        /// <summary>Shrine upgrade menu. The callback fires once with the chosen upgrade.</summary>
        void ShowUpgradePanel(Action<UpgradeKind> onPick);
        bool UpgradePanelOpen { get; }""")
rw("Core/Contracts.cs", contracts)

# ------------------------------------------------------------------ enemies award ash
def enemybase(s):
    s = sub(s, "        public virtual string DisplayName { get { return Type.ToString(); } }",
"""        public virtual string DisplayName { get { return Type.ToString(); } }
        /// <summary>Ash granted for the kill.</summary>
        public virtual int AshValue { get { return 22; } }""")
    s = sub(s, """            var d = Died; if (d != null) d(this);
            GameEvents.RaiseEnemyKilled(this);""",
"""            Progression.AddAsh(AshValue);
            var d = Died; if (d != null) d(this);
            GameEvents.RaiseEnemyKilled(this);""")
    return s
rw("Enemies/EnemyBase.cs", enemybase)

for rel, old, new in [
    ("Enemies/SpearSentinel.cs", 'public override string DisplayName { get { return "Spear Sentinel"; } }',
     'public override string DisplayName { get { return "Spear Sentinel"; } }\n        public override int AshValue { get { return 34; } }'),
    ("Enemies/WatcherDrone.cs", 'public override string DisplayName { get { return "Watcher Drone"; } }',
     'public override string DisplayName { get { return "Watcher Drone"; } }\n        public override int AshValue { get { return 16; } }'),
    ("Boss/ArtisanController.cs", 'public override string DisplayName { get { return ArtisanName; } }',
     'public override string DisplayName { get { return ArtisanName; } }\n        public override int AshValue { get { return 190; } }'),
    ("Boss/BossController.cs", 'public override string DisplayName { get { return WardenName; } }',
     'public override string DisplayName { get { return WardenName; } }\n        public override int AshValue { get { return 260; } }'),
]:
    rw(rel, lambda s, o=old, n=new: sub(s, o, n))

# ------------------------------------------------------------------ player uses the upgrades
def player(s):
    s = sub(s, "        public int MaxHp { get { return PlayerTuning.MaxHp; } }",
               "        public int MaxHp { get { return PlayerTuning.MaxHp + Progression.BonusHp; } }")
    s = sub(s, "        public int MaxQi { get { return PlayerTuning.MaxQi; } }",
               "        public int MaxQi { get { return PlayerTuning.MaxQi + Progression.BonusQi; } }")
    return s
rw("Player/PlayerController.cs", player)

def combat(s):
    s = sub(s, """                var info = new AttackInfo
                {
                    Source = c.gameObject, Team = Team.Player, Damage = h.Damage, Origin = c.Center,""",
"""                var info = new AttackInfo
                {
                    Source = c.gameObject, Team = Team.Player, Damage = Mathf.RoundToInt(h.Damage * Progression.DamageMul), Origin = c.Center,""")
    s = sub(s, "Damage = PlayerTuning.QiBlastDamage, Origin = c.Center,",
               "Damage = Mathf.RoundToInt(PlayerTuning.QiBlastDamage * Progression.DamageMul), Origin = c.Center,")
    s = sub(s, "Damage = victim.ExecuteDamage, Origin = c.Center,",
               "Damage = Mathf.RoundToInt(victim.ExecuteDamage * Progression.DamageMul), Origin = c.Center,")
    return s
rw("Player/PlayerCombat.cs", combat)

# ------------------------------------------------------------------ shrines spend levels
def objects(s):
    s = sub(s, """        void OnTriggerEnter2D(Collider2D other)
        {
            if (other.gameObject.layer != Layers.Player) return;
            Activate(false);
        }""",
"""        void OnTriggerEnter2D(Collider2D other)
        {
            if (other.gameObject.layer != Layers.Player) return;
            Activate(false);
            OfferUpgrade();
        }

        void OnTriggerStay2D(Collider2D other)
        {
            if (other.gameObject.layer != Layers.Player) return;
            OfferUpgrade();
        }

        /// <summary>Resting at a shrine is where levels are spent.</summary>
        void OfferUpgrade()
        {
            if (!Progression.CanSpend || Services.Ui.UpgradePanelOpen) return;
            if (GameFlow.Instance != null && (GameFlow.Instance.Busy || GameFlow.Instance.IsPaused)) return;
            Services.Ui.ShowUpgradePanel(kind =>
            {
                if (!Progression.Spend(kind)) return;
                var p = PlayerController.Instance;
                if (p != null && kind == UpgradeKind.Vigor) p.Heal(Progression.VigorHp);
                Services.Audio.PlaySfx("qi_gain", 1f);
                Services.Vfx.Embers((Vector2)transform.position + new Vector2(0f, 1.2f), 26, Palette.Gold);
                Services.Vfx.FlashLight((Vector2)transform.position + new Vector2(0f, 1.2f), Palette.Gold, 3f, 5f, 0.5f);
                GameEvents.RaisePlayerHealthChanged(p != null ? p.Hp : 0, p != null ? p.MaxHp : 0);
                GameEvents.RaisePlayerQiChanged(p != null ? p.Qi : 0, p != null ? p.MaxQi : 0);
            });
        }""")
    return s
rw("Level/LevelObjects.cs", objects)

# ------------------------------------------------------------------ new run resets progression
def flow(s):
    s = sub(s, """            Stats = new GameStats();
            Services.Ui.HideTitle();""",
"""            Stats = new GameStats();
            Progression.Reset();
            Services.Ui.HideTitle();""")
    s = sub(s, """            if (Services.Input.PausePressed && !deathScreenShowing && !victoryPending) TogglePause();""",
"""            if (Services.Ui.UpgradePanelOpen) return;      // the shrine menu owns input while it is up
            if (Services.Input.PausePressed && !deathScreenShowing && !victoryPending) TogglePause();""")
    return s
rw("Core/GameFlow.cs", flow)

print("progression wired")
