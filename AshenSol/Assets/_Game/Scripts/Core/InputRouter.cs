using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace AshenSol.Core
{
    /// <summary>Ticks the active IInputProvider first thing every frame.</summary>
    [DefaultExecutionOrder(-1000)]
    public class InputRouter : MonoBehaviour
    {
        public static InputRouter Instance { get; private set; }
        public KeyboardGamepadInput Default { get; private set; }

        void Awake()
        {
            Instance = this;
            Default = new KeyboardGamepadInput();
            Services.Input = Default;
        }

        void Update()
        {
            Services.Input?.Tick();
        }

        public void SetProvider(IInputProvider provider)
        {
            Services.Input = provider ?? Default;
        }
    }

    /// <summary>Keyboard + mouse + gamepad via the Input System (no action maps needed).</summary>
    public class KeyboardGamepadInput : IInputProvider
    {
        public float Horizontal { get; private set; }
        public float Vertical { get; private set; }
        public bool JumpPressed { get; private set; }
        public bool JumpHeld { get; private set; }
        public bool AttackPressed { get; private set; }
        public bool ParryPressed { get; private set; }
        public bool ParryHeld { get; private set; }
        public bool DashPressed { get; private set; }
        public bool QiBlastPressed { get; private set; }
        public bool HealPressed { get; private set; }
        public bool PausePressed { get; private set; }
        public bool ConfirmPressed { get; private set; }
        public bool QuitPressed { get; private set; }
        public bool AnyPressed { get; private set; }

        static bool Down(ButtonControl b) { return b != null && b.wasPressedThisFrame; }
        static bool Held(ButtonControl b) { return b != null && b.isPressed; }

        public void Tick()
        {
            var k = Keyboard.current;
            var g = Gamepad.current;
            var m = Mouse.current;

            float h = 0f, v = 0f;
            if (k != null)
            {
                if (k.aKey.isPressed || k.leftArrowKey.isPressed) h -= 1f;
                if (k.dKey.isPressed || k.rightArrowKey.isPressed) h += 1f;
                if (k.wKey.isPressed || k.upArrowKey.isPressed) v += 1f;
                if (k.sKey.isPressed || k.downArrowKey.isPressed) v -= 1f;
            }
            if (g != null)
            {
                Vector2 ls = g.leftStick.ReadValue();
                Vector2 dp = g.dpad.ReadValue();
                float gh = Mathf.Abs(ls.x) > 0.3f ? ls.x : dp.x;
                float gv = Mathf.Abs(ls.y) > 0.3f ? ls.y : dp.y;
                if (Mathf.Abs(gh) > 0.3f) h = gh;
                if (Mathf.Abs(gv) > 0.3f) v = gv;
            }
            Horizontal = Mathf.Clamp(h, -1f, 1f);
            Vertical = Mathf.Clamp(v, -1f, 1f);

            JumpPressed = Down(k?.spaceKey) || Down(k?.wKey) || Down(k?.upArrowKey) || Down(g?.buttonSouth);
            JumpHeld = Held(k?.spaceKey) || Held(k?.wKey) || Held(k?.upArrowKey) || Held(g?.buttonSouth);
            AttackPressed = Down(k?.jKey) || Down(m?.leftButton) || Down(g?.buttonWest);
            ParryPressed = Down(k?.kKey) || Down(m?.rightButton) || Down(g?.buttonEast);
            ParryHeld = Held(k?.kKey) || Held(m?.rightButton) || Held(g?.buttonEast);
            DashPressed = Down(k?.lKey) || Down(k?.leftShiftKey) || Down(g?.rightShoulder) || Down(g?.rightTrigger);
            QiBlastPressed = Down(k?.iKey) || Down(g?.buttonNorth);
            HealPressed = Down(k?.hKey) || Down(g?.leftShoulder);
            PausePressed = Down(k?.escapeKey) || Down(g?.startButton);
            ConfirmPressed = Down(k?.enterKey) || Down(k?.numpadEnterKey) || Down(k?.spaceKey) || Down(g?.buttonSouth) || Down(g?.startButton);
            QuitPressed = Down(k?.qKey) || Down(g?.buttonNorth);
            AnyPressed = (k != null && k.anyKey.wasPressedThisFrame)
                         || (m != null && (m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame))
                         || (g != null && (g.buttonSouth.wasPressedThisFrame || g.buttonEast.wasPressedThisFrame || g.buttonWest.wasPressedThisFrame || g.buttonNorth.wasPressedThisFrame || g.startButton.wasPressedThisFrame));
        }
    }

    /// <summary>Programmatic input (AutoPilot / tests). Call the Press* methods during Update; they become
    /// "pressed" on the next Tick (start of the following frame) for exactly one frame.</summary>
    public class ScriptedInput : IInputProvider
    {
        public float Horizontal { get; set; }
        public float Vertical { get; set; }
        public bool JumpHeld { get; set; }
        public bool ParryHeld { get; set; }
        public bool JumpPressed { get; private set; }
        public bool AttackPressed { get; private set; }
        public bool ParryPressed { get; private set; }
        public bool DashPressed { get; private set; }
        public bool QiBlastPressed { get; private set; }
        public bool HealPressed { get; private set; }
        public bool PausePressed { get; private set; }
        public bool ConfirmPressed { get; private set; }
        public bool QuitPressed { get { return false; } }
        public bool AnyPressed { get; private set; }

        bool qJump, qAttack, qParry, qDash, qQi, qHeal, qPause, qConfirm;

        public void Jump() { qJump = true; }
        public void Attack() { qAttack = true; }
        public void Parry() { qParry = true; }
        public void Dash() { qDash = true; }
        public void QiBlast() { qQi = true; }
        public void Heal() { qHeal = true; }
        public void Pause() { qPause = true; }
        public void Confirm() { qConfirm = true; }

        public void Tick()
        {
            JumpPressed = qJump; AttackPressed = qAttack; ParryPressed = qParry; DashPressed = qDash;
            QiBlastPressed = qQi; HealPressed = qHeal; PausePressed = qPause; ConfirmPressed = qConfirm;
            AnyPressed = qJump || qAttack || qParry || qDash || qQi || qHeal || qConfirm;
            qJump = qAttack = qParry = qDash = qQi = qHeal = qPause = qConfirm = false;
        }
    }
}
