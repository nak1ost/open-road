using System;
using RaceGame.Cars;
using RaceGame.Data;
using RaceGame.Tuning;
using UnityEngine;

namespace RaceGame.UI
{
    /// <summary>
    /// Garage screen: tabs with paint, tint, suspension, wheels, engine, gearbox, brakes, exterior.
    /// Everything is free. Changes are previewed live; "Save" stores them, "Cancel" reverts.
    /// IMGUI layout scaled to a 1280×720 reference.
    /// </summary>
    public class GarageUI : MonoBehaviour
    {
        const float RefHeight = 720f;
        const float PanelW = 450f;

        static readonly string[] Tabs = { "Покраска", "Тонировка", "Подвеска", "Колёса", "Двигатель", "КПП", "Тормоза", "Внешний вид" };
        static readonly Color[] HeadlightPresets =
        {
            new Color(1f, 0.97f, 0.85f), new Color(1f, 1f, 1f), new Color(0.7f, 0.85f, 1f), new Color(1f, 0.85f, 0.3f),
        };
        static readonly string[] HeadlightNames = { "Тёплый", "Белый", "Ксенон", "Жёлтый" };

        public bool IsOpen { get; private set; }

        CarController car;
        CarSpec spec;
        TuningData saved;
        TuningData work;
        Action<TuningData> onSaved;
        Action onClosed;
        Vector3 basePosition;
        int tab;
        Vector2 scroll;
        string message;
        float messageUntil;
        bool showMessage;
        bool confirmExit;

        GUIStyle title, label, small, button, tabStyle, tallTab, swatch, slider, thumb, box, rightStyle, toggle;
        float scale = 1f;

        public void Open(CarController target, TuningData savedTuning, Action<TuningData> saveCallback, Action closeCallback)
        {
            car = target;
            spec = target.Spec;
            saved = savedTuning.Clone();
            work = savedTuning.Clone();
            onSaved = saveCallback;
            onClosed = closeCallback;
            basePosition = car.transform.position;
            tab = 0;
            scroll = Vector2.zero;
            confirmExit = false;
            IsOpen = true;
            Preview();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            work = saved.Clone();
            Preview();
            car.transform.position = basePosition + Vector3.up * 0.15f;
            onClosed?.Invoke();
        }

        /// <summary>Is a screen point (pixels, origin bottom-left) over a garage panel?</summary>
        public bool IsOverUi(Vector2 screenPos)
        {
            if (!IsOpen) return false;
            var p = new Vector2(screenPos.x, Screen.height - screenPos.y) / scale;
            return LeftPanel.Contains(p) || BottomPanel.Contains(p) || StatsPanel.Contains(p);
        }

        float VirtualWidth => Screen.width / scale;
        Rect LeftPanel => new Rect(10f, 10f, PanelW, RefHeight - 20f);
        Rect StatsPanel => new Rect(VirtualWidth - 330f, 10f, 320f, 250f);
        Rect BottomPanel => new Rect(VirtualWidth - 330f, RefHeight - 170f, 320f, 160f);

        void Preview()
        {
            TuningApplier.Apply(car, work);
            // in the garage the car is frozen, so lift/lower the body by hand to show ride height and wheel size
            car.transform.position = basePosition + Vector3.up * (car.Stats.rideHeight + work.wheelSize);
        }

        void Toast(string text)
        {
            message = text;
            messageUntil = Time.unscaledTime + 3f;
        }

        void InitStyles()
        {
            if (title != null) return;
            title = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, richText = true, wordWrap = true };
            label = new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true, wordWrap = true };
            small = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true, wordWrap = true };
            rightStyle = new GUIStyle(small) { alignment = TextAnchor.MiddleRight };
            button = new GUIStyle(GUI.skin.button) { fontSize = 18, fixedHeight = 44f, richText = true };
            tabStyle = new GUIStyle(GUI.skin.button) { fontSize = 16, fixedHeight = 40f };
            tallTab = new GUIStyle(tabStyle) { fixedHeight = 48f, fontSize = 15 };
            swatch = new GUIStyle { normal = { background = Texture2D.whiteTexture }, margin = new RectOffset(3, 3, 3, 3) };
            slider = new GUIStyle(GUI.skin.horizontalSlider) { fixedHeight = 26f };
            thumb = new GUIStyle(GUI.skin.horizontalSliderThumb) { fixedHeight = 30f, fixedWidth = 30f };
            box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(12, 12, 10, 10) };
            toggle = new GUIStyle(GUI.skin.toggle) { fontSize = 18, fixedHeight = 36f };
        }

        void OnGUI()
        {
            if (!IsOpen || car == null) return;
            InitStyles();
            scale = Screen.height / RefHeight;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            DrawLeftPanel();
            DrawStats();
            DrawBottom();

            GUI.matrix = Matrix4x4.identity;
        }

        // ---------------- left panel ----------------

        void DrawLeftPanel()
        {
            GUILayout.BeginArea(LeftPanel, box);
            GUILayout.Label($"ГАРАЖ · {spec.DisplayName}", title);
            tab = GUILayout.SelectionGrid(tab, Tabs, 4, tabStyle);
            GUILayout.Space(8f);

            scroll = GUILayout.BeginScrollView(scroll);
            GUI.changed = false;
            switch (tab)
            {
                case 0: PaintTab(); break;
                case 1: TintTab(); break;
                case 2: SuspensionTab(); break;
                case 3: WheelsTab(); break;
                case 4: EngineTab(); break;
                case 5: GearboxTab(); break;
                case 6: BrakesTab(); break;
                case 7: ExteriorTab(); break;
            }
            if (GUI.changed) { confirmExit = false; Preview(); }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void PaintTab()
        {
            Header("Цвет кузова");
            work.paint = ColorPicker(work.paint);
            GUILayout.Space(10f);
            Header("Тип покраски");
            work.finish = (PaintFinish)GUILayout.SelectionGrid((int)work.finish, TuningCatalog.FinishNames, 3, tallTab);
        }

        void TintTab()
        {
            Header("Тонировка стёкол");
            work.tintFront = Slider("Лобовое стекло", work.tintFront, 0f, TuningCatalog.MaxTintFront, v => $"{v * 100f:0} %");
            work.tintSide = Slider("Боковые стёкла", work.tintSide, 0f, 1f, v => $"{v * 100f:0} %");
            work.tintRear = Slider("Заднее стекло", work.tintRear, 0f, 1f, v => $"{v * 100f:0} %");
            GUILayout.Label("<i>Лобовое — максимум 70 %, иначе ничего не видно ночью.</i>", small);
        }

        void SuspensionTab()
        {
            work.coilovers = GUILayout.Toggle(work.coilovers, " Регулируемая подвеска (койловеры)", toggle);
            if (!work.coilovers)
            {
                GUILayout.Label("Включи, чтобы менять клиренс, жёсткость, развал и вылет колёс.", small);
                return;
            }
            work.rideHeight = Slider("Клиренс", work.rideHeight, TuningCatalog.RideHeightMin, TuningCatalog.RideHeightMax, v => $"{v * 100f:+0;-0;0} см");
            work.springStiffness = Slider("Жёсткость пружин", work.springStiffness, 0.6f, 1.6f, v => $"{v * 100f:0} %");
            work.damperStiffness = Slider("Амортизаторы", work.damperStiffness, 0.6f, 1.6f, v => $"{v * 100f:0} %");
            work.camber = Slider("Развал колёс", work.camber, TuningCatalog.CamberMin, TuningCatalog.CamberMax, v => $"{v:0.0}°");
            work.trackOffset = Slider("Вылет колёс", work.trackOffset, 0f, TuningCatalog.TrackMax, v => $"+{v * 100f:0} см");
            if (GUILayout.Button("Сбросить настройки подвески", button))
            {
                work.rideHeight = 0f; work.springStiffness = 1f; work.damperStiffness = 1f; work.camber = 0f; work.trackOffset = 0f;
                GUI.changed = true;
            }
        }

        void WheelsTab()
        {
            Header("Диски");
            work.rimStyle = GUILayout.SelectionGrid(work.rimStyle, TuningCatalog.RimStyleNames, 3, tabStyle);
            work.wheelSize = Slider("Размер дисков", work.wheelSize, 0f, TuningCatalog.WheelSizeMax, v => $"+{v / 0.0127f:0.#}\"");
            GUILayout.Space(8f);
            Header("Цвет дисков");
            work.rimColor = ColorPicker(work.rimColor);
            GUILayout.Space(8f);
            Header("Шины");
            work.tires = (TireType)GUILayout.SelectionGrid((int)work.tires, TuningCatalog.TireNames, 3, tallTab);
            var g = TuningCatalog.TireGrip[(int)work.tires];
            GUILayout.Label($"Сцепление: вперёд {g.x * 100f:0} %, вбок {g.y * 100f:0} %" + (g.z < 1f ? ", зад скользит легче (для дрифта)" : ""), small);
        }

        void EngineTab()
        {
            work.ecu = Level("Чип-тюнинг", TuningCatalog.EcuNames, work.ecu);
            work.turbo = Level("Турбина", TuningCatalog.TurboNames, work.turbo);
            work.exhaust = Level("Выхлоп", TuningCatalog.ExhaustNames, work.exhaust);
            work.weightReduction = Level("Облегчение", TuningCatalog.WeightNames, work.weightReduction);
            if (GUILayout.Button("Всё на максимум", button))
            {
                work.ecu = TuningCatalog.MaxLevel(TuningCatalog.EcuNames);
                work.turbo = TuningCatalog.MaxLevel(TuningCatalog.TurboNames);
                work.exhaust = TuningCatalog.MaxLevel(TuningCatalog.ExhaustNames);
                work.weightReduction = TuningCatalog.MaxLevel(TuningCatalog.WeightNames);
                GUI.changed = true;
            }
        }

        void GearboxTab()
        {
            work.adjustableGearbox = GUILayout.Toggle(work.adjustableGearbox, " Регулируемая главная пара", toggle);
            if (!work.adjustableGearbox)
            {
                GUILayout.Label("Короче — быстрее разгон, длиннее — выше максимальная скорость.", small);
                return;
            }
            work.finalDriveMul = Slider("Главная пара", work.finalDriveMul, 0.85f, 1.15f,
                v => v > 1.005f ? $"короче на {(v - 1f) * 100f:0} % (разгон)" : v < 0.995f ? $"длиннее на {(1f - v) * 100f:0} % (скорость)" : "сток");
        }

        void BrakesTab()
        {
            work.brakes = Level("Тормоза", TuningCatalog.BrakeNames, work.brakes);
            if (work.brakes > 0)
                work.brakeBias = Slider("Баланс (перед / зад)", work.brakeBias, 0.5f, 0.75f, v => $"{v * 100f:0} / {(1f - v) * 100f:0}");
            else
                GUILayout.Label("<i>Баланс тормозов настраивается со спортивными тормозами.</i>", small);
        }

        void ExteriorTab()
        {
            work.spoiler = Level("Спойлер", TuningCatalog.SpoilerNames, work.spoiler);
            GUILayout.Label("<i>Спойлер прижимает машину на скорости, но немного снижает максималку.</i>", small);
            GUILayout.Space(10f);

            work.neon = GUILayout.Toggle(work.neon, " Неон под днищем", toggle);
            if (work.neon) work.neonColor = ColorPicker(work.neonColor, false);
            GUILayout.Space(10f);

            Header("Цвет фар");
            int current = -1;
            for (int i = 0; i < HeadlightPresets.Length; i++)
                if (HeadlightPresets[i] == work.headlightColor) current = i;
            int picked = GUILayout.SelectionGrid(current, HeadlightNames, 4, tabStyle);
            if (picked != current && picked >= 0) work.headlightColor = HeadlightPresets[picked];
        }

        // ---------------- widgets ----------------

        void Header(string text) => GUILayout.Label($"<b>{text}</b>", label);

        float Slider(string name, float value, float min, float max, Func<float, string> format)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label);
            GUILayout.Label(format(value), rightStyle, GUILayout.Width(190f));
            GUILayout.EndHorizontal();
            float v = GUILayout.HorizontalSlider(value, min, max, slider, thumb);
            GUILayout.Space(10f);
            return v;
        }

        int Level(string name, string[] names, int level)
        {
            int max = names.Length - 1;
            GUILayout.Label($"<b>{name}</b>: {names[level]}", label);
            GUILayout.BeginHorizontal();
            GUI.enabled = level > 0;
            if (GUILayout.Button("−", button, GUILayout.Width(60f))) level--;
            GUI.enabled = level < max;
            if (GUILayout.Button(level < max ? $"+  {names[level + 1]}" : "Максимум", button)) level++;
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Space(10f);
            return level;
        }

        Color ColorPicker(Color c, bool rgb = true)
        {
            const int perRow = 8;
            var palette = TuningCatalog.Palette;
            for (int i = 0; i < palette.Length; i += perRow)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(i + perRow, palette.Length); j++)
                {
                    var old = GUI.backgroundColor;
                    GUI.backgroundColor = palette[j];
                    if (GUILayout.Button(GUIContent.none, swatch, GUILayout.Width(44f), GUILayout.Height(36f)))
                    {
                        c = palette[j];
                        GUI.changed = true;
                    }
                    GUI.backgroundColor = old;
                }
                GUILayout.EndHorizontal();
            }
            if (!rgb) return c;
            c.r = Slider("Красный", c.r, 0f, 1f, v => $"{v * 255f:0}");
            c.g = Slider("Зелёный", c.g, 0f, 1f, v => $"{v * 255f:0}");
            c.b = Slider("Синий", c.b, 0f, 1f, v => $"{v * 255f:0}");
            return c;
        }

        // ---------------- right side ----------------

        void DrawStats()
        {
            var baseStats = TuningStats.Compute(spec, new TuningData());
            var s = car.Stats;
            GUILayout.BeginArea(StatsPanel, box);
            GUILayout.Label("<b>Характеристики</b>", label);
            StatRow("Мощность", baseStats.powerHp, s.powerHp, "л.с.", true);
            StatRow("Момент", baseStats.torqueNm, s.torqueNm, "Н·м", true);
            StatRow("Масса", baseStats.massKg, s.massKg, "кг", false);
            StatRow("Макс. скорость", baseStats.topSpeedKmh, s.topSpeedKmh, "км/ч", true);
            StatRow("Л.с. на тонну", baseStats.hpPerTon, s.hpPerTon, "", true);
            GUILayout.Label($"Привод {spec.drive} · {spec.gears} ст. · {TuningCatalog.TireNames[(int)work.tires].ToLowerInvariant()} шины", small);
            GUILayout.EndArea();
        }

        void StatRow(string name, float before, float after, string unit, bool higherIsBetter)
        {
            string a = $"{after:0}";
            if (Mathf.Abs(after - before) >= 0.5f)
            {
                bool better = (after > before) == higherIsBetter;
                a = $"{before:0} → <color={(better ? "#6dff7a" : "#ff6d6d")}>{after:0}</color>";
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, small);
            GUILayout.Label($"{a} {unit}", rightStyle);
            GUILayout.EndHorizontal();
        }

        void DrawBottom()
        {
            bool dirty = JsonUtility.ToJson(work) != JsonUtility.ToJson(saved);

            GUILayout.BeginArea(BottomPanel, box);
            if (confirmExit)
            {
                GUILayout.Label("Есть несохранённые изменения.", small);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Сохранить", button)) { Save(); Close(); }
                if (GUILayout.Button("Не сохранять", button)) Close();
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUI.enabled = dirty;
                if (GUILayout.Button("Сохранить", button)) Save();
                if (GUILayout.Button("Отмена", button)) { work = saved.Clone(); Preview(); }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                if (GUILayout.Button("Выйти из гаража", button))
                {
                    if (dirty) confirmExit = true;
                    else Close();
                }
            }

            // decide visibility only on Layout so Layout and Repaint see the same controls
            if (Event.current.type == EventType.Layout)
                showMessage = !string.IsNullOrEmpty(message) && Time.unscaledTime < messageUntil;
            if (showMessage)
                GUILayout.Label($"<color=#ffd34d>{message}</color>", small);
            GUILayout.EndArea();
        }

        void Save()
        {
            saved = work.Clone();
            onSaved?.Invoke(saved.Clone());
            Toast("Сохранено");
        }
    }
}
