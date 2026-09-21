using System;

namespace FiftyFifty.Scoring
{
    /// <summary>
    /// Every number the bank runs on. The defaults are #8's decisions, written down once.
    ///
    /// Plain C# so the tests can build one without an editor. In the game these are never edited
    /// here: <see cref="PlayerBank"/> exposes each one in the Inspector and hands the bank a fresh
    /// copy whenever a value changes, including mid-Play.
    /// </summary>
    public sealed class BankRules
    {
        /// <summary>
        /// The minimum grind time with no bank to ask — grind detection needs it to tell a graze
        /// from a fall even in a scene that does not score.
        /// </summary>
        public const float DefaultGrindMinSeconds = 0.2f;

        /// <summary>An empty bank. A goal is always worth at least this.</summary>
        public float StartValue = 1f;

        /// <summary>
        /// The bank clamps here (#8). Further credit adds nothing and costs nothing. On the bank
        /// rather than the payout, because the bank is the number the player watches — a hidden
        /// ceiling silently eating landed tricks is the worst version of this rule.
        /// </summary>
        public float Cap = 3f;

        /// <summary>A 180 on top of a named trick.</summary>
        public float SpinBonusFirstHalfTurn = 0.10f;

        /// <summary>Every further 180: a 360 pays 0.15, a 540 pays 0.20, and so on.</summary>
        public float SpinBonusPerExtraHalfTurn = 0.05f;

        /// <summary>
        /// Fraction paid for the Nth landing of the same name within one bank. THPS's shipped
        /// anti-spam curve (#4, #8). The last entry repeats forever.
        /// </summary>
        public float[] RepeatCurve = { 1f, 0.75f, 0.5f, 0.25f, 0.1f };

        /// <summary>
        /// Seconds on a rail before the grind clock starts. Below it a grind is a graze: nothing
        /// earned, and nothing lost either.
        /// </summary>
        public float GrindMinSeconds = DefaultGrindMinSeconds;

        /// <summary>Most a single grind can add, before repetition decay. Kills the long-rail generator.</summary>
        public float GrindCapPerGrind = 0.2f;

        /// <summary>Fraction of the bank's gain kept on a bail. 0 is #8's total wipe.</summary>
        public float BailKeeps;

        /// <summary>Fraction of the bank's gain kept on conceding. 0 is #8's total wipe.</summary>
        public float ConcedeKeeps;

        /// <summary>What a spin adds on top of a named trick. A spin on its own is worth nothing (#8).</summary>
        public float SpinBonus(int halfTurns) =>
            halfTurns <= 0 ? 0f : SpinBonusFirstHalfTurn + ((halfTurns - 1) * SpinBonusPerExtraHalfTurn);

        /// <summary>Fraction paid for a name already credited <paramref name="timesBefore"/> times this bank.</summary>
        public float RepeatFraction(int timesBefore)
        {
            if (RepeatCurve == null || RepeatCurve.Length == 0)
            {
                return 1f;
            }

            return RepeatCurve[Math.Min(Math.Max(0, timesBefore), RepeatCurve.Length - 1)];
        }

        /// <summary>Too short to count. Pays nothing, and falling off one is not a bail.</summary>
        public bool IsGraze(float secondsOnRail) => secondsOnRail <= GrindMinSeconds;

        /// <summary>
        /// A grind's pay before repetition: the clock starts at the minimum, runs at the grind's
        /// own rate, and stops at the per-grind cap.
        /// </summary>
        public float GrindPay(float secondsOnRail, float ratePerSecond)
        {
            if (IsGraze(secondsOnRail))
            {
                return 0f;
            }

            float paid = (secondsOnRail - GrindMinSeconds) * Math.Max(0f, ratePerSecond);
            return Math.Min(GrindCapPerGrind, paid);
        }
    }
}
