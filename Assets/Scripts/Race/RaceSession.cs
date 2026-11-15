using System;
using System.Collections.Generic;
using RaceGame.Cars;
using RaceGame.Core;
using RaceGame.World;
using UnityEngine;

namespace RaceGame.Race
{
    public class Racer
    {
        public int id;
        public string name;
        public CarController car;     // may be null for remote players (then only transform is used)
        public Transform transform;
        public bool isLocal;
        public bool isBot;
        public int nextCheckpoint = 1;
        public int lap;
        public float finishTime = -1f;
        public float progress;        // for ordering: bigger = further
        public bool Finished => finishTime >= 0f;
    }

    /// <summary>
    /// One race on a route: start grid, countdown, checkpoints, laps, positions, finish.
    /// Works the same offline (with bots) and online (other players' cars are moved by the network).
    /// </summary>
    public class RaceSession : MonoBehaviour
    {
        public const float CheckpointSpacing = 150f;
        public const float CheckpointRadius = 16f;
        public const float CountdownSeconds = 3f;

        public enum State { Countdown, Running, Finished }

        public RouteData Route { get; private set; }
        public State Phase { get; private set; }
        public float Clock { get; private set; }          // seconds since GO (negative during countdown)
        public int Laps => Route.circuit ? Route.laps : 1;
        public int CheckpointCount => checkpoints.Count;
        public readonly List<Racer> racers = new List<Racer>();
        public Racer Local { get; private set; }

        /// <summary>Raised once when the local player crosses the finish line (time in seconds).</summary>
        public event Action<float> LocalFinished;

        readonly List<Vector3> checkpoints = new List<Vector3>();
        readonly List<Vector3> checkpointDirs = new List<Vector3>();
        GameObject gateNext, gateAfter;
        Material gateMat, gateMatDim, finishMat;

        // ------------------------------------------------------------------ setup

        public void Setup(RouteData route)
        {
            Route = route;
            transform.position = -OriginShift.Offset; // children (gates) are placed in GLOBAL coordinates
            BuildCheckpoints();
            gateMat = MaterialFactory.Create(MaterialFactory.Emissive, new Color(1f, 0.75f, 0.1f));
            gateMatDim = MaterialFactory.Create(MaterialFactory.Emissive, new Color(0.4f, 0.3f, 0.05f));
            finishMat = MaterialFactory.Create(MaterialFactory.Emissive, new Color(0.2f, 1f, 0.3f));
            gateNext = BuildGate("GateNext", gateMat);
            gateAfter = BuildGate("GateAfter", gateMatDim);
            Phase = State.Countdown;
            Clock = -CountdownSeconds;
        }

        void BuildCheckpoints()
        {
            checkpoints.Clear();
            checkpointDirs.Clear();
            var pts = Route.points;
            checkpoints.Add(pts[0]);
            float acc = 0f;
            for (int i = 1; i < pts.Count; i++)
            {
                acc += Vector3.Distance(pts[i - 1], pts[i]);
                if (acc >= CheckpointSpacing && (Route.circuit || i < pts.Count - 10))
                {
                    checkpoints.Add(pts[i]);
                    acc = 0f;
                }
            }
            if (!Route.circuit) checkpoints.Add(pts[pts.Count - 1]); // finish line of a sprint
            for (int i = 0; i < checkpoints.Count; i++)
            {
                Vector3 a = checkpoints[Mathf.Max(0, i - 1)], b = checkpoints[Mathf.Min(checkpoints.Count - 1, i + 1)];
                if (i == 0) b = pts[Mathf.Min(pts.Count - 1, 3)];
                Vector3 d = b - a; d.y = 0f;
                checkpointDirs.Add(d.sqrMagnitude > 0.01f ? d.normalized : Vector3.forward);
            }
        }

        /// <summary>Grid slot n (0 = pole position) behind the start line, GLOBAL coordinates.</summary>
        public Pose GridSlot(int n)
        {
            var pts = Route.points;
            Vector3 fwd = checkpointDirs[0];
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
            int row = n / 2;
            float side = (n % 2 == 0) ? -2.8f : 2.8f;
            // walk backwards along the route for circuits, straight back for sprints
            Vector3 basePos = pts[0] - fwd * (12f + row * 9f);
            if (Route.circuit)
            {
                float back = 12f + row * 9f, acc = 0f;
                int i = pts.Count - 1;
                Vector3 prev = pts[0];
                while (i > 0 && acc < back) { acc += Vector3.Distance(prev, pts[i]); prev = pts[i]; i--; }
                basePos = prev;
            }
            return new Pose(basePos + right * side + Vector3.up * 0.5f, Quaternion.LookRotation(fwd, Vector3.up));
        }

        public Racer AddRacer(int id, string name, CarController car, Transform t, bool isLocal, bool isBot)
        {
            var r = new Racer { id = id, name = name, car = car, transform = t, isLocal = isLocal, isBot = isBot };
            racers.Add(r);
            if (isLocal) Local = r;
            return r;
        }

        public void RemoveRacer(int id) => racers.RemoveAll(r => r.id == id);

        // ------------------------------------------------------------------ loop

        void Update()
        {
            if (Route == null) return;
            Clock += Time.deltaTime;

            if (Phase == State.Countdown)
            {
                if (Clock >= 0f)
                {
                    Phase = State.Running;
                    foreach (var r in racers)
                        if ((r.isLocal || r.isBot) && r.car != null) r.car.SetFrozen(false);
                }
            }

            if (Phase == State.Running)
            {
                foreach (var r in racers)
                {
                    if (r.Finished || r.transform == null) continue;
                    // only the local player and bots are judged here; remote players report their own finish
                    if (r.isLocal || r.isBot) CheckProgress(r);
                    r.progress = Progress(r);
                }
                if (Local != null && Local.Finished && AllLocalOrBotsDone()) Phase = State.Finished;
            }

            UpdateGates();
        }

        bool AllLocalOrBotsDone()
        {
            foreach (var r in racers)
                if ((r.isLocal || r.isBot) && !r.Finished) return false;
            return true;
        }

        void CheckProgress(Racer r)
        {
            int target = r.nextCheckpoint % checkpoints.Count;
            Vector3 cp = checkpoints[target];
            Vector3 p = OriginShift.ToGlobal(r.transform.position);
            if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(cp.x, cp.z)) > CheckpointRadius) return;

            bool isFinishLine = Route.circuit ? target == 0 : target == checkpoints.Count - 1;
            if (isFinishLine)
            {
                r.lap++;
                if (!Route.circuit || r.lap >= Laps)
                {
                    r.finishTime = Clock;
                    if (r.isLocal) LocalFinished?.Invoke(Clock);
                    return;
                }
            }
            r.nextCheckpoint++;
        }

        float Progress(Racer r)
        {
            if (r.Finished) return 1e6f - r.finishTime;
            int target = r.nextCheckpoint % checkpoints.Count;
            float d = Vector3.Distance(OriginShift.ToGlobal(r.transform.position), checkpoints[target]);
            // nextCheckpoint keeps counting across laps, so it alone orders the racers
            return r.nextCheckpoint - Mathf.Clamp01(d / CheckpointSpacing);
        }

        /// <summary>For remote racers: set progress counters from network data.</summary>
        public void SetRemoteProgress(int id, int lap, int nextCheckpoint, float finishTime)
        {
            foreach (var r in racers)
            {
                if (r.id != id || r.isLocal) continue;
                r.lap = lap;
                r.nextCheckpoint = nextCheckpoint;
                r.finishTime = finishTime;
            }
        }

        /// <summary>1-based position of a racer.</summary>
        public int PositionOf(Racer racer)
        {
            int pos = 1;
            foreach (var r in racers)
                if (r != racer && r.progress > racer.progress) pos++;
            return pos;
        }

        public List<Racer> Standings()
        {
            var list = new List<Racer>(racers);
            list.Sort((a, b) => b.progress.CompareTo(a.progress));
            return list;
        }

        /// <summary>GLOBAL position of the racer's next checkpoint.</summary>
        public Vector3 NextCheckpointPosition(Racer r) => checkpoints[r.nextCheckpoint % checkpoints.Count];

        /// <summary>Route point index nearest to a GLOBAL position (for bots and respawn).</summary>
        public int NearestRouteIndex(Vector3 p, int hint)
        {
            var pts = Route.points;
            int best = hint;
            float bestD = float.MaxValue;
            for (int k = -20; k <= 60; k++)
            {
                int i = Wrap(hint + k);
                if (i < 0) continue;
                float d = (pts[i] - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        public int Wrap(int i)
        {
            int n = Route.points.Count;
            if (Route.circuit) return (i % n + n) % n;
            return i < 0 || i >= n ? -1 : i;
        }

        /// <summary>Puts a racer's car back on the route near its last checkpoint.</summary>
        public void Respawn(Racer r)
        {
            if (r.car == null) return;
            int cp = Mathf.Max(0, (r.nextCheckpoint - 1) % checkpoints.Count);
            Vector3 pos = OriginShift.ToLocal(checkpoints[cp]) + Vector3.up * 1.2f;
            var rb = r.car.Body;
            r.car.SetFrozen(true);
            r.car.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(checkpointDirs[cp], Vector3.up));
            r.car.SetFrozen(false);
            rb.linearVelocity = Vector3.zero;
        }

        // ------------------------------------------------------------------ visuals

        GameObject BuildGate(string name, Material mat)
        {
            var root = new GameObject(name);
            root.transform.SetParent(transform, false);
            const float halfWidth = 9f, height = 6f;
            Part(root.transform, new Vector3(-halfWidth, height * 0.5f, 0f), new Vector3(0.5f, height, 0.5f), mat);
            Part(root.transform, new Vector3(halfWidth, height * 0.5f, 0f), new Vector3(0.5f, height, 0.5f), mat);
            Part(root.transform, new Vector3(0f, height, 0f), new Vector3(halfWidth * 2f + 0.5f, 0.6f, 0.5f), mat);
            return root;
        }

        static void Part(Transform parent, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }

        void UpdateGates()
        {
            if (Local == null || Local.Finished)
            {
                gateNext.SetActive(false);
                gateAfter.SetActive(false);
                return;
            }
            int n = Local.nextCheckpoint % checkpoints.Count;
            PlaceGate(gateNext, n);
            bool lastLap = !Route.circuit || Local.lap >= Laps - 1;
            bool isFinish = Route.circuit ? n == 0 && lastLap : n == checkpoints.Count - 1;
            SetGateMat(gateNext, isFinish ? finishMat : gateMat);

            int after = Local.nextCheckpoint + 1;
            bool hasAfter = Route.circuit || after < checkpoints.Count;
            gateAfter.SetActive(hasAfter && !isFinish);
            if (hasAfter) PlaceGate(gateAfter, after % checkpoints.Count);
        }

        void PlaceGate(GameObject gate, int index)
        {
            gate.SetActive(true);
            gate.transform.localPosition = checkpoints[index]; // parent sits at -Offset
            gate.transform.rotation = Quaternion.LookRotation(checkpointDirs[index], Vector3.up);
        }

        static void SetGateMat(GameObject gate, Material m)
        {
            foreach (var r in gate.GetComponentsInChildren<Renderer>()) r.sharedMaterial = m;
        }
    }
}
