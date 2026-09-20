namespace FiftyFifty.Board.Tricks
{
    /// <summary>
    /// A trick press held for a moment so it can commit the instant the board leaves the ground
    /// (#6). Pressing slightly early should still land the trick you asked for.
    ///
    /// It expires on **simulation time**: the window is counted down with the step's `dt`, never
    /// measured against the wall clock (#26). Whether a press commits is simulation state, and
    /// wall-clock time does not rewind, so it cannot be what decides.
    ///
    /// Counting on `dt` is necessary but not sufficient for reconciliation: the slot and its
    /// remaining time survive across ticks and affect simulation output, so they are reconcile
    /// data and will need a snapshot and restore path. Nothing in the project has one yet — #27
    /// owns finding everything in that position.
    ///
    /// Plain C#, no Unity types, so the rule is testable headlessly.
    /// </summary>
    public sealed class TrickInputBuffer
    {
        /// <summary>
        /// How long a press is held before it goes stale. Set by the owner from its own
        /// Inspector value, so the number lives in one place; a buffer given no window holds
        /// nothing.
        /// </summary>
        public float WindowSeconds;

        private int _slot;
        private float _remaining;

        /// <summary>The slot still worth committing, or 0 when there is nothing live.</summary>
        public int Live => _remaining > 0f ? _slot : 0;

        /// <summary>
        /// Hold a press. A newer one replaces an older one and gets its own full window. Slot 0
        /// is the "nothing asked for" value of the input struct, so accepting it clears instead
        /// of holding a press that could never commit.
        /// </summary>
        public void Accept(int slot)
        {
            if (slot <= 0)
            {
                Clear();
                return;
            }

            _slot = slot;
            _remaining = WindowSeconds;
        }

        /// <summary>Advance one simulation step.</summary>
        public void Tick(float dt)
        {
            if (_slot == 0)
            {
                return;
            }

            _remaining -= dt;

            if (_remaining <= 0f)
            {
                Clear();
            }
        }

        /// <summary>Drop whatever is held — the trick committed, or the run was cancelled.</summary>
        public void Clear()
        {
            _slot = 0;
            _remaining = 0f;
        }
    }
}
