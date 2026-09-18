using System.Collections.Generic;
using UnityEngine;

namespace FiftyFifty.Board.Tricks
{
    /// <summary>
    /// A place a bailed player can be put back on their feet.
    ///
    /// #6 settled that a bail is a knockdown in place followed by a respawn at the NEAREST safe
    /// point — nearest, deliberately, rather than a fixed spawn. A fixed spawn would teleport a
    /// player away from the ball, so one mistake would cost them the bank and the play, and
    /// nobody would ever attempt a flip near a goal, which is exactly where they should be
    /// attempted.
    ///
    /// Placing these is #9's job: the arena owes safe points the way it owes ramps. Until it
    /// exists, a scene with none falls back to the board's own spawn, which is what Respawn()
    /// already did.
    ///
    /// Drop the component on an empty GameObject, point its forward where a player should face,
    /// and it registers itself.
    /// </summary>
    public class SafePoint : MonoBehaviour
    {
        private static readonly List<SafePoint> All = new();

        [Tooltip("Drawn in the editor so a scene's safe points can be read at a glance.")]
        public float GizmoSize = 1.2f;

        private void OnEnable() => All.Add(this);

        private void OnDisable() => All.Remove(this);

        /// <summary>
        /// Nearest registered safe point to a position, or null when a scene has none.
        /// </summary>
        public static SafePoint Nearest(Vector3 position)
        {
            SafePoint best = null;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < All.Count; i++)
            {
                SafePoint candidate = All[i];

                if (candidate == null)
                {
                    continue;
                }

                float sqr = (candidate.transform.position - position).sqrMagnitude;

                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = candidate;
                }
            }

            return best;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 0.9f, 0.4f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, GizmoSize * 0.3f);
            Gizmos.DrawRay(transform.position, transform.forward * GizmoSize);
        }
    }
}
