using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace FiftyFifty.Board
{
    /// <summary>
    /// Reads a gamepad and keyboard directly and turns them into <see cref="BoardInputState"/>.
    ///
    /// Reads the devices directly rather than going through an Input Actions asset: one less
    /// thing to wire while the control scheme is still moving. Swap it for an actions asset
    /// once the bindings settle.
    ///
    /// Control map (settled on issue #16):
    ///   Left stick    — steer on the ground, board attitude in the air
    ///   Right stick   — camera only
    ///   Right trigger — accelerate (analog)
    ///   Left trigger  — brake
    ///   A / Space     — ollie
    ///   RB / E        — grab the ball, held (#7)
    ///   LB / F        — punch: the shot, and what strips a carrier (#7)
    ///   L3 / Shift     — powerslide, held, with the left stick for direction (#18)
    ///
    /// Buttons are Inspector fields rather than an Input Actions asset: the mapping is still
    /// moving, and a rebinding menu is its own ticket. Swap the whole class for an actions
    /// asset when it settles — the bindings are the only thing that has to move.
    /// </summary>
    public class PlayerBoardInput : BoardInputSource
    {
        [Header("Deadzones")]
        [Tooltip("Stick movement below this is treated as zero.")]
        [Range(0f, 0.5f)] public float StickDeadzone = 0.15f;

        [Header("Inversion")]
        public bool InvertAttitudePitch = false;

        [Header("Bindings — ball")]
        [Tooltip("Gamepad button held to carry the ball.")]
        public GamepadButton GrabButton = GamepadButton.RightShoulder;

        [Tooltip("Gamepad button that punches. Punch is the shot.")]
        public GamepadButton PunchButton = GamepadButton.LeftShoulder;

        [Tooltip("Keyboard equivalent of the grab button.")]
        public Key GrabKey = Key.E;

        [Tooltip("Keyboard equivalent of the punch button.")]
        public Key PunchKey = Key.F;

        [Header("Bindings — movement")]
        [Tooltip("Gamepad button held to powerslide. Left stick click by default, so the hand " +
                 "steering is the hand drifting.")]
        public GamepadButton PowerslideButton = GamepadButton.LeftStick;

        [Tooltip("Keyboard equivalent of the powerslide button.")]
        public Key PowerslideKey = Key.LeftShift;

        [Header("Read-only (for debugging in play mode)")]
        [SerializeField] private Vector2 _debugLeftStick;
        [SerializeField] private float _debugThrottle;
        [SerializeField] private bool _debugPopLatched;

        // One-shot latches. Set in Update (which can run many times per fixed step, or none),
        // consumed in Read. Without them, a quick tap between fixed steps is silently lost.
        private bool _popLatched;
        private bool _punchLatched;

        private void Update()
        {
            Gamepad pad = Gamepad.current;
            Keyboard keys = Keyboard.current;

            if (pad != null && pad.buttonSouth.wasPressedThisFrame)
            {
                _popLatched = true;
            }

            if (keys != null && keys.spaceKey.wasPressedThisFrame)
            {
                _popLatched = true;
            }

            if (pad != null && pad[PunchButton].wasPressedThisFrame)
            {
                _punchLatched = true;
            }

            if (keys != null && keys[PunchKey].wasPressedThisFrame)
            {
                _punchLatched = true;
            }

            _debugPopLatched = _popLatched;
        }

        public override BoardInputState Read()
        {
            Gamepad pad = Gamepad.current;
            Keyboard keys = Keyboard.current;

            Vector2 left = Vector2.zero;
            Vector2 right = Vector2.zero;
            float throttle = 0f;
            float brake = 0f;

            if (pad != null)
            {
                left = pad.leftStick.ReadValue();
                right = pad.rightStick.ReadValue();
                throttle = pad.rightTrigger.ReadValue();
                brake = pad.leftTrigger.ReadValue();
            }

            if (keys != null)
            {
                if (keys.aKey.isPressed) left.x -= 1f;
                if (keys.dKey.isPressed) left.x += 1f;
                if (keys.wKey.isPressed) left.y += 1f;
                if (keys.sKey.isPressed) left.y -= 1f;

                if (keys.leftArrowKey.isPressed) right.x -= 1f;
                if (keys.rightArrowKey.isPressed) right.x += 1f;
                if (keys.upArrowKey.isPressed) right.y += 1f;
                if (keys.downArrowKey.isPressed) right.y -= 1f;

                // Keyboard fallback: W accelerates, S brakes. These double as air pitch,
                // which is fine because ground and air are exclusive states.
                if (keys.wKey.isPressed) throttle = 1f;
                if (keys.sKey.isPressed) brake = 1f;
            }

            bool grabHeld = (pad != null && pad[GrabButton].isPressed)
                            || (keys != null && keys[GrabKey].isPressed);

            bool powerslideHeld = (pad != null && pad[PowerslideButton].isPressed)
                                  || (keys != null && keys[PowerslideKey].isPressed);

            left = ApplyDeadzone(Vector2.ClampMagnitude(left, 1f));
            right = ApplyDeadzone(Vector2.ClampMagnitude(right, 1f));
            _debugLeftStick = left;
            _debugThrottle = throttle;

            var state = new BoardInputState
            {
                Steer = left.x,
                Attitude = new Vector2(left.x, InvertAttitudePitch ? -left.y : left.y),
                Throttle = Mathf.Clamp01(throttle),
                Brake = Mathf.Clamp01(brake),
                PopPressed = _popLatched,
                GrabHeld = grabHeld,
                PunchPressed = _punchLatched,
                PowerslideHeld = powerslideHeld,
                CameraLook = right,
            };

            _popLatched = false;
            _punchLatched = false;

            return state;
        }

        /// <summary>
        /// Camera stick, read without consuming the one-shot latches. The camera runs in
        /// LateUpdate and must never eat a push or an ollie by calling Read.
        /// </summary>
        public Vector2 PeekCameraLook()
        {
            Gamepad pad = Gamepad.current;
            Keyboard keys = Keyboard.current;

            Vector2 look = pad != null ? pad.rightStick.ReadValue() : Vector2.zero;

            if (keys != null)
            {
                if (keys.leftArrowKey.isPressed) look.x -= 1f;
                if (keys.rightArrowKey.isPressed) look.x += 1f;
                if (keys.upArrowKey.isPressed) look.y += 1f;
                if (keys.downArrowKey.isPressed) look.y -= 1f;
            }

            return ApplyDeadzone(Vector2.ClampMagnitude(look, 1f));
        }

        private Vector2 ApplyDeadzone(Vector2 v)
        {
            return v.magnitude < StickDeadzone ? Vector2.zero : v;
        }
    }
}
