using FiftyFifty.Board.Grinds;
using FiftyFifty.Scoring;
using NUnit.Framework;

namespace FiftyFifty.Tests
{
    /// <summary>
    /// #12's grind rules, pinned: naming by angle, lock-on, and what each exit is worth.
    /// The physics of holding a grind is not here — that is judged by riding it.
    /// </summary>
    public class GrindRulesTests
    {
        private const float Tolerance = 0.0001f;

        // --- Naming ----------------------------------------------------------------------

        [Test]
        public void Along_the_rail_is_a_50_50_whichever_end_leads()
        {
            Assert.AreEqual(GrindRules.FiftyFifty, GrindRules.Name(0f, 0f));
            Assert.AreEqual(GrindRules.FiftyFifty, GrindRules.Name(30f, 0f));
            Assert.AreEqual(GrindRules.FiftyFifty, GrindRules.Name(170f, 0f));
            Assert.AreEqual(GrindRules.FiftyFifty, GrindRules.Name(200f, 10f));
        }

        [Test]
        public void Across_the_rail_is_a_boardslide()
        {
            Assert.AreEqual(GrindRules.Boardslide, GrindRules.Name(90f, 0f));
            Assert.AreEqual(GrindRules.Boardslide, GrindRules.Name(-100f, 0f));
            Assert.AreEqual(GrindRules.Boardslide, GrindRules.Name(46f, 0f));
        }

        [Test]
        public void Exactly_45_degrees_goes_to_the_50_50() =>
            Assert.AreEqual(GrindRules.FiftyFifty, GrindRules.Name(45f, 0f));

        [Test]
        public void The_rails_own_heading_does_not_matter_only_the_angle_to_it()
        {
            Assert.AreEqual(GrindRules.FiftyFifty, GrindRules.Name(95f, 90f));
            Assert.AreEqual(GrindRules.Boardslide, GrindRules.Name(95f, 0f));
        }

        // --- Snapping --------------------------------------------------------------------

        [Test]
        public void A_50_50_snaps_to_the_nearest_end_of_the_rail()
        {
            Assert.AreEqual(0f, GrindRules.SnapHeading(30f, 0f), Tolerance);
            Assert.AreEqual(180f, GrindRules.SnapHeading(170f, 0f), Tolerance);
            Assert.AreEqual(-180f, GrindRules.SnapHeading(-160f, 0f), Tolerance);
        }

        [Test]
        public void A_boardslide_snaps_to_the_nearest_side()
        {
            Assert.AreEqual(90f, GrindRules.SnapHeading(100f, 0f), Tolerance);
            Assert.AreEqual(-90f, GrindRules.SnapHeading(-60f, 0f), Tolerance);
        }

        [Test]
        public void Snapping_never_jumps_a_whole_turn() =>
            Assert.AreEqual(360f, GrindRules.SnapHeading(370f, 0f), Tolerance);

        // --- Lock-on ---------------------------------------------------------------------

        private static GrindLockProbe Good() => new()
        {
            Airborne = true,
            VerticalSpeed = -2f,
            Lateral = 0.1f,
            Vertical = 0.05f,
            TravelAngle = 10f,
            AlongSpeed = 6f,
        };

        [Test]
        public void A_board_coming_down_along_a_rail_locks_on() =>
            Assert.IsTrue(GrindRules.CanLock(Good(), new GrindLockLimits()));

        [Test]
        public void Each_lock_on_condition_is_required_on_its_own()
        {
            var limits = new GrindLockLimits();
            GrindLockProbe p;

            p = Good(); p.Airborne = false;
            Assert.IsFalse(GrindRules.CanLock(p, limits), "grounded");

            p = Good(); p.VerticalSpeed = 0.5f;
            Assert.IsFalse(GrindRules.CanLock(p, limits), "rising");

            p = Good(); p.Lateral = 0.5f;
            Assert.IsFalse(GrindRules.CanLock(p, limits), "too far sideways");

            p = Good(); p.Vertical = -0.4f;
            Assert.IsFalse(GrindRules.CanLock(p, limits), "too far below");

            p = Good(); p.TravelAngle = 60f;
            Assert.IsFalse(GrindRules.CanLock(p, limits), "crossing the rail");

            p = Good(); p.AlongSpeed = 1f;
            Assert.IsFalse(GrindRules.CanLock(p, limits), "too slow");
        }

        // --- Exits -----------------------------------------------------------------------

        [Test]
        public void Popping_out_and_riding_off_the_end_are_both_clean()
        {
            Assert.AreEqual(GrindOutcome.Credit, GrindRules.Resolve(GrindExit.Popped, graze: false));
            Assert.AreEqual(GrindOutcome.Credit, GrindRules.Resolve(GrindExit.RodeOffEnd, graze: false));
        }

        [Test]
        public void Stalling_and_being_knocked_off_are_falls()
        {
            Assert.AreEqual(GrindOutcome.Fall, GrindRules.Resolve(GrindExit.Stalled, graze: false));
            Assert.AreEqual(GrindOutcome.Fall, GrindRules.Resolve(GrindExit.KnockedOff, graze: false));
        }

        [Test]
        public void A_graze_is_forgiven_however_it_ended()
        {
            foreach (GrindExit exit in new[] { GrindExit.Popped, GrindExit.RodeOffEnd, GrindExit.Stalled, GrindExit.KnockedOff })
            {
                Assert.AreEqual(GrindOutcome.Nothing, GrindRules.Resolve(exit, graze: true), exit.ToString());
            }
        }

        [Test]
        public void An_interruption_is_neither_paid_nor_punished() =>
            Assert.AreEqual(GrindOutcome.Nothing, GrindRules.Resolve(GrindExit.Interrupted, graze: false));

        // --- What riding on the rail is worth ---------------------------------------------

        [Test]
        public void The_preview_matches_what_a_clean_exit_then_pays()
        {
            var bank = new PendingBank();
            bank.CreditGrind(GrindRules.FiftyFifty, 5f, 0.2f);

            float preview = bank.PreviewGrind(GrindRules.FiftyFifty, 0.9f, 0.2f);

            Assert.AreEqual(0.14f * 0.75f, preview, Tolerance);
            Assert.AreEqual(preview, bank.CreditGrind(GrindRules.FiftyFifty, 0.9f, 0.2f).Added, Tolerance);
        }

        [Test]
        public void The_preview_is_zero_during_a_graze() =>
            Assert.AreEqual(0f, new PendingBank().PreviewGrind(GrindRules.FiftyFifty, 0.1f, 0.2f), Tolerance);
    }
}
