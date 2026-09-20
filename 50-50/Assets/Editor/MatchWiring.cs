using FiftyFifty.Ball;
using FiftyFifty.Board;
using FiftyFifty.Match;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FiftyFifty.EditorTools
{
    /// <summary>
    /// Puts a match (#24) into a scene without rebuilding it — the arena is hand-placed now, and a
    /// rebuild would throw that away. Adds a MatchDirector and three KickoffSpots if they are
    /// missing; running it twice changes nothing.
    ///
    /// The spots start where the scene already has things: side A on the board as it sits, side B
    /// mirrored through the centre (the arena is rotationally symmetric, #9), the ball on the
    /// ball. Drag them anywhere afterwards.
    ///
    /// Player two is never added here. The director clones player one at Play time, so the scene
    /// stays single-player and a mode menu can pick Free Skate or Match Play later.
    ///
    /// Menu: 50-50 > Wire Match Into Open Scene. Undoable; save the scene afterwards.
    /// </summary>
    public static class MatchWiring
    {
        [MenuItem("50-50/Wire Match Into Open Scene")]
        public static void WireOpenScene()
        {
            bool addedDirector = false;
            int addedSpots = 0;

            if (Object.FindFirstObjectByType<MatchDirector>() == null)
            {
                var go = new GameObject("Match Director");
                Undo.RegisterCreatedObjectUndo(go, "Add match director");
                go.AddComponent<MatchDirector>();
                addedDirector = true;
            }

            if (Object.FindObjectsByType<KickoffSpot>(FindObjectsSortMode.None).Length == 0)
            {
                var board = Object.FindFirstObjectByType<BoardController>();
                var ball = Object.FindFirstObjectByType<BallController>();

                Vector3 a = board != null ? board.transform.position : new Vector3(0f, 0.4f, -10f);
                float yaw = board != null ? board.transform.eulerAngles.y : 0f;

                AddSpot(KickoffSpotKind.SideA, a, yaw);
                AddSpot(KickoffSpotKind.SideB, new Vector3(-a.x, a.y, -a.z), yaw + 180f);
                AddSpot(KickoffSpotKind.Ball, ball != null ? ball.transform.position : new Vector3(0f, 1.5f, 0f), 0f);
                addedSpots = 3;
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[50-50] Match wired: {(addedDirector ? "director added" : "director already there")}, " +
                      $"{addedSpots} kickoff spot(s) added. Save the scene to keep it.");
        }

        private static void AddSpot(KickoffSpotKind kind, Vector3 position, float yaw)
        {
            var go = new GameObject($"Kickoff {kind}");
            Undo.RegisterCreatedObjectUndo(go, "Add kickoff spot");
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            go.AddComponent<KickoffSpot>().Kind = kind;
        }
    }
}
