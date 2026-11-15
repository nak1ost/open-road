using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaceGame.World
{
    /// <summary>
    /// Floating origin for the endless map: when the player drives far from (0,0,0) the whole scene is moved back,
    /// so float precision stays good (no shaking cars at 50 km).
    /// global = local + Offset. Offsets are whole kilometres, so they are exact in float up to thousands of km.
    /// The map, race routes and LAN messages use GLOBAL coordinates; transforms are LOCAL.
    /// </summary>
    public static class OriginShift
    {
        public const float Threshold = 1500f;
        public const float Step = 1000f;

        public static Vector3 Offset { get; private set; }

        public static Vector3 ToGlobal(Vector3 local) => local + Offset;
        public static Vector3 ToLocal(Vector3 global) => global - Offset;

        public static void Reset() => Offset = Vector3.zero;

        /// <summary>Moves every root object of the scene if the focus is too far from the origin. Returns true if shifted.</summary>
        public static bool Check(Vector3 focusLocal)
        {
            if (Mathf.Abs(focusLocal.x) < Threshold && Mathf.Abs(focusLocal.z) < Threshold) return false;
            var delta = new Vector3(Mathf.Round(focusLocal.x / Step) * Step, 0f, Mathf.Round(focusLocal.z / Step) * Step);
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                root.transform.position -= delta;
            Offset += delta;
            Physics.SyncTransforms();
            return true;
        }
    }
}
