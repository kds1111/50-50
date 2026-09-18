using System;

namespace FiftyFifty.Board.Tricks
{
    public enum TrickRunState
    {
        Idle,
        Running,
        Finished,
    }

    public enum TrickLandingOutcome
    {
        /// <summary>Nothing was asked for. An ollie, or a pure spin. Never punished.</summary>
        NoTrick,

        /// <summary>The trick ran its full duration before touchdown. Pays.</summary>
        Landed,

        /// <summary>Touchdown before the trick finished. Bail (#6 rule 6).</summary>
        Bailed,
    }

    /// <summary>
    /// One named trick, from commit to landing.
    ///
    /// The whole risk model of the trick system lives in this class, and it is deliberately
    /// tiny: you ask for a trick, you are locked in (#6 rule 5, no cancel), and if the ground
    /// arrives before the trick finishes you bail (rule 6).
    ///
    /// Two subtleties worth not losing:
    ///
    ///   - Failure is detected AT LANDING, not at the moment it becomes inevitable (#6 rule 18).
    ///     The game usually knows within a frame of the commit that a 0.6s trick will not fit
    ///     into 0.3s of air, and says nothing. The player watches the flip fail to come round
    ///     and then eats it. Bailing at press time is the same outcome delivered as if the game
    ///     had read their intention, which reads as unfair.
    ///
    ///   - Once Finished, the run stays Finished until landing. The player is only punishable
    ///     by the ball for the trick's DURATION (#6 rule 13), not from press to touchdown, so
    ///     `InTrick` reads Running and not "has committed".
    ///
    /// Plain C#, no Unity types: the rules are testable headlessly per #1's 90/10 split.
    /// </summary>
    public sealed class NamedTrickRun
    {
        public TrickRunState State { get; private set; } = TrickRunState.Idle;
        public TrickDefinition Trick { get; private set; }
        public float Elapsed { get; private set; }

        /// <summary>True only while the trick is actually turning. This is the ball's tag.</summary>
        public bool InTrick => State == TrickRunState.Running;

        /// <summary>Something was asked for this air, finished or not.</summary>
        public bool Committed => State != TrickRunState.Idle;

        /// <summary>0 to 1 through the trick. Drives the cosmetic rotation, nothing else.</summary>
        public float Progress
        {
            get
            {
                if (Trick == null || Trick.Seconds <= 0f)
                {
                    return State == TrickRunState.Finished ? 1f : 0f;
                }

                return Math.Min(1f, Elapsed / Trick.Seconds);
            }
        }

        /// <summary>
        /// Ask for a trick. Refused if one is already committed this air — one named trick per
        /// air (#6 rule 3). Spin is unbounded alongside and is not this class's business.
        /// </summary>
        public bool TryCommit(TrickDefinition trick)
        {
            if (trick == null || Committed)
            {
                return false;
            }

            Trick = trick;
            Elapsed = 0f;
            State = TrickRunState.Running;
            return true;
        }

        public void Tick(float dt)
        {
            if (State != TrickRunState.Running)
            {
                return;
            }

            Elapsed += dt;

            if (Elapsed >= Trick.Seconds)
            {
                State = TrickRunState.Finished;
            }
        }

        /// <summary>
        /// Touchdown. Reports what happened and resets for the next air.
        /// </summary>
        public TrickLandingOutcome Land()
        {
            TrickLandingOutcome outcome = State switch
            {
                TrickRunState.Idle => TrickLandingOutcome.NoTrick,
                TrickRunState.Running => TrickLandingOutcome.Bailed,
                TrickRunState.Finished => TrickLandingOutcome.Landed,
                _ => TrickLandingOutcome.NoTrick,
            };

            Reset();
            return outcome;
        }

        public void Reset()
        {
            State = TrickRunState.Idle;
            Trick = null;
            Elapsed = 0f;
        }
    }
}
