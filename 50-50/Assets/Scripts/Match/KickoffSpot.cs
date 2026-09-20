using UnityEngine;

namespace FiftyFifty.Match
{
    public enum KickoffSpotKind
    {
        SideA,
        SideB,
        Ball,
    }

    /// <summary>
    /// Where a kickoff puts someone (#24). One per side, plus one for the ball. Drag them anywhere;
    /// a player's spot faces the way its forward arrow points.
    ///
    /// With none in the scene the director falls back to the scene's own layout: player one where
    /// their board starts, player two mirrored through the centre, the ball at its spawn.
    /// </summary>
    public class KickoffSpot : MonoBehaviour
    {
        public KickoffSpotKind Kind = KickoffSpotKind.SideA;

        private void OnDrawGizmos()
        {
            Gizmos.color = Kind switch
            {
                KickoffSpotKind.SideA => new Color(0.25f, 0.55f, 1f, 0.95f),
                KickoffSpotKind.SideB => new Color(1f, 0.4f, 0.25f, 0.95f),
                _ => new Color(1f, 1f, 1f, 0.95f),
            };

            Gizmos.DrawWireCube(transform.position, new Vector3(0.8f, 0.2f, 0.8f));

            if (Kind != KickoffSpotKind.Ball)
            {
                Gizmos.DrawRay(transform.position, transform.forward * 1.5f);
            }
        }
    }
}
