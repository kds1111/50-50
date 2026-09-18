using System.Collections.Generic;
using UnityEngine;

namespace FiftyFifty.Board.Grinds
{
    /// <summary>
    /// Makes a box grindable (#12). Drop it on anything with a BoxCollider and it works out its
    /// own grind lines from the box: along the top, down the long axis.
    ///
    ///   - A narrow box (a rail's bar) is one line along the middle of its top.
    ///   - A wide box (a ledge, a hubba) is two lines, one down each long top edge. The middle of
    ///     a ledge is not a grind — the board just rides on it, as it always has.
    ///
    /// Lock-on reads these lines rather than colliders, which is the whole detection model: a
    /// board is snapped onto a line, never balanced on geometry by the solver (#16's lesson).
    ///
    /// Lines are built from the transform once, when enabled. That is fine for scenery that does
    /// not move and is the only transform read anywhere in the grind system — the physics step
    /// itself reads the rigidbody, never a transform.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class GrindRail : MonoBehaviour
    {
        /// <summary>One straight grind line in world space.</summary>
        public struct Line
        {
            public Vector3 A;
            public Vector3 Dir;
            public float Length;

            public Vector3 PointAt(float t) => A + (Dir * t);

            /// <summary>Compass heading of the line, degrees — the same convention as the board's.</summary>
            public float Heading => Mathf.Atan2(Dir.x, Dir.z) * Mathf.Rad2Deg;
        }

        private static readonly List<GrindRail> Registered = new();

        public static IReadOnlyList<GrindRail> All => Registered;

        [Tooltip("Narrower than this, in metres, and the top is one line down the middle (a rail). " +
                 "Wider, and each long top edge is its own line (a ledge).")]
        public float EdgeSplitWidth = 0.4f;

        [Tooltip("How far in from a ledge's edge its grind line sits, in metres.")]
        public float EdgeInset = 0.05f;

        private Line[] _lines = System.Array.Empty<Line>();

        public IReadOnlyList<Line> Lines => _lines;

        private void OnEnable()
        {
            Rebuild();
            Registered.Add(this);
        }

        private void OnDisable() => Registered.Remove(this);

        public void Rebuild()
        {
            var box = GetComponent<BoxCollider>();
            Vector3 c = box.center;
            Vector3 s = box.size;

            float lengthX = transform.TransformVector(Vector3.right * s.x).magnitude;
            float lengthZ = transform.TransformVector(Vector3.forward * s.z).magnitude;
            bool alongZ = lengthZ >= lengthX;

            Vector3 along = alongZ ? Vector3.forward : Vector3.right;
            Vector3 across = alongZ ? Vector3.right : Vector3.forward;
            float halfAlong = (alongZ ? s.z : s.x) * 0.5f;
            float halfAcross = (alongZ ? s.x : s.z) * 0.5f;
            float worldWidth = alongZ ? lengthX : lengthZ;

            Vector3 top = c + (Vector3.up * (s.y * 0.5f));

            if (worldWidth < EdgeSplitWidth)
            {
                _lines = new[] { Build(top, along, halfAlong) };
                return;
            }

            // Inset is in metres; convert to the box's local units across its width.
            float localPerMetre = (halfAcross * 2f) / Mathf.Max(0.0001f, worldWidth);
            float offset = halfAcross - (EdgeInset * localPerMetre);

            _lines = new[]
            {
                Build(top + (across * offset), along, halfAlong),
                Build(top - (across * offset), along, halfAlong),
            };
        }

        private Line Build(Vector3 localMid, Vector3 localAlong, float halfLength)
        {
            Vector3 a = transform.TransformPoint(localMid - (localAlong * halfLength));
            Vector3 b = transform.TransformPoint(localMid + (localAlong * halfLength));
            Vector3 d = b - a;

            return new Line { A = a, Dir = d.normalized, Length = d.magnitude };
        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying && TryGetComponent(out BoxCollider _))
            {
                Rebuild();
            }

            Gizmos.color = new Color(1f, 0.8f, 0.1f, 0.95f);

            foreach (Line line in _lines)
            {
                Gizmos.DrawLine(line.A, line.PointAt(line.Length));
            }
        }
    }
}
