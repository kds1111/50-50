namespace FiftyFifty.Board.Tricks
{
    /// <summary>
    /// What a clean touchdown reports: the named trick that finished, if any, and the spin that
    /// went with it.
    ///
    /// It carries facts, not a value. What any of this is worth is the bank's to decide (#8),
    /// so the trick system never has to change when the prices do.
    ///
    /// Plain C#, no Unity types, so the bank that consumes it is testable headlessly.
    /// </summary>
    public readonly struct TrickLanding
    {
        /// <summary>The named trick that ran its full duration, or null for an ollie or a pure spin.</summary>
        public readonly TrickDefinition Trick;

        /// <summary>The spin's name — "180", "360" — or <see cref="SpinClassifier.GenericAir"/>.</summary>
        public readonly string SpinName;

        /// <summary>Clean half-turns of spin. 0 when the spin has no name.</summary>
        public readonly int SpinHalfTurns;

        public TrickLanding(TrickDefinition trick, string spinName, int spinHalfTurns)
        {
            Trick = trick;
            SpinName = spinName;
            SpinHalfTurns = spinHalfTurns;
        }
    }
}
