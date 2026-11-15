using System.Collections.Generic;
using System.Net;
using RaceGame.Cameras;
using RaceGame.Cars;
using RaceGame.Data;
using RaceGame.Input;
using RaceGame.Net;
using RaceGame.Race;
using RaceGame.Save;
using RaceGame.Tuning;
using RaceGame.UI;
using RaceGame.World;
using UnityEngine;

namespace RaceGame.Core
{
    /// <summary>
    /// Entry point: builds the island, spawns the player's car, and runs menus, garage,
    /// free roam, races with bots and LAN sessions. Added to any scene automatically.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public const int MapSeed = 20261115;

        MapLayout layout;
        WorldStreamer world;
        Minimap minimap;

        CarController car;
        TuningData tuning;
        int carIndex;

        FollowCamera followCam;
        GarageCamera garageCam;
        PlayerCarInput input;
        Hud hud;
        GarageUI garage;
        MenuUI menu;
        RaceHud raceHud;

        RaceSession race;
        readonly List<GameObject> bots = new List<GameObject>();
        float resultsTimer;

        NetSession net;
        readonly Dictionary<int, RemoteCar> remotes = new Dictionary<int, RemoteCar>();
        int sentLap = -1, sentCheckpoint = -1;
        bool sentFinish;

        float waterTimer;
        GUIStyle tagStyle;

        static readonly Color[] DefaultPaints =
        {
            new Color(0.75f, 0.05f, 0.05f), new Color(0.05f, 0.05f, 0.06f), new Color(0.92f, 0.92f, 0.92f),
            new Color(0.1f, 0.25f, 0.65f), new Color(0.55f, 0.55f, 0.58f), new Color(0.95f, 0.6f, 0.05f),
            new Color(0.15f, 0.45f, 0.2f),
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureExists()
        {
            if (FindAnyObjectByType<GameManager>() == null)
                new GameObject("Game").AddComponent<GameManager>();
        }

        // ------------------------------------------------------------------ start

        void Start()
        {
            Application.targetFrameRate = 60;
            Time.fixedDeltaTime = 1f / 60f;
            OriginShift.Reset();
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 500f;
            RenderSettings.fogEndDistance = 1500f;
            RenderSettings.fogColor = new Color(0.72f, 0.80f, 0.88f);
            EnsureSun();

            int seed = SaveSystem.Data.mapSeed != 0 ? SaveSystem.Data.mapSeed : MapSeed;
            BuildWorld(seed);

            SetupCameras();
            input = gameObject.AddComponent<PlayerCarInput>();
            hud = gameObject.AddComponent<Hud>();
            hud.input = input;
            garage = gameObject.AddComponent<GarageUI>();
            garageCam.isOverUi = garage.IsOverUi;
            raceHud = gameObject.AddComponent<RaceHud>();
            raceHud.minimap = minimap;
            menu = gameObject.AddComponent<MenuUI>();
            SetupMenu();

            var all = CarCatalog.All;
            string selected = SaveSystem.Data.selectedCarId;
            for (int i = 0; i < all.Count; i++)
                if (all[i].id == selected) carIndex = i;

            Vector3 spawn = OriginShift.ToLocal(layout.SpawnPoint);
            world.Prewarm(layout.SpawnPoint);
            spawn.y = world.GroundHeight(spawn) + 0.4f;
            SpawnCar(carIndex, spawn, Quaternion.LookRotation(layout.SpawnForward));
            world.target = car.transform;

            OpenMenu(MenuUI.Page.Main);
        }

        /// <summary>The player's position in GLOBAL map coordinates.</summary>
        Vector3 PlayerGlobal => car != null ? OriginShift.ToGlobal(car.transform.position) : layout.SpawnPoint;

        /// <summary>Creates the island for a seed (layout, streamed world, minimap).</summary>
        void BuildWorld(int seed)
        {
            if (world != null) DestroyImmediate(world.gameObject); // old colliders must be gone before raycasts on the new island
            layout = new MapLayout(seed);
            world = new GameObject("World").AddComponent<WorldStreamer>();
            world.Init(layout);
            minimap = new Minimap(layout);
            if (raceHud != null) raceHud.minimap = minimap;
            if (menu != null) menu.layout = layout;
        }

        /// <summary>Switches to another island (same seed → nothing happens) and puts the car at its start.</summary>
        void LoadMap(int seed)
        {
            if (seed == 0) seed = MapSeed;
            SaveSystem.Data.mapSeed = seed;
            SaveSystem.Save();
            if (menu != null) menu.seedText = seed.ToString();
            if (layout != null && layout.seed == seed) return;

            if (garage.IsOpen) garage.Close();
            EndRace();
            BuildWorld(seed);

            Vector3 spawn = OriginShift.ToLocal(layout.SpawnPoint);
            if (car != null)
            {
                car.SetFrozen(true);
                car.transform.position = spawn + Vector3.up * 50f; // out of the way of the ground raycast
            }
            world.Prewarm(layout.SpawnPoint);
            spawn.y = world.GroundHeight(spawn) + 0.4f;
            if (car != null)
            {
                car.transform.SetPositionAndRotation(spawn, Quaternion.LookRotation(layout.SpawnForward));
                if (!MenuOpen) car.SetFrozen(false);
                world.target = car.transform;
                followCam.SnapToTarget();
            }
        }

        void EnsureSun()
        {
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) return;
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        void SetupCameras()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            cam.farClipPlane = 1600f;
            if (!cam.TryGetComponent(out followCam)) followCam = cam.gameObject.AddComponent<FollowCamera>();
            if (!cam.TryGetComponent(out garageCam)) garageCam = cam.gameObject.AddComponent<GarageCamera>();
            garageCam.enabled = false;
        }

        void SetupMenu()
        {
            menu.layout = layout;
            menu.playerName = SaveSystem.Data.playerName;
            menu.seedText = layout.seed.ToString();
            menu.onApplySeed = () => LoadMap(menu.Seed);
            menu.getNet = () => net;
            menu.getRace = () => race;
            menu.getRoutes = () => layout.RoutesNear(PlayerGlobal.x, PlayerGlobal.z);
            menu.onFreeRoam = CloseMenu;
            menu.onResume = CloseMenu;
            menu.onSoloRace = (route, botCount) => StartRace(route, new Dictionary<int, int> { { 0, 0 } }, botCount);
            menu.onHostLan = HostLan;
            menu.onJoinLan = JoinLan;
            menu.onLeaveLan = LeaveLan;
            menu.onHostRace = route =>
            {
                var slots = net.HostStartRace(route.id);
                StartRace(route, slots, 0);
            };
            menu.onFinishResults = () =>
            {
                EndRace();
                CloseMenu();
            };
            menu.onStopRaceForAll = () =>
            {
                if (net != null && net.IsHost) net.HostCancelRace();
                EndRace();
                CloseMenu();
            };
            menu.onOpenGarage = () =>
            {
                CloseMenu();
                OpenGarage();
            };
        }

        // ------------------------------------------------------------------ menus

        bool MenuOpen => menu.page != MenuUI.Page.Hidden;

        void OpenMenu(MenuUI.Page page)
        {
            menu.Show(page);
            hud.hidden = true;
            input.car = null;
            if (car != null)
            {
                car.throttleInput = car.steerInput = 0f;
                car.handbrakeInput = false;
                // offline the world can pause; online and during races it keeps going
                if (net == null && race == null) car.SetFrozen(true);
            }
        }

        void CloseMenu()
        {
            SaveSystem.Data.playerName = string.IsNullOrWhiteSpace(menu.playerName) ? "Игрок" : menu.playerName.Trim();
            SaveSystem.Save();
            menu.Show(MenuUI.Page.Hidden);
            menu.message = null;
            hud.hidden = false;
            input.car = car;
            if (car != null && (race == null || race.Phase != RaceSession.State.Countdown)) car.SetFrozen(false);
        }

        // ------------------------------------------------------------------ cars

        static Color DefaultPaint(int index) => DefaultPaints[index % DefaultPaints.Length];

        void SpawnCar(int index, Vector3 pos, Quaternion rot)
        {
            var all = CarCatalog.All;
            if (all.Count == 0) return;
            index = (index % all.Count + all.Count) % all.Count;
            carIndex = index;
            var spec = all[index];

            if (car != null) Destroy(car.gameObject);
            tuning = SaveSystem.GetTuning(spec.id, DefaultPaint(index));
            car = CarFactory.Build(spec, pos, rot, tuning);

            input.car = MenuOpen ? null : car;
            hud.car = car;
            raceHud.player = car.transform;
            followCam.target = car.Body;
            followCam.SnapToTarget();
            if (world != null) world.target = car.transform;

            SaveSystem.Data.selectedCarId = spec.id;
            SaveSystem.Save();
            net?.SendCarChanged(spec.id, JsonUtility.ToJson(tuning));
        }

        PlayerInfo MyInfo() => new PlayerInfo
        {
            name = string.IsNullOrWhiteSpace(menu.playerName) ? "Игрок" : menu.playerName.Trim(),
            carId = car.Spec.id,
            tuningJson = JsonUtility.ToJson(tuning),
        };

        void ResetToRoad()
        {
            if (race != null && race.Local != null) { race.Respawn(race.Local); return; }
            var pose = layout.NearestRoadPose(PlayerGlobal);
            world.Prewarm(pose.position);
            car.SetFrozen(true);
            car.transform.SetPositionAndRotation(OriginShift.ToLocal(pose.position), pose.rotation);
            car.SetFrozen(false);
        }

        // ------------------------------------------------------------------ garage

        void OpenGarage()
        {
            if (car == null || garage.IsOpen || race != null) return;
            var fwd = Vector3.ProjectOnPlane(car.transform.forward, Vector3.up);
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            var pos = car.transform.position;
            float best = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(pos + Vector3.up * 3f, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.attachedRigidbody == car.Body) continue; // skip the car itself
                if (hit.distance < best) { best = hit.distance; pos.y = hit.point.y + 0.05f; }
            }
            car.SetFrozen(true);
            car.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd.normalized, Vector3.up));

            input.car = null;
            hud.hidden = true;
            raceHud.hidden = true;
            followCam.enabled = false;
            garageCam.target = car.transform;
            garageCam.carLength = car.Spec.LengthM;
            garageCam.carHeight = car.Spec.HeightM;
            garageCam.ResetView();
            garageCam.enabled = true;

            var spec = car.Spec;
            garage.Open(car, SaveSystem.GetTuning(spec.id, DefaultPaint(carIndex)),
                t =>
                {
                    tuning = t.Clone();
                    SaveSystem.SetTuning(spec.id, t);
                    SaveSystem.Save();
                    net?.SendCarChanged(spec.id, JsonUtility.ToJson(t));
                },
                CloseGarage);
        }

        void CloseGarage()
        {
            garageCam.enabled = false;
            followCam.enabled = true;
            followCam.SnapToTarget();
            hud.hidden = false;
            raceHud.hidden = false;
            if (car != null)
            {
                car.SetFrozen(false);
                input.car = car;
            }
        }

        // ------------------------------------------------------------------ races

        void StartRace(RouteData route, Dictionary<int, int> slots, int botCount)
        {
            if (route == null) return;
            if (garage.IsOpen) garage.Close();
            EndRace();

            race = new GameObject("Race").AddComponent<RaceSession>();
            race.Setup(route);
            race.LocalFinished += _ => resultsTimer = 2.5f;

            int myId = net != null ? net.LocalId : 0;
            int mySlot = slots.TryGetValue(myId, out var s) ? s : 0;
            var grid = race.GridSlot(mySlot);
            world.Prewarm(grid.position);
            car.SetFrozen(true);
            car.transform.SetPositionAndRotation(OriginShift.ToLocal(grid.position), grid.rotation);
            followCam.SnapToTarget();
            race.AddRacer(myId, MyInfo().name, car, car.transform, true, false);

            raceHud.others.Clear();
            foreach (var kv in slots)
            {
                if (kv.Key == myId || !remotes.TryGetValue(kv.Key, out var rc)) continue;
                race.AddRacer(kv.Key, rc.playerName, null, rc.transform, false, false);
                raceHud.others.Add(rc.transform);
            }

            var all = CarCatalog.All;
            var rng = new System.Random();
            for (int i = 0; i < botCount; i++)
            {
                var spec = all[rng.Next(all.Count)];
                var pose = race.GridSlot(slots.Count + i);
                var paint = TuningCatalog.Palette[rng.Next(TuningCatalog.Palette.Length)];
                var botTuning = TuningData.Default(paint);
                botTuning.ecu = rng.Next(0, 4);
                botTuning.turbo = rng.Next(0, 4);
                var bot = CarFactory.Build(spec, OriginShift.ToLocal(pose.position), pose.rotation, botTuning);
                bot.SetFrozen(true);
                var racer = race.AddRacer(1000 + i, spec.DisplayName, bot, bot.transform, false, true);
                var ai = bot.gameObject.AddComponent<AIDriver>();
                ai.race = race;
                ai.racer = racer;
                ai.skill = 0.8f + (float)rng.NextDouble() * 0.2f;
                bots.Add(bot.gameObject);
                raceHud.others.Add(bot.transform);
            }

            raceHud.race = race;
            hud.carButtons = false;
            sentLap = sentCheckpoint = -1;
            sentFinish = false;
            resultsTimer = -1f;
            CloseMenu();
        }

        void EndRace()
        {
            foreach (var b in bots) if (b != null) Destroy(b);
            bots.Clear();
            if (race != null) Destroy(race.gameObject);
            race = null;
            raceHud.race = null;
            raceHud.others.Clear();
            foreach (var rc in remotes.Values) raceHud.others.Add(rc.transform);
            hud.carButtons = true;
            if (car != null && !MenuOpen) car.SetFrozen(false);
        }

        void SendRaceProgress()
        {
            if (net == null || race == null || race.Local == null) return;
            var me = race.Local;
            if (me.lap == sentLap && me.nextCheckpoint == sentCheckpoint && (sentFinish || !me.Finished)) return;
            sentLap = me.lap;
            sentCheckpoint = me.nextCheckpoint;
            sentFinish = me.Finished;
            net.SendProgress(me.lap, me.nextCheckpoint, me.finishTime);
        }

        // ------------------------------------------------------------------ LAN

        void HostLan()
        {
            LeaveLan(false);
            try
            {
                net = NetSession.Host($"Игра {MyInfo().name}", MyInfo(), layout.seed);
            }
            catch (System.Exception e)
            {
                net = null;
                menu.message = "Не удалось создать игру: " + e.Message;
                return;
            }
            Subscribe(net);
            menu.Show(MenuUI.Page.LanLobby);
        }

        void JoinLan(IPAddress address)
        {
            LeaveLan(false);
            net = NetSession.Join(address, MyInfo());
            Subscribe(net);
        }

        void Subscribe(NetSession n)
        {
            n.JoinedGame += () =>
            {
                menu.message = null;
                LoadMap(n.Seed); // build the host's island
                menu.Show(MenuUI.Page.LanLobby);
            };
            n.PlayerAdded += SpawnRemote;
            n.PlayerUpdated += info =>
            {
                Vector3 pos = remotes.TryGetValue(info.id, out var old) ? old.transform.position : OriginShift.ToLocal(layout.SpawnPoint);
                RemoveRemote(info.id);
                SpawnRemote(info);
                if (remotes.TryGetValue(info.id, out var rc)) rc.transform.position = pos;
            };
            n.PlayerRemoved += id =>
            {
                race?.RemoveRacer(id);
                RemoveRemote(id);
            };
            n.StateReceived += (id, state) =>
            {
                if (remotes.TryGetValue(id, out var rc)) rc.Apply(state);
            };
            n.RaceStarted += (routeId, slots) => StartRace(layout.FindRoute(routeId), slots, 0);
            n.ProgressReceived += (id, lap, cp, finish) => race?.SetRemoteProgress(id, lap, cp, finish);
            n.RaceCancelled += () =>
            {
                EndRace();
                if (menu.page == MenuUI.Page.Results) CloseMenu();
            };
            n.Disconnected += reason =>
            {
                LeaveLan(false);
                OpenMenu(MenuUI.Page.Lan);
                menu.message = reason;
            };
        }

        void SpawnRemote(PlayerInfo info)
        {
            if (net != null && info.id == net.LocalId) return;
            RemoveRemote(info.id);
            var spec = CarCatalog.Find(info.carId) ?? CarCatalog.All[0];
            TuningData t = null;
            try { t = string.IsNullOrEmpty(info.tuningJson) ? null : JsonUtility.FromJson<TuningData>(info.tuningJson); }
            catch (System.Exception) { }
            t ??= TuningData.Default(Color.gray);
            var c = CarFactory.Build(spec, OriginShift.ToLocal(layout.SpawnPoint) + Vector3.down * 50f, Quaternion.identity, t);
            c.gameObject.name = "Remote_" + info.name;
            var rc = c.gameObject.AddComponent<RemoteCar>();
            rc.playerId = info.id;
            rc.playerName = info.name;
            remotes[info.id] = rc;
            raceHud.others.Add(rc.transform);
        }

        void RemoveRemote(int id)
        {
            if (!remotes.TryGetValue(id, out var rc)) return;
            raceHud.others.Remove(rc.transform);
            if (rc != null) Destroy(rc.gameObject);
            remotes.Remove(id);
        }

        void LeaveLan() => LeaveLan(true);

        void LeaveLan(bool toMainMenu)
        {
            if (net != null)
            {
                EndRace();
                net.Dispose();
                net = null;
            }
            foreach (var id in new List<int>(remotes.Keys)) RemoveRemote(id);
            if (toMainMenu) OpenMenu(MenuUI.Page.Main);
        }

        // ------------------------------------------------------------------ loop

        void Update()
        {
            net?.Poll();
            if (net != null && car != null) net.TickLocalState(car);
            SendRaceProgress();

            // show results a moment after the finish
            if (resultsTimer > 0f)
            {
                resultsTimer -= Time.deltaTime;
                if (resultsTimer <= 0f && !garage.IsOpen) OpenMenu(MenuUI.Page.Results);
            }

            if (garage.IsOpen)
            {
                if (input.GaragePressed || input.MenuPressed) garage.Close();
                return;
            }

            // endless map: keep the player near (0,0,0) so physics and rendering stay precise
            if (car != null) OriginShift.Check(car.transform.position);

            if (MenuOpen)
            {
                if (input.MenuPressed && menu.page == MenuUI.Page.Pause) CloseMenu();
                return;
            }

            if (input.MenuPressed || hud.MenuTapped)
            {
                OpenMenu(MenuUI.Page.Pause);
                return;
            }

            if (race == null)
            {
                if (input.GaragePressed || hud.GarageTapped) { OpenGarage(); return; }
                bool next = input.NextCarPressed || hud.NextCarTapped;
                bool prev = input.PrevCarPressed || hud.PrevCarTapped;
                if (next || prev)
                {
                    var t = car.transform;
                    Vector3 fwd = Vector3.ProjectOnPlane(t.forward, Vector3.up);
                    if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
                    SpawnCar(carIndex + (next ? 1 : -1), t.position + Vector3.up * 0.5f, Quaternion.LookRotation(fwd.normalized, Vector3.up));
                }
            }

            if (input.ResetPressed || hud.ResetTapped) ResetToRoad();
            if (input.CameraPressed || hud.CameraTapped) followCam.NextMode();

            // fell into the sea or off the map → back to the road
            if (car != null && car.transform.position.y < MapLayout.SeaLevel - 0.7f) waterTimer += Time.deltaTime;
            else waterTimer = 0f;
            if (waterTimer > 2f) { waterTimer = 0f; ResetToRoad(); }
        }

        void OnGUI()
        {
            // name tags over other players
            if (remotes.Count == 0 || garage.IsOpen) return;
            var cam = Camera.main;
            if (cam == null) return;
            tagStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = Mathf.Max(12, Screen.height / 45) };
            foreach (var rc in remotes.Values)
            {
                if (rc == null) continue;
                Vector3 p = rc.transform.position + Vector3.up * 2.4f;
                Vector3 sp = cam.WorldToScreenPoint(p);
                if (sp.z < 0f || sp.z > 250f) continue;
                GUI.Label(new Rect(sp.x - 150f, Screen.height - sp.y - 15f, 300f, 30f), rc.playerName, tagStyle);
            }
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) SaveSystem.Save();
        }

        void OnApplicationQuit()
        {
            SaveSystem.Save();
            net?.Dispose();
        }

        void OnDestroy() => net?.Dispose();
    }
}
