using System;
using System.Collections.Generic;
using FiftyFifty.Board.Tricks;

namespace FiftyFifty.Scoring
{
    public enum ForfeitReason
    {
        Bail,
        Conceded,
    }

    /// <summary>One credit to the bank, with enough detail for a readout to explain it.</summary>
    public readonly struct BankCredit
    {
        /// <summary>The name the repetition chain is keyed on. Null when nothing was credited.</summary>
        public readonly string Name;

        public readonly int SpinHalfTurns;

        /// <summary>Full value before repetition.</summary>
        public readonly float Raw;

        /// <summary>Repetition fraction applied: 1, 0.75, 0.5…</summary>
        public readonly float Fraction;

        /// <summary>How many times this name had already paid this bank. 0 on the first.</summary>
        public readonly int Repeat;

        /// <summary>What actually went on the bank, after the cap.</summary>
        public readonly float Added;

        public BankCredit(string name, int spinHalfTurns, float raw, float fraction, int repeat, float added)
        {
            Name = name;
            SpinHalfTurns = spinHalfTurns;
            Raw = raw;
            Fraction = fraction;
            Repeat = repeat;
            Added = added;
        }

        /// <summary>False for a bare spin, an ollie or a graze: nothing was credited and nothing counted.</summary>
        public bool Counted => Name != null;

        /// <summary>The cap ate some or all of it.</summary>
        public bool Clamped => Counted && Added < (Raw * Fraction) - 0.00001f;
    }

    /// <summary>
    /// The pending bank: a multiplier built from landed tricks, at risk until a goal cements it.
    /// The hook of the whole game, and every rule of it is #8's.
    ///
    ///   - Starts at 1.00. A goal pays exactly the bank, so an empty bank still scores 1.
    ///   - A named trick adds its own value plus a spin bonus. A spin alone adds nothing.
    ///   - Repeating a name decays the WHOLE add, keyed on the name alone — kickflip, kickflip
    ///     180 and kickflip 360 are one chain, or cycling spin variants would dodge the decay.
    ///   - Grinds pay per second past a minimum, capped per grind, decayed by grind name.
    ///   - The bank clamps at a cap. Credit past it is lost silently; nothing is taken away.
    ///   - Cementing or forfeiting resets it to the start, and resets every repetition chain.
    ///
    /// This class holds the bank and nothing else. Score — cemented, permanent — is kept by
    /// whoever cements, because a value that can never be lost has no business living next to
    /// one that is always at risk.
    ///
    /// Net-shaped: the value and the repetition counts survive across ticks and change what a
    /// goal pays, so both belong in reconcile data when FishNet arrives.
    ///
    /// Plain C#, no Unity types, tested headlessly per the map's 90/10 split.
    /// </summary>
    public sealed class PendingBank
    {
        private readonly Dictionary<string, int> _repeats = new();
        private BankRules _rules;

        public PendingBank(BankRules rules = null)
        {
            _rules = rules ?? new BankRules();
            Value = _rules.StartValue;
        }

        /// <summary>
        /// Swappable at any time so the Inspector can retune a live bank. Changing the rules
        /// never changes what is already banked.
        /// </summary>
        public BankRules Rules
        {
            get => _rules;
            set => _rules = value ?? new BankRules();
        }

        /// <summary>The multiplier at risk right now.</summary>
        public float Value { get; private set; }

        /// <summary>How many times a name has paid this bank.</summary>
        public int TimesCredited(string name) =>
            name != null && _repeats.TryGetValue(name, out int n) ? n : 0;

        /// <summary>
        /// A clean touchdown. Pays only when a named trick finished: an ollie and a bare spin are
        /// worth nothing and do not start a repetition chain.
        /// </summary>
        public BankCredit CreditTrick(TrickLanding landing)
        {
            if (landing.Trick == null)
            {
                return default;
            }

            float raw = landing.Trick.BankValue + _rules.SpinBonus(landing.SpinHalfTurns);
            return Credit(landing.Trick.Name, landing.SpinHalfTurns, raw);
        }

        /// <summary>
        /// A grind left cleanly. A graze pays nothing and does not count toward repetition.
        /// Falling off is not this method's business — that is a bail, and a bail forfeits.
        /// </summary>
        /// <param name="ratePerSecond">
        /// The grind's own rate. Every grind shares one to start (#8); it is passed in rather than
        /// held here so pricing grinds apart later is a change to the grind table, not to the bank.
        /// </param>
        public BankCredit CreditGrind(string grindName, float secondsOnRail, float ratePerSecond)
        {
            if (grindName == null || _rules.IsGraze(secondsOnRail))
            {
                return default;
            }

            return Credit(grindName, 0, _rules.GrindPay(secondsOnRail, ratePerSecond));
        }

        /// <summary>
        /// What leaving a grind cleanly right now would add, without adding it: repetition and the
        /// cap applied, nothing counted. For a readout that shows what is riding on the rail.
        /// </summary>
        public float PreviewGrind(string grindName, float secondsOnRail, float ratePerSecond)
        {
            if (grindName == null || _rules.IsGraze(secondsOnRail) || Value >= _rules.Cap)
            {
                return 0f;
            }

            float wanted = _rules.GrindPay(secondsOnRail, ratePerSecond) * _rules.RepeatFraction(TimesCredited(grindName));
            return Math.Min(_rules.Cap - Value, wanted);
        }

        /// <summary>A goal. Returns what it pays — the bank as it stands — and resets.</summary>
        public float Cement()
        {
            float payout = Value;
            Reset();
            return payout;
        }

        /// <summary>
        /// Lose the bank. Returns how much was lost. Total by default (#8); the keep fractions in
        /// the rules exist because these two are the rules most likely to want softening, and
        /// halving on concede is the obvious first retune. Repetition chains reset either way —
        /// the bank that earned them is gone.
        /// </summary>
        public float Forfeit(ForfeitReason reason)
        {
            float keep = reason == ForfeitReason.Bail ? _rules.BailKeeps : _rules.ConcedeKeeps;
            keep = Math.Min(1f, Math.Max(0f, keep));

            float before = Value;
            float gain = Math.Max(0f, before - _rules.StartValue);

            _repeats.Clear();
            Value = _rules.StartValue + (gain * keep);
            return before - Value;
        }

        /// <summary>Back to an empty bank. A match reset will want this (#22).</summary>
        public void Reset()
        {
            _repeats.Clear();
            Value = _rules.StartValue;
        }

        private BankCredit Credit(string name, int spinHalfTurns, float raw)
        {
            int repeat = TimesCredited(name);
            _repeats[name] = repeat + 1;

            float fraction = _rules.RepeatFraction(repeat);
            float before = Value;

            // Clamp rather than min against the cap: if the cap is lowered below the bank
            // mid-Play, the bank stops growing but is never cut. The cap takes nothing away.
            if (Value < _rules.Cap)
            {
                Value = Math.Min(_rules.Cap, Value + (raw * fraction));
            }

            return new BankCredit(name, spinHalfTurns, raw, fraction, repeat, Value - before);
        }
    }
}
