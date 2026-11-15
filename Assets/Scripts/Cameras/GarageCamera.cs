using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RaceGame.Cameras
{
    /// <summary>
    /// Garage camera: orbits the car. Drag with finger/mouse to rotate,
    /// mouse wheel to zoom; slowly turns by itself when idle.
    /// </summary>
    public class GarageCamera : MonoBehaviour
    {
        public Transform target;
        public float carLength = 4.5f;
        public float carHeight = 1.5f;
        /// <summary>Returns true if a screen point (pixels, origin bottom-left) is over the garage UI.</summary>
        public Func<Vector2, bool> isOverUi;

        float yaw = 145f, pitch = 12f, zoom = 1f;
        bool dragging;
        Vector2 lastPos;
        float idleTime;

        public void ResetView()
        {
            yaw = 145f; pitch = 12f; zoom = 1f; idleTime = 0f;
        }

        void LateUpdate()
        {
            if (target == null) return;

            bool pressed = false;
            Vector2 pos = Vector2.zero;
            float scroll = 0f;
#if ENABLE_INPUT_SYSTEM
            var pointer = Pointer.current;
            if (pointer != null)
            {
                pressed = pointer.press.isPressed;
                pos = pointer.position.ReadValue();
            }
            if (Mouse.current != null)
            {
                float raw = Mouse.current.scroll.ReadValue().y; // 120 per notch on some platforms, 1 on others
                if (Mathf.Abs(raw) > 0.01f) scroll = Mathf.Sign(raw);
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            pressed = UnityEngine.Input.GetMouseButton(0);
            pos = UnityEngine.Input.mousePosition;
            scroll = UnityEngine.Input.mouseScrollDelta.y;
#endif
            if (pressed && !dragging && (isOverUi == null || !isOverUi(pos)))
            {
                dragging = true;
                lastPos = pos;
            }
            if (!pressed) dragging = false;

            if (dragging)
            {
                Vector2 d = pos - lastPos;
                lastPos = pos;
                float k = 180f / Mathf.Max(1, Screen.height);
                yaw += d.x * k;
                pitch = Mathf.Clamp(pitch - d.y * k, 2f, 60f);
                idleTime = 0f;
            }
            else
            {
                idleTime += Time.unscaledDeltaTime;
                if (idleTime > 4f) yaw += 8f * Time.unscaledDeltaTime;
            }
            zoom = Mathf.Clamp(zoom - scroll * 0.1f, 0.6f, 1.8f);

            float distance = (carLength * 1.1f + 2.5f) * zoom;
            Vector3 center = target.position + Vector3.up * (carHeight * 0.45f);
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 camPos = center + rot * new Vector3(0f, 0f, -distance);
            if (camPos.y < 0.3f) camPos.y = 0.3f;
            transform.position = camPos;

            // shift the car to the right part of the screen (the tuning panel is on the left)
            Vector3 right = rot * Vector3.right;
            transform.rotation = Quaternion.LookRotation(center - right * (distance * 0.28f) - camPos);
        }
    }
}
