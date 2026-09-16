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
