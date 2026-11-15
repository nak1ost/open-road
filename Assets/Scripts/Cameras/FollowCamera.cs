using UnityEngine;

namespace RaceGame.Cameras
{
    /// <summary>Chase camera: follows behind the car, smoothly, zooms out with speed.</summary>
    public class FollowCamera : MonoBehaviour
    {
        public Rigidbody target;
        public float distance = 6.5f;
        public float height = 2.2f;
        public float positionSmooth = 6f;
        public float rotationSmooth = 8f;

        static readonly float[] Distances = { 6.5f, 10f, 4.5f };
        int mode;

        Camera cam;

        void Awake() => cam = GetComponent<Camera>();

        public void NextMode()
        {
            mode = (mode + 1) % Distances.Length;
            distance = Distances[mode];
            height = distance * 0.34f;
        }

        public void SnapToTarget()
        {
            if (target == null) return;
            var t = target.transform;
            transform.position = t.position - t.forward * distance + Vector3.up * height;
            transform.LookAt(t.position + Vector3.up);
        }

        void LateUpdate()
        {
            if (target == null) return;
            var t = target.transform;
            Vector3 vel = target.linearVelocity;
            float speed = vel.magnitude;

            // look along velocity when moving forward fast, otherwise along car heading
            Vector3 dir = Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized;
            Vector3 flatVel = Vector3.ProjectOnPlane(vel, Vector3.up);
            if (speed > 5f && Vector3.Dot(flatVel, dir) > 0f)
                dir = Vector3.Slerp(dir, flatVel.normalized, 0.4f);

            float d = distance + Mathf.Clamp(speed * 0.04f, 0f, 2.5f);
            Vector3 desired = t.position - dir * d + Vector3.up * height;
            transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-positionSmooth * Time.deltaTime));

            var look = Quaternion.LookRotation(t.position + Vector3.up * 1.0f - transform.position);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-rotationSmooth * Time.deltaTime));

            if (cam != null) cam.fieldOfView = Mathf.Lerp(60f, 75f, speed / 70f);
        }
    }
}
