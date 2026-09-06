using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AshenSol.Core
{
    /// <summary>Physics / application setup, error counting, shared coroutine runner.</summary>
    [DefaultExecutionOrder(-1500)]
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }
        public static int ExceptionCount { get; private set; }
        public static readonly List<string> RecentErrors = new List<string>();

        void Awake()
        {
            Instance = this;
            Application.targetFrameRate = 144;
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 1;

            Physics2D.gravity = new Vector2(0f, -30f);
            Physics2D.queriesHitTriggers = true;
            Physics2D.queriesStartInColliders = true;
            Time.fixedDeltaTime = 1f / 60f;

            // Collision matrix (affects triggers too, so keep Player<->Projectile / Enemy<->Projectile enabled).
            Ignore(Layers.Player, Layers.Enemy);
            Ignore(Layers.Enemy, Layers.Enemy);
            Ignore(Layers.Projectile, Layers.Projectile);
            Ignore(Layers.Projectile, Layers.Hazard);
            Ignore(Layers.Projectile, Layers.Trigger);
            Ignore(Layers.Projectile, Layers.OneWay);
            Ignore(Layers.Enemy, Layers.Hazard);
            Ignore(Layers.Enemy, Layers.Trigger);
            Ignore(Layers.Hazard, Layers.Trigger);
            Ignore(Layers.Hazard, Layers.Hazard);
            Ignore(Layers.Trigger, Layers.Trigger);
            Ignore(Layers.Trigger, Layers.Ground);
            Ignore(Layers.Hazard, Layers.Ground);

            Application.logMessageReceived += OnLog;
        }

        static void Ignore(int a, int b) { Physics2D.IgnoreLayerCollision(a, b, true); }

        void OnDestroy() { Application.logMessageReceived -= OnLog; }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error)
            {
                ExceptionCount++;
                if (RecentErrors.Count < 50) RecentErrors.Add(condition);
            }
        }

        public static Coroutine Run(IEnumerator routine)
        {
            return Instance != null ? Instance.StartCoroutine(routine) : null;
        }

        public static void Stop(Coroutine c)
        {
            if (Instance != null && c != null) Instance.StopCoroutine(c);
        }
    }
}
