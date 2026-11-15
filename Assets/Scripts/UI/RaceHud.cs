using System.Collections.Generic;
using RaceGame.Race;
using UnityEngine;

namespace RaceGame.UI
{
    /// <summary>Race overlay: countdown, position, lap, time, distance to the next checkpoint, minimap.</summary>
    public class RaceHud : MonoBehaviour
    {
        public RaceSession race;
        public Minimap minimap;
        public Transform player;
        public List<Transform> others = new List<Transform>();
        public bool hidden;

        GUIStyle big, mid, small;

        void OnGUI()
        {
            if (hidden) return;
            if (big == null)
            {
                big = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, richText = true };
                mid = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold, richText = true };
                small = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, richText = true };
            }
            int unit = Mathf.Max(12, Screen.height / 40);
            big.fontSize = unit * 6;
            mid.fontSize = unit * 2;
            small.fontSize = unit;

            DrawMinimap(unit);
            if (race == null || race.Route == null) return;

            var local = race.Local;
            if (race.Phase == RaceSession.State.Countdown)
            {
                int n = Mathf.CeilToInt(-race.Clock);
                GUI.Label(new Rect(0, Screen.height * 0.25f, Screen.width, Screen.height * 0.3f), n > 0 ? n.ToString() : "", big);
            }
            else if (race.Clock < 1.2f)
            {
                GUI.Label(new Rect(0, Screen.height * 0.25f, Screen.width, Screen.height * 0.3f), "<color=#6dff7a>ВПЕРЁД!</color>", big);
            }

            if (local == null) return;
            float x = Screen.width * 0.3f, w = Screen.width * 0.4f;
            float y = Screen.height * 0.12f;
            GUI.Label(new Rect(x, y, w, unit * 2.5f), $"{race.PositionOf(local)} / {race.racers.Count}", mid);
            string lap = race.Route.circuit ? $"Круг {Mathf.Min(local.lap + 1, race.Laps)} / {race.Laps} · " : "";
            GUI.Label(new Rect(x, y + unit * 2.6f, w, unit * 1.5f),
                $"{lap}{MenuUI.FormatTime(local.Finished ? local.finishTime : race.Clock)}", small);
            if (!local.Finished && player != null)
            {
                float d = Vector3.Distance(player.position, race.NextCheckpointPosition(local));
                GUI.Label(new Rect(x, y + unit * 4.1f, w, unit * 1.5f), $"До чекпоинта: {d:0} м", small);
            }
            else if (local.Finished)
            {
                GUI.Label(new Rect(0, Screen.height * 0.3f, Screen.width, unit * 6f), $"ФИНИШ — {race.PositionOf(local)} место", new GUIStyle(big) { fontSize = unit * 3 });
            }
        }

        void DrawMinimap(int unit)
        {
            if (minimap == null || player == null) return;
            float size = Screen.height * 0.26f;
            var rect = new Rect(Screen.width - size - unit, Screen.height - size - unit * 6f, size, size);
            var markers = new List<(Vector3, Color, float)>();
            foreach (var o in others) if (o != null) markers.Add((o.position, new Color(0.3f, 0.7f, 1f), size * 0.035f));
            if (race != null && race.Local != null && !race.Local.Finished)
                markers.Add((race.NextCheckpointPosition(race.Local), new Color(1f, 0.8f, 0.1f), size * 0.04f));
            markers.Add((player.position, Color.red, size * 0.05f));
            minimap.Draw(rect, markers, race != null ? race.Route.points : null);
        }
    }
}
