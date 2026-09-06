# ASHEN SOL — Build Specification

A small, pretty, parry-heavy 2D action game in the spirit of *Nine Sols* (Taopunk: Taoist temple ruins + cyberpunk tech; ink-black silhouettes; teal / red / gold accents). Two levels: **Level 1 "Ruined Shrine Corridor"** (platforming + enemy encounters ending at a sealed gate) and **Level 2 "The Sealed Sanctum"** (boss arena with **THE FORSAKEN WARDEN**).

This document is the single source of truth for every implementation agent. `Assets/_Game/Scripts/Core/Contracts.cs` is the cross-module API and MUST NOT be edited by module agents.

---

## 0. Ground rules for every agent

| Item | Value |
|---|---|
| Unity project | `C:\Users\User\Desktop\nine_sols\AshenSol` |
| Unity version | 6000.4.6f1 (Editor: `C:\Program Files\Unity\Hub\Editor\6000.4.6f1\Editor\Unity.exe`) |
| Render pipeline | URP 17.4 with the **2D Renderer** (`Assets/Settings/Renderer2D.asset`, `Assets/Settings/UniversalRP.asset`) — `Light2D`, `Volume` post-processing available |
| Input | **Input System 1.19 only** (`activeInputHandler = 1`). `UnityEngine.Input.*` legacy calls THROW. Use `Keyboard.current`, `Gamepad.current`, `Mouse.current`. |
| UI | uGUI (`com.unity.ugui` 2.0). Use legacy `UnityEngine.UI.Text` (NOT TextMeshPro — TMP essential resources are not imported). Built-in font: `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")`. If `Resources/Fonts/Cinzel-Regular` exists, prefer it. |
| Scripting | C# for Unity 6 (.NET Standard 2.1). No `record`, no `init`, no `async` gameplay. Coroutines/Update only. |
| Assets | Everything is built at runtime from code. **No prefabs, no hand-edited scenes.** Sprites via `Res.Sprite("name")` (`Assets/_Game/Resources/Sprites/*.png`, 100 PPU). Audio via `Services.Audio.PlaySfx("name")`. |
| Never | Launch `Unity.exe` (only the integration stage may). Edit files you don't own. Edit `Contracts.cs`. Add packages. |
| Missing assets | Must never throw — `Res.Sprite` returns a white sprite, `Res.Clip` returns null (AudioManager ignores). |
| Units | 100 px = 1 unit. Player ≈ 1.7 u tall. Camera orthographic size 6 (12 u tall, ≈21.3 u wide at 16:9). Gravity −30. |
| Time | Gameplay uses `Time.deltaTime` (affected by hit-stop / pause). UI, fades, camera shake use `Time.unscaledDeltaTime`. |
| Layers | Numeric constants in `Layers` (Ground 6, Player 7, Enemy 8, Hazard 9, Projectile 10, Trigger 11, OneWay 12). Editor bootstrap names them; code never uses `LayerMask.NameToLayer`. |
| Sorting | One sorting layer ("Default"); use `SortOrder` constants. |
| Events | Subscribe to `GameEvents` in `OnEnable`/`Awake`, ALWAYS unsubscribe in `OnDestroy`. |
| Namespaces | `AshenSol.Core`, `.Player`, `.Enemies`, `.Boss`, `.Level`, `.UI`, `.VFX`, `.Audio`, `AshenSol.EditorTools` (Editor folder). All types `public`. |
| Style | Tunables as `const`/`static readonly` at the top of the class or in a per-module `static class XxxTuning`. Small methods. Comments on non-obvious gameplay rules. |
| Logging | `GameEvents.RaiseLog("...")` for gameplay events (parry, kill, checkpoint, gate, boss phase, death). `Debug.LogWarning` only for real problems. |

**Report** (each agent's structured output): files written, public API summary, anything you assumed, `apiNotes` (cross-module needs you could not satisfy from Contracts/SPEC), and known gaps.

---

## 1. Game overview

* **Pillars:** parry is the core verb (white flash = parryable → perfect parry; red flash = unblockable → dash/jump); every parry rewards with *internal damage* (red on enemy bar) + Qi; Qi buys a **Qi Blast** (detonates internal damage) or a **heal**.
* **Feel:** hit-stop, camera shake/kick, slash arcs, sparks, afterimages, squash & stretch, bloom, 2D lights, parallax, mist and embers.
* **Flow:** Title → Level 1 → sealed gate → Boss arena → intro → fight (2 phases) → victory screen → Title.

### Controls (keyboard / gamepad)

| Action | Keyboard | Gamepad |
|---|---|---|
| Move | A/D, ←/→ | Left stick / D-pad |
| Jump | Space / W / ↑ | South (A) |
| Attack | J / Mouse left | West (X) |
| Parry | K / Mouse right | East (B) |
| Dash | L / Left Shift | Right shoulder (RB) / Right trigger |
| Qi Blast | I | North (Y) |
| Heal | H | Left shoulder (LB) |
| Pause | Esc | Start |
| Confirm | Enter / Space | South (A) / Start |

---

## 2. Runtime architecture

One scene: `Assets/_Game/Scenes/Main.unity` (created by the editor bootstrap) containing a single GameObject `GameBootstrap` with the `AshenSol.Core.GameBootstrap` component. Everything else is created in code.

`GameBootstrap.Awake()` creates, **in this order**, as components on one persistent root GameObject `"Game"` (`DontDestroyOnLoad`):

1. `AshenSol.Core.GameManager` (physics setup: `Physics2D.gravity = (0,-30)`, `Physics2D.IgnoreLayerCollision` Player↔Enemy, Enemy↔Enemy, Projectile↔Projectile, Player↔Projectile (projectiles use triggers), Enemy↔Projectile (trigger), Projectile↔OneWay; `Application.targetFrameRate = 144`; `QualitySettings.vSyncCount = 1`)
2. `AshenSol.Core.TimeController`
3. `AshenSol.Core.InputRouter` (registers `Services.Input`)
4. `AshenSol.Audio.AudioManager` (registers `Services.Audio`)
5. `AshenSol.Core.CameraController` (registers `Services.Cam`; creates the Camera, Global Light2D, post-process Volume)
6. `AshenSol.VFX.VfxManager` (registers `Services.Vfx`)
7. `AshenSol.UI.UiManager` (registers `Services.Ui`)
8. `AshenSol.Core.GameFlow`
9. `AshenSol.Core.AutoPilot` **only if** `CmdArgs.Has("-autopilot")`

### GameFlow (Core) — the state machine

```
Boot → Title ──StartGame──▶ Level1 ──Gate entered──▶ BossArena ──BossDefeated──▶ Victory ──Confirm──▶ Title
                                 ▲ death/respawn ↺            ▲ death/respawn ↺
```

* `Title`: `Ui.ShowTitle(StartGame)`, `Audio.PlayMusic("music_title")`, ambient embers on a dark backdrop (VFX). `-skipTitle` or `-autopilot` skips straight to Level1 (`-startLevel boss` → BossArena).
* `LoadLevel(LevelId)`: `Ui.Fade(1, 0.5)` → destroy old `LevelRoot` → `Projectile.ClearAll()` → new `LevelRoot` → `LevelBuilder.BuildLevel1/BuildBossArena(LevelRoot)` → `LevelInfo` → create/`Respawn` player at `info.PlayerSpawn` → `Cam.SetTarget(player)`, `SetBounds(info.Bounds)`, `SnapToTarget()` → global light from `info.AmbientColor/AmbientIntensity` → `Audio.PlayMusic(info.MusicTrack)`, `PlayAmbience(info.Ambience)` → `GameEvents.RaiseLevelBuilt` → `Ui.Fade(0, 0.7)` → `Ui.ShowNameCard(info.Title, info.Subtitle, 3)`.
* Respawn point = `info.PlayerSpawn` until a `Checkpoint` activates (`GameEvents.CheckpointActivated` + `GameFlow.Instance.SetRespawnPoint(Vector2)`).
* On `GameEvents.PlayerDied`: `Stats.Deaths++`, `TimeController.SlowMo(0.35, 1.2)`, `Audio.SetMusicDuck(0.3, 1)`, wait 1.4 s (unscaled), `Ui.ShowDeathScreen(Respawn)`. `Respawn()`: fade to black → `info.ResetEnemies()` → `player.Respawn(respawnPoint)` → `Cam.SnapToTarget()` → `GameEvents.RaisePlayerRespawned()` → `Audio.SetMusicDuck(1, 1)` → fade in.
* `Gate.PlayerEntered` (from `info.ExitGate`) → `Audio.PlaySfx("level_start")` → `LoadLevel(BossArena)`.
* `GameEvents.BossDefeated` → `Stats.BossDefeated = true` → `TimeController.SlowMo(0.25, 1.5)` → wait 3 s → `Ui.ShowVictoryScreen(Stats, GoToTitle)`.
* Pause: `Services.Input.PausePressed` while in a level → `TimeController.SetPaused(!paused)` + `Ui.SetPaused(paused)`.
* `Stats` (`GameStats`) is filled from events: parries, perfect parries, deaths, kills, damage taken (via `PlayerAttacked` with outcome Hit/Blocked), damage dealt (`EnemyDamaged`), play time (unscaled, only while in a level).

Public API (Core):
```csharp
public class GameFlow : MonoBehaviour {
  public static GameFlow Instance;
  public GameState State { get; }
  public LevelId CurrentLevel { get; }
  public Transform LevelRoot { get; }
  public LevelInfo CurrentLevelInfo { get; }
  public GameStats Stats { get; }
  public Vector2 RespawnPoint { get; }
  public bool IsPaused { get; }
  public void StartGame();            // Title → Level1
  public void GoToTitle();
  public void LoadLevel(LevelId id);
  public void SetRespawnPoint(Vector2 p);
  public void Respawn();
}
public class TimeController : MonoBehaviour {
  public static TimeController Instance;
  public void SlowMo(float scale, float seconds);   // eased back to 1
  public void SetPaused(bool paused);               // timeScale 0 / restore
  public bool IsPaused { get; }
  // applies HitStop.Remaining every frame: while > 0, timeScale = 0.02 and Remaining -= unscaledDeltaTime
}
```

---

## 3. Player (`AshenSol.Player`, owner: **player** agent)

Files: `PlayerController.cs`, `PlayerCombat.cs`, `PlayerRig.cs`, `PlayerTuning.cs`, `SashChain.cs`.

```csharp
public class PlayerController : MonoBehaviour, IDamageable {
  public static PlayerController Instance { get; }
  public static PlayerController Create(Vector2 spawnPos, Transform parent);   // builds the whole object (rigidbody, collider, rig, light)
  public int Hp { get; } public int MaxHp { get; }    // 100
  public int Qi { get; } public int MaxQi { get; }    // 3
  public int Facing { get; }                          // +1 / -1
  public bool IsGrounded, IsDashing, IsInvulnerable, IsParryWindowOpen, IsBlocking, IsAttacking, IsDead, IsHealing, IsStunned, ControlEnabled { get; }
  public Vector2 Position { get; } public Vector2 Center { get; } public Vector2 Velocity { get; }
  public Rigidbody2D Body { get; } public SpriteRenderer[] Renderers { get; }
  public Vector2 LastSafeGroundPosition { get; }
  public void SetControlEnabled(bool enabled);        // false: idle animation, no input, physics still on
  public void Respawn(Vector2 pos);                   // full HP, Qi 0, clear states, face right, teleport
  public void TeleportSafe();                          // to LastSafeGroundPosition (used by hazards)
  public void ApplyHazard(int damage);                 // spikes/kill zone: damage (respecting i-frames), red flash, then TeleportSafe after 0.35 s
  public void Heal(int amount); public void AddQi(int n); public bool SpendQi(int n);
  public HitOutcome ReceiveAttack(in AttackInfo info);
  public event Action Died;
}
```

### Movement (PlayerTuning)
* Rigidbody2D dynamic, `interpolation = Interpolate`, `collisionDetectionMode = Continuous`, freeze rotation, `CapsuleCollider2D` size (0.6, 1.5), vertical, offset (0, 0.75) — pivot at feet. `PhysicsMaterial2D` friction 0 bounce 0. Layer `Layers.Player`.
* Max run speed 8.5; ground accel 60, air accel 35, decel 70. Facing from input except during attack/parry/heal.
* Ground check: `Physics2D.OverlapBox` at feet (0.5 × 0.12) on `Layers.GroundMask`. Also record `LastSafeGroundPosition` every 0.2 s while grounded and vertical speed ≈ 0 and standing on a `Layers.Ground` collider that is at least 1 u wide under the player (spike-free ground is guaranteed by level design; hazards use triggers).
* Jump: velocity 13.5; releasing jump while rising → `vy *= 0.45`; coyote time 0.1; jump buffer 0.12; apex hang: gravityScale 0.75 while |vy| < 2.5 in air (else 1). Max fall speed 22. One-way platforms: pressing down + jump drops through (temporarily ignore OneWay for 0.3 s).
* Dash: speed 26 for 0.16 s (no gravity, vy = 0), i-frames for the dash + 0.05 s, cooldown 0.45 s, one air dash per airtime (refilled on ground). Afterimages every 0.03 s (`Vfx.Afterimage`), `Vfx.DustPuff`, sfx `dash`, camera kick.
* Landing: squash (scale 1.15, 0.85 → 1 over 0.15 s), `DustPuff`, sfx `land` (if fall speed > 6). Jump: stretch (0.9, 1.15), sfx `jump`. Footsteps: sfx `footstep` every 0.28 s while running on ground (volume 0.5, pitch var 0.15).

### Combat (PlayerCombat)
* **Attack** (J): 3-hit combo. Timings (startup / active / recovery): hit1 0.07/0.08/0.16, hit2 0.07/0.08/0.16, hit3 0.10/0.10/0.26. Damage 10 / 10 / 18, knockback 3 / 3 / 6. Hitbox = `OverlapBox` centre `Center + Facing * (1.0, 0.1)` size (1.9, 1.5) on `Layers.EnemyMask`, resolved once per hit per target via `GetComponentInParent<IDamageable>()` → `ReceiveAttack` with `Team.Player`, `Tag = "player_combo{n}"`. Ground attacks lunge forward 1.6 u/s during startup+active. Next combo input accepted from the active frame until 0.35 s after recovery starts. Air attacks allowed (repeat hit1 pattern). Parry cancels attack at any time; dash cancels after the active frames. Each hit: `Vfx.SlashArc` (angle −20°, 20°, overhead 260° spin for hit3, colour `Palette.PlayerSlash`), sfx `sword_swing_1/2/3`. On connecting: `HitStop.Request(0.045)`, `Cam.Kick(dir, 0.15)`, sfx `sword_hit`.
* **Parry** (K): stance total 0.40 s: **perfect window 0.18 s**, then **block window 0.22 s**, then recovery 0.12 s (no new parry). Can be pressed during any state except dash/hurt/heal/dead. Player can't move during stance (retains momentum in air). Visual: sword raised vertical, `fx_parry_glyph` in front (alpha 0.55, teal), sfx `parry_ready` (soft, optional).
* **Resolve `ReceiveAttack`** (this is THE core rule):
  1. `IsDead` → `Ignored`.
  2. `IsDashing || IsInvulnerable` → `Dodged` (no feedback except a faint afterimage).
  3. `info.Kind == Parryable`:
     * `IsParryWindowOpen` → **Parried**: no damage; `AddQi(1)`; `Stats` via `GameEvents.RaisePlayerParried(pos, true)`; `HitStop.Request(0.09)`; `Cam.Shake(0.35)`; `Vfx.ParrySpark(hitPoint, true)`; `Vfx.FlashLight`; sfx `parry_perfect`; if `info.IsProjectile && info.Payload is IReflectable r` → `r.Reflect((info.Origin - Center).normalized * -1 ... )` i.e. back toward the attacker: direction from player to `info.Source` position, `Team.Player`, damage 30. Parry stance ends immediately with a 0.1 s "success" pose; a new parry may be pressed right away (enables rhythm parries vs. multi-hit).
     * `IsBlocking` → **Blocked**: chip damage `ceil(dmg * 0.3)`, knockback 4, `HitStop.Request(0.04)`, `Vfx.ParrySpark(hitPoint, false)`, sfx `parry_block`, `RaisePlayerParried(pos, false)`.
     * else → **Hit**.
  4. `info.Kind == Unblockable`: parry/block do nothing → **Hit** with sfx `parry_fail` + `Vfx.ScreenFlash(red, 0.12)` if the player was in stance (teaches the rule); otherwise regular Hit.
  5. **Hit**: `Hp -= dmg`, `GameEvents.RaisePlayerHealthChanged`, i-frames 0.8 s (renderer flicker 12 Hz), stun 0.2 s, knockback impulse `info.Knockback` (min 5) away from `info.Origin` with slight upward component, `HitStop.Request(0.06)`, `Cam.Shake(0.5)`, `Vfx.ChromaticPulse(0.6, 0.25)`, `Vfx.InkSplatter(red)`, sfx `player_hurt`, cancels heal/attack. `Hp <= 0` → **Die**: `IsDead`, control off, collider off, `Vfx.Dissolve(Renderers, Center, Palette.Red)`, sfx `player_death`, `Died` + `GameEvents.RaisePlayerDied()`.
  Always `GameEvents.RaisePlayerAttacked(info, outcome)` before returning.
* **Qi Blast** (I): needs `Qi ≥ 1` (else sfx `ui_move` + tiny flash). Costs 1. 0.35 s animation (both arms thrust). `OverlapCircle` centre `Center + Facing*(1.2,0)` radius 3.2 on `Layers.EnemyMask` → each `IDamageable` gets `ReceiveAttack` with `Tag = "qi_blast"`, `Damage = 12`, `Knockback 6` — enemies treat tag `qi_blast` as *detonate internal damage first, then apply Damage* (see §4). `Vfx.Shockwave(teal, 3.2)`, `Cam.Shake(0.5)`, `HitStop.Request(0.05)`, sfx `qi_blast`.
* **Heal** (H): needs `Qi ≥ 1` and `Hp < MaxHp`. Channel 0.55 s (kneel, teal glow light rising, sfx `heal`), then `Heal(35)`, `SpendQi(1)`. Interrupted by damage (Qi refunded).
* Qi also comes from kills (`GameEvents.EnemyKilled` → `AddQi(1)`), the player subscribes itself.

### Rig (PlayerRig) — procedural puppet
Parts (child GameObjects, each a joint pivot with a child `SpriteRenderer` offset so the joint sits at the pivot): `leg_back`, `leg_front` (hip pivots), `torso`, `head`, `arm_back`, `arm_front` (shoulder pivots), `sword` (child of arm_front hand), `sash` (SashChain at upper back). Sprites: `player_leg_back`, `player_leg_front`, `player_torso`, `player_head`, `player_arm_back`, `player_arm_front`, `player_sword`, `player_sash_seg`. Sort orders: leg_back 40, arm_back 42, torso 50, head 51, leg_front 52, arm_front 55, sword 56. Facing flips the rig root `localScale.x`.
A `Light2D` (point, `Palette.Teal`, intensity 0.7, outer radius 1.6) on the sword; intensity spikes to 3 for 0.15 s on perfect parry.
States & motion: Idle (bob 0.03 u @ 1.2 Hz, sword sway ±4°), Run (legs ±38° @ 9 Hz × speed factor, arms counter-swing, torso lean 8°, bob 0.05 u), Jump (legs tucked, arms raised), Fall (legs apart, arms out), Dash (lean 28°, scale (1.25, 0.8)), Attack (arm_front sweeps −110° → +70° over startup+active with `Ease.OutCubic`; hit3 = full overhead spin 360°), Parry (arm_front at 95°, sword vertical in front, slight crouch), Hurt (recoil, red tint 0.12 s), Heal (kneel), QiBlast (arms thrust forward). Blend by lerping joint angles toward state targets at 18/s; run cycle is time based.
`SashChain`: 6 segments (verlet: gravity 6, damping 0.92, follow anchor, wind sin), red sprites scaled 0.9→0.5, sort 39; adds swing based on velocity.

---

## 4. Enemies (`AshenSol.Enemies`, owner: **enemies** agent)

Files: `EnemyBase.cs`, `EnemyRig.cs`, `Grunt.cs`, `SpearSentinel.cs`, `HammerBrute.cs`, `WatcherDrone.cs`, `Projectile.cs`, `EnemyHealthBar.cs`, `EnemyTuning.cs`.

```csharp
public abstract class EnemyBase : MonoBehaviour, IDamageable {
  public static readonly List<EnemyBase> All;                // living or dead, all instances (boss included)
  public static EnemyBase Create(EnemyType type, Vector2 pos, Transform parent, string zoneId = null, int facing = -1);
  public EnemyType Type { get; } public string ZoneId { get; set; }
  public int Hp, MaxHp; public int InternalDamage { get; }
  public bool IsAlive { get; } public bool IsStaggered { get; }
  public bool IsTelegraphing { get; } public AttackKind TelegraphKind { get; } public float TimeUntilStrike { get; } // -1 when not telegraphing
  public Vector2 SpawnPos { get; } public int SpawnFacing { get; } public int Facing { get; }
  public Vector2 Center { get; } public Team Team => Team.Enemy; public Transform Transform => transform;
  public SpriteRenderer[] Renderers { get; }
  public float DistanceToPlayer { get; }
  public virtual void ResetToSpawn();           // revive, full HP, clear internal, reposition, idle
  public void AddInternalDamage(int amount);    // clamped so InternalDamage <= Hp
  public int DetonateInternal();                // converts all internal to real damage, returns amount
  public virtual HitOutcome ReceiveAttack(in AttackInfo info);
  public event Action<EnemyBase> Died;
  public string DisplayName { get; }
  // helpers for subclasses:
  protected HitOutcome StrikePlayer(AttackInfo info, Vector2 boxCenter, Vector2 boxSize);  // overlap Player layer → PlayerController.Instance.ReceiveAttack → OnAttackResolved
  protected virtual void OnAttackResolved(HitOutcome outcome, in AttackInfo info);          // default: Parried → AddInternalDamage(info.Damage) + Stagger(staggerSeconds)
  protected void BeginTelegraph(AttackKind kind, float seconds);  // sets IsTelegraphing, TimeUntilStrike, rig flash, sfx enemy_telegraph / enemy_telegraph_red
  protected void EndTelegraph();
  protected virtual void Stagger(float seconds);
  protected virtual void Die();
}
public class Projectile : MonoBehaviour, IReflectable {
  public static Projectile Fire(Vector2 pos, Vector2 velocity, Team team, int damage, AttackKind kind, Color color, string tag, float lifetime = 4f, float radius = 0.18f);
  public static void ClearAll();
  public Team Team { get; } public Vector2 Velocity { get; }
  public void Reflect(Vector2 newDirection, Team newTeam, int newDamage);   // speed *= 1.5, colour → Palette.Teal, sfx projectile_reflect, trail re-coloured
}
```

**ReceiveAttack (enemy)**: dead → `Ignored`. Otherwise **Hit**: if `info.Tag == "qi_blast"` → `dmg = DetonateInternal() + info.Damage` (floating text red, bigger); else `dmg = info.Damage + max(0, InternalDamage/4)` and `InternalDamage -= InternalDamage/4` (normal hits bleed 25 % of internal). Apply, `GameEvents.RaiseEnemyDamaged`, white flash 0.08 s, knockback (dynamic bodies only), `Vfx.InkSplatter(hitPoint, dir, Palette.Ink)`, `Vfx.HitSpark`, `Vfx.FloatingText(dmg)`, `HitStop.Request(0.03)`, sfx `enemy_hit`, health bar shows. Hp ≤ 0 → `Die()`: `IsAlive=false`, colliders off, `Vfx.Dissolve(Renderers, Center, Palette.Red)`, `Vfx.Embers`, sfx `enemy_death` (`drone_death` for drones), `Died`, `GameEvents.RaiseEnemyKilled(this)`, `GameEvents.RaiseLog`. Stagger interrupts any attack.

**Telegraph** (`EnemyRig`): all part sprites tint-lerp to white (`Palette.TelegraphWhite`) or red (`Palette.TelegraphRed`) with two pulses across the telegraph time, a `Light2D` flash (white/red) and a small `fx_flare` sprite at the weapon; `TimeUntilStrike` counts down to the active frame. AutoPilot and tests read these.

**Health bar** (`EnemyHealthBar`): world-space, 1.1 × 0.09 u, 0.35 u above the head; dark back, bone fill for HP, `Palette.InternalDamage` overlay for internal damage (drawn from the right edge of the current HP); appears on first damage, hides 3 s after the last change. Uses `Res.White`.

### Types
| | Grunt (Husk) | Spear Sentinel | Watcher Drone |
|---|---|---|---|
| HP | 45 | 70 | 30 |
| Move | 3.2 u/s, patrol ±3 u at spawn, aggro ≤ 7 u (x) & 3 u (y), approach to 1.5 u, ledge/wall checks | 2.6 u/s, keeps 3.0 u (backs off < 2 u) | Kinematic hover (bob 0.3 u @ 0.7 Hz), follows player x within 6 u leash, stays at spawn height |
| Attack | Slash: telegraph 0.45 (white) → active 0.10, box (1.8,1.4) in front, dmg 14, kb 5 → recovery 0.6; 30 % chance of a 2nd slash (telegraph 0.30) | Thrust: telegraph 0.5 (white) → active 0.12, box (3.0,1.0) in front, dmg 18 → recovery 0.7. **Every 3rd attack: RED lunge** telegraph 0.65 (red) → dash 5 u @ 14 u/s with active box (1.6,1.4) for 0.3 s, dmg 26, kb 8 → recovery 1.0 | Shot: every 2.2 s when player ≤ 9 u: telegraph 0.4 (white, eye glows) → `Projectile.Fire` toward player centre, speed 9, dmg 10, Parryable, colour `Palette.Amber`, tag `drone_bolt` |
| Parry reaction | stagger 0.55 s, pushed 1 u back | stagger 0.5 s | — (bolts reflect: 30 dmg to a drone kills it) |
| Rig sprites | `grunt_leg`×2, `grunt_torso`, `grunt_head`, `grunt_arm`, `grunt_blade` | `spear_leg`×2, `spear_torso`, `spear_head`, `spear_arm`, `spear_spear` | `drone_body`, `drone_eye`, `drone_ring` (ring rotates) |
| Sounds | `grunt_swing` | `spear_thrust`, `spear_lunge` | `drone_shot`, `drone_hover` (looped ambience while alive, quiet) |

**Foundry Brute** (`HammerBrute.cs`, `EnemyType.HammerBrute`, Level III onward): HP 150, posture 100 but **a perfect parry adds only 50 — two parries break it**; hits, heavies and Qi blasts add only 40 % of their usual posture (`PostureFromHitsMul`), so the guard is parried open, not chipped (regen 22/s, execute 95, ash 55, knockback resist 0.8). Walks at 1.9 u/s, aggro ≤ 8 u (x) & 3 u (y), attacks inside 2.4 u, cycling Smash → Sweep → Quake. **Smash**: telegraph 0.95 (white) → hammer over the top, active 0.16, box (2.4,1.9) low in front, dmg 30, kb 9, `PierceGuard` (a late block still eats 60 %), then the hammer stays buried for 1.1 s. **Sweep**: telegraph 0.8 (white) → step 3.5 u/s, active 0.2, box (3.2,1.6), dmg 24, kb 8, recovery 0.9. **Quake**: telegraph 1.05 (red) → slam at the feet, box (3.0,2.2) dmg 32 kb 10 unblockable, plus a `GroundShockwave` each way (speed 9, dmg 16, life 1.0), then kneels 1.4 s. A parry that does not break it punches the rig, kicks the camera and floats "ONE MORE" once the bar is half full. Rig sprites `brute_leg`×2, `brute_torso`, `brute_head`, `brute_arm`, `brute_hammer` (hammer rests upright: `ArmIdle` 30, `WeaponIdleLocal` −30; the telegraph windup hauls it behind the shoulder). Sounds reuse `boss_swing`, `boss_slam`, `boss_shockwave`, `land` (footfalls).

Ground enemies: Rigidbody2D dynamic (freeze rotation, mass 2), `CapsuleCollider2D`, layer `Layers.Enemy`; movement via velocity; ledge check (ray 0.6 u ahead, 1.2 u down) & wall check; facing flips rig scale. Drone: Rigidbody2D kinematic + `CircleCollider2D`, layer Enemy.
Projectile: `CircleCollider2D` trigger radius 0.18, layer `Layers.Projectile`, kinematic body moved by velocity in `FixedUpdate`; on trigger with Player layer (team Enemy) → `PlayerController.Instance.ReceiveAttack` (`IsProjectile=true`, `Payload=this`, `Origin=position`); outcome Parried → the projectile was reflected by the player (do not destroy); Dodged → pass through; Hit/Blocked → `Vfx.HitSpark` + destroy. Team Player projectile hits `Layers.EnemyMask` → `ReceiveAttack` (Team.Player) → destroy. Hits Ground → spark + destroy. Visual: `fx_bolt` sprite (additive, rotated to velocity) + `Vfx.ProjectileTrail` + `Light2D`.
`EnemyBase.Create(EnemyType.Boss, ...)` throws `NotSupportedException` (use `BossController.Create`).

---

## 5. Boss (`AshenSol.Boss`, owner: **boss** agent)

Files: `BossController.cs`, `BossRig.cs`, `BossAttacks.cs` (coroutines per attack), `BossArenaDirector.cs`, `GroundShockwave.cs`, `BossTuning.cs`.

```csharp
public class BossController : EnemyBase {              // Type == EnemyType.Boss, ZoneId = "boss"
  public static BossController Create(Vector2 pos, Transform parent);
  public const string BossName = "THE FORSAKEN WARDEN"; public const string BossSubtitle = "Keeper of the Sealed Gate";
  public int Phase { get; }                              // 1 or 2
  public bool IsFightActive { get; }
  public string CurrentAttack { get; }                   // "TripleSlash", "DashSlash", "Slam", "Bolts", "Whirl", "RedThrust", "" 
  public void BeginFight(); public void ResetFight();    // ResetFight = ResetToSpawn + idle + hide bar
  public event Action Defeated;
}
public class BossArenaDirector : MonoBehaviour {
  public static BossArenaDirector Create(Rect arenaBounds, Vector2 bossSpawn, AshenSol.Level.Gate entranceGate, Transform parent);
  public BossController Boss { get; }
  public bool IntroPlayed { get; } public bool FightActive { get; }
  public void StartIntro();                              // called automatically when the player crosses arenaBounds.xMin + 1
}
```

* **Stats:** HP 650, height ≈ 3.2 u, CapsuleCollider (1.4, 3.0), dynamic body mass 20 (never knocked back), speed 3.5. Phase 2 at HP ≤ 55 %: 2 s invulnerable transition (roar, `Vfx.Shockwave(red, 6)`, `Cam.Shake(0.8)`, sfx `boss_phase2`, `GameEvents.RaiseBossPhaseChanged(2)`, telegraph durations × 0.8).
* **Attack selection:** weighted random, never the same attack twice in a row; gating by distance d to player: TripleSlash (d ≤ 3.6, w 3), DashSlash (3 < d ≤ 12, w 3), Slam (any, w 2), Bolts (d > 5 → w 3, else w 1), P2: Whirl (d ≤ 4.5, w 3), RedThrust (d > 3, w 2). Between attacks: walk toward player 0.4–0.9 s or hold 0.35 s.
* **Attacks** (telegraph → active → recovery; boxes are in front of the boss centre):
  * **TripleSlash**: 3 × [telegraph 0.38 white → active 0.10 box (3.0, 2.4) dmg 16 kb 6] with 0.25 s gaps. Perfect-parrying all three → **Stagger 1.4 s** (kneel, 1.5× damage taken, sfx `boss_stagger`, floating text "STAGGERED"). Each parried hit adds internal damage as usual.
  * **DashSlash**: telegraph 0.5 white (crouch) → dash 12 u/s toward player until ≤ 1.6 u or 0.6 s → slash active 0.12 box (3.2, 2.4) dmg 20 kb 8 → recovery 0.7. sfx `boss_dash`, `boss_swing`.
  * **Slam** (RED, Unblockable): telegraph 0.7 red while leaping +4.5 u over 0.35 s and hanging → drops onto the player's x (tracked until 0.2 s before impact) at 40 u/s → impact: box (3.6, 3.0) dmg 30 kb 10 unblockable, `Vfx.Shockwave(pos, 4, red)`, `Cam.Shake(0.9)`, sfx `boss_slam`, and two `GroundShockwave`s travelling ±x at 9 u/s (height 0.8 u, Unblockable dmg 15, life 1.6 s, sfx `boss_shockwave`, visual: `fx_shockwave` + dust + red light) → recovery 1.0 (punish window, floating text none).
  * **Bolts**: telegraph 0.45 white (glaive raised, core glows) → 3 × `Projectile.Fire` fan (−15°, 0°, +15°) toward player, speed 9, dmg 12, Parryable, colour `Palette.Red`, tag `boss_bolt` (reflected bolts deal 35 to the boss) → recovery 0.6. sfx `boss_bolts`.
  * **Whirl** (P2): telegraph 0.5 white → 4 hits every 0.28 s, `OverlapCircle` radius 3.4 dmg 12 kb 4, boss drifts toward player at 2 u/s → recovery 0.8. sfx `boss_whirl`.
  * **RedThrust** (P2, RED): telegraph 0.6 red → lunge 7 u @ 16 u/s with box (2.2, 1.8) dmg 28 kb 9 unblockable → recovery 1.0. sfx `spear_lunge` (reuse) or `boss_dash`.
* **Reactions:** never flinches; hurt flash; parry perfect → 0.15 s recoil + internal damage (base rule); internal damage ≥ 110 also triggers Stagger (once per 8 s). `GameEvents.RaiseBossHealthChanged(hp01, internal01)` on any change (internal01 = InternalDamage / MaxHp).
* **Death:** Hp ≤ 0 → `IsFightActive=false`, `IsAlive=false`, `CurrentAttack=""`, all hitboxes off; sequence: 1.5 s slow-mo (`TimeController.SlowMo(0.2, 1.5)`), 3 white `Vfx.FlashLight`/`ScreenFlash` pulses, `Vfx.Dissolve`, `Vfx.Embers` big, `Cam.Shake(1)`, sfx `boss_death`, `GameEvents.RaiseEnemyKilled(this)`, then after 2.5 s (unscaled) `Defeated` + `GameEvents.RaiseBossDefeated()`.
* **Rig** (`BossRig`): parts `boss_leg`×2, `boss_torso`, `boss_head` (mask, teal eyes), `boss_arm`×2, `boss_glaive` (in front hand), `boss_core` (glowing chest core, additive + red `Light2D` intensity 1.2 radius 3), cape = 6 × `boss_cape_seg` verlet (dark red). Sort order base `SortOrder.Boss`. Same telegraph tinting as enemies. Slash arcs via `Vfx.SlashArc(..., Palette.BossSlash, scale 2.4)`. Landing dust/shake. Idle breathing, walk cycle, crouch, leap, kneel poses.
* **BossArenaDirector:** trigger at `arenaBounds.xMin + 1` (BoxCollider2D trigger, layer Trigger). `StartIntro()`: `player.SetControlEnabled(false)`, `entranceGate.Close()` (sfx `gate_close`), `Cam.Focus(boss.Center + (0,1), 0.8)`, `Cam.SetZoom(7.2, 1)`, boss rises from kneel (1.2 s), sfx `boss_roar`, `Ui.ShowNameCard(BossName, BossSubtitle, 3)`, `Ui.ShowBossBar(BossName, BossSubtitle)`, `Audio.PlayMusic("music_boss", 1)`, `GameEvents.RaiseBossFightStarted`, after 2.6 s total: `Cam.ReleaseFocus(0.8)`, `Cam.SetZoom(6.8, 1)`, control on, `Boss.BeginFight()`. On `GameEvents.PlayerRespawned` (player died): `Boss.ResetFight()`, `Ui.HideBossBar()`, `entranceGate.Open()` silently, `Audio.PlayMusic("music_title")`, `Cam.SetZoom(6, 0.5)`, `IntroPlayed=false` (intro replays). On `Boss.Defeated`: `Ui.HideBossBar()`, `Audio.StopMusic(2)`, `Audio.PlaySfx("victory_sting")`.

---

## 6. Levels (`AshenSol.Level`, owner: **level** agent)

Files: `LevelBuilder.cs`, `Level1Data.cs`, `BossArenaData.cs`, `LevelInfo.cs`, `Checkpoint.cs`, `Gate.cs`, `EncounterZone.cs`, `SpikeHazard.cs`, `KillZone.cs`, `TutorialSign.cs`, `ParallaxLayer.cs`, `LevelDecor.cs`, `GeometryBuilder.cs`.

```csharp
public static class LevelBuilder {
  public static LevelInfo BuildLevel1(Transform root);
  public static LevelInfo BuildBossArena(Transform root);
}
public class LevelInfo {
  public LevelId Id; public string Title, Subtitle; public Rect Bounds; public Vector2 PlayerSpawn;
  public string MusicTrack; public string Ambience; public Color AmbientColor; public float AmbientIntensity;
  public List<Checkpoint> Checkpoints; public Gate ExitGate; public List<EnemyBase> Enemies; public List<EncounterZone> Zones;
  public AshenSol.Boss.BossArenaDirector Arena;     // null for Level 1
  public void ResetEnemies();                        // ResetToSpawn on all, Projectile.ClearAll(), re-arm zones that were not cleared
}
public class Checkpoint : MonoBehaviour { public string Id; public Vector2 RespawnPoint; public bool IsActive; public event Action<Checkpoint> Activated; }   // trigger (1.2×2) → activates once: shrine lights up (teal Light2D + glow), sfx checkpoint, prompt "Checkpoint", GameEvents.RaiseCheckpointActivated(Id), GameFlow.Instance.SetRespawnPoint(RespawnPoint)
public class Gate : MonoBehaviour { public bool IsOpen; public void Open(); public void Close(); public event Action PlayerEntered; public string SealedPrompt = "The seal holds. Slay the guardians."; }
public class EncounterZone : MonoBehaviour { public string Id; public Rect Area; public List<EnemyBase> Enemies; public bool Cleared; public event Action<EncounterZone> OnCleared; }
public class SpikeHazard : MonoBehaviour { public int Damage = 15; }   // trigger on Layers.Hazard → PlayerController.ApplyHazard(Damage)
public class KillZone : MonoBehaviour { }                              // below the level: ApplyHazard(20)
public class TutorialSign : MonoBehaviour { public string Text; }      // world text (TextMesh, builtin font, bone colour, size ~0.5 u) + `prop_sign`; fades in when the player is within 5 u
public class ParallaxLayer : MonoBehaviour { public float Factor; public bool RepeatX; public float ExtraScroll; }  // follows Services.Cam each LateUpdate: pos = camPos * (1-Factor)... (standard parallax), infinite horizontal repeat by 3 copies
```

**Geometry** (`GeometryBuilder`): `Ground(Rect)` = GameObject with `SpriteRenderer` (`tile_stone`, drawMode Tiled, size = rect size, sort `SortOrder.Ground`) + `BoxCollider2D` + layer `Layers.Ground`; top edge gets a `tile_stone_top` strip (Tiled, height 0.24, sort `GroundDecor`). `Platform(x, y, width)` = `tile_platform` (Tiled, height 0.3) + `BoxCollider2D` (usedByEffector) + `PlatformEffector2D` (one-way, surface arc 170) on layer `Layers.OneWay`. `Spikes(x, y, width)` = `tile_spikes` (Tiled, height 0.4, sort GroundDecor) + `BoxCollider2D` trigger (height 0.3, slightly inset) + `SpikeHazard`, layer `Layers.Hazard`. `Wall(Rect)` = Ground. `KillZone(Rect)`.

**Decor** (`LevelDecor`): lanterns (`prop_lantern` + warm `Light2D` intensity 1.0 radius 3.5, flicker ±10 %), pillars (`prop_pillar`, `prop_pillar_broken`, sort BgProps), banners (`prop_banner`, gentle sway), statues (`prop_statue`), bamboo (`prop_bamboo`), rocks, wall panels & lattices (`prop_wall_panel`, `prop_lattice`, Tiled, sort BgWall, dark tint), shrine (`prop_shrine`), torii frame (`prop_torii`), sealed gate (`prop_gate_sealed` + `prop_gate_glyph` glowing red until opened, then teal; opening = door slides down 2.6 u over 1.2 s with dust, sfx `gate_open`). Ambient VFX: `VfxManager.CreateAmbientEmbers(Rect area, Color color, float rate, Transform parent)` and `VfxManager.CreateMist(Rect area, Color tint, int count, Transform parent)` (see §8). Parallax layers: `bg_sky` (Factor 1 = fixed to camera, sort BgSky, scaled to always cover the view), `bg_far` (0.85, RepeatX), `bg_mid` (0.6), `bg_near` (0.35), and a foreground `bg_fog` (−0.2, sort Fog, alpha 0.35, slow ExtraScroll). (Factor here = how much the layer moves *with* the camera: 1 = glued, 0 = world-locked.)

**Level 1 — "Ruined Shrine Corridor"** (`Level1Data`): Bounds (0, −8) → (150, 26). Spawn (3, 1). `MusicTrack = "music_level1"`, `Ambience = "ambience_wind"`, ambient light (0.72, 0.84, 1.0) × 0.62. Title card "RUINED SHRINE CORRIDOR" / "Level I".
Design constraints (the AutoPilot must be able to traverse it with simple rules): all gaps ≤ 3.4 u; step-ups ≤ 2.4 u; no wall jumps; one-way platforms for vertical routes; the main path goes left→right and ends at the gate on the far right; pits contain spikes (floor of pit at −3 with `Spikes`) and a `KillZone` covers y < −7. Suggested sections (agent may refine but keep the count/order):
1. x 0–18 courtyard: checkpoint C0 (auto-activates on spawn), signs (§1 controls), 1 Grunt at x 14 (zone Z1).
2. x 18–40 broken bridge: 3 gaps (2.5–3.4 u) with spike pits, a platform up to a ledge.
3. x 40–62 shrine hall: Z2 = 2 Grunts + 1 Drone (drone at height +4). Checkpoint C1 at x 58.
4. x 62–88 ascent: platforms climbing +6 u, Spear Sentinel on a ledge + Drone (Z3), lanterns.
5. x 88–108 descent & corridor: drop down (≤ 6 u, no damage), Checkpoint C2 at x 104.
6. x 108–140 gauntlet Z4: Grunt + Spear + 2 Drones; arena-like floor with lattice walls.
7. x 140–150 gate plaza: torii frame, the **sealed gate** at x 146 (`Gate`, closed until Z4 cleared → `Open()`, prompt "The seal breaks.", `GameEvents.RaiseGateOpened`). Touching the closed gate shows `SealedPrompt` (throttled 3 s).
Solid ground below everything except pits; walls at x = 0 and x = 150 (tall).

**Boss Arena — "The Sealed Sanctum"** (`BossArenaData`): Bounds (0, −6) → (50, 18). Spawn (2.5, 1). `MusicTrack = "music_title"` (ambient until the fight), `Ambience = "ambience_arena"`, ambient light (1.0, 0.72, 0.68) × 0.42. Title card "THE SEALED SANCTUM" / "Level II". Geometry: corridor floor x 0–12 (y 0), `Gate` at x 11.5 (starts **open**), arena floor x 12–47, low ledges (1.2 u high, 3 u wide) at x 16 and x 43, walls at x 0 and x 48, ceiling implied by bounds. Boss spawn (37, 0). Checkpoint at spawn (auto). Decor: `bg_boss_sky` (eclipse), `bg_boss_far` (colossal statues), red lanterns, chains (`prop_chain`), statues, ash embers (red, slow), heavy mist. `Arena = BossArenaDirector.Create(new Rect(12, 0, 35, 12), bossSpawn, gate, root)`; `ExitGate = null`.

---

## 7. UI (`AshenSol.UI`, owner: **ui** agent)

Files: `UiManager.cs`, `HudView.cs`, `BossBarView.cs`, `NameCardView.cs`, `ScreenFlow.cs` (title / death / victory / pause panels), `UiKit.cs` (helpers to build Text/Image/RectTransforms in code, font loading, fonts + letter-spacing emulation).

* Canvas: Screen Space Overlay, `CanvasScaler` ScaleWithScreenSize (1920×1080, match 0.5), sorting order 100, `GraphicRaycaster` not needed. All UI animation uses unscaled time.
* Font: `Resources.Load<Font>("Fonts/Cinzel-Regular")` if present else built-in `LegacyRuntime.ttf`. Big prestige texts are pre-rendered sprites (`ui_title`, `ui_text_fallen`, `ui_text_vanquished`, `ui_bossname`) — use them; dynamic text uses the font.
* **HUD** (top-left, margin 48): HP bar (`ui_bar_frame` 420×26; fill: image sliced/white tinted `Palette.Teal`→`Palette.Bone` gradient via two images; trailing white "recent damage" bar that waits 0.5 s then shrinks over 0.4 s; flashes red on damage) + Qi pips below (3 × `ui_pip` 30×30, filled = `ui_pip_full` gold with a soft pulse; gaining a pip pops scale 1.4→1). Subscribes to `PlayerHealthChanged`, `PlayerQiChanged`. Hidden on Title/Victory.
* **Boss bar** (bottom-centre, 900×18, margin-bottom 60): dark back, `Palette.Red` HP fill, `Palette.InternalDamage` overlay drawn from the right edge of the HP fill, thin bone frame, boss name above in font (letter-spaced upper case, size 26), subtitle small. Slides in from below over 0.6 s. `UpdateBossBar` also driven by `GameEvents.BossHealthChanged`. Phase 2: name text turns `Palette.Amber` briefly.
* **Name card** (`ShowNameCard`): centre; title size 64 with horizontal rule lines left/right (bone, 30 % alpha), subtitle size 22 letter-spaced; fade in 0.4, hold, fade out 0.6. For the boss (title == `BossController.BossName`) show the `ui_bossname` sprite instead of text.
* **Prompt** (`ShowPrompt`): top-centre (y −140), size 24, bone; fades; re-calls restart the timer.
* **Fade**: full-screen black `Image`, raycast off, top-most.
* **Death screen**: dark overlay (alpha 0.75 over 0.8 s) + `ui_text_fallen` (fade & slight scale-down from 1.1) + "Press ENTER / A to rise again" (blinking); `Services.Input.ConfirmPressed` → callback (once).
* **Victory screen**: black overlay → `ui_text_vanquished` (scale-in with `Ease.OutBack`) → stats lines appear one by one (0.25 s apart): Time, Parries (perfect/total), Deaths, Damage taken, then "Press ENTER / A" → callback.
* **Title**: dark backdrop image (`bg_boss_sky` dimmed) + `ui_title` logo (slow breathing scale 1→1.02, teal glow behind: `fx_glow` big, additive) + "Press ENTER / A to begin" blinking + controls table (two columns, small) + bottom-right "Ashen Sol — a Nine Sols-inspired prototype". `AnyPressed`/`ConfirmPressed` → callback once.
* **Pause**: overlay + "PAUSED" + controls list + "ESC / Start — resume". `SetPaused(false)` hides.
* `SetHudVisible`. `HideTitle`.
* Helper: `UiKit.Text(parent, text, size, color, anchor...)`, `UiKit.Image(parent, sprite, color, size, anchor...)`, `UiKit.LetterSpaced(string)` inserts thin spaces for headings.

---

## 8. VFX (`AshenSol.VFX`, owner: **vfx** agent)

Files: `VfxManager.cs`, `ParticleFactory.cs`, `SpriteFx.cs` (one-shot tweened sprite objects), `LightFx.cs`, `ScreenFx.cs`.

`VfxManager : MonoBehaviour, IVfxService` implements every `IVfxService` method (see Contracts) plus:
```csharp
public static ParticleSystem CreateAmbientEmbers(Rect area, Color color, float rate, Transform parent);  // slow rising glowing motes, additive, size 0.04–0.1, life 4–7 s, slight sideways drift
public static GameObject CreateMist(Rect area, Color tint, int count, Transform parent);               // count `bg_fog` sprites, sort SortOrder.Fog, alpha 0.18–0.3, drifting slowly (each its own speed), wrap around area
public static ParticleSystem CreateEmbersBurst(...)  // optional
public static Material AdditiveFor(Sprite s);        // cached additive material per sprite texture (MaterialLibrary.Additive clone with mainTexture)
```
Looks:
* `SlashArc`: `fx_slash` sprite object, additive, colour (HDR ×1.6), rotation = angle (flipX mirrors), scale 1.8×scale → 2.3, alpha 1→0 over 0.16 s, plus a `FlashLight` (colour, 1.5, 2.5, 0.1).
* `ParrySpark(perfect)`: burst 26 `fx_spark` particles (white→teal, speed 7–13, gravity 0.6, life 0.3–0.45, size 0.08–0.16, stretched billboard), `fx_ring` expanding 0.2→2.6 u fading over 0.25 s (white, additive), `fx_flare` star flash scale 3.2 rotating 45°, `FlashLight(white, 3.5, 5, 0.15)`, `ScreenFlash(white α 0.22, 0.09)`; **block**: 10 amber sparks, small ring 1.4 u, light 1.5.
* `HitSpark`: 8–12 sparks in a ±35° cone along dir, small flare.
* `InkSplatter`: 8–14 `fx_ink` droplets (colour, gravity 1.2, speed 3–8, life 0.5–0.7, stretch by speed).
* `DustPuff`: 6 `fx_dust` particles (grey-bone alpha 0.5, speed 0.5–1.5, life 0.4–0.6, size grows).
* `Dissolve`: clone each renderer into a temp object; each clone drifts (random velocity 1–3 u/s, rotates), tints toward `tint`, fades over 0.6 s; plus `Embers(pos, 30, tint)` and an `InkSplatter`.
* `Shockwave`: `fx_shockwave` ring scaled from 0.5 → radius over 0.35 s fading (additive colour), `DustPuff`s along the ground, `FlashLight`.
* `Afterimage`: clones renderers (same sprite/pos/rot/scale/flip/sort order−1), tint colour, alpha 0.55→0 over life.
* `FlashLight`: pooled `Light2D` (point) with intensity → 0 over seconds.
* `ScreenFlash`: a full-screen quad (sprite `white`, unlit) parented to the camera at z +5, sort `SortOrder.Foreground + 50`, colour alpha eased to 0.
* `ChromaticPulse`: uses `CameraController.GlobalVolume` (public static `Volume`) → `profile.TryGet<ChromaticAberration>()` intensity = strength → 0 over seconds; also `LensDistortion` intensity −0.2×strength → 0 if present.
* `FloatingText`: world `TextMesh` (builtin font, size scaled to ~0.45 u, outline via 4 shadow copies optional), rises 1.0 u over 0.8 s, fades, slight random x; colour param; `scale` multiplies size.
* `Embers`: burst of glowing motes rising.
* `ProjectileTrail`: adds a `TrailRenderer` (width 0.16→0, time 0.22, additive material, colour) to `follow`.
All particle systems: `ParticleSystemRenderer.material = AdditiveFor(sprite)` (or `MaterialLibrary.SpriteUnlit` clone with texture for opaque ink/dust), `renderMode = Billboard`/`Stretch` for sparks, `sortingOrder = SortOrder.Fx`. Pool one-shot objects (simple list pools; never allocate every call more than necessary).

---

## 9. Audio (`AshenSol.Audio`, owner: **core** agent writes `AudioManager.cs`; **audio** agent generates the clips)

`AudioManager : MonoBehaviour, IAudioService`:
* 12 pooled `AudioSource`s for SFX (2D, spatialBlend 0). Same clip started within 40 ms twice → skip the third. Random pitch `1 ± pitchVariance`. Master SFX volume 0.9.
* Music: two `AudioSource`s crossfading (`PlayMusic`), volume 0.55 × duck. Loop with a seamless crossfade: when the current clip has ≤ 2.5 s left, start the same clip again on the other source and crossfade over 2.5 s (so ElevenLabs tracks loop without a click). `StopMusic` fades out.
* Ambience: one looping source at `volume`, crossfade on change.
* Clips: `Res.Clip(Res.SfxPath + name)` / `Res.MusicPath`. Missing → ignore silently after one warning.
* `PlaySfxAt`: volume × `1 - clamp01((dist-6)/12)`.

### SFX list (all in `Assets/_Game/Resources/Audio/SFX/<name>.mp3`, 44.1 kHz, ≤ 5 s, trimmed, peak-normalised to −1 dBFS)
Player: `sword_swing_1`, `sword_swing_2`, `sword_swing_3`, `sword_hit`, `parry_perfect`, `parry_block`, `parry_fail`, `parry_ready`, `dash`, `jump`, `land`, `footstep`, `player_hurt`, `player_death`, `heal`, `qi_blast`, `qi_gain`.
Enemies: `enemy_telegraph`, `enemy_telegraph_red`, `grunt_swing`, `spear_thrust`, `spear_lunge`, `drone_shot`, `drone_hover` (loop, 3 s), `drone_death`, `projectile_reflect`, `enemy_hit`, `enemy_death`.
Boss: `boss_roar`, `boss_swing`, `boss_dash`, `boss_slam`, `boss_shockwave`, `boss_bolts`, `boss_whirl`, `boss_stagger`, `boss_hurt`, `boss_death`, `boss_phase2`.
World/UI: `checkpoint`, `gate_open`, `gate_close`, `spikes`, `ui_confirm`, `ui_move`, `level_start`, `victory_sting`, `ambience_wind` (loop, 5 s), `ambience_arena` (loop, 5 s).

### Music (`Assets/_Game/Resources/Audio/Music/<name>.mp3`)
`music_title` (≈ 75 s, melancholic Taopunk ambient: guzheng, dizi, distant synth pads, wind, sparse), `music_level1` (≈ 110 s, tense exploration: guzheng plucks, erhu phrases, low synth pulse, sparse taiko, industrial textures), `music_boss` (≈ 110 s, intense hybrid orchestral-electronic: taiko, erhu, distorted synth bass, aggressive rhythm, 140 bpm), `music_victory` (≈ 20 s, solemn triumphant resolve, guzheng + pads).

---

## 10. Art (owner: **art** agent) — `Tools/gen_sprites.py` → `Assets/_Game/Resources/Sprites/*.png`

Python 3.13 + Pillow + numpy. Style: **ink-black silhouettes with luminous accents**, soft baked glows (alpha), subtle noise/texture, clean anti-aliased shapes; palette from `Palette` (Ink #07090f, Night #0d1526, Deep #132238, Slate #243b52, Stone #5a6b7a, Bone #e8e0d0, Teal #4fe3d0, Red #ff3a3a, Gold #ffcc55, Amber #ff9a3c, Violet #7b5cff). Supersample 2× then downscale (LANCZOS). 100 PPU. All sprites face **right**. Transparent backgrounds. Also write `Tools/contact_sheet.png` (all sprites labelled, dark background) for review, and try to download an OFL font (`Cinzel-Regular.ttf`, e.g. `https://github.com/google/fonts/raw/main/ofl/cinzel/static/Cinzel-Regular.ttf` or the variable `Cinzel[wght].ttf`) into `Assets/_Game/Resources/Fonts/Cinzel-Regular.ttf` (skip silently if unavailable; pre-rendered text sprites then use a Windows serif such as Palatino Linotype / Georgia / Cambria).

| Sprite | px (w×h) | Notes |
|---|---|---|
| `player_head` | 44×46 | hooded head, pale face slit, teal eye glint, red headband tail |
| `player_torso` | 46×62 | dark robe, red sash knot, gold trim lines, teal chest glyph |
| `player_arm_back`, `player_arm_front` | 16×46 | arm hanging down from shoulder (shoulder at top centre) |
| `player_sword` | 14×96 | straight jian, blade pale teal-white with glow, dark hilt, tip at top; hilt at bottom |
| `player_leg_back`, `player_leg_front` | 20×54 | hip at top centre, boot at bottom |
| `player_sash_seg` | 12×12 | red soft square/circle |
| `grunt_torso` 52×62, `grunt_head` 38×38 (cracked mask, hollow red eyes), `grunt_arm` 14×42, `grunt_blade` 12×72 (rusty, amber edge), `grunt_leg` 16×46 | | husk soldier, ashen grey-blue, red cracks |
| `spear_torso` 50×66, `spear_head` 36×42 (tall helm, teal visor), `spear_arm` 14×42, `spear_spear` 10×160 (tip at top, teal-glow tip), `spear_leg` 16×48 | | |
| `drone_body` 58×42 (dark lens body), `drone_eye` 18×18 (amber glow), `drone_ring` 76×76 (thin rotating glyph ring, gold) | | |
| `boss_torso` 112×150, `boss_head` 72×82 (bronze mask, teal eyes, horns), `boss_arm` 28×92, `boss_glaive` 26×270 (blade at top, red edge glow), `boss_leg` 36×110, `boss_cape_seg` 24×24 (dark red), `boss_core` 34×34 (red glowing core) | | |
| `tile_stone` | 100×100 | tileable dark stone blocks, subtle teal moss cracks |
| `tile_stone_top` | 100×24 | tileable horizontally: mossy edge with a thin bone highlight line |
| `tile_platform` | 100×30 | tileable: carved dark wood/stone beam with gold inlay |
| `tile_spikes` | 100×40 | tileable: 4 bone-white spikes with red base glow |
| `prop_lantern` 32×52, `prop_pillar` 64×230, `prop_pillar_broken` 64×130, `prop_banner` 44×130 (red cloth, gold glyph), `prop_torii` 320×280, `prop_gate_sealed` 170×280 (double door with seal), `prop_gate_glyph` 90×90 (glowing seal), `prop_shrine` 130×150 (small altar with lantern), `prop_statue` 150×280 (seated guardian silhouette), `prop_bamboo` 70×320, `prop_rock` 90×56, `prop_sign` 54×64, `prop_chain` 20×300, `prop_wall_panel` 100×100 (tileable dark panel with circuit-glyph lines), `prop_lattice` 100×100 (tileable lattice) | | |
| `bg_sky` | 1024×1024 | vertical gradient Night→Deep with a huge dim red sun (Nine Sols style) and faint stars |
| `bg_boss_sky` | 1024×1024 | darker, eclipse: black disc with red corona |
| `bg_far` | 1024×512 | tileable X: layered mountains, pagoda silhouettes, mist gradient at the bottom |
| `bg_mid` | 1024×512 | tileable X: ruined temple silhouettes, hanging cables, a few lit windows (teal/amber) |
| `bg_near` | 1024×512 | tileable X: bamboo & broken pillars silhouettes (Ink), bottom fades to transparent |
| `bg_boss_far` | 1024×512 | tileable X: colossal seated statues, chains |
| `bg_fog` | 512×256 | soft cloud blob, white, feathered |
| `fx_slash` 220×130 (crescent, sharp outer edge, soft inner), `fx_ring` 128×128 (thin ring), `fx_glow` 64×64 (soft radial), `fx_spark` 16×16 (diamond), `fx_dust` 32×32 (soft blob), `fx_ink` 24×24 (irregular blob), `fx_bolt` 44×18 (elongated energy bolt with core), `fx_shockwave` 256×64 (flat ground ring), `fx_flare` 256×256 (4-point star), `fx_parry_glyph` 100×100 (Taoist circle glyph, teal, semi-transparent) | | |
| `ui_bar_frame` 420×26 (thin bone frame, dark inner), `ui_pip` 30×30 (empty ring), `ui_pip_full` 30×30 (gold filled with glow), `ui_title` 900×260 ("ASHEN SOL" — serif, letter-spaced, bone with teal glow & red underline glyph), `ui_text_fallen` 800×120 ("YOU HAVE FALLEN", red-bone), `ui_text_vanquished` 900×120 ("SOL VANQUISHED", gold), `ui_bossname` 1000×140 ("THE FORSAKEN WARDEN" + small "Keeper of the Sealed Gate"), `white` 4×4 | | |

---

## 11. Editor tooling (`AshenSol.EditorTools`, `Assets/_Game/Editor/`, owner: **editor** agent)

* `ProjectBootstrap.cs` — `public static void Run()` (for `-executeMethod AshenSol.EditorTools.ProjectBootstrap.Run`) and a `[MenuItem("Ashen Sol/Bootstrap Project")]`:
  1. Name layers in `TagManager` (6 Ground, 7 Player, 8 Enemy, 9 Hazard, 10 Projectile, 11 Trigger, 12 OneWay) via `SerializedObject`.
  2. Always-included shaders: `Universal Render Pipeline/2D/Sprite-Lit-Default`, `Universal Render Pipeline/2D/Sprite-Unlit-Default`, `Universal Render Pipeline/Particles/Unlit`, `Sprites/Default`, `UI/Default`, `Legacy Shaders/Particles/Additive`, `GUI/Text Shader`.
  3. `PlayerSettings`: productName "Ashen Sol", companyName "AshenSol", `fullScreenMode = FullScreenWindow`, default 1920×1080, `resizableWindow = true`, `runInBackground = true`, `usePlayerLog = true`, colour space Linear (keep), api compatibility default, `bundleVersion "0.1.0"`.
  4. URP asset (`Assets/Settings/UniversalRP.asset`): `supportsHDR = true`, MSAA off; Renderer2D keep. Quality: vSync 1.
  5. Create `Assets/_Game/Scenes/Main.unity` if missing: empty scene, one GameObject `GameBootstrap` with `AshenSol.Core.GameBootstrap`; save; `EditorBuildSettings.scenes = [Main]`.
  6. `AssetDatabase.SaveAssets()`, refresh. Log `[Bootstrap] OK`.
* `BuildScript.cs` — `public static void BuildWindows()`: `BuildPipeline.BuildPlayer` → `C:\Users\User\Desktop\nine_sols\Builds\Windows\AshenSol.exe`, `StandaloneWindows64`, options `None` (`-dev` arg → Development). Log summary `[Build] result=<...> size=<MB> errors=<n>`; `EditorApplication.Exit(1)` on failure.
* `SpriteImportPostprocessor.cs` (`AssetPostprocessor.OnPreprocessTexture` for `Assets/_Game/Resources/Sprites/`): `textureType = Sprite`, `spriteImportMode = Single`, `spritePixelsPerUnit = 100`, `filterMode = Bilinear`, `textureCompression = Uncompressed`, `mipmapEnabled = false`, `alphaIsTransparency = true`, `wrapMode = Repeat` for names starting with `tile_`/`bg_`/`prop_wall`/`prop_lattice` else Clamp, `spriteMeshType = FullRect`, max size 2048.
* `AudioImportPostprocessor.cs` (`OnPreprocessAudio`): Music → `loadType = Streaming`, `compressionFormat = Vorbis`, quality 0.6; SFX → `DecompressOnLoad`, Vorbis 0.7; `forceToMono = false`.
* `Tools/unity_compile.ps1` / `.sh`: runs `Unity.exe -batchmode -quit -nographics -projectPath <P> -executeMethod AshenSol.EditorTools.ProjectBootstrap.Run -logFile Tools/compile.log`, then prints every `error CS`, `Exception`, `[Bootstrap]` line and the exit code. `Tools/unity_build.ps1`: same with `BuildScript.BuildWindows` (no `-nographics`). `Tools/run_autopilot.ps1`: runs the built exe with `-autopilot -screenshotDir C:\Users\User\Desktop\nine_sols\Screenshots -quitAfter 240 -screen-width 1920 -screen-height 1080 -screen-fullscreen 0` and waits, then prints `Screenshots/autopilot_log.txt`.

---

## 12. AutoPilot (`AshenSol.Core.AutoPilot`, owner: **core** agent)

Runs when `-autopilot` is present. Provides a `ScriptedInput : IInputProvider` (replaces `Services.Input`) driven by a simple brain each frame, so the real player code is exercised:
* Title: press Confirm. Level: walk right (`Horizontal = 1`). Jump when: no ground within 1.3 u ahead & 2.5 u down (ledge/gap), or a wall ≤ 0.9 u ahead (`Physics2D.Raycast` on `GroundMask`), or spikes ahead (Hazard layer raycast 2 u). Drop-through never needed.
* Combat (nearest alive enemy within 3.2 u horizontally & 2.5 u vertically): face it; if it `IsTelegraphing`: Parryable & `TimeUntilStrike ≤ 0.14` → Parry; Unblockable & `TimeUntilStrike ≤ 0.25` → Dash away; else attack when within 1.8 u (cadence 0.25 s); Qi Blast when `Qi > 0` and target `InternalDamage ≥ 20`; Heal when `Hp < 40 && Qi > 0` and no telegraph. Drones: if a drone bolt (Projectile, team Enemy) is within 1.2 u and approaching → Parry. Standing still to fight while enemies are within 4 u; otherwise keep walking.
* Boss: same brain; additionally during `CurrentAttack == "Slam"` keep dashing away from the boss; jump when a `GroundShockwave` is within 1.5 u and approaching.
* Death screen / victory: Confirm after 1 s.
* Screenshots: `ScreenCapture.CaptureScreenshot(dir/shot_<n>_<label>.png)` every 6 s and on events (first parry, checkpoint, gate open, boss intro, boss phase 2, victory), max 40. Log every `GameEvents.Log` line + state changes to `dir/autopilot_log.txt` (flushed), final summary line `AUTOPILOT RESULT: reachedGate=<b> bossStarted=<b> bossDefeated=<b> deaths=<n> parries=<n> perfect=<n> time=<s> exceptions=<n>` (count `Application.logMessageReceived` errors/exceptions), then `Application.Quit()` at `-quitAfter` seconds or 4 s after victory.

---

## 13. Definition of done (integration stage)

1. `Tools/unity_compile.ps1` → 0 `error CS`, `[Bootstrap] OK`.
2. `Tools/unity_build.ps1` → `Builds/Windows/AshenSol.exe` exists.
3. `Tools/run_autopilot.ps1` → log shows `reachedGate=True bossStarted=True`, ideally `bossDefeated=True`, `exceptions=0`; screenshots look like the intended art direction (dark Taopunk, lights, bloom, parallax), HUD visible, no magenta/pink materials, no white placeholder squares.
4. Human playthrough is the final judge: the game must be *fun to parry*.

---

## 14. Title menu & difficulty (added after the first playable build)

`AshenSol.Core.Settings` (static, PlayerPrefs-backed) holds the player-facing options and derives the
difficulty multipliers:

| | DISCIPLE | SOL SLAYER |
|---|---|---|
| `ParryPerfectWindow` | 0.22 s | 0.14 s |
| `DamageTakenMul` | 0.8 | 1.3 |
| `TelegraphMul` | 1.12 | 0.9 |
| `HealAmount` | 40 | 30 |

`Settings.MusicVolume` / `SfxVolume` feed `AudioManager`'s masters, `ScreenShake` gates
`CameraController.Shake`. Everything is saved on change.

`AshenSol.UI.TitleMenu` builds the rows (START GAME / DIFFICULTY / MUSIC / SOUND / SCREEN SHAKE /
QUIT) under the title panel and is ticked by `UiManager` while the title is visible. Navigation:
W/S or stick to select, A/D to change a value, Enter/South to activate; the mouse takes over only
after it actually moves (otherwise a resting cursor silently steals the selection).

`GameFlow` now boots into the title unless `-skipTitle` is passed, so `-autopilot` runs exercise the
menu too.

---

## 15. Posture system (replaces "internal damage")

Every enemy carries a second resource next to HP: **posture** (`EnemyBase.Posture` / `MaxPosture`,
yellow bar). It replaces the old internal-damage mechanic entirely.

| source | posture gained |
|---|---|
| perfect parry | `PostureOnParry` — a full bar for mooks, 105 of 300 for the boss |
| late block | 25 % of max |
| player sword hit | 7 % of max |
| Qi Blast | 45 % of max |

Posture drains at `PostureRegen`/s after `PostureRegenDelay` (1.6 s) without new posture damage.

**Break** (`EnemyBase.BreakPosture`): the enemy is staggered for `PostureBreakSeconds` (3 s), takes
`BrokenDamageMul` (2×) damage, its bar flashes white, "GUARD BROKEN" floats up and a pulsing `I`
prompt appears over its head. The boss additionally drops to one knee and gets a shockwave + screen
flash.

**Execution** (`PlayerCombat.ExecuteRoutine`): pressing the Qi key (`I` / gamepad North) while a
broken enemy is within `ExecuteRangeX/Y` spends 1 Qi and deals that enemy's `ExecuteDamage`
(60 / 85 / 45 / 130) as an `AttackKind.Unblockable` hit tagged `"execute"`. The break is consumed
(`RecoverPosture`). Away from a broken enemy the same key is the ordinary Qi Blast.

Boss bar UI carries both: red HP on top, yellow posture underneath
(`IUiService.UpdateBossBar(hp01, posture01, broken)`).

---

## 16. Souls progression (`Core/Progression.cs`, `Level/AshPile.cs`, `UI/UpgradePanel.cs`)

Static `Progression` holds the run: `Level`, `Ash`, `BonusHp`, `DamageMul`, `BonusQi`, and the
pile waiting to be picked up (`DroppedAsh`, `DropPoint`, `DropLevel`). `GameFlow.StartRun` resets it.

* `EnemyBase.AshValue` awards ash on death (Grunt 22, Sentinel 34, Drone 16, Brute 55, Artisan 190, Warden 260).
* `DropOnDeath(where, level)` moves everything carried onto the ground and spawns an `AshPile`
  there; walking into the pile calls `Recover()`. A second death replaces the pile — the old ash
  is gone.
* `LevelCost = 60 + (Level - 1) * 55`. `Buy(kind)` spends it on `Vigor` (+20 hp), `Edge` (+12 %
  sword damage) or `Focus` (+1 max Qi, capped at +3).
* `PlayerController.MaxHp/MaxQi` add the bonuses; `PlayerCombat` multiplies every outgoing sword,
  blast and execution number by `Progression.DamageMul`.

### Shrines (`Level/LevelObjects.cs`, class `Checkpoint`)

A shrine activates the respawn point on touch, but **never opens the menu by itself**. While the
player stands in it, it shows `E   rest at the shrine` and waits for `IInputProvider.InteractPressed`
(E / F / gamepad LT). `Rest()` then plays the transition — duck the music, focus and zoom the camera,
ring `shrine_open`, raise embers for ~0.85 s — opens `UpgradePanel`, waits for it to close, and puts
the camera, music and player control back.

## 17. SOLAR COLLAPSE (`Boss/BossController.SolarCollapse`)

The phase-two ultimate. `PhaseTransition` sets `solarPending`, so the new form always opens with it;
after that it re-enters the rotation on a `SolarCooldown` (17 s) timer.

1. He plants, raises the blade and starts a **parryable** (white) telegraph of `SolarTelegraph` (2.1 s).
   A sun grows over his head — `fx_glow` + two counter-rotating `fx_ring`s + `fx_flare`, all additive,
   with a 30-unit `Light2D`. Camera shake and chromatic aberration ramp with the charge.
2. The sun **folds inward** over 0.14 s. That is the beat to parry on.
3. The strike is a single `StrikePlayer` with an 80 × 44 box — the whole arena. Damage is
   `player.MaxHp * SolarDamageFraction` (0.75), and `AttackInfo.PierceGuard` is set, so a non-perfect
   block still takes `PlayerTuning.PierceChipFraction` (60 %) instead of the usual 30 %.
   A perfect parry costs nothing; dash i-frames still dodge it.
4. Five expanding shockwave rings, a white screen flash, hit-stop, slow-motion and two
   `GroundShockwave`s roll out; then `SolarRecovery` (2.2 s) of open guard.

### Second-form presentation

`ApplySecondForm` scales him to 1.24, tints the armour to a lit charred `(0.98, 0.55, 0.46)`,
brightens core and rig lights, and calls `SetAura(1)`. `TickAura` runs every frame from then on: a
pulsing `fx_glow` fill with two counter-rotating rings, a `Light2D` breathing with it, embers shed
every 0.2 s, and afterimages while `|velocity.x| > 5`.

## 18. End credits (`UI/CreditsRoll.cs`)

`GameFlow.VictoryRoutine` shows the stats card, and its continue button now calls `RollCredits()`
→ `IUiService.ShowCredits(GoToTitle)` instead of returning straight to the title.

The roll is built once from a `Line[]` script of styled entries (Title, Tagline, Role, Name, Cast,
Rule, Gap, End) laid out top-down into a `content` rect, which is then lerped from fully below the
screen to the last line resting on the centre line over `RollSeconds` (56 s), held `HoldSeconds`
(5 s), and faded out. Backdrop is the painted `bg_title` sunk under an ink veil. `music_credits`
plays under it. Any key skips (armed 1.5 s in, so a key held from the victory screen does not).

Top billing is **Producer: Markus Lang** and **Lead Engineer: Claude Code**.

## 19. Title screen art

`bg_title` is the hand-painted valley (`AshenSol/background.png` imported to
`Resources/Sprites/bg_title.png`), drawn full-bleed at 2240 × 1260 with `preserveAspect = false`
behind an ink veil at 0.3. It replaced the generated `bg_boss_sky` gradient.
