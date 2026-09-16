using FiftyFifty.Board;
using UnityEngine;

namespace FiftyFifty.CameraRig
{
    /// <summary>
    /// Chase camera that follows the board's heading on the ground plane, with right-stick
    /// orbit on top.
    ///
    /// It deliberately ignores the board's roll and pitch: a camera that tumbles with a
    /// flipping board makes air unreadable, and unreadable air means you cannot tell whether
    /// a landing was your fault.
    /// </summary>
    public class BoardChaseCamera : MonoBehaviour
    {
        [Header("Target")]
        public Transform Target;

        [Tooltip("Optional. Used to read the camera stick. Found on the target if empty.")]
        public BoardInputSource InputSource;

        [Header("Framing")]
        [Tooltip("Where the camera sits relative to the board's heading.")]
        public Vector3 Offset = new Vector3(0f, 2.1f, -5f);

        [Tooltip("How high above the board the camera aims.")]
        public float LookAtHeight = 0.7f;

        [Header("Follow")]
        [Tooltip("How quickly the camera catches up in position. Higher = stiffer.")]
        public float PositionDamping = 8f;

        [Tooltip("How quickly the camera swings to the board's heading. Lower = lazier.")]
        public float HeadingDamping = 4f;

        [Header("Orbit (right stick)")]
        public float OrbitSpeed = 140f;
        public float PitchSpeed = 90f;
        public float MinPitch = -15f;
        public float MaxPitch = 55f;

        [Tooltip("Seconds of no input before the camera drifts back behind the board. 0 = never.")]
        public float RecenterDelay = 1.2f;

        public float RecenterSpeed = 2.5f;

        private float _orbitYaw;
        private float _orbitPitch;
        private float _idleTime;
        private Quaternion _heading = Quaternion.identity;

        private void Start()
        {
            if (Target != null && InputSource == null)
            {
                InputSource = Target.GetComponent<BoardInputSource>();
            }

            if (Target != null)
            {
                _heading = FlatHeading();
            }
        }

        private void LateUpdate()
        {
            if (Target == null)
            {
                return;
            }

            float dt = Time.deltaTime;

            // Read the stick without consuming the board's input latches: the camera must
            // never eat a push or an ollie. BoardInputSource.Read clears one-shots, so the
            // camera reads the device separately via whatever the source exposes publicly.
            Vector2 look = CameraLook();

            if (look.sqrMagnitude > 0.0001f)
            {
                _orbitYaw += look.x * OrbitSpeed * dt;
                _orbitPitch = Mathf.Clamp(_orbitPitch - (look.y * PitchSpeed * dt), MinPitch, MaxPitch);
                _idleTime = 0f;
            }
            else
            {
                _idleTime += dt;
                if (RecenterDelay > 0f && _idleTime > RecenterDelay)
                {
                    _orbitYaw = Mathf.Lerp(_orbitYaw, 0f, RecenterSpeed * dt);
                    _orbitPitch = Mathf.Lerp(_orbitPitch, 0f, RecenterSpeed * dt);
                }
            }

            _heading = Quaternion.Slerp(_heading, FlatHeading(), HeadingDamping * dt);

            Quaternion orbit = Quaternion.Euler(_orbitPitch, _orbitYaw, 0f);
            Vector3 wanted = Target.position + (_heading * orbit * Offset);

            transform.position = Vector3.Lerp(transform.position, wanted, PositionDamping * dt);
            transform.LookAt(Target.position + (Vector3.up * LookAtHeight));
        }

        /// <summary>The board's facing, flattened — roll and pitch are deliberately discarded.</summary>
        private Quaternion FlatHeading()
        {
            Vector3 flat = Vector3.ProjectOnPlane(Target.forward, Vector3.up);
            if (flat.sqrMagnitude < 0.001f)
            {
                // Board is nose-up (mid-trick). Fall back to the last good heading.
                return _heading;
            }

            return Quaternion.LookRotation(flat.normalized, Vector3.up);
        }

        private Vector2 CameraLook()
        {
            var player = InputSource as PlayerBoardInput;
            return player != null ? player.PeekCameraLook() : Vector2.zero;
        }
    }
}
