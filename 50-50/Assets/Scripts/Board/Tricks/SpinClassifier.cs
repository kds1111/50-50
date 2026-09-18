using System;

namespace FiftyFifty.Board.Tricks
{
    /// <summary>
    /// Names a spin from accumulated yaw, on landing.
    ///
    /// This is the half of #4's rotation-classification model that survived #6's grill. It kept
    /// the axis it is good at — yaw, which is already commanded input rather than a physics
    /// accident — and dropped the flip-axis taxonomy, where stance signs are what shipped broken
    /// twice in `skate.`.
    ///
    /// Two rules carried over from #4 unchanged:
    ///   - A rotation that matches no name is credited as a generic "air", never as a guessed
    ///     name. Being wrong about a trick's name is worse than declining to name it.
    ///   - Naming happens on landing, not during. Nothing is named in the air.
    ///
    /// Spins are never punished and can never bail (#6 rules 12 and 14). This class therefore
    /// has no failure case at all: it returns a name or it returns "air".
    ///
    /// Plain C#, no Unity types, so it can be tested headlessly.
    /// </summary>
    public static class SpinClassifier
    {
        public const string GenericAir = "Air";

        /// <summary>
        /// Quantise to the nearest half-turn and name it, or return "air".
        /// </summary>
        /// <param name="turns">Signed accumulated yaw, in whole turns.</param>
        /// <param name="tolerance">
        /// How far from a clean half-turn still counts as that half-turn. Generous on purpose:
        /// airtime is short (~0.5-1.2s per #5), so a player rarely hits an exact multiple, and
        /// #4's finding was that landings here are routinely disturbed by things the player did
        /// not do. Too tight and everything reads as a generic air.
        /// </param>
        public static string Classify(float turns, float tolerance = 0.2f)
        {
            float magnitude = Math.Abs(turns);

            // Below half a turn there is nothing to name. An ollie is not a trick (#6 rule 12).
            if (magnitude < 0.5f - tolerance)
            {
                return GenericAir;
            }

            float halves = (float)Math.Round(magnitude * 2f);
            float quantised = halves * 0.5f;

            if (Math.Abs(magnitude - quantised) > tolerance)
            {
                return GenericAir;
            }

            int degrees = (int)Math.Round(quantised * 360f);
            return degrees.ToString();
        }

        /// <summary>
        /// The multiplier a spin contributes. A named trick supplies the name, the spin supplies
        /// this (#6 rule 2) — THPS's model, where rotation is a parallel channel rather than part
        /// of the trick's identity.
        ///
        /// The curve is a PLACEHOLDER. How a spin scales, whether it caps, and what it is worth
        /// relative to a named trick are all balance and belong to #8.
        /// </summary>
        public static float Multiplier(float turns, float perHalfTurn = 0.5f, float tolerance = 0.2f)
        {
            float magnitude = Math.Abs(turns);

            if (magnitude < 0.5f - tolerance)
            {
                return 1f;
            }

            float halves = (float)Math.Round(magnitude * 2f);
            return 1f + (halves * perHalfTurn);
        }
    }
}
