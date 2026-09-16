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
    ///   Left stick  — steer on the ground, board attitude in the air
    ///   Right stick — camera only
    ///   A / Space   — push (kick)
    ///   X / LShift  — ollie
    ///   B / S       — brake
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
        [SerializeField] private bool _debugPushLatched;
        [SerializeField] private bool _debugPopLatched;

        // One-shot latches. Set in Update (which can run many times per fixed step, or none),
        // consumed in Read. Without these, a quick tap between fixed steps is silently lost.
        private bool _pushLatched;
        private bool _popLatched;

        private void Update()
        {
            Gamepad pad = Gamepad.current;
            Keyboard keys = Keyboard.current;

            if (pad != null)
            {
                if (pad.buttonSouth.wasPressedThisFrame) _pushLatched = true;
                if (pad.buttonWest.wasPressedThisFrame) _popLatched = true;
            }

            if (keys != null)
            {
                if (keys.spaceKey.wasPressedThisFrame) _pushLatched = true;
                if (keys.leftShiftKey.wasPressedThisFrame) _popLatched = true;
            }

            _debugPushLatched = _pushLatched;
            _debugPopLatched = _popLatched;
        }

        public override BoardInputState Read()
        {
            Gamepad pad = Gamepad.current;
            Keyboard keys = Keyboard.current;

            Vector2 left = Vector2.zero;
            Vector2 right = Vector2.zero;
            bool brake = false;

            if (pad != null)
            {
                left = pad.leftStick.ReadValue();
                right = pad.rightStick.ReadValue();
                brake = pad.buttonEast.isPressed;
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

                if (keys.sKey.isPressed) brake = true;
            }

            left = ApplyDeadzone(Vector2.ClampMagnitude(left, 1f));
            right = ApplyDeadzone(Vector2.ClampMagnitude(right, 1f));
            _debugLeftStick = left;

            var state = new BoardInputState
            {
                Steer = left.x,
                Attitude = new Vector2(left.x, InvertAttitudePitch ? -left.y : left.y),
                PushPressed = _pushLatched,
                PopPressed = _popLatched,
                BrakeHeld = brake,
                CameraLook = right,
            };

            _pushLatched = false;
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
