using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FiftyFifty.EditorTools
{
    /// <summary>
    /// Borrowing the editor for a moment, and giving it back.
    ///
    /// The probes need an empty scene to measure in, and the scene builders overwrite one. Both
    /// used to call NewScene directly, which has two nasty edges: it discards unsaved changes to
    /// whatever scene you had open without asking, and it destroys the objects the Inspector is
    /// currently showing, which throws a wall of SerializedObjectNotCreatableException from deep
    /// inside Unity's own inspectors.
    ///
    /// So: ask about unsaved work, drop the selection, do the job, and put the scene you were in
    /// back when it is done.
    /// </summary>
    public static class ProbeScene
    {
        /// <summary>
        /// Clears the way and returns the scene to restore afterwards, or null if the user
        /// cancelled at the save prompt — in which case the caller must not proceed.
        /// </summary>
        public static bool Begin(out string sceneToRestore)
        {
            sceneToRestore = null;

            // Ask before throwing away work. Returns false only if the user hits Cancel.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return false;
            }

            // The Inspector holds references to objects that are about to stop existing.
            Selection.objects = new Object[0];

            Scene active = SceneManager.GetActiveScene();
            sceneToRestore = string.IsNullOrEmpty(active.path) ? null : active.path;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            return true;
        }

        /// <summary>Puts the editor back where it was.</summary>
        public static void End(string sceneToRestore)
        {
            Selection.objects = new Object[0];

            if (string.IsNullOrEmpty(sceneToRestore))
            {
                return;
            }

            EditorSceneManager.OpenScene(sceneToRestore, OpenSceneMode.Single);
        }
    }
}
