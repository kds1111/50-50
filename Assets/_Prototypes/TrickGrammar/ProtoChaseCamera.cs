using UnityEngine;

namespace Prototypes.TrickGrammar
{
    /// <summary>PROTOTYPE. Chase camera, no cinemachine, no ceremony.</summary>
    public class ProtoChaseCamera : MonoBehaviour
    {
        public Transform Target;
        public Vector3 Offset = new Vector3(0f, 2.2f, -5.5f);
        public float Damping = 6f;

        private void LateUpdate()
        {
            if (Target == null)
            {
                return;
            }

            // Follow the target's heading on the ground plane only — a camera that rolls
            // with a flipping board makes every trick unreadable.
            Vector3 flatForward = Vector3.ProjectOnPlane(Target.forward, Vector3.up);
            if (flatForward.sqrMagnitude < 0.01f)
            {
                flatForward = transform.forward;
            }

            Quaternion heading = Quaternion.LookRotation(flatForward.normalized, Vector3.up);
            Vector3 wanted = Target.position + (heading * Offset);

            transform.position = Vector3.Lerp(transform.position, wanted, Damping * Time.deltaTime);
            transform.LookAt(Target.position + (Vector3.up * 0.6f));
        }
    }
}
