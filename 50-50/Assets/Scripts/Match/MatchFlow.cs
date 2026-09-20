using System;

namespace FiftyFifty.Match
{
    public enum MatchPhase
    {
        /// <summary>Everyone is on their spot and held still. Counting down to "go".</summary>
        Countdown,

        /// <summary>Live. The clock, if there is one, runs.</summary>
        Playing,

        /// <summary>A goal just went in. Play carries on for a moment so it lands, then a kickoff.</summary>
        GoalPause,

        /// <summary>The clock ran out. Everyone is held until a restart.</summary>
        Over,
    }

    /// <summary>What the director has to do after a tick. More than one can happen at once.</summary>
    [Flags]
    public enum MatchSignal
    {
        None = 0,

        /// <summary>Put the ball and every player back on their kickoff spots.</summary>
        Reset = 1,

        /// <summary>The countdown finished. Let everyone go.</summary>
        Go = 2,

        /// <summary>The clock hit zero.</summary>
        Ended = 4,
    }

    /// <summary>
    /// The shape of a match (#24): a kickoff on load and after every goal, and an optional clock.
    ///
    ///   Countdown ──2s──▶ Playing ──goal──▶ GoalPause ──1.5s──▶ (reset) Countdown …
    ///                        │
    ///                        └──clock hits 0──▶ Over ──restart──▶ (reset) Countdown
    ///
    /// The clock only runs while Playing. It stops for the pause after a goal and for the
    /// countdown, so a kickoff never eats match time.
    ///
    /// This class decides WHEN; the director does the resetting. Plain C#, no Unity types,
    /// ticked on the fixed step and tested headlessly. Nothing here runs in Update().
    /// </summary>
    public sealed class MatchFlow
    {
        public float CountdownSeconds = 2f;
        public float GoalPauseSeconds = 1.5f;
        public bool TimerEnabled;
        public float MatchSeconds = 300f;

        public MatchPhase Phase { get; private set; } = MatchPhase.Countdown;

        /// <summary>Time left in the countdown or the post-goal pause.</summary>
        public float PhaseRemaining { get; private set; }

        /// <summary>Match time left. Meaningless with the timer off.</summary>
        public float ClockRemaining { get; private set; }

        /// <summary>Players are held still: counting down, or the match is over.</summary>
        public bool Frozen => Phase == MatchPhase.Countdown || Phase == MatchPhase.Over;

        /// <summary>
        /// Whether a goal counts right now (#29). Only in play: not during the opening countdown,
        /// not in the pause after a goal, not after full time. The goal volume asks before it
        /// settles any bank, so a ball still rolling when the clock runs out cements nothing.
        /// </summary>
        public bool ScoringOpen => Phase == MatchPhase.Playing;

        /// <summary>A fresh match: the clock refills, everyone goes to their spots, the countdown starts.</summary>
        public MatchSignal Begin()
        {
            ClockRemaining = Math.Max(0f, MatchSeconds);
            return StartCountdown();
        }

        /// <summary>A goal went in. Only counts while Playing — there is no scoring during a pause.</summary>
        public void Goal()
        {
            if (Phase != MatchPhase.Playing)
            {
                return;
            }

            Phase = MatchPhase.GoalPause;
            PhaseRemaining = GoalPauseSeconds;
        }

        public MatchSignal Tick(float dt)
        {
            switch (Phase)
            {
                case MatchPhase.Playing:
                    if (!TimerEnabled)
                    {
                        return MatchSignal.None;
                    }

                    ClockRemaining = Math.Max(0f, ClockRemaining - dt);

                    if (ClockRemaining > 0f)
                    {
                        return MatchSignal.None;
                    }

                    Phase = MatchPhase.Over;
                    return MatchSignal.Ended;

                case MatchPhase.GoalPause:
                    PhaseRemaining -= dt;
                    return PhaseRemaining > 0f ? MatchSignal.None : StartCountdown();

                case MatchPhase.Countdown:
                    PhaseRemaining -= dt;

                    if (PhaseRemaining > 0f)
                    {
                        return MatchSignal.None;
                    }

                    Phase = MatchPhase.Playing;
                    return MatchSignal.Go;

                default:
                    return MatchSignal.None;
            }
        }

        private MatchSignal StartCountdown()
        {
            if (CountdownSeconds <= 0f)
            {
                Phase = MatchPhase.Playing;
                PhaseRemaining = 0f;
                return MatchSignal.Reset | MatchSignal.Go;
            }

            Phase = MatchPhase.Countdown;
            PhaseRemaining = CountdownSeconds;
            return MatchSignal.Reset;
        }
    }
}
