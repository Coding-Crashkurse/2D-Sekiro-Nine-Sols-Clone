using UnityEngine;

namespace AshenSol.Core
{
    /// <summary>Owns Time.timeScale: pause, hit-stop (from HitStop.Remaining) and eased slow-motion.</summary>
    [DefaultExecutionOrder(-1400)]
    public class TimeController : MonoBehaviour
    {
        public static TimeController Instance { get; private set; }
        public bool IsPaused { get; private set; }
        public float CurrentScale { get; private set; } = 1f;

        const float BaseFixedDelta = 1f / 60f;
        const float HitStopScale = 0.02f;

        float slowScale = 1f, slowRemaining, slowTotal;

        void Awake() { Instance = this; }

        public void SlowMo(float scale, float seconds)
        {
            slowScale = Mathf.Clamp(scale, 0.02f, 1f);
            slowRemaining = slowTotal = Mathf.Max(0.01f, seconds);
        }

        public void SetPaused(bool paused)
        {
            IsPaused = paused;
            Apply();
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (HitStop.Remaining > 0f) HitStop.Remaining -= dt;
            if (slowRemaining > 0f) slowRemaining -= dt;
            Apply();
        }

        void Apply()
        {
            float s = 1f;
            if (IsPaused) s = 0f;
            else
            {
                if (slowRemaining > 0f)
                {
                    float t = 1f - slowRemaining / slowTotal;
                    s = Mathf.Lerp(slowScale, 1f, Ease.InCubic(t));
                }
                if (HitStop.Remaining > 0f) s = Mathf.Min(s, HitStopScale);
            }
            CurrentScale = s;
            Time.timeScale = s;
            Time.fixedDeltaTime = Mathf.Max(0.0005f, BaseFixedDelta * Mathf.Max(s, 0.02f));
        }
    }
}
