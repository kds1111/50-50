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
    /// </summary>
    public static class RespawnSpotFinder
    {
        private static readonly Collider[] Hits = new Collider[16];

        public struct Settings
        {
            public float[] Radii;
            public int[] SamplesPerRing;
            public float MaxGroundSlope;
            public Vector3 Clearance;
            public float ProbeHeight;
            public float MaxDrop;
        }

        /// <summary>
        /// True when a spot was found, with <paramref name="spot"/> set to it. False means the
        /// caller should fall back to the nearest safe point — nothing nearby will do, or the
        /// fall point was somewhere no search should start from.
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

            if (IsOutOfPlay(scene, fallPoint, settings))
            {
                return false;
            }

            (float X, float Z)[] offsets = RespawnPlacement.Offsets(settings.Radii, settings.SamplesPerRing);
            Quaternion turn = Quaternion.Euler(0f, facing, 0f);

            for (int i = 0; i < offsets.Length; i++)
            {
                // Rotated by the way you were facing, so the first sample of every ring is ahead
                // of where you were going rather than ahead of the world's Z axis.
                Vector3 candidate = fallPoint + (turn * new Vector3(offsets[i].X, 0f, offsets[i].Z));

                if (Standable(scene, candidate, settings, self, out Vector3 ground))
                {
                    spot = ground;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Out of bounds, or inside a goal. Out of bounds is "nothing underneath within the drop
        /// distance" — the arena is a floor with walls, so a point with no floor beneath it is a
        /// point outside the arena.
        /// </summary>
        private static bool IsOutOfPlay(PhysicsScene scene, Vector3 point, Settings settings)
        {
            Vector3 origin = point + (Vector3.up * settings.ProbeHeight);

            if (!scene.Raycast(origin, Vector3.down, out RaycastHit _, settings.ProbeHeight + settings.MaxDrop))
            {
                return true;
            }

            int found = Physics.OverlapBoxNonAlloc(
                point, settings.Clearance * 0.5f, Hits, Quaternion.identity, ~0, QueryTriggerInteraction.Collide);

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
        /// Whether a board could sit here: level enough ground, nothing solid in the way, and not
        /// a rail — landing a respawn on a grind line would lock you straight back onto it.
        /// </summary>
        private static bool Standable(
            PhysicsScene scene, Vector3 candidate, Settings settings, Transform self, out Vector3 ground)
        {
            ground = candidate;
            Vector3 origin = candidate + (Vector3.up * settings.ProbeHeight);

            if (!scene.Raycast(origin, Vector3.down, out RaycastHit hit, settings.ProbeHeight + settings.MaxDrop))
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

            Vector3 centre = hit.point + (Vector3.up * (settings.Clearance.y * 0.5f));

            int found = Physics.OverlapBoxNonAlloc(
                centre, settings.Clearance * 0.5f, Hits, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < found; i++)
            {
                Collider other = Hits[i];

                if (other == null || other.transform.IsChildOf(self) || other.transform == self)
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

            ground = hit.point;
            return true;
        }
    }
}
