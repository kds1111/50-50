using FiftyFifty.Board.Tricks;
using FiftyFifty.Scoring;
using NUnit.Framework;

namespace FiftyFifty.Tests
{
    /// <summary>
    /// #8's banking rules, pinned. Every number here is a decision recorded on that ticket, so a
    /// failure means either the code drifted or the decision changed — and if it is the second,
    /// the ticket changes first.
    ///
    /// Headless: Window > General > Test Runner > EditMode.
    /// </summary>
    public class PendingBankTests
    {
        private const float Tolerance = 0.0001f;

        private static readonly TrickDefinition Kickflip = new("Kickflip", TrickAxis.Roll, -1f, 0.45f, 0.05f);
        private static readonly TrickDefinition Heelflip = new("Heelflip", TrickAxis.Roll, 1f, 0.45f, 0.05f);
        private static readonly TrickDefinition Shuvit = new("Shuvit", TrickAxis.Yaw, 0.5f, 0.40f, 0.05f);

        private static TrickLanding Land(TrickDefinition trick, int spinHalfTurns = 0) =>
            new(trick, spinHalfTurns > 0 ? (spinHalfTurns * 180).ToString() : SpinClassifier.GenericAir, spinHalfTurns);

        private static float BankAfter(TrickLanding landing)
        {
            var bank = new PendingBank();
            bank.CreditTrick(landing);
            return bank.Value;
        }

        // --- The table on #8 --------------------------------------------------------------

        [Test]
        public void An_empty_bank_is_one_and_a_goal_on_it_pays_one()
        {
            var bank = new PendingBank();

            Assert.AreEqual(1f, bank.Value, Tolerance);
            Assert.AreEqual(1f, bank.Cement(), Tolerance);
        }

        [Test]
        public void An_ollie_pays_nothing() =>
            Assert.AreEqual(1f, BankAfter(Land(null)), Tolerance);

        [Test]
        public void A_spin_on_its_own_pays_nothing()
        {
            Assert.AreEqual(1f, BankAfter(Land(null, 1)), Tolerance);
            Assert.AreEqual(1f, BankAfter(Land(null, 2)), Tolerance);
        }

        [Test]
        public void A_bare_spin_does_not_start_a_repetition_chain()
        {
            var bank = new PendingBank();
            BankCredit credit = bank.CreditTrick(Land(null, 2));

            Assert.IsFalse(credit.Counted);
            Assert.AreEqual(0f, credit.Added, Tolerance);
        }

        [Test]
        public void Every_flip_adds_five_hundredths()
        {
            Assert.AreEqual(1.05f, BankAfter(Land(Kickflip)), Tolerance);
            Assert.AreEqual(1.05f, BankAfter(Land(Heelflip)), Tolerance);
            Assert.AreEqual(1.05f, BankAfter(Land(Shuvit)), Tolerance);
        }

        [Test]
        public void A_spin_stacks_on_a_flip()
        {
            Assert.AreEqual(1.15f, BankAfter(Land(Kickflip, 1)), Tolerance);
            Assert.AreEqual(1.20f, BankAfter(Land(Kickflip, 2)), Tolerance);
        }

        [Test]
        public void Every_further_180_adds_another_five_hundredths()
        {
            Assert.AreEqual(1.25f, BankAfter(Land(Kickflip, 3)), Tolerance);
            Assert.AreEqual(1.30f, BankAfter(Land(Kickflip, 4)), Tolerance);
        }

        [Test]
        public void An_unnamed_spin_adds_no_bonus_but_the_flip_still_pays() =>
            Assert.AreEqual(1.05f, BankAfter(Land(Kickflip, 0)), Tolerance);

        // --- Repetition ------------------------------------------------------------------

        [Test]
        public void Repeating_a_trick_pays_100_75_50_25_10_then_10_forever()
        {
            var bank = new PendingBank();
            float[] expected = { 0.05f, 0.0375f, 0.025f, 0.0125f, 0.005f, 0.005f, 0.005f };

            foreach (float add in expected)
            {
                Assert.AreEqual(add, bank.CreditTrick(Land(Kickflip)).Added, Tolerance);
            }
        }

        [Test]
        public void Spin_variants_of_one_flip_share_a_chain_and_the_whole_add_decays()
        {
            var bank = new PendingBank();

            Assert.AreEqual(0.05f, bank.CreditTrick(Land(Kickflip)).Added, Tolerance);
            Assert.AreEqual(0.15f * 0.75f, bank.CreditTrick(Land(Kickflip, 1)).Added, Tolerance);
            Assert.AreEqual(0.20f * 0.5f, bank.CreditTrick(Land(Kickflip, 2)).Added, Tolerance);
        }

        [Test]
        public void Different_tricks_decay_independently()
        {
            var bank = new PendingBank();
            bank.CreditTrick(Land(Kickflip));

            Assert.AreEqual(0.05f, bank.CreditTrick(Land(Heelflip)).Added, Tolerance);
        }

        [Test]
        public void Cementing_resets_the_bank_and_every_chain()
        {
            var bank = new PendingBank();
            bank.CreditTrick(Land(Kickflip, 2));
            bank.CreditTrick(Land(Kickflip, 2));

            Assert.AreEqual(1.35f, bank.Cement(), Tolerance);
            Assert.AreEqual(1f, bank.Value, Tolerance);
            Assert.AreEqual(0, bank.TimesCredited("Kickflip"));
            Assert.AreEqual(0.20f, bank.CreditTrick(Land(Kickflip, 2)).Added, Tolerance);
        }

        // --- Losing it -------------------------------------------------------------------

        [Test]
        public void A_bail_wipes_the_whole_bank_and_every_chain()
        {
            var bank = new PendingBank();
            bank.CreditTrick(Land(Kickflip, 2));
            bank.CreditTrick(Land(Heelflip, 1));

            Assert.AreEqual(0.35f, bank.Forfeit(ForfeitReason.Bail), Tolerance);
            Assert.AreEqual(1f, bank.Value, Tolerance);
            Assert.AreEqual(0, bank.TimesCredited("Kickflip"));
        }

        [Test]
        public void Conceding_wipes_the_whole_bank()
        {
            var bank = new PendingBank();
            bank.CreditTrick(Land(Kickflip, 2));

            bank.Forfeit(ForfeitReason.Conceded);

            Assert.AreEqual(1f, bank.Value, Tolerance);
        }

        [Test]
        public void A_keep_fraction_softens_a_forfeit_to_part_of_the_gain()
        {
            var bank = new PendingBank(new BankRules { ConcedeKeeps = 0.5f });
            bank.CreditTrick(Land(Kickflip, 2));
            bank.CreditTrick(Land(Heelflip, 2));

            Assert.AreEqual(0.20f, bank.Forfeit(ForfeitReason.Conceded), Tolerance);
            Assert.AreEqual(1.20f, bank.Value, Tolerance);
        }

        // --- The cap ---------------------------------------------------------------------

        [Test]
        public void The_bank_clamps_at_the_cap_and_the_excess_is_simply_not_added()
        {
            var bank = new PendingBank(new BankRules { Cap = 1.3f });
            bank.CreditTrick(Land(Kickflip, 2));

            BankCredit credit = bank.CreditTrick(Land(Heelflip, 2));

            Assert.AreEqual(1.3f, bank.Value, Tolerance);
            Assert.AreEqual(0.1f, credit.Added, Tolerance);
            Assert.IsTrue(credit.Clamped);
        }

        [Test]
        public void Lowering_the_cap_below_the_bank_never_takes_anything_away()
        {
            var bank = new PendingBank();
            bank.CreditTrick(Land(Kickflip, 2));

            bank.Rules = new BankRules { Cap = 1.1f };
            bank.CreditTrick(Land(Heelflip, 2));

            Assert.AreEqual(1.2f, bank.Value, Tolerance);
        }

        [Test]
        public void A_great_run_lands_near_x2_6_and_crawls_after_that()
        {
            var bank = new PendingBank();

            for (int i = 0; i < 5; i++)
            {
                bank.CreditTrick(Land(Kickflip, 2));
                bank.CreditTrick(Land(Heelflip, 2));
                bank.CreditTrick(Land(Shuvit, 2));
            }

            Assert.AreEqual(2.56f, bank.Value, Tolerance);
            Assert.AreEqual(0.02f, bank.CreditTrick(Land(Kickflip, 2)).Added, Tolerance);
        }

        // --- Grinds ----------------------------------------------------------------------

        [Test]
        public void A_graze_pays_nothing_and_does_not_count()
        {
            var bank = new PendingBank();
            BankCredit credit = bank.CreditGrind("50-50", 0.2f, 0.2f);

            Assert.IsFalse(credit.Counted);
            Assert.AreEqual(1f, bank.Value, Tolerance);
            Assert.AreEqual(0, bank.TimesCredited("50-50"));
        }

        [Test]
        public void The_grind_clock_starts_at_the_minimum()
        {
            var bank = new PendingBank();

            Assert.AreEqual(0.14f, bank.CreditGrind("50-50", 0.9f, 0.2f).Added, Tolerance);
        }

        [Test]
        public void A_long_grind_is_capped_per_grind()
        {
            var bank = new PendingBank();

            Assert.AreEqual(0.2f, bank.CreditGrind("50-50", 30f, 0.2f).Added, Tolerance);
        }

        [Test]
        public void Grinds_decay_by_grind_name_on_the_same_curve()
        {
            var bank = new PendingBank();
            bank.CreditGrind("50-50", 5f, 0.2f);

            Assert.AreEqual(0.15f, bank.CreditGrind("50-50", 5f, 0.2f).Added, Tolerance);
            Assert.AreEqual(0.2f, bank.CreditGrind("Boardslide", 5f, 0.2f).Added, Tolerance);
        }

        // --- Goals -----------------------------------------------------------------------

        [Test]
        public void A_goal_pays_the_attackers_and_costs_the_defenders()
        {
            Assert.AreEqual(GoalOutcome.Forfeit, GoalRule.For(Side.A, goalDefendedBy: Side.A));
            Assert.AreEqual(GoalOutcome.Cement, GoalRule.For(Side.B, goalDefendedBy: Side.A));
            Assert.AreEqual(GoalOutcome.Cement, GoalRule.For(Side.A, goalDefendedBy: Side.B));
        }

        // --- Spin classification feeding the bank ------------------------------------------

        [Test]
        public void Half_turns_and_names_agree()
        {
            Assert.AreEqual(1, SpinClassifier.HalfTurns(0.5f));
            Assert.AreEqual(2, SpinClassifier.HalfTurns(-1.05f));
            Assert.AreEqual(3, SpinClassifier.HalfTurns(1.4f));
            Assert.AreEqual(0, SpinClassifier.HalfTurns(0.75f));
            Assert.AreEqual(0, SpinClassifier.HalfTurns(0.1f));

            Assert.AreEqual("180", SpinClassifier.Classify(0.5f));
            Assert.AreEqual("360", SpinClassifier.Classify(-1.05f));
            Assert.AreEqual(SpinClassifier.GenericAir, SpinClassifier.Classify(0.75f));
        }
    }
}
