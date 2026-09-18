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
            int halves = HalfTurns(turns, tolerance);
            return halves == 0 ? GenericAir : (halves * 180).ToString();
        }

        /// <summary>
        /// How many clean half-turns a spin counts as: 1 for a 180, 2 for a 360, and so on. 0 for
        /// anything that is not a name — too little rotation, or too far between two names.
        ///
        /// This is what the bank prices a spin from (#8), which is why it shares its rule with
        /// <see cref="Classify"/> exactly: a spin the HUD calls "Air" must never pay as a 180.
        /// </summary>
        public static int HalfTurns(float turns, float tolerance = 0.2f)
        {
            float magnitude = Math.Abs(turns);

            // Below half a turn there is nothing to name. An ollie is not a trick (#6 rule 12).
            if (magnitude < 0.5f - tolerance)
            {
                return 0;
            }

            float halves = (float)Math.Round(magnitude * 2f);

            if (Math.Abs(magnitude - (halves * 0.5f)) > tolerance)
            {
                return 0;
            }

            return (int)halves;
        }
    }
}
