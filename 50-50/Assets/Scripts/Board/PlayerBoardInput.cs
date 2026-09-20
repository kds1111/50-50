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
        [Tooltip("Gamepad button that carries the ball. Tap to grab, tap again to let go — see " +
                 "GrabIsToggle.")]
        public GamepadButton GrabButton = GamepadButton.RightShoulder;

        [Tooltip("Tap to grab and tap again to release, rather than holding the button down the " +
                 "whole time you are carrying.\n\n" +
                 "The simulation never learns the difference: GrabHeld still means 'wants to be " +
                 "carrying', so the hold limit, the fumble and the grab cooldown are untouched " +
                 "(#7). Off restores the held grab.")]
        public bool GrabIsToggle = true;

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

        [Tooltip("Reverses the direction you drive. Right stick click by default.\n\n" +
                 "Simulation input, not a camera control: it moves the heading, and the camera " +
                 "follows the heading, so the view comes with it for free. The board itself does " +
                 "not rotate. Ground only.")]
        public GamepadButton HeadingFlipButton = GamepadButton.RightStick;

        [Tooltip("Keyboard equivalent of the heading flip.")]
        public Key HeadingFlipKey = Key.C;

        [Header("Tricks (#6 — bindings still open)")]
        [Tooltip("Held (or just pressed) alongside a trick button. Defaults to A, the ollie.\n\n" +
                 "Reusing A is safe rather than clever: the ollie already ignores a press made " +
                 "in the air, so holding A and tapping a trick button mid-air cannot re-pop. " +
                 "Pressing them together on the ground gives ollie-and-flip as one motion, " +
                 "which is what a kickflip physically is. Both readings work with one binding.")]
        public GamepadButton TrickModifier = GamepadButton.South;

        [Tooltip("Trick buttons, in table order. Slot 1 is the first entry on BoardTrickController.")]
        public GamepadButton[] TrickButtons =
        {
            GamepadButton.East,
            GamepadButton.West,
            GamepadButton.North,
        };

        [Tooltip("Keyboard equivalent of the trick modifier.")]
        public Key TrickModifierKey = Key.Space;

        [Tooltip("Keyboard equivalents of the trick buttons, in the same order.")]
        public Key[] TrickKeys = { Key.J, Key.K, Key.L };

        [Tooltip("Seconds a trick press stays valid after the modifier was pressed. Two buttons " +
                 "are never pressed on the same frame, so a chord needs a window or it is a " +
                 "dexterity test rather than an input.")]
        public float ChordGraceSeconds = 0.2f;

        [Header("Device (split-screen, #24)")]
        [Tooltip("0 for player one, 1 for player two. MatchDirector sets this; a lone board leaves it at 0.")]
        public int PlayerIndex;

        [Tooltip("Players sharing this machine. With one, this board reads the keyboard and whichever " +
                 "pad was touched last. With more, pads are dealt out in connection order and the " +
                 "keyboard stays with player one.")]
        public int PlayersOnThisMachine = 1;

        [Tooltip("Force a particular pad, by connection order from 0. -1 deals them out automatically.")]
        public int PadOverride = -1;

        [Tooltip("Whether this board reads the keyboard at all. Only player one's does in split-screen.")]
        public bool UseKeyboard = true;

        [Header("Read-only (for debugging in play mode)")]
        [SerializeField] private Vector2 _debugLeftStick;
        [SerializeField] private float _debugThrottle;
        [SerializeField] private bool _debugPopLatched;

        // One-shot latches. Set in Update (which can run many times per fixed step, or none),
        // consumed in Read. Without them, a quick tap between fixed steps is silently lost.
        private bool _popLatched;
        private bool _punchLatched;
        private int _trickSlotLatched;
        private float _modifierHeldUntil;
        private bool _grabToggled;
        private bool _headingFlipLatched;

        private void Update()
        {
            Gamepad pad = ResolvePad();
            Keyboard keys = UseKeyboard ? Keyboard.current : null;

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

            PollTrickChord(pad, keys);

            bool grabDown = (pad != null && pad[GrabButton].wasPressedThisFrame)
                            || (keys != null && keys[GrabKey].wasPressedThisFrame);

            if (grabDown)
            {
                _grabToggled = !_grabToggled;
            }

            if ((pad != null && pad[HeadingFlipButton].wasPressedThisFrame)
                || (keys != null && keys[HeadingFlipKey].wasPressedThisFrame))
            {
                _headingFlipLatched = true;
            }

            _debugPopLatched = _popLatched;
        }

        /// <summary>
        /// A held modifier plus a direction button, rather than a raw chord. The modifier opens
        /// a short window (<see cref="ChordGraceSeconds"/>) so pressing "A+B" does not require
        /// hitting both on one frame, which no player does.
        ///
        /// The latched slot is consumed by Read and cleared there, so one press produces exactly
        /// one trick request however the frame rate and the fixed step line up.
        /// </summary>
        private void PollTrickChord(Gamepad pad, Keyboard keys)
        {
            bool modifierDown = (pad != null && pad[TrickModifier].wasPressedThisFrame)
                                || (keys != null && keys[TrickModifierKey].wasPressedThisFrame);

            bool modifierHeld = (pad != null && pad[TrickModifier].isPressed)
                                || (keys != null && keys[TrickModifierKey].isPressed);

            if (modifierDown)
            {
                _modifierHeldUntil = Time.unscaledTime + ChordGraceSeconds;
            }

            bool windowOpen = modifierHeld || Time.unscaledTime <= _modifierHeldUntil;

            if (!windowOpen)
            {
                return;
            }

            int count = TrickButtons != null ? TrickButtons.Length : 0;

            for (int i = 0; i < count; i++)
            {
                bool padPressed = pad != null && pad[TrickButtons[i]].wasPressedThisFrame;
                bool keyPressed = keys != null
                                  && TrickKeys != null
                                  && i < TrickKeys.Length
                                  && keys[TrickKeys[i]].wasPressedThisFrame;

                if (padPressed || keyPressed)
                {
                    _trickSlotLatched = i + 1;
                    return;
                }
            }
        }

        public override BoardInputState Read()
        {
            Gamepad pad = ResolvePad();
            Keyboard keys = UseKeyboard ? Keyboard.current : null;

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

            bool grabHeld = GrabIsToggle
                ? _grabToggled
                : (pad != null && pad[GrabButton].isPressed)
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
                TrickSlot = _trickSlotLatched,
                HeadingFlipPressed = _headingFlipLatched,
                CameraLook = right,
            };

            _popLatched = false;
            _punchLatched = false;
            _trickSlotLatched = 0;
            _headingFlipLatched = false;

            return state;
        }

        /// <summary>
        /// Force the grab toggle off.
        ///
        /// Needed because a toggle can go out of sync with reality in a way a held button never
        /// can: fumble the ball on the hold limit, get stripped, or bail while carrying, and the
        /// toggle still says "carrying" while your hands are empty. The player would then have
        /// to tap once to clear a state they can no longer see before they could grab again,
        /// which reads as a dropped input.
        /// </summary>
        public void ClearGrabToggle()
        {
            _grabToggled = false;
        }

        /// <summary>
        /// Camera stick, read without consuming the one-shot latches. The camera runs in
        /// LateUpdate and must never eat a push or an ollie by calling Read.
        /// </summary>
        public Vector2 PeekCameraLook()
        {
            Gamepad pad = ResolvePad();
            Keyboard keys = UseKeyboard ? Keyboard.current : null;

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

        /// <summary>This player's pad, dealt by PadDealing. Resolved on every read, so a pad
        /// plugged in or pulled out mid-session is picked up without a restart.</summary>
        private Gamepad ResolvePad()
        {
            int index = PadDealing.PadFor(PlayerIndex, PlayersOnThisMachine, Gamepad.all.Count, PadOverride);

            return index switch
            {
                PadDealing.AnyPad => Gamepad.current,
                PadDealing.NoPad => null,
                _ => Gamepad.all[index],
            };
        }

        private Vector2 ApplyDeadzone(Vector2 v)
        {
            return v.magnitude < StickDeadzone ? Vector2.zero : v;
        }
    }
}
