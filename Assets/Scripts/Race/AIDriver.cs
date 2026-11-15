using RaceGame.Cars;
using UnityEngine;

namespace RaceGame.Race
{
    /// <summary>
    /// Simple race bot: follows the route points, slows down before corners,
    /// respawns on the route if stuck or fallen into water.
    /// </summary>
    public class AIDriver : MonoBehaviour
    {
        public RaceSession race;
        public Racer racer;
        public float skill = 0.9f; // 0.7..1.0 — how close to the grip limit it drives

        CarController car;
        int index;
        float stuckTime;

        void Start()
        {
            car = GetComponent<CarController>();
            index = race.NearestRouteIndex(transform.position, 0);
        }

        void FixedUpdate()
        {
            if (car == null || race == null || racer == null) return;
            if (race.Phase != RaceSession.State.Running || racer.Finished)
            {
                car.throttleInput = racer != null && racer.Finished ? -0.3f : 0f;
                car.steerInput = 0f;
                return;
            }

            index = race.NearestRouteIndex(transform.position, index);
            float speed = car.SpeedKmh / 3.6f;

            // steer at a point ahead, further at higher speed
            int look = 3 + Mathf.RoundToInt(speed * 0.25f);
            int ti = race.Wrap(index + look);
            if (ti < 0) ti = race.Route.points.Count - 1;
            Vector3 target = race.Route.points[ti];
            Vector3 local = transform.InverseTransformPoint(target);
            float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            car.steerInput = Mathf.Clamp(angle / 25f, -1f, 1f);

            // corner speed from the bend over the next ~80 m
            float bend = 0f;
            for (int k = 2; k <= 10; k++)
            {
                int a = race.Wrap(index + k), b = race.Wrap(index + k + 2), c = race.Wrap(index + k - 2);
                if (a < 0 || b < 0 || c < 0) break;
                Vector3 d1 = race.Route.points[b] - race.Route.points[a];
                Vector3 d0 = race.Route.points[a] - race.Route.points[c];
                d0.y = d1.y = 0f;
                if (d0.sqrMagnitude > 0.01f && d1.sqrMagnitude > 0.01f) bend = Mathf.Max(bend, Vector3.Angle(d0, d1));
            }
            // radius of a bend that turns `bend` degrees over 16 m
            float radius = bend > 0.5f ? 16f / (bend * Mathf.Deg2Rad) : 1000f;
            float grip = 9.81f * 0.95f * skill * car.Stats.gripSide;
            float targetSpeed = Mathf.Sqrt(grip * radius);
            car.throttleInput = speed < targetSpeed ? 1f : (speed > targetSpeed * 1.1f ? -0.7f : 0.1f);
            car.handbrakeInput = false;

            // stuck / in water → respawn
            if (speed < 1.5f || transform.position.y < World.MapLayout.SeaLevel - 1f) stuckTime += Time.fixedDeltaTime;
            else stuckTime = 0f;
            if (stuckTime > 3f)
            {
                stuckTime = 0f;
                race.Respawn(racer);
            }
        }
    }
}
