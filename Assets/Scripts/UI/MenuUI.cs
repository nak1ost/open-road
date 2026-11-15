using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using RaceGame.Net;
using RaceGame.Race;
using RaceGame.World;
using UnityEngine;

namespace RaceGame.UI
{
    /// <summary>
    /// Main menu and pause menu (IMGUI, 1280×720 reference): free roam, solo races with bots,
    /// LAN game (create / find / join by IP), race list for the LAN host.
    /// </summary>
    public class MenuUI : MonoBehaviour
    {
        const float RefHeight = 720f;

        public enum Page { Hidden, Main, Races, Lan, LanLobby, Pause, Results }

        public Page page = Page.Main;
        public string playerName = "Игрок";
        public string message;

        // filled by GameManager
        public MapLayout layout;
        public Func<NetSession> getNet;
        public Func<RaceSession> getRace;

        // actions back to the game
        public Action onFreeRoam;
        public Action<RouteData, int> onSoloRace;          // route, bots
        public Action onHostLan;
        public Action<IPAddress> onJoinLan;
        public Action onLeaveLan;
        public Action<RouteData> onHostRace;
        public Action onFinishResults;
        public Action onStopRaceForAll;
        public Action onResume;
        public Action onOpenGarage;

        int bots = 3;
        Vector2 scroll;
        string ipText = "192.168.1.";
        Task<List<LanNetwork.FoundGame>> scan;
        List<LanNetwork.FoundGame> found = new List<LanNetwork.FoundGame>();
        Page racesBack = Page.Main;

        GUIStyle title, label, small, button, box, field;
        float scale = 1f;

        float VW => Screen.width / scale;

        void InitStyles()
        {
            if (title != null) return;
            title = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = true };
            label = new GUIStyle(GUI.skin.label) { fontSize = 20, richText = true, wordWrap = true };
            small = new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true, wordWrap = true };
            button = new GUIStyle(GUI.skin.button) { fontSize = 20, fixedHeight = 52f, richText = true };
            box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(16, 16, 14, 14) };
            field = new GUIStyle(GUI.skin.textField) { fontSize = 22, fixedHeight = 44f };
        }

        public void Show(Page p)
        {
            page = p;
            scroll = Vector2.zero;
        }

        void OnGUI()
        {
            if (page == Page.Hidden) return;
            InitStyles();
            scale = Screen.height / RefHeight;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            // dim the game behind the menu
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, page == Page.Results ? 0.35f : 0.55f);
            GUI.DrawTexture(new Rect(0, 0, VW, RefHeight), Texture2D.whiteTexture);
            GUI.color = old;

            var panel = new Rect(VW * 0.5f - 300f, 30f, 600f, RefHeight - 60f);
            GUILayout.BeginArea(panel, box);
            switch (page)
            {
                case Page.Main: MainPage(); break;
                case Page.Races: RacesPage(); break;
                case Page.Lan: LanPage(); break;
                case Page.LanLobby: LobbyPage(); break;
                case Page.Pause: PausePage(); break;
                case Page.Results: ResultsPage(); break;
            }
            if (!string.IsNullOrEmpty(message))
                GUILayout.Label($"<color=#ffd34d>{message}</color>", small);
            GUILayout.EndArea();
            GUI.matrix = Matrix4x4.identity;
        }

        // ------------------------------------------------------------------ pages

        void MainPage()
        {
            GUILayout.Label("OPEN ROAD", title, GUILayout.Height(70f));
            GUILayout.Label("Остров 8×8 км · 7 марок · тюнинг · гонки · игра по Wi-Fi", small);
            GUILayout.Space(10f);
            GUILayout.Label("Твоё имя:", label);
            playerName = GUILayout.TextField(playerName, 16, field);
            GUILayout.Space(14f);
            if (GUILayout.Button("Свободная езда", button)) { message = null; onFreeRoam?.Invoke(); }
            if (GUILayout.Button("Гонки с ботами", button)) { racesBack = Page.Main; Show(Page.Races); }
            if (GUILayout.Button("Игра по Wi-Fi (LAN)", button)) { message = null; Show(Page.Lan); }
            if (GUILayout.Button("Гараж", button)) onOpenGarage?.Invoke();
            GUILayout.FlexibleSpace();
            GUILayout.Label("<i>Управление на ПК: WASD, пробел — ручник, G — гараж, Esc — меню.</i>", small);
        }

        void RacesPage()
        {
            var net = getNet?.Invoke();
            bool hostRace = net != null && net.IsHost;
            GUILayout.Label(hostRace ? "<b>Гонка для всех игроков</b>" : "<b>Гонки</b>", label);
            if (!hostRace)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Соперников-ботов: {bots}", label);
                if (GUILayout.Button("−", button, GUILayout.Width(60f))) bots = Mathf.Max(0, bots - 1);
                if (GUILayout.Button("+", button, GUILayout.Width(60f))) bots = Mathf.Min(7, bots + 1);
                GUILayout.EndHorizontal();
            }
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (var r in layout.routes)
            {
                string kind = r.circuit ? $"круг × {r.laps}" : "спринт";
                if (GUILayout.Button($"{r.name}\n<size=14>{kind} · {r.Length * r.laps / 1000f:0.0} км</size>", new GUIStyle(button) { fixedHeight = 62f }))
                {
                    message = null;
                    if (hostRace) onHostRace?.Invoke(r);
                    else onSoloRace?.Invoke(r, bots);
                }
            }
            GUILayout.EndScrollView();
            if (GUILayout.Button("Назад", button)) Show(racesBack);
        }

        void LanPage()
        {
            GUILayout.Label("<b>Игра по Wi-Fi</b>", label);
            GUILayout.Label("Все телефоны должны быть в одной Wi-Fi сети (или раздай точку доступа с одного из них).", small);
            GUILayout.Label($"Твой IP: <b>{NetSession.LocalAddressText()}</b>", small);
            GUILayout.Space(8f);
            if (GUILayout.Button("Создать игру", button)) { message = null; onHostLan?.Invoke(); }
            GUILayout.Space(8f);

            bool scanning = scan != null && !scan.IsCompleted;
            if (scan != null && scan.IsCompleted)
            {
                found = scan.Status == TaskStatus.RanToCompletion ? scan.Result : new List<LanNetwork.FoundGame>();
                scan = null;
                if (found.Count == 0) message = "Игр не найдено. Попробуй ввести IP хоста вручную.";
            }
            GUI.enabled = !scanning;
            if (GUILayout.Button(scanning ? "Ищу игры в сети…" : "Найти игры", button))
            {
                message = null;
                found.Clear();
                scan = Task.Run(() => LanNetwork.Scan());
            }
            GUI.enabled = true;

            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(150f));
            foreach (var g in found)
            {
                string text = $"{g.name} · игроков: {g.players} · {g.address}" + (g.version != LanProtocol.Version ? " (другая версия)" : "");
                GUI.enabled = g.version == LanProtocol.Version;
                if (GUILayout.Button(text, button)) { message = "Подключаюсь…"; onJoinLan?.Invoke(g.address); }
                GUI.enabled = true;
            }
            GUILayout.EndScrollView();

            GUILayout.Label("Подключиться по IP хоста:", small);
            GUILayout.BeginHorizontal();
            ipText = GUILayout.TextField(ipText, 15, field);
            if (GUILayout.Button("Войти", button, GUILayout.Width(120f)))
            {
                if (IPAddress.TryParse(ipText.Trim(), out var ip)) { message = "Подключаюсь…"; onJoinLan?.Invoke(ip); }
                else message = "Неверный IP-адрес";
            }
            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Назад", button)) Show(Page.Main);
        }

        void LobbyPage()
        {
            var net = getNet?.Invoke();
            if (net == null) { Show(Page.Lan); return; }
            GUILayout.Label($"<b>{(net.IsHost ? "Ты хост" : "Ты в игре")}: {net.GameName}</b>", label);
            if (net.IsHost) GUILayout.Label($"Друзья подключаются к IP: <b>{NetSession.LocalAddressText()}</b>", small);
            GUILayout.Label($"Игроки ({net.players.Count}):", label);
            foreach (var p in net.players.Values)
                GUILayout.Label($"• {p.name} — {CarName(p.carId)}" + (p.id == net.LocalId ? " (ты)" : ""), small);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Ехать (свободная езда)", button)) onResume?.Invoke();
            if (net.IsHost && GUILayout.Button("Начать гонку для всех", button)) { racesBack = Page.LanLobby; Show(Page.Races); }
            if (!net.IsHost) GUILayout.Label("<i>Гонку запускает хост.</i>", small);
            if (GUILayout.Button("Гараж", button)) onOpenGarage?.Invoke();
            if (GUILayout.Button("Покинуть игру", button)) onLeaveLan?.Invoke();
        }

        void PausePage()
        {
            var net = getNet?.Invoke();
            var race = getRace?.Invoke();
            GUILayout.Label("<b>Пауза</b>", label);
            if (net != null) GUILayout.Label("<i>По сети игра не останавливается.</i>", small);
            if (GUILayout.Button("Продолжить", button)) onResume?.Invoke();
            if (race == null && GUILayout.Button("Гараж", button)) onOpenGarage?.Invoke();
            if (net == null)
            {
                if (race == null && GUILayout.Button("Гонки", button)) { racesBack = Page.Pause; Show(Page.Races); }
                if (race != null && GUILayout.Button("Сойти с гонки", button)) onFinishResults?.Invoke();
            }
            else
            {
                if (GUILayout.Button("Игроки и гонки", button)) Show(Page.LanLobby);
                if (race != null && net.IsHost && GUILayout.Button("Остановить гонку для всех", button)) onStopRaceForAll?.Invoke();
                if (race != null && !net.IsHost && GUILayout.Button("Сойти с гонки", button)) onFinishResults?.Invoke();
            }
            if (GUILayout.Button("Главное меню", button))
            {
                if (net != null) onLeaveLan?.Invoke();
                else Show(Page.Main);
            }
        }

        void ResultsPage()
        {
            var race = getRace?.Invoke();
            GUILayout.Label("<b>Результаты</b>", label);
            if (race != null)
            {
                int pos = 1;
                foreach (var r in race.Standings())
                {
                    string time = r.Finished ? FormatTime(r.finishTime) : "ещё едет";
                    string me = r.isLocal ? " <color=#6dff7a>(ты)</color>" : "";
                    GUILayout.Label($"{pos++}. {r.name}{me} — {time}", label);
                }
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Продолжить", button)) onFinishResults?.Invoke();
        }

        public static string FormatTime(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int m = (int)(seconds / 60f);
            return $"{m}:{seconds - m * 60f:00.00}";
        }

        static string CarName(string id)
        {
            var spec = Data.CarCatalog.Find(id);
            return spec != null ? spec.DisplayName : id;
        }
    }
}
