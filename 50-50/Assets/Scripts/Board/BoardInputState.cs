using UnityEngine;

namespace FiftyFifty.Board
{
    /// <summary>
    /// One tick of intent for a board. Everything that can drive a board — a human, and
    /// later the bot — produces this and nothing else, so the controller never knows or
    /// cares who is steering.
    ///
    /// Shaped like a network input struct on purpose (plain struct, public fields, default
    /// means "no input"), so it can become a FishNet replicate struct later without the
    /// controller changing.
    /// </summary>
    public struct BoardInputState
    {
        /// <summary>Steer left/right on the ground, -1 to 1.</summary>
        public float Steer;

        /// <summary>Airborne stick. X spins the board (yaw). Y is unused for now.</summary>
        public Vector2 Attitude;

        /// <summary>Acceleration, 0 to 1. Analog: right trigger.</summary>
        public float Throttle;

        /// <summary>Braking, 0 to 1. Analog: left trigger.</summary>
        public float Brake;

        /// <summary>True on the tick the ollie is requested.</summary>
        public bool PopPressed;

        /// <summary>Held, not latched: the grab is a state you hold, not an action you fire (#7).</summary>
        public bool GrabHeld;

        /// <summary>True on the tick a punch is requested. Punch is the shot (#7).</summary>
        public bool PunchPressed;

        /// <summary>Held: break traction for a quick turn (#18). Held, not latched — a drift is
        /// a state you hold, and letting go is how you hook back up.</summary>
        public bool PowerslideHeld;

        /// <summary>
        /// Named trick requested this tick, 1-based into the trick table. 0 is "nothing asked
        /// for", so a default struct means no trick — which is what a network input struct has
        /// to mean (#3).
        ///
        /// A slot rather than a button because the bindings are still open on #6. Whatever the
        /// chord turns out to be, it resolves to a slot here and the simulation never learns
        /// what a gamepad is.
        /// </summary>
        public int TrickSlot;

        /// <summary>Camera orbit, right stick. Presentation only, not simulation state.</summary>
        public Vector2 CameraLook;
    }

    /// <summary>
    /// Anything that can drive a board. The controller reads from one of these, so swapping
    /// a human for a bot is a component swap and nothing else.
    /// </summary>
    public abstract class BoardInputSource : MonoBehaviour
    {
        /// <summary>
        /// Current intent. Called once per fixed step. Implementations must clear their own
        /// one-shot latches (push, pop) when read, so one button press produces exactly one
        /// action however the frame rate and the fixed step line up.
        /// </summary>
        public abstract BoardInputState Read();
    }
}
