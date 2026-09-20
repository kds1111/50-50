using FiftyFifty.Board;
using FiftyFifty.Match;
using NUnit.Framework;

namespace FiftyFifty.Tests
{
    /// <summary>
    /// #24's match shape: a kickoff on load and after every goal, an optional clock that only runs
    /// in play, and how pads are dealt to players sharing one machine.
    /// </summary>
    public class MatchFlowTests
    {
        private const float Tick = 0.02f;
        private const float Tolerance = 0.0001f;

        private static MatchSignal Run(MatchFlow flow, float seconds)
        {
            MatchSignal all = MatchSignal.None;

            for (float t = 0f; t < seconds - 0.00001f; t += Tick)
            {
                all |= flow.Tick(Tick);
            }

            return all;
        }

        // --- Kickoff ---------------------------------------------------------------------

        [Test]
        public void A_match_opens_with_a_reset_and_a_frozen_countdown()
        {
            var flow = new MatchFlow();

            Assert.AreEqual(MatchSignal.Reset, flow.Begin());
            Assert.AreEqual(MatchPhase.Countdown, flow.Phase);
            Assert.IsTrue(flow.Frozen);
        }

        [Test]
        public void The_countdown_ends_in_go()
        {
            var flow = new MatchFlow { CountdownSeconds = 2f };
            flow.Begin();

            Assert.AreEqual(MatchSignal.None, Run(flow, 1.9f));
            Assert.AreEqual(MatchSignal.Go, Run(flow, 0.2f));
            Assert.AreEqual(MatchPhase.Playing, flow.Phase);
            Assert.IsFalse(flow.Frozen);
        }

        [Test]
        public void A_goal_pauses_then_resets_then_counts_down_again()
        {
            var flow = new MatchFlow { CountdownSeconds = 2f, GoalPauseSeconds = 1.5f };
            flow.Begin();
            Run(flow, 2.1f);

            flow.Goal();
            Assert.AreEqual(MatchPhase.GoalPause, flow.Phase);
            Assert.IsFalse(flow.Frozen, "play carries on while the goal lands");

            Assert.AreEqual(MatchSignal.None, Run(flow, 1.4f));
            Assert.AreEqual(MatchSignal.Reset, Run(flow, 0.2f));
            Assert.AreEqual(MatchPhase.Countdown, flow.Phase);
        }

        [Test]
        public void Goals_outside_play_are_ignored()
        {
            var flow = new MatchFlow();
            flow.Begin();

            flow.Goal();
            Assert.AreEqual(MatchPhase.Countdown, flow.Phase, "during the countdown");

            Run(flow, 2.1f);
            flow.Goal();
            flow.Goal();
            Assert.AreEqual(1.5f, flow.PhaseRemaining, Tolerance, "a second goal during the pause does not restart it");
        }

        [Test]
        public void With_no_countdown_a_reset_goes_straight_to_play()
        {
            var flow = new MatchFlow { CountdownSeconds = 0f };

            Assert.AreEqual(MatchSignal.Reset | MatchSignal.Go, flow.Begin());
            Assert.AreEqual(MatchPhase.Playing, flow.Phase);
        }

        // --- The clock -------------------------------------------------------------------

        [Test]
        public void With_the_timer_off_a_match_never_ends()
        {
            var flow = new MatchFlow { TimerEnabled = false, MatchSeconds = 1f };
            flow.Begin();

            Assert.AreEqual(MatchSignal.Go, Run(flow, 10f));
            Assert.AreEqual(MatchPhase.Playing, flow.Phase);
        }

        [Test]
        public void The_clock_runs_only_in_play()
        {
            var flow = new MatchFlow { TimerEnabled = true, MatchSeconds = 60f, CountdownSeconds = 2f, GoalPauseSeconds = 1.5f };
            flow.Begin();

            Run(flow, 2f);
            Assert.AreEqual(60f, flow.ClockRemaining, Tolerance, "the countdown does not cost time");

            Run(flow, 5f);
            Assert.AreEqual(55f, flow.ClockRemaining, 0.05f);

            flow.Goal();
            Run(flow, 1.5f + 2f);
            Assert.AreEqual(55f, flow.ClockRemaining, 0.05f, "nor does a kickoff");
        }

        [Test]
        public void The_clock_running_out_ends_the_match_and_freezes_everyone()
        {
            var flow = new MatchFlow { TimerEnabled = true, MatchSeconds = 3f, CountdownSeconds = 1f };
            flow.Begin();

            MatchSignal signals = Run(flow, 1f + 3.1f);

            Assert.IsTrue((signals & MatchSignal.Ended) != 0);
            Assert.AreEqual(MatchPhase.Over, flow.Phase);
            Assert.IsTrue(flow.Frozen);
            Assert.AreEqual(MatchSignal.None, Run(flow, 5f), "nothing happens after the whistle");
        }

        [Test]
        public void Beginning_again_refills_the_clock()
        {
            var flow = new MatchFlow { TimerEnabled = true, MatchSeconds = 3f, CountdownSeconds = 1f };
            flow.Begin();
            Run(flow, 5f);

            Assert.AreEqual(MatchSignal.Reset, flow.Begin());
            Assert.AreEqual(3f, flow.ClockRemaining, Tolerance);
            Assert.AreEqual(MatchPhase.Countdown, flow.Phase);
        }

        // --- Pads ------------------------------------------------------------------------

        [Test]
        public void A_lone_player_takes_whichever_pad_was_touched_last() =>
            Assert.AreEqual(PadDealing.AnyPad, PadDealing.PadFor(0, players: 1, padCount: 3));

        [Test]
        public void Two_pads_are_one_each_in_connection_order()
        {
            Assert.AreEqual(0, PadDealing.PadFor(0, players: 2, padCount: 2));
            Assert.AreEqual(1, PadDealing.PadFor(1, players: 2, padCount: 2));
        }

        [Test]
        public void With_one_pad_player_one_is_on_the_keyboard_and_player_two_has_the_pad()
        {
            Assert.AreEqual(PadDealing.NoPad, PadDealing.PadFor(0, players: 2, padCount: 1));
            Assert.AreEqual(0, PadDealing.PadFor(1, players: 2, padCount: 1));
        }

        [Test]
        public void With_no_pads_nobody_has_one()
        {
            Assert.AreEqual(PadDealing.NoPad, PadDealing.PadFor(0, players: 2, padCount: 0));
            Assert.AreEqual(PadDealing.NoPad, PadDealing.PadFor(1, players: 2, padCount: 0));
        }

        [Test]
        public void An_override_wins_when_that_pad_exists()
        {
            Assert.AreEqual(1, PadDealing.PadFor(0, players: 2, padCount: 2, padOverride: 1));
            Assert.AreEqual(PadDealing.NoPad, PadDealing.PadFor(0, players: 2, padCount: 1, padOverride: 1));
        }
    }
}
