using UnityEngine;

namespace Prototypes.TrickGrammar
{
    /// <summary>
    /// PROTOTYPE. One tick of player intent.
    ///
    /// Shaped like a FishNet replicate struct on purpose (plain struct, public fields,
    /// default means "no input") so the feel tuned here survives the port. It does not
    /// implement IReplicateData yet — that arrives with the seams ticket.
    /// </summary>
    public struct BoardInput
    {
        /// <summary>Steer, -1 left to 1 right. Grounded only.</summary>
        public float Steer;

        /// <summary>Push/brake, -1 to 1. Grounded only.</summary>
        public float Throttle;

        /// <summary>Ollie is charged while held, pops on release.</summary>
        public bool PopHeld;

        /// <summary>True on the tick pop is released.</summary>
        public bool PopReleased;

        /// <summary>Airborne trick stick, scheme-dependent. Right stick.</summary>
        public Vector2 TrickStick;

        /// <summary>Airborne body spin, scheme-dependent. Left stick.</summary>
        public Vector2 SpinStick;

        public bool IsNeutral =>
            Steer == 0f && Throttle == 0f && !PopHeld && !PopReleased
            && TrickStick == Vector2.zero && SpinStick == Vector2.zero;
    }

    /// <summary>
    /// PROTOTYPE. The competing answers to "how deep does the trick grammar go".
    /// Switch live with 1 / 2 so they can be played back to back.
    /// </summary>
    public enum TrickScheme
    {
        /// <summary>
        /// Rotation is purely a CONSEQUENCE of physics: lean at takeoff tilts the pop
        /// impulse, and whatever the board does is what you get. Costs zero inputs.
        /// The research recommended this; the risk is that it plays like a slot machine.
        /// </summary>
        Consequence = 0,

        /// <summary>
        /// FLICK. Right stick at pop imparts rotation — X flips (kickflip/heelflip),
        /// Y shuvits the board under the rider. Left stick in the air spins the whole
        /// body (180/360). Costs the right stick, but you can aim for a named trick.
        /// </summary>
        Flick = 1,
    }
}
