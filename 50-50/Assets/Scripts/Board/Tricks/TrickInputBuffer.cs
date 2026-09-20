namespace FiftyFifty.Board.Tricks
{
    /// <summary>
    /// A trick press held for a moment so it can commit the instant the board leaves the ground
    /// (#6). Pressing slightly early should still land the trick you asked for.
    ///
    /// It expires on **simulation time**: the window is counted down with the step's `dt`, never
    /// measured against the wall clock (#26). Whether a press commits is simulation state, and a
    /// tick replayed under reconciliation has to reach the same answer as the tick it replaces —
    /// wall-clock time does not rewind, so it cannot be what decides.
    ///
    /// Plain C#, no Unity types, so the rule is testable headlessly.
    /// </summary>
    public sealed class TrickInputBuffer
    {
        /// <summary>How long a press is held before it goes stale.</summary>
        public float WindowSeconds = 0.25f;

        private int _slot;
        private float _remaining;

        /// <summary>The slot still worth committing, or 0 when there is nothing live.</summary>
        public int Live => _remaining > 0f ? _slot : 0;

        /// <summary>Hold a press. A newer one replaces an older one and gets its own full window.</summary>
        public void Accept(int slot)
        {
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
