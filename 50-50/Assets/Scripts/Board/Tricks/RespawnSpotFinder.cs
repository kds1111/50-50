using FiftyFifty.Ball;
using FiftyFifty.Board.Grinds;
using UnityEngine;

namespace FiftyFifty.Board.Tricks
{
    /// <summary>
    /// The physics half of #25: given the point where a player lost it, find somewhere clear to
    /// put them back. <see cref="RespawnPlacement"/> decides the order spots are tried in; this
    /// asks the world whether each one will do.
    ///
    /// A spot is no good if the ground under it is too steep, if the board would overlap anything
    /// solid there, if what it would stand on is a grind rail, or if the other player is already
    /// in it. A loose ball is **not** an obstruction — a respawning board simply shoves it, and
    /// treating it as solid would push you away from exactly the spot the play is at.
    ///
    /// Two places skip the search entirely: out of bounds, and inside a goal mouth. "Beside where
    /// you fell" is a wrong answer in both, however clear the ground is.
    ///
    /// Every query goes through the <see cref="PhysicsScene"/> it is handed, never the global
    /// `Physics` helpers: the editor probes run in their own scene, and a global query there sees
    /// an empty world and passes everything.
    /// </summary>
    public static class RespawnSpotFinder
    {
        // Sized well past what a spot can plausibly touch. A truncated result reads as "nothing
        // in the way", so the buffer errs large rather than small.
        //
        // Every query here states its mask and its trigger handling rather than taking Unity's
        // defaults. The project sets m_QueriesHitTriggers, so a defaulted raycast will happily
        // take a trigger volume for the floor and stand a board on thin air — while the overlap
        // beside it, which does pass Ignore, cannot see that same collider to reject it.
        private static readonly Collider[] Hits = new Collider[32];

        public struct Settings
        {
            public float[] Radii;
            public int[] SamplesPerRing;
            public float MaxGroundSlope;
            public Vector3 Clearance;

            /// <summary>How high the board sits above the ground it stands on.</summary>
            public float StandHeight;

            /// <summary>Lifts the clearance test off the floor so it does not graze the slab it stands on.</summary>
            public float Skin;

            public float ProbeHeight;

            /// <summary>How far a candidate spot may sit below the fall point and still count as beside it.</summary>
            public float MaxStepDown;

            /// <summary>How far down the out-of-bounds probe looks before calling it no floor at all.</summary>
            public float MaxDrop;
        }

        /// <summary>
        /// True when a spot was found, with <paramref name="spot"/> set to the pose the board
        /// should be placed at — already lifted to ride height, so what was tested is what is
        /// used. False means the caller should fall back to the nearest safe point.
        /// </summary>
        public static bool TryFind(
            PhysicsScene scene,
            Vector3 fallPoint,
            float facing,
            Settings settings,
            Transform self,
            out Vector3 spot)
        {
            spot = fallPoint;
            Quaternion turn = Quaternion.Euler(0f, facing, 0f);

            if (IsOutOfPlay(scene, fallPoint, turn, settings))
            {
                return false;
            }

            (float X, float Z)[] offsets = RespawnPlacement.Offsets(settings.Radii, settings.SamplesPerRing);

            for (int i = 0; i < offsets.Length; i++)
            {
                // Rotated by the way you were facing, so the first sample of every ring is ahead
                // of where you were going rather than ahead of the world's Z axis.
                Vector3 candidate = fallPoint + (turn * new Vector3(offsets[i].X, 0f, offsets[i].Z));

                // Every candidate answers the same question the fall point did. Asking it only of
                // the fall point left the rings free to walk into a goal: Standable ignores
                // triggers, so a goal mouth is invisible to it however clear the floor reads, and
                // a bail a metre outside the line came back standing in the net.
                if (IsOutOfPlay(scene, candidate, turn, settings))
                {
                    continue;
                }

                if (Standable(scene, candidate, turn, settings, self, out Vector3 stand))
                {
                    spot = stand;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Out of bounds, or inside a goal. Out of bounds is "nothing underneath within the drop
        /// distance" — the arena is a floor inside walls, so a point with no floor beneath it is
        /// a point outside the arena.
        /// </summary>
        private static bool IsOutOfPlay(PhysicsScene scene, Vector3 point, Quaternion turn, Settings settings)
        {
            Vector3 origin = point + (Vector3.up * settings.ProbeHeight);

            if (!scene.Raycast(
                    origin, Vector3.down, out RaycastHit _, settings.ProbeHeight + settings.MaxDrop,
                    ~0, QueryTriggerInteraction.Ignore))
            {
                return true;
            }

            int found = scene.OverlapBox(
                point, settings.Clearance * 0.5f, Hits, turn, ~0, QueryTriggerInteraction.Collide);

            for (int i = 0; i < found; i++)
            {
                if (Hits[i] != null && Hits[i].GetComponentInParent<GoalTargetVolume>() != null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether a board could sit here: ground within reach and level enough, nothing solid in
        /// the way, and not a rail — landing a respawn on a grind line would lock you straight
        /// back onto it.
        /// </summary>
        private static bool Standable(
            PhysicsScene scene,
            Vector3 candidate,
            Quaternion turn,
            Settings settings,
            Transform self,
            out Vector3 stand)
        {
            stand = candidate;
            Vector3 origin = candidate + (Vector3.up * settings.ProbeHeight);

            // Only as far down as a step: a spot six metres away and forty metres below is not
            // beside where you fell, it is off the edge of the thing you fell from.
            float reach = settings.ProbeHeight + settings.MaxStepDown;

            if (!scene.Raycast(
                    origin, Vector3.down, out RaycastHit hit, reach, ~0, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            if (Vector3.Angle(hit.normal, Vector3.up) > settings.MaxGroundSlope)
            {
                return false;
            }

            if (hit.collider != null && hit.collider.GetComponentInParent<GrindRail>() != null)
            {
                return false;
            }

            // Lifted by a skin so the box does not graze the slab it stands on, or the next slab
            // along — the arena is flat boxes with seams, and every seam would otherwise reject.
            Vector3 centre = hit.point + (Vector3.up * (settings.Skin + (settings.Clearance.y * 0.5f)));

            int found = scene.OverlapBox(
                centre, settings.Clearance * 0.5f, Hits, turn, ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < found; i++)
            {
                Collider other = Hits[i];

                if (other == null || other.transform.IsChildOf(self))
                {
                    continue;
                }

                // The ground itself is not in the way, and neither is the ball.
                if (other == hit.collider || other.GetComponentInParent<BallController>() != null)
                {
                    continue;
                }

                return false;
            }

            // The pose the board is placed at, not the floor under it: what was tested is what
            // gets used, so a respawn never arrives sunk into the ground.
            stand = hit.point + (Vector3.up * settings.StandHeight);
            return true;
        }
    }
}
