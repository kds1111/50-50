namespace FiftyFifty.Board
{
    /// <summary>
    /// Which gamepad belongs to which player on a shared machine (#24).
    ///
    /// Dealt out in connection order, and the keyboard always belongs to player one:
    ///   - one player: the keyboard and whichever pad was touched last — the board as it always was.
    ///   - two players, two pads: one pad each.
    ///   - two players, one pad: player one on the keyboard, player two on the pad, so one person
    ///     can drive both sides while testing alone.
    ///
    /// Plain C#, so the rule is tested headlessly.
    /// </summary>
    public static class PadDealing
    {
        /// <summary>Whichever pad was touched last. A lone player's answer.</summary>
        public const int AnyPad = -2;

        /// <summary>No pad for this player.</summary>
        public const int NoPad = -1;

        /// <param name="playerIndex">0 for player one.</param>
        /// <param name="players">Players sharing this machine.</param>
        /// <param name="padCount">Pads connected right now.</param>
        /// <param name="padOverride">Force a pad by connection order; negative deals automatically.</param>
        public static int PadFor(int playerIndex, int players, int padCount, int padOverride = -1)
        {
            if (padOverride >= 0)
            {
                return padOverride < padCount ? padOverride : NoPad;
            }

            if (players <= 1)
            {
                return AnyPad;
            }

            if (padCount >= players)
            {
                return playerIndex;
            }

            // Fewer pads than players: player one is on the keyboard, the rest take pads in order.
            int index = playerIndex - 1;
            return index >= 0 && index < padCount ? index : NoPad;
        }
    }
}
