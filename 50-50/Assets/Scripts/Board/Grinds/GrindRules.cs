using System;

namespace FiftyFifty.Board.Grinds
{
    /// <summary>How a grind ended.</summary>
    public enum GrindExit
    {
        /// <summary>Ollied off. The deliberate exit.</summary>
        Popped,

        /// <summary>Ran out of rail. Clean, like skating off the end of a real one (#12 Q7).</summary>
        RodeOffEnd,

        /// <summary>Friction took the speed below the stall line. A fall.</summary>
        Stalled,

        /// <summary>The ball hit the grinder. A fall (#12 Q4).</summary>
        KnockedOff,

        /// <summary>Something else ended it — a bail from elsewhere, a respawn. No credit, no fall.</summary>
        Interrupted,
    }

    public enum GrindOutcome
    {
        /// <summary>Nothing earned and nothing lost.</summary>
        Nothing,

        /// <summary>Bank the grind.</summary>
        Credit,

        /// <summary>A bail — the same one a trick uses.</summary>
        Fall,
    }

    /// <summary>Everything lock-on needs to know about one candidate line, as plain numbers.</summary>
    public struct GrindLockProbe
    {
        public bool Airborne;

        /// <summary>m/s, up positive. Lock-on only happens coming down.</summary>
        public float VerticalSpeed;

        /// <summary>Metres sideways from the line, flat.</summary>
        public float Lateral;

        /// <summary>Metres above (+) or below (−) where the board would ride on the line.</summary>
        public float Vertical;

        /// <summary>Degrees between the flat travel direction and the line, 0 to 90.</summary>
        public float TravelAngle;

        /// <summary>m/s along the line, either way.</summary>
        public float AlongSpeed;
    }

    public sealed class GrindLockLimits
    {
        public float SnapRadius = 0.35f;
        public float SnapHeight = 0.3f;
        public float MaxTravelAngle = 45f;
        public float MinLockSpeed = 2f;
    }

    /// <summary>
    /// The rules of a grind, apart from the physics of holding one (#12).
    ///
    /// Naming reads the board's angle to the rail at lock-on, and only that: the air controls
    /// turn the board flat, with no pitch and no simulated trucks, so the one thing a player
    /// chooses is how far round they are. Two names, nearest wins. Frontside, backside and
    /// lipslide are deliberately absent — they hang on the sign of the approach, which is where
    /// #4's shipped naming bugs live.
    ///
    /// Plain C#, no Unity types, so it is tested headlessly.
    /// </summary>
    public static class GrindRules
    {
        public const string FiftyFifty = "50-50";
        public const string Boardslide = "Boardslide";

        /// <summary>Degrees the board sits off the rail's line, 0 (along it, either end first) to 90 (across it).</summary>
        public static float AngleOffLine(float boardHeading, float railHeading)
        {
            float d = Math.Abs(Mod(boardHeading - railHeading, 180f));
            return Math.Min(d, 180f - d);
        }

        /// <summary>Within 45° of the line is a 50-50; closer to across it is a boardslide.</summary>
        public static string Name(float boardHeading, float railHeading) =>
            AngleOffLine(boardHeading, railHeading) <= 45f ? FiftyFifty : Boardslide;

        /// <summary>
        /// The heading the board snaps to on lock-on: exactly along the rail for a 50-50, exactly
        /// across it for a boardslide, whichever way round is nearest to where it already points.
        /// Returned as the board's own heading plus the smallest turn, so it never jumps a whole
        /// revolution.
        /// </summary>
        public static float SnapHeading(float boardHeading, float railHeading)
        {
            float off = DeltaAngle(railHeading, boardHeading);

            float target = Name(boardHeading, railHeading) == FiftyFifty
                ? (float)Math.Round(off / 180f) * 180f
                : (off >= 0f ? 90f : -90f);

            return boardHeading + (target - off);
        }

        public static bool CanLock(GrindLockProbe p, GrindLockLimits limits) =>
            p.Airborne
            && p.VerticalSpeed <= 0f
            && p.Lateral <= limits.SnapRadius
            && Math.Abs(p.Vertical) <= limits.SnapHeight
            && p.TravelAngle <= limits.MaxTravelAngle
            && p.AlongSpeed >= limits.MinLockSpeed;

        /// <summary>
        /// What an exit means. Under the minimum time a grind is a graze, and a graze is
        /// forgiven whatever ended it: nothing earned, nothing lost (#8).
        /// </summary>
        public static GrindOutcome Resolve(GrindExit exit, bool graze)
        {
            if (exit == GrindExit.Interrupted || graze)
            {
                return GrindOutcome.Nothing;
            }

            return exit == GrindExit.Popped || exit == GrindExit.RodeOffEnd
                ? GrindOutcome.Credit
                : GrindOutcome.Fall;
        }

        /// <summary>Signed smallest turn from a to b, in [-180, 180).</summary>
        public static float DeltaAngle(float from, float to) => Mod(to - from + 180f, 360f) - 180f;

        private static float Mod(float value, float m) => ((value % m) + m) % m;
    }
}
