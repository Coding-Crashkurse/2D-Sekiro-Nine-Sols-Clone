using UnityEngine;

namespace AshenSol.Core
{
    /// <summary>The only object in the scene. Creates every manager in a fixed order on a persistent "Game" root.</summary>
    [DefaultExecutionOrder(-2000)]
    public class GameBootstrap : MonoBehaviour
    {
        public static GameObject Root { get; private set; }

        void Awake()
        {
            if (Root != null) { Destroy(gameObject); return; }
            Root = new GameObject("Game");
            DontDestroyOnLoad(Root);

            Root.AddComponent<GameManager>();
            Root.AddComponent<TimeController>();
            Root.AddComponent<InputRouter>();
            Root.AddComponent<AshenSol.Audio.AudioManager>();
            Root.AddComponent<CameraController>();
            Root.AddComponent<AshenSol.VFX.VfxManager>();
            Root.AddComponent<AshenSol.UI.UiManager>();
            Root.AddComponent<GameFlow>();
            if (CmdArgs.Has("-autopilot")) Root.AddComponent<AutoPilot>();
        }
    }
}
