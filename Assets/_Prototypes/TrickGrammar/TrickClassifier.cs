using System;
using UnityEngine;

namespace Prototypes.TrickGrammar
{
    public enum LandingGrade
    {
        Clean,
        Sketchy,
        Bail,
    }

    /// <summary>
    /// PROTOTYPE. Rotation accumulated over one airborne window, in turns (1.0 = 360 degrees).
    /// Signed: sign carries direction, which is what separates a kickflip from a heelflip.
    /// </summary>
    public struct AirRotation
    {
        /// <summary>About the board's forward axis. Flips: kickflip / heelflip.</summary>
        public float FlipTurns;

        /// <summary>About the board's up axis, board only. Shuvits.</summary>
        public float ShuvTurns;

        /// <summary>About world up, whole body. Spins: 180 / 360.</summary>
        public float SpinTurns;

        /// <summary>About the board's right axis. Front/back flips — unnamed for now.</summary>
        public float PitchTurns;

        public float AirTime;

        public override string ToString() =>
            $"flip {FlipTurns:+0.00;-0.00} | shuv {ShuvTurns:+0.00;-0.00} | " +
            $"spin {SpinTurns:+0.00;-0.00} | pitch {PitchTurns:+0.00;-0.00} | air {AirTime:0.00}s";
    }

    /// <summary>
    /// PROTOTYPE. Names a trick from what the board actually did — no input grammar involved.
    ///
    /// Pure C#, no UnityEngine state, no MonoBehaviour: this is the piece that has to be
    /// testable headlessly per the map's 90/10 split, so it is written that way from the start
    /// even though the prototype around it is throwaway.
    ///
    /// Deliberately quantised. The name is a lookup on a rounded tuple, never a function of
    /// raw degrees, and anything unmatched is a generic "air" rather than a guessed name —
    /// silence beats a wrong name when the score depends on it.
    /// </summary>
    public static class TrickClassifier
    {
        /// <summary>
        /// How much of a turn must be completed before it is credited. Below this, the
        /// rotation is treated as noise — which it often is, since a ball or an opponent
        /// can spin the board without the player doing anything.
        /// </summary>
        public const float TurnThreshold = 0.75f;

        /// <summary>Airborne windows shorter than this never produce a named trick.</summary>
        public const float MinAirTime = 0.18f;

        /// <summary>
        /// Quantise a signed turn count to the nearest credited half-turn, or zero.
        /// 0.8 turns -> 1, -1.9 -> -2, 0.3 -> 0.
        /// </summary>
        public static int CreditedHalfTurns(float turns)
        {
            float halves = turns * 2f;
            int rounded = Mathf.RoundToInt(halves);
            if (rounded == 0)
            {
                return 0;
            }

            // Require the rotation to be at least TurnThreshold of the way into the
            // half-turn it is being rounded to, so jitter never mints a trick.
            float required = (Mathf.Abs(rounded) - 1 + TurnThreshold) / 2f;
            return Mathf.Abs(turns) >= required ? rounded : 0;
        }

        public static string Name(AirRotation r)
        {
            if (r.AirTime < MinAirTime)
            {
                return "—";
            }

            int flip = CreditedHalfTurns(r.FlipTurns);
            int shuv = CreditedHalfTurns(r.ShuvTurns);
            int spin = CreditedHalfTurns(r.SpinTurns);

            bool hasFlip = flip != 0;
            bool hasShuv = shuv != 0;
            bool hasSpin = spin != 0;

            if (!hasFlip && !hasShuv && !hasSpin)
            {
                return "Ollie";
            }

            // Compound: a flip plus board-only yaw is a varial.
            if (hasFlip && hasShuv)
            {
                return $"Varial {FlipName(flip)}";
            }

            if (hasFlip && hasSpin)
            {
                return $"{SpinName(spin)} {FlipName(flip)}";
            }

            if (hasFlip)
            {
                return FlipName(flip);
            }

            if (hasShuv && hasSpin)
            {
                return $"{SpinName(spin)} Shuvit";
            }

            if (hasShuv)
            {
                return ShuvName(shuv);
            }

            return SpinName(spin);
        }

        private static string FlipName(int halfTurns)
        {
            // A flip is a full rotation about the board's long axis. Half a flip is not
            // a trick, it is a crash, so anything odd degrades to "air".
            if (Math.Abs(halfTurns) < 2)
            {
                return "Air";
            }

            string baseName = halfTurns > 0 ? "Kickflip" : "Heelflip";
            int fullTurns = Math.Abs(halfTurns) / 2;
            return fullTurns > 1 ? $"Double {baseName}" : baseName;
        }

        private static string ShuvName(int halfTurns)
        {
            int abs = Math.Abs(halfTurns);
            return abs switch
            {
                1 => "Shuvit",
                2 => "360 Shuvit",
                _ => $"{abs * 180} Shuvit",
            };
        }

        private static string SpinName(int halfTurns)
        {
            int abs = Math.Abs(halfTurns);
            return $"{abs * 180}";
        }

        /// <summary>
        /// Grade a landing rather than gate it. Graded because in an arena a landing is
        /// routinely disturbed by the ball or the opponent rather than by player error,
        /// and a binary bail on every imperfect landing reads as the game being unfair.
        /// </summary>
        /// <param name="boardUpDotSurfaceUp">Board up vs surface normal, 1 = flat.</param>
        /// <param name="velocityAlignment">Velocity vs board forward, 1 = rolling straight.</param>
        public static LandingGrade Grade(float boardUpDotSurfaceUp, float velocityAlignment)
        {
            if (boardUpDotSurfaceUp < 0.45f || velocityAlignment < 0.2f)
            {
                return LandingGrade.Bail;
            }

            if (boardUpDotSurfaceUp < 0.85f || velocityAlignment < 0.75f)
            {
                return LandingGrade.Sketchy;
            }

            return LandingGrade.Clean;
        }
    }
}
