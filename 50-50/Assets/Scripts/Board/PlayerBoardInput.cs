using UnityEngine;
using UnityEngine.InputSystem;

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
    ///   Bumpers       — roll the board in the air (flips)
    ///   A / Space     — ollie
    /// </summary>
    public class PlayerBoardInput : BoardInputSource
    {
        [Header("Deadzones")]
        [Tooltip("Stick movement below this is treated as zero.")]
        [Range(0f, 0.5f)] public float StickDeadzone = 0.15f;

        [Header("Inversion")]
        public bool InvertAttitudePitch = false;

        [Header("Read-only (for debugging in play mode)")]
        [SerializeField] private Vector2 _debugLeftStick;
        [SerializeField] private float _debugThrottle;
        [SerializeField] private bool _debugPopLatched;

        // One-shot latch. Set in Update (which can run many times per fixed step, or none),
        // consumed in Read. Without it, a quick tap between fixed steps is silently lost.
        private bool _popLatched;

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
            float roll = 0f;

            if (pad != null)
            {
                left = pad.leftStick.ReadValue();
                right = pad.rightStick.ReadValue();
                throttle = pad.rightTrigger.ReadValue();
                brake = pad.leftTrigger.ReadValue();

                if (pad.leftShoulder.isPressed) roll -= 1f;
                if (pad.rightShoulder.isPressed) roll += 1f;
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

                if (keys.qKey.isPressed) roll -= 1f;
                if (keys.eKey.isPressed) roll += 1f;
            }

            left = ApplyDeadzone(Vector2.ClampMagnitude(left, 1f));
            right = ApplyDeadzone(Vector2.ClampMagnitude(right, 1f));
            _debugLeftStick = left;
            _debugThrottle = throttle;

            var state = new BoardInputState
            {
                Steer = left.x,
                Attitude = new Vector2(left.x, InvertAttitudePitch ? -left.y : left.y),
                AirRoll = Mathf.Clamp(roll, -1f, 1f),
                Throttle = Mathf.Clamp01(throttle),
                Brake = Mathf.Clamp01(brake),
                PopPressed = _popLatched,
                CameraLook = right,
            };

            _popLatched = false;

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
