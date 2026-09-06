using UnityEngine;
using AshenSol.Core;
using AshenSol.Enemies;
using AshenSol.Boss;
using AshenSol.VFX;

namespace AshenSol.Level
{
    /// <summary>Builds the two levels entirely from code (see SPEC §6).</summary>
    public static class LevelBuilder
    {
        public static LevelInfo BuildLevel1(Transform root)
        {
            var info = new LevelInfo
            {
                Id = LevelId.Level1, Title = "RUINED SHRINE CORRIDOR", Subtitle = "Level I",
                Bounds = new Rect(0f, -8f, 150f, 34f), PlayerSpawn = new Vector2(3f, 0.05f),
                MusicTrack = "music_level1", Ambience = "ambience_wind",
                AmbientColor = new Color(0.72f, 0.84f, 1f), AmbientIntensity = 0.62f
            };
            var geo = new GameObject("Geometry").transform; geo.SetParent(root, false);
            var deco = new GameObject("Decor").transform; deco.SetParent(root, false);
            var bg = new GameObject("Background").transform; bg.SetParent(root, false);
            var ens = new GameObject("Enemies").transform; ens.SetParent(root, false);

            // ---------- background ----------
            LevelDecor.ParallaxStack(bg, "bg_sky", "bg_far", "bg_mid", "bg_near", 0f,
                new Color(0.55f, 0.62f, 0.78f), new Color(0.6f, 0.65f, 0.8f), new Color(0.75f, 0.78f, 0.9f));

            // ---------- solid geometry (ground tops at y=0 unless noted) ----------
            GeometryBuilder.Wall(new Rect(-1f, -8f, 1f, 34f), geo);
            GeometryBuilder.Wall(new Rect(150f, -8f, 1f, 34f), geo);
            GeometryBuilder.Ground(new Rect(0f, -6f, 20f, 6f), geo);              // courtyard
            Pit(geo, 20f, 2.8f);                                                   // gap A
            GeometryBuilder.Ground(new Rect(22.8f, -6f, 5.2f, 6f), geo);
            Pit(geo, 28f, 3.2f);                                                   // gap B
            GeometryBuilder.Ground(new Rect(31.2f, -6f, 4.8f, 6f), geo);
            GeometryBuilder.Ground(new Rect(36f, -6f, 4f, 8f), geo);               // ledge top 2
            Pit(geo, 40f, 3.4f);                                                   // gap C
            GeometryBuilder.Ground(new Rect(43.4f, -6f, 26.6f, 6f), geo);          // shrine hall 43.4–70
            GeometryBuilder.Ground(new Rect(70f, -6f, 6f, 8f), geo);               // steps: top 2
            GeometryBuilder.Ground(new Rect(76f, -6f, 6f, 10f), geo);              // top 4
            GeometryBuilder.Ground(new Rect(82f, -6f, 6f, 12f), geo);              // top 6
            GeometryBuilder.Ground(new Rect(88f, -6f, 62f, 6f), geo);              // corridor + gauntlet + plaza
            GeometryBuilder.Platform(64f, 3f, 3f, geo);
            GeometryBuilder.Platform(92f, 3f, 4f, geo);
            GeometryBuilder.Platform(119f, 2.6f, 3f, geo);
            GeometryBuilder.Platform(129f, 2.6f, 3f, geo);
            GeometryBuilder.KillZone(new Rect(0f, -12f, 150f, 4f), geo);

            // ---------- checkpoints ----------
            var c0 = Checkpoint.Create(new Vector2(1.6f, 0f), "C0", root);
            var c1 = Checkpoint.Create(new Vector2(60f, 0f), "C1", root);
            var c2 = Checkpoint.Create(new Vector2(104f, 0f), "C2", root);
            info.Checkpoints.Add(c0); info.Checkpoints.Add(c1); info.Checkpoints.Add(c2);
            c0.Activate(true);

            // ---------- signs ----------
            TutorialSign.Create(new Vector2(6.5f, 0f), "A / D  move        SPACE  jump", root);
            TutorialSign.Create(new Vector2(11f, 0f), "J  attack        H  heal  (1 Qi)", root);
            TutorialSign.Create(new Vector2(25.5f, 0f), "K  parry the WHITE flash\nL / SHIFT  dash away from the RED flash", root);
            TutorialSign.Create(new Vector2(46.5f, 0f), "Parries fill the yellow GUARD bar.\nWhen it breaks:  I  executes  (1 Qi)", root);

            // ---------- encounters ----------
            var z1 = EncounterZone.Create("Z1", new Rect(8f, 0f, 12f, 6f), root);
            z1.Add(Spawn(info, EnemyType.Grunt, new Vector2(15f, 0.1f), ens));
            var z2 = EncounterZone.Create("Z2", new Rect(46f, 0f, 14f, 8f), root);
            z2.Add(Spawn(info, EnemyType.Grunt, new Vector2(51f, 0.1f), ens));
            z2.Add(Spawn(info, EnemyType.Grunt, new Vector2(56f, 0.1f), ens));
            z2.Add(Spawn(info, EnemyType.WatcherDrone, new Vector2(53.5f, 3.3f), ens));
            var z3 = EncounterZone.Create("Z3", new Rect(74f, 0f, 16f, 12f), root);
            z3.Add(Spawn(info, EnemyType.SpearSentinel, new Vector2(85f, 6.1f), ens));
            z3.Add(Spawn(info, EnemyType.WatcherDrone, new Vector2(79f, 7.3f), ens));
            var z4 = EncounterZone.Create("Z4", new Rect(108f, 0f, 32f, 10f), root);
            z4.Add(Spawn(info, EnemyType.Grunt, new Vector2(115f, 0.1f), ens));
            z4.Add(Spawn(info, EnemyType.SpearSentinel, new Vector2(127f, 0.1f), ens));
            z4.Add(Spawn(info, EnemyType.WatcherDrone, new Vector2(118f, 3.3f), ens));
            z4.Add(Spawn(info, EnemyType.WatcherDrone, new Vector2(133f, 3.3f), ens));
            info.Zones.Add(z1); info.Zones.Add(z2); info.Zones.Add(z3); info.Zones.Add(z4);

            // ---------- gate ----------
            var gate = Gate.Create(new Vector2(146f, 0f), root, false);
            info.ExitGate = gate;
            z4.OnCleared += z =>
            {
                gate.Open();
                Services.Ui.ShowPrompt("The seal breaks.", 2.5f);
                GameEvents.RaiseGateOpened();
            };

            // ---------- decor ----------
            float[] lanternX = { 4f, 17.5f, 26f, 34f, 38f, 47f, 57.5f, 63f, 74f, 80f, 86f, 95f, 103f, 112f, 121f, 131f, 138f, 143.5f };
            foreach (var x in lanternX) LevelDecor.Lantern(new Vector2(x, GroundTopAt(x) + 2.4f), deco);
            float[] pillarX = { 2f, 9f, 18f, 33f, 45f, 49f, 62f, 68f, 90f, 100f, 110f, 120f, 136f, 148.5f };
            for (int i = 0; i < pillarX.Length; i++) LevelDecor.Pillar(new Vector2(pillarX[i], GroundTopAt(pillarX[i])), i % 3 == 1, deco);
            float[] bannerX = { 7f, 44.5f, 53f, 108.5f, 116f, 124f, 141.5f };
            foreach (var x in bannerX) LevelDecor.Banner(new Vector2(x, GroundTopAt(x) + 4.2f), deco);
            LevelDecor.Statue(new Vector2(47f, 0f), deco);
            LevelDecor.Statue(new Vector2(141f, 0f), deco);
            LevelDecor.Statue(new Vector2(58f, 0f), deco);
            float[] bambooX = { 24f, 30f, 32.5f, 38.5f, 65f, 67f, 96f, 98.5f, 101f, 147f };
            foreach (var x in bambooX) LevelDecor.Bamboo(new Vector2(x, GroundTopAt(x)), 0.9f + (x % 3f) * 0.2f, deco);
            float[] rockX = { 12f, 27f, 44f, 72f, 97f, 113f, 135f };
            foreach (var x in rockX) LevelDecor.Rock(new Vector2(x, GroundTopAt(x)), deco);
            LevelDecor.WallPanel(new Rect(108f, 0f, 32f, 9f), deco, "prop_wall_panel", 0.75f);
            LevelDecor.WallPanel(new Rect(44f, 0f, 16f, 7f), deco, "prop_lattice", 0.55f);
            LevelDecor.WallPanel(new Rect(140f, 0f, 10f, 9f), deco, "prop_lattice", 0.5f);
            foreach (var x in new[] { 110f, 118f, 126f, 134f }) LevelDecor.Chain(new Vector2(x, 9f), 4f + (x % 5f) * 0.4f, deco);
            VfxManager.CreateAmbientEmbers(new Rect(0f, -2f, 150f, 14f), Palette.Amber, 14f, deco);
            VfxManager.CreateMist(new Rect(0f, -1.5f, 150f, 5f), Palette.Bone, 34, deco);

            return info;
        }

        static void Pit(Transform geo, float x, float w)
        {
            GeometryBuilder.Ground(new Rect(x, -6f, w, 3f), geo, false);   // pit floor at y = -3
            GeometryBuilder.Spikes(x, -3f, w, geo);
        }

        /// <summary>Ground height at x for Level 1 decor placement (mirrors the geometry above).</summary>
        static float GroundTopAt(float x)
        {
            if (x >= 36f && x < 40f) return 2f;
            if (x >= 70f && x < 76f) return 2f;
            if (x >= 76f && x < 82f) return 4f;
            if (x >= 82f && x < 88f) return 6f;
            return 0f;
        }

        static EnemyBase Spawn(LevelInfo info, EnemyType type, Vector2 pos, Transform parent)
        {
            var e = EnemyBase.Create(type, pos, parent, null, -1);
            info.Enemies.Add(e);
            return e;
        }

        // ==================================================================
        //  LEVEL II — THE ASCENDING WORKS
        //  The traversal level: movers, qi vents and climbing chains carry you up through the forge,
        //  and THE SEVENTH ARTISAN holds the top deck.
        // ==================================================================
        public static LevelInfo BuildWorks(Transform root)
        {
            var info = new LevelInfo
            {
                Id = LevelId.Works, Title = "THE ASCENDING WORKS", Subtitle = "Level II",
                Bounds = new Rect(0f, -14f, 146f, 60f), PlayerSpawn = new Vector2(3f, 0.05f),
                MusicTrack = "music_works", Ambience = "ambience_arena",
                AmbientColor = new Color(1f, 0.85f, 0.72f), AmbientIntensity = 0.5f
            };
            var geo = new GameObject("Geometry").transform; geo.SetParent(root, false);
            var deco = new GameObject("Decor").transform; deco.SetParent(root, false);
            var bg = new GameObject("Background").transform; bg.SetParent(root, false);
            var ens = new GameObject("Enemies").transform; ens.SetParent(root, false);

            LevelDecor.ParallaxStack(bg, "bg_sky", "bg_far", "bg_mid", "bg_near", 0f,
                new Color(0.75f, 0.52f, 0.45f), new Color(0.58f, 0.42f, 0.42f), new Color(0.5f, 0.4f, 0.44f));

            GeometryBuilder.Wall(new Rect(-1f, -14f, 1f, 60f), geo);
            GeometryBuilder.Wall(new Rect(146f, -14f, 1f, 60f), geo);
            GeometryBuilder.KillZone(new Rect(0f, -18f, 146f, 4f), geo);

            // ---------- floor 0 ----------
            GeometryBuilder.Ground(new Rect(0f, -12f, 18f, 12f), geo);            // start, top 0
            // a 12 unit chasm: no jump and no dash clears this, the mover is the only way over
            GeometryBuilder.Ground(new Rect(30f, -12f, 14f, 12f), geo);            // top 0, holds the vent

            // ---------- floor 8: casting ledges ----------
            GeometryBuilder.Ground(new Rect(46f, -12f, 12f, 20f), geo);            // top 8
            // the tower: its left face is the climb
            GeometryBuilder.Ground(new Rect(58f, -12f, 6f, 32f), geo);             // top 20

            // ---------- floor 20: the gallery ----------
            GeometryBuilder.Ground(new Rect(64f, -12f, 16f, 32f), geo);            // top 20

            // ---------- floor 28: the forge deck ----------
            GeometryBuilder.Ground(new Rect(86f, -12f, 60f, 40f), geo);            // top 28

            GeometryBuilder.Platform(33f, 4.5f, 3.5f, geo);
            GeometryBuilder.Platform(50f, 12.5f, 3f, geo);
            GeometryBuilder.Platform(70f, 24f, 3.5f, geo);
            GeometryBuilder.Platform(104f, 32.5f, 4f, geo);
            GeometryBuilder.Platform(122f, 32.5f, 4f, geo);

            // ---------- traversal ----------
            // 1. the long chasm: ride it or fall
            MovingPlatform.Create(new Vector2(19.5f, 0.6f), new Vector2(28.5f, 0.6f), 3.2f, 3f, geo, 0.6f);
            // 2. a vent column from floor 0 up to the casting ledge
            QiVent.Create(new Vector2(42f, 0f), 2.6f, 13f, QiVent.Mode.Column, geo, 13f);
            // 3. the chains up the tower wall — 12 units, far past any jump
            ClimbSurface.Create(new Vector2(57.4f, 8f), 12.2f, geo, 1.1f);
            // 4. a launch pad for the optional high platform
            QiVent.Create(new Vector2(54f, 8f), 2.2f, 1.8f, QiVent.Mode.Pad, geo, 18f);
            // 5. the lift to the deck: 8 units of height, nothing else reaches it
            MovingPlatform.Create(new Vector2(82.5f, 21f), new Vector2(82.5f, 29f), 3.2f, 2.6f, geo, 1.2f);
            // 6. deck mobility for the boss fight
            QiVent.Create(new Vector2(95f, 28f), 2.6f, 8f, QiVent.Mode.Column, geo, 12f);

            // ---------- checkpoints ----------
            var w0 = Checkpoint.Create(new Vector2(1.6f, 0f), "W0", root);
            var w1 = Checkpoint.Create(new Vector2(47f, 8f), "W1", root);
            var w2 = Checkpoint.Create(new Vector2(65.5f, 20f), "W2", root);
            var w3 = Checkpoint.Create(new Vector2(88f, 28f), "W3", root);
            info.Checkpoints.Add(w0); info.Checkpoints.Add(w1); info.Checkpoints.Add(w2); info.Checkpoints.Add(w3);
            w0.Activate(true);

            TutorialSign.Create(new Vector2(8f, 0f), "The works still run.\nRide what moves.", root);
            TutorialSign.Create(new Vector2(39f, 0f), "Qi vents lift you —\nand refill your air dash.", root);
            TutorialSign.Create(new Vector2(52f, 8f), "W  climbs the chains\nSPACE  kicks off them", root);

            // ---------- enemies ----------
            var y1 = EncounterZone.Create("W-A", new Rect(4f, 0f, 14f, 8f), root);
            y1.Add(Spawn(info, EnemyType.Grunt, new Vector2(13f, 0.1f), ens));
            var y2 = EncounterZone.Create("W-B", new Rect(30f, 0f, 14f, 10f), root);
            y2.Add(Spawn(info, EnemyType.Grunt, new Vector2(36f, 0.1f), ens));
            y2.Add(Spawn(info, EnemyType.WatcherDrone, new Vector2(40f, 3.4f), ens));
            var y3 = EncounterZone.Create("W-C", new Rect(46f, 8f, 12f, 10f), root);
            y3.Add(Spawn(info, EnemyType.SpearSentinel, new Vector2(53f, 8.1f), ens));
            var y4 = EncounterZone.Create("W-D", new Rect(64f, 20f, 16f, 10f), root);
            y4.Add(Spawn(info, EnemyType.Grunt, new Vector2(70f, 20.1f), ens));
            y4.Add(Spawn(info, EnemyType.WatcherDrone, new Vector2(75f, 23.3f), ens));
            var y5 = EncounterZone.Create("W-E", new Rect(88f, 28f, 22f, 10f), root);
            y5.Add(Spawn(info, EnemyType.SpearSentinel, new Vector2(99f, 28.1f), ens));
            y5.Add(Spawn(info, EnemyType.Grunt, new Vector2(106f, 28.1f), ens));
            info.Zones.Add(y1); info.Zones.Add(y2); info.Zones.Add(y3); info.Zones.Add(y4); info.Zones.Add(y5);

            // ---------- the forge arena ----------
            var entrance = Gate.Create(new Vector2(112f, 28f), root, true);
            Vector2 artisanSpawn = new Vector2(128f, 34f);
            info.Arena = BossArenaDirector.Create(new Rect(114f, 28f, 28f, 14f), artisanSpawn, entrance, root, BossKind.Artisan);
            info.Enemies.Add(info.Arena.BossEnemy);

            // the exit exists from the start so the flow can subscribe; the Artisan holds the key
            var exit = Gate.Create(new Vector2(143f, 28f), root, false);
            exit.SealedPrompt = "The forge still burns.";
            info.ExitGate = exit;
            info.Arena.Boss.Defeated += () =>
            {
                exit.Open();
                Services.Ui.ShowPrompt("The forge falls silent. The way up is open.", 3f);
                GameEvents.RaiseGateOpened();
            };

            // ---------- decor ----------
            foreach (var v in new[] { new Vector2(5f, 0f), new Vector2(34f, 0f), new Vector2(48f, 8f),
                                      new Vector2(66f, 20f), new Vector2(77f, 20f), new Vector2(92f, 28f), new Vector2(110f, 28f) })
                LevelDecor.Lantern(v + new Vector2(0f, 2.6f), deco, Palette.Amber, 1.1f);
            foreach (var v in new[] { new Vector2(2f, 0f), new Vector2(31f, 0f), new Vector2(47f, 8f),
                                      new Vector2(66f, 20f), new Vector2(90f, 28f), new Vector2(118f, 28f), new Vector2(144f, 28f) })
                LevelDecor.Pillar(v, false, deco);
            foreach (var x in new[] { 22f, 26f, 45f, 62f, 84f, 100f, 116f, 134f })
                LevelDecor.Chain(new Vector2(x, 48f), 14f, deco);
            LevelDecor.WallPanel(new Rect(0f, 0f, 18f, 12f), deco, "prop_wall_panel", 0.7f);
            LevelDecor.WallPanel(new Rect(64f, 20f, 16f, 14f), deco, "prop_wall_panel", 0.6f);
            LevelDecor.WallPanel(new Rect(96f, 28f, 46f, 14f), deco, "prop_lattice", 0.5f);
            LevelDecor.Statue(new Vector2(102f, 28f), deco);
            VfxManager.CreateAmbientEmbers(new Rect(0f, -2f, 146f, 48f), Palette.Amber, 24f, deco);
            VfxManager.CreateMist(new Rect(0f, -2f, 146f, 10f), new Color(1f, 0.8f, 0.68f), 24, deco);

            return info;
        }

        // ------------------------------------------------------------------
        public static LevelInfo BuildBossArena(Transform root)
        {
            var info = new LevelInfo
            {
                Id = LevelId.BossArena, Title = "THE SEALED SANCTUM", Subtitle = "Level II",
                Bounds = new Rect(0f, -6f, 50f, 24f), PlayerSpawn = new Vector2(2.5f, 0.05f),
                MusicTrack = "music_title", Ambience = "ambience_arena",
                AmbientColor = new Color(1f, 0.72f, 0.68f), AmbientIntensity = 0.42f
            };
            var geo = new GameObject("Geometry").transform; geo.SetParent(root, false);
            var deco = new GameObject("Decor").transform; deco.SetParent(root, false);
            var bg = new GameObject("Background").transform; bg.SetParent(root, false);

            LevelDecor.ParallaxStack(bg, "bg_boss_sky", "bg_boss_far", "bg_mid", "bg_near", 0f,
                new Color(0.7f, 0.5f, 0.5f), new Color(0.55f, 0.45f, 0.5f), new Color(0.6f, 0.5f, 0.55f));

            GeometryBuilder.Wall(new Rect(-1f, -6f, 1f, 30f), geo);
            GeometryBuilder.Wall(new Rect(48f, -6f, 2f, 30f), geo);
            GeometryBuilder.Ground(new Rect(0f, -6f, 48f, 6f), geo);
            GeometryBuilder.Ground(new Rect(16f, 0f, 3f, 1.2f), geo);
            GeometryBuilder.Ground(new Rect(43f, 0f, 3f, 1.2f), geo);
            GeometryBuilder.KillZone(new Rect(0f, -12f, 50f, 4f), geo);

            var c0 = Checkpoint.Create(new Vector2(1.2f, 0f), "Arena", root);
            info.Checkpoints.Add(c0);
            c0.Activate(true);

            var gate = Gate.Create(new Vector2(11.5f, 0f), root, true);
            Vector2 bossSpawn = new Vector2(37f, 0.05f);
            info.Arena = BossArenaDirector.Create(new Rect(12f, 0f, 35f, 12f), bossSpawn, gate, root);
            info.Enemies.Add(info.Arena.BossEnemy);

            // decor: sanctum
            foreach (var x in new[] { 3f, 8f, 14f, 22f, 30f, 38f, 45f }) LevelDecor.Lantern(new Vector2(x, 2.6f), deco, Palette.Red, 0.9f);
            foreach (var x in new[] { 6f, 13f, 20f, 27f, 34f, 41f, 47f }) LevelDecor.Pillar(new Vector2(x, 0f), x > 30f, deco);
            foreach (var x in new[] { 10f, 24f, 36f, 44f }) LevelDecor.Banner(new Vector2(x, 4.5f), deco);
            LevelDecor.Statue(new Vector2(18f, 0f), deco);
            LevelDecor.Statue(new Vector2(30f, 0f), deco);
            LevelDecor.Statue(new Vector2(42f, 0f), deco);
            foreach (var x in new[] { 15f, 21f, 26f, 33f, 40f, 46f }) LevelDecor.Chain(new Vector2(x, 12f), 5f + (x % 4f) * 0.6f, deco);
            LevelDecor.WallPanel(new Rect(12f, 0f, 36f, 11f), deco, "prop_wall_panel", 0.6f);
            LevelDecor.WallPanel(new Rect(0f, 0f, 12f, 8f), deco, "prop_lattice", 0.5f);
            VfxManager.CreateAmbientEmbers(new Rect(0f, -2f, 50f, 14f), Palette.Red, 12f, deco);
            VfxManager.CreateMist(new Rect(0f, -1.5f, 50f, 5f), new Color(1f, 0.85f, 0.85f), 16, deco);

            return info;
        }
    }
}
