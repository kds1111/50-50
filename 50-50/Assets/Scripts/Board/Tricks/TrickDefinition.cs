namespace FiftyFifty.Board.Tricks
{
    /// <summary>Which axis of the board mesh a named trick rotates.</summary>
    public enum TrickAxis
    {
        /// <summary>Nose-to-tail. Kickflips and heelflips. Never touches simulation (#16).</summary>
        Roll,

        /// <summary>Vertical. Shuvits. Cosmetic here too — heading is travel-relative (#19).</summary>
        Yaw,
    }

    /// <summary>
    /// One named trick. A name, an axis, how far it turns, how long it takes and what it adds to
    /// the bank.
    ///
    /// Plain C# with no Unity types on purpose (#1's 90/10 split): the trick table and its
    /// rules are testable headlessly, and only the MonoBehaviour that drives them needs an
    /// editor.
    ///
    /// The three fields that matter and why:
    ///
    ///   Turns     — signed, in whole turns. A kickflip and a heelflip are the same rotation
    ///               with opposite signs, which is why they cost one definition each and no
    ///               extra code.
    ///   Seconds   — FIXED, not a fraction of available airtime (#6 rule 4). Fixed is what
    ///               makes ramp size mean something: a long trick needs a big transition, and
    ///               asking for one you have no room for is a bail.
    ///   BankValue — what landing it adds to the bank (#8): 0.05 for every flip today. Stored
    ///               per trick so pricing them apart later is a data change. Any spin bonus
    ///               stacks on top, and repetition decays the two together.
    /// </summary>
    public sealed class TrickDefinition
    {
        public readonly string Name;
        public readonly TrickAxis Axis;
        public readonly float Turns;
        public readonly float Seconds;
        public readonly float BankValue;

        public TrickDefinition(string name, TrickAxis axis, float turns, float seconds, float bankValue)
        {
            Name = name;
            Axis = axis;
            Turns = turns;
            Seconds = seconds;
            BankValue = bankValue;
        }

        public override string ToString() => Name;
    }
}
