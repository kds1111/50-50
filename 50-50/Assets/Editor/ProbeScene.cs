using System;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FiftyFifty.EditorTools
{
    /// <summary>
    /// A private world to measure in, alongside the scene you are working in rather than on top
    /// of it.
    ///
    /// The probes used to open an empty scene, which replaced whatever you had open. That threw
    /// away unsaved work and — because the objects the Inspector was showing stopped existing
    /// underneath it — produced a wall of SerializedObjectNotCreatableException and
    /// MissingReferenceException from inside Unity's own inspectors. Clearing the selection first
    /// was not enough: the Inspector rebuilds its editors a frame later, by which point the
    /// targets are dead, and a locked Inspector ignores the selection entirely. The fix is not to
    /// destroy anything of yours in the first place.
    ///
    /// A preview scene is Unity's own answer to this — it is what the prefab editor works in.
    /// Measured before relying on it: it carries its own physics scene (so Step moves nothing
    /// outside it), creating and moving objects does not mark your open scene dirty, and closing
    /// it leaves your scene exactly as it was. The project's global simulation mode is left alone
    /// too, because a local physics scene only ever advances when you ask it to.
    /// </summary>
    public sealed class ProbeWorld : IDisposable
    {
        private readonly Scene _scene;
        private readonly PhysicsScene _physics;
        private bool _closed;

        public ProbeWorld()
        {
            _scene = EditorSceneManager.NewPreviewScene();
            _physics = _scene.GetPhysicsScene();
        }

        /// <summary>Takes an object out of whatever scene it was born in and into this one.</summary>
        public GameObject Adopt(GameObject go)
        {
            EditorSceneManager.MoveGameObjectToScene(go, _scene);
            return go;
        }

        public GameObject CreatePrimitive(PrimitiveType type)
        {
            return Adopt(GameObject.CreatePrimitive(type));
        }

        public GameObject CreateObject(string name)
        {
            return Adopt(new GameObject(name));
        }

        /// <summary>Advance only this world. Nothing else in the editor moves.</summary>
        public void Step(float dt)
        {
            _physics.Simulate(dt);
        }

        public void Dispose()
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            EditorSceneManager.ClosePreviewScene(_scene);
        }
    }
}
