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

        /// <summary>
        /// Where a player is put back, captured when the point registers rather than read at the
        /// moment of a bail (#26). The search runs inside the physics step, where a transform
        /// gives the rendered pose; safe points are scenery and never move, so reading them once
        /// is both correct and cheaper.
        /// </summary>
        public Vector3 Position { get; private set; }

        /// <summary>The heading a player faces on being put back here.</summary>
        public float Yaw { get; private set; }

        private void OnEnable()
        {
            Transform t = transform;
            Position = t.position;
            Yaw = t.eulerAngles.y;
            All.Add(this);
        }

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

                float sqr = (candidate.Position - position).sqrMagnitude;

                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = candidate;
                }
            }

            return best;
        }

        /// <summary>
        /// In play, drawn from the captured pose rather than the live transform — so what the
        /// editor shows is where a player would actually be put back. Dragging a safe point
        /// during Play moves the gizmo nowhere, which is the honest picture: the capture happened
        /// when it registered. Move them while stopped.
        /// </summary>
        private void OnDrawGizmos()
        {
            Vector3 at = Application.isPlaying ? Position : transform.position;
            Vector3 facing = Application.isPlaying
                ? Quaternion.Euler(0f, Yaw, 0f) * Vector3.forward
                : transform.forward;

            Gizmos.color = new Color(0.3f, 0.9f, 0.4f, 0.9f);
            Gizmos.DrawWireSphere(at, GizmoSize * 0.3f);
            Gizmos.DrawRay(at, facing * GizmoSize);
        }
    }
}
