namespace FiftyFifty.Scoring
{
    /// <summary>
    /// Which end a player plays for. Every player is on one side, and every goal belongs to one.
    ///
    /// By convention side A starts at the -Z end and attacks +Z; the scene wiring on the 50-50
    /// menu assigns goals by that rule, so nothing has to be set by hand.
    /// </summary>
    public enum Side
    {
        A,
        B,
    }

    public enum GoalOutcome
    {
        /// <summary>You were attacking this goal. Your bank becomes score.</summary>
        Cement,

        /// <summary>You were defending it. Your bank is gone (#8).</summary>
        Forfeit,
    }

    /// <summary>
    /// What a goal means for a player, and nothing else.
    ///
    /// The goal is credited by where the ball went, not by who touched it last — the soccer rule,
    /// so an own goal counts for the opponent. That is why no touch tracking exists anywhere: a
    /// goal needs to know which side it belongs to, and that is all.
    ///
    /// Plain C#, so the rule is tested headlessly with the rest of the bank.
    /// </summary>
    public static class GoalRule
    {
        public static Side Opponent(Side side) => side == Side.A ? Side.B : Side.A;

        public static GoalOutcome For(Side player, Side goalDefendedBy) =>
            player == goalDefendedBy ? GoalOutcome.Forfeit : GoalOutcome.Cement;
    }
}
