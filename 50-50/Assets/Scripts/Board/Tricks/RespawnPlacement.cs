using System;

namespace FiftyFifty.Board.Tricks
{
    /// <summary>
    /// Where a bail puts you back (#25). A bail leaves you **where you lost it**: the fall point
    /// is captured the moment the bail fires, not where the board slid to afterwards — the slide
    /// is the picture of the crash, and the respawn is what undoes it.
    ///
    /// This holds the two parts of that rule which are geometry rather than physics: the order
    /// spots are tried in, and which way you face when you arrive. Whether a spot is actually
    /// clear — slope, overlap, a rail underfoot, the opponent standing in it — needs the physics
    /// scene and lives in <see cref="RespawnSpotFinder"/>.
    ///
    /// Plain C#, no Unity types, so the rule is testable headlessly (the map's 90/10 split).
    /// </summary>
    public static class RespawnPlacement
    {
        /// <summary>
        /// The spots to try, in order, as offsets from the fall point on the ground plane. The
        /// fall point itself always comes first; then rings fan outward, nearest ring first, so
        /// a bail never costs you more ground than the mistake did.
        /// </summary>
        public static (float X, float Z)[] Offsets(float[] radii, int[] samplesPerRing)
        {
            if (radii == null || samplesPerRing == null)
            {
                return new[] { (0f, 0f) };
            }

            int rings = Math.Min(radii.Length, samplesPerRing.Length);
            int total = 1;

            for (int i = 0; i < rings; i++)
            {
                total += Math.Max(0, samplesPerRing[i]);
            }

            var spots = new (float X, float Z)[total];
            spots[0] = (0f, 0f);
            int next = 1;

            for (int i = 0; i < rings; i++)
            {
                int samples = Math.Max(0, samplesPerRing[i]);
                float radius = radii[i];

                for (int s = 0; s < samples; s++)
                {
                    // Starting straight ahead and turning, so the first sample of every ring is
                    // the one directly in front of where the fall point faces.
                    double turn = 2.0 * Math.PI * s / samples;
                    spots[next++] = ((float)(Math.Sin(turn) * radius), (float)(Math.Cos(turn) * radius));
                }
            }

            return spots;
        }

        /// <summary>
        /// Which way you face on coming back: the direction you were travelling when you lost it,
        /// or the board's own heading when you were too slow for travel to mean anything.
        ///
        /// The threshold is this rule's own, exposed on the trick controller. Below it, travel
        /// direction is a slide or a shove rather than an intention, and facing you that way on
        /// return would aim you at whatever knocked you over.
        /// </summary>
        public static float Facing(float speed, float travelHeading, float boardHeading, float travelSpeedFloor) =>
            speed >= travelSpeedFloor ? travelHeading : boardHeading;
    }
}
