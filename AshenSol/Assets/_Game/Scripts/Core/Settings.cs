using UnityEngine;

namespace AshenSol.Core
{
    public enum DifficultyLevel { Disciple = 0, SolSlayer = 1 }

    /// <summary>Player-facing options (title menu) plus the difficulty multipliers derived from them.
    /// Persisted with PlayerPrefs.</summary>
    public static class Settings
    {
        public static DifficultyLevel Difficulty = DifficultyLevel.Disciple;
        public static float MusicVolume = 0.7f;
        public static float SfxVolume = 0.9f;
        public static bool ScreenShake = true;

        static bool loaded;

        public static void Load()
        {
            if (loaded) return;
            loaded = true;
            Difficulty = (DifficultyLevel)Mathf.Clamp(PlayerPrefs.GetInt("difficulty", 0), 0, 1);
            MusicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("musicVolume", 0.7f));
            SfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("sfxVolume", 0.9f));
            ScreenShake = PlayerPrefs.GetInt("screenShake", 1) != 0;
        }

        public static void Save()
        {
            PlayerPrefs.SetInt("difficulty", (int)Difficulty);
            PlayerPrefs.SetFloat("musicVolume", MusicVolume);
            PlayerPrefs.SetFloat("sfxVolume", SfxVolume);
            PlayerPrefs.SetInt("screenShake", ScreenShake ? 1 : 0);
            PlayerPrefs.Save();
        }

        public static string DifficultyName
        {
            get { return Difficulty == DifficultyLevel.Disciple ? "DISCIPLE" : "SOL SLAYER"; }
        }

        public static string DifficultyBlurb
        {
            get
            {
                return Difficulty == DifficultyLevel.Disciple
                    ? "Wider parry window, softer blows, longer telegraphs."
                    : "Tight parry window, heavy blows, faster telegraphs.";
            }
        }

        // ---- derived gameplay multipliers ----
        /// <summary>Damage the player takes, scaled.</summary>
        public static float DamageTakenMul { get { return Difficulty == DifficultyLevel.Disciple ? 0.8f : 1.3f; } }
        /// <summary>Length of the perfect-parry window in seconds.</summary>
        public static float ParryPerfectWindow { get { return Difficulty == DifficultyLevel.Disciple ? 0.22f : 0.14f; } }
        /// <summary>Enemy/boss telegraph duration multiplier (longer = easier to read).</summary>
        public static float TelegraphMul { get { return Difficulty == DifficultyLevel.Disciple ? 1.12f : 0.9f; } }
        public static int HealAmount { get { return Difficulty == DifficultyLevel.Disciple ? 40 : 30; } }
        public static float ShakeMul { get { return ScreenShake ? 1f : 0f; } }
    }
}
