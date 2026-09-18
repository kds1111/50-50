using FiftyFifty.Ball;
using FiftyFifty.Board;
using FiftyFifty.Scoring;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FiftyFifty.EditorTools
{
    /// <summary>
    /// Puts the bank (#20) into a scene without rebuilding it.
    ///
    /// Rebuilding a scene from the 50-50 menu wipes anything hand-tuned in it, so this is the
    /// non-destructive way in: it only ADDS what is missing and assigns sides, and running it
    /// twice changes nothing. The scene builders call the same code, so a rebuilt scene and a
    /// wired one come out identical.
    ///
    /// Sides follow one convention, the one <see cref="Side"/> documents: side A starts at the
    /// -Z end and attacks +Z. So a board spawned at negative Z plays for A, and the goal at the
    /// -Z end is A's to defend.
    ///
    /// Menu: 50-50 > Wire Scoring Into Open Scene. Undoable; save the scene afterwards.
    /// </summary>
    public static class ScoringWiring
    {
        [MenuItem("50-50/Wire Scoring Into Open Scene")]
        public static void WireOpenScene()
        {
            int banks = 0;
            int goals = 0;

            foreach (BoardController board in Object.FindObjectsByType<BoardController>(FindObjectsSortMode.None))
            {
                if (board.GetComponent<PlayerBank>() != null)
                {
                    continue;
                }

                var bank = Undo.AddComponent<PlayerBank>(board.gameObject);
                bank.Side = SideAt(board.transform.position);
                banks++;
            }

            foreach (GoalTargetVolume goal in Object.FindObjectsByType<GoalTargetVolume>(FindObjectsSortMode.None))
            {
                Side side = SideAt(goal.transform.position);

                if (goal.DefendedBy == side)
                {
                    continue;
                }

                Undo.RecordObject(goal, "Assign goal side");
                goal.DefendedBy = side;
                goals++;
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[50-50] Scoring wired: {banks} bank(s) added, {goals} goal side(s) set. Save the scene to keep it.");
        }

        /// <summary>The side that owns the half of the arena a position sits in.</summary>
        public static Side SideAt(Vector3 position) => position.z < 0f ? Side.A : Side.B;
    }
}
