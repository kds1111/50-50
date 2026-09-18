using FiftyFifty.Board;
using FiftyFifty.Board.Grinds;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FiftyFifty.EditorTools
{
    /// <summary>
    /// Puts grinds (#12) into a scene without rebuilding it, for the same reason as
    /// <see cref="ScoringWiring"/>: a rebuild wipes anything hand-tuned. Only adds what is missing;
    /// running it twice changes nothing.
    ///
    /// Grindable is recognised by the names the arena builder gives things: the "Bar" of a rail,
    /// and anything called Ledge or Hubba. Anything else can be made grindable by hand — drop a
    /// GrindRail on any object with a BoxCollider.
    ///
    /// Menu: 50-50 > Wire Grinds Into Open Scene. Undoable; save the scene afterwards.
    /// </summary>
    public static class GrindWiring
    {
        [MenuItem("50-50/Wire Grinds Into Open Scene")]
        public static void WireOpenScene()
        {
            int boards = 0;
            int rails = 0;

            foreach (BoardController board in Object.FindObjectsByType<BoardController>(FindObjectsSortMode.None))
            {
                if (board.GetComponent<BoardGrindController>() == null)
                {
                    Undo.AddComponent<BoardGrindController>(board.gameObject);
                    boards++;
                }
            }

            foreach (BoxCollider box in Object.FindObjectsByType<BoxCollider>(FindObjectsSortMode.None))
            {
                if (!IsGrindable(box.gameObject) || box.GetComponent<GrindRail>() != null)
                {
                    continue;
                }

                Undo.AddComponent<GrindRail>(box.gameObject);
                rails++;
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[50-50] Grinds wired: {boards} board(s), {rails} rail(s)/ledge(s). Save the scene to keep it.");
        }

        private static bool IsGrindable(GameObject go)
        {
            string name = go.name;

            if (name.StartsWith("Ledge") || name.StartsWith("Hubba"))
            {
                return true;
            }

            Transform parent = go.transform.parent;
            return name == "Bar" && parent != null && parent.name.StartsWith("Rail");
        }
    }
}
