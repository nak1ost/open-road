using RaceGame.Cars;
using RaceGame.Input;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
#endif

namespace RaceGame.UI
{
    /// <summary>
    /// Temporary HUD (speed, gear, RPM, car name) and on-screen touch buttons for phones.
    /// Will be replaced by a proper uGUI interface in stage 6.
    /// </summary>
    public class Hud : MonoBehaviour
    {
        public CarController car;
        public PlayerCarInput input;
        public bool showTouchControls;

        public bool NextCarTapped { get; private set; }
        public bool PrevCarTapped { get; private set; }
        public bool ResetTapped { get; private set; }
        public bool CameraTapped { get; private set; }
        public bool GarageTapped { get; private set; }
        public bool MenuTapped { get; private set; }
        /// <summary>Hidden while the garage or a menu is open.</summary>
        public bool hidden;
        /// <summary>Car switching and garage buttons (off during races).</summary>
        public bool carButtons = true;

        GUIStyle big, small, button;

        void OnEnable()
        {
            showTouchControls = Application.isMobilePlatform;
#if ENABLE_INPUT_SYSTEM
            EnhancedTouchSupport.Enable();
#endif
        }

        // Button rectangles in screen pixels (y from top, as in GUI).
        Rect Left => R(0.02f, 0.68f, 0.12f, 0.28f);
        Rect Right => R(0.16f, 0.68f, 0.12f, 0.28f);
        Rect Gas => R(0.86f, 0.55f, 0.12f, 0.41f);
        Rect Brake => R(0.72f, 0.72f, 0.12f, 0.24f);
        Rect Handbrake => R(0.72f, 0.52f, 0.12f, 0.17f);
        Rect BtnPrev => R(0.30f, 0.02f, 0.09f, 0.08f);
        Rect BtnNext => R(0.61f, 0.02f, 0.09f, 0.08f);
        Rect BtnReset => R(0.88f, 0.02f, 0.10f, 0.08f);
        Rect BtnCam => R(0.88f, 0.12f, 0.10f, 0.08f);
        Rect BtnGarage => R(0.88f, 0.22f, 0.10f, 0.08f);
        Rect BtnMenu => R(0.455f, 0.02f, 0.09f, 0.08f);

        static Rect R(float x, float y, float w, float h) =>
            new Rect(x * Screen.width, y * Screen.height, w * Screen.width, h * Screen.height);

        void Update()
        {
            NextCarTapped = PrevCarTapped = ResetTapped = CameraTapped = GarageTapped = MenuTapped = false;
            if (!showTouchControls || input == null) return;
            if (hidden)
            {
                input.touchThrottle = input.touchSteer = 0f;
                input.touchHandbrake = false;
                return;
            }

            float throttle = 0f, steer = 0f;
            bool handbrake = false;
#if ENABLE_INPUT_SYSTEM
            foreach (var t in ETouch.activeTouches)
            {
                Vector2 p = t.screenPosition;
                p.y = Screen.height - p.y; // to GUI coordinates
                bool began = t.phase == UnityEngine.InputSystem.TouchPhase.Began;
                if (Left.Contains(p)) steer -= 1f;
                if (Right.Contains(p)) steer += 1f;
                if (Gas.Contains(p)) throttle += 1f;
                if (Brake.Contains(p)) throttle -= 1f;
                if (Handbrake.Contains(p)) handbrake = true;
                if (began && BtnNext.Contains(p)) NextCarTapped = true;
                if (began && BtnPrev.Contains(p)) PrevCarTapped = true;
                if (began && BtnReset.Contains(p)) ResetTapped = true;
                if (began && BtnCam.Contains(p)) CameraTapped = true;
                if (began && BtnGarage.Contains(p)) GarageTapped = true;
                if (began && BtnMenu.Contains(p)) MenuTapped = true;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            for (int i = 0; i < UnityEngine.Input.touchCount; i++)
            {
                var t = UnityEngine.Input.GetTouch(i);
                Vector2 p = t.position;
                p.y = Screen.height - p.y;
                bool began = t.phase == UnityEngine.TouchPhase.Began;
                if (Left.Contains(p)) steer -= 1f;
                if (Right.Contains(p)) steer += 1f;
                if (Gas.Contains(p)) throttle += 1f;
                if (Brake.Contains(p)) throttle -= 1f;
                if (Handbrake.Contains(p)) handbrake = true;
                if (began && BtnNext.Contains(p)) NextCarTapped = true;
                if (began && BtnPrev.Contains(p)) PrevCarTapped = true;
                if (began && BtnReset.Contains(p)) ResetTapped = true;
                if (began && BtnCam.Contains(p)) CameraTapped = true;
                if (began && BtnGarage.Contains(p)) GarageTapped = true;
                if (began && BtnMenu.Contains(p)) MenuTapped = true;
            }
#endif
            if (!carButtons) NextCarTapped = PrevCarTapped = GarageTapped = false;
            input.touchThrottle = throttle;
            input.touchSteer = steer;
            input.touchHandbrake = handbrake;
        }

        void OnGUI()
        {
            if (hidden) return;
            if (big == null)
            {
                big = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerRight, fontStyle = FontStyle.Bold, richText = true };
                small = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, richText = true };
                button = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            }
            int unit = Mathf.Max(12, Screen.height / 40);
            big.fontSize = unit * 3;
            small.fontSize = unit;
            button.fontSize = unit;

            if (car != null && car.Spec != null)
            {
                string gear = car.Gear < 0 ? "R" : car.Gear.ToString();
                var speedRect = showTouchControls ? R(0.30f, 0.70f, 0.40f, 0.28f) : R(0.60f, 0.70f, 0.38f, 0.28f);
                big.alignment = showTouchControls ? TextAnchor.LowerCenter : TextAnchor.LowerRight;
                GUI.Label(speedRect, $"{car.SpeedKmh:0} км/ч\n<size={unit * 2}>{gear}  ·  {car.EngineRpm:0} об/мин</size>", big);

                var s = car.Spec;
                GUI.Label(R(0.02f, 0.02f, 0.5f, 0.2f),
                    $"<b>{s.DisplayName}</b>  ({s.years})\n{car.Stats.powerHp:0} л.с. · {car.Stats.torqueNm:0} Н·м · {car.Stats.massKg:0} кг · {s.drive} · {s.gears} ст.", small);
            }

            if (!showTouchControls)
            {
                GUI.Label(R(0.02f, 0.86f, 0.6f, 0.14f),
                    "W/S — газ/тормоз-назад · A/D — руль · Пробел — ручник · R — на колёса · C — камера\nQ/E — сменить машину · G — гараж · Esc — меню", small);
                return;
            }

            GUI.Box(Left, "◀", button);
            GUI.Box(Right, "▶", button);
            GUI.Box(Gas, "ГАЗ", button);
            GUI.Box(Brake, "ТОРМОЗ", button);
            GUI.Box(Handbrake, "РУЧНИК", button);
            if (carButtons)
            {
                GUI.Box(BtnPrev, "◀ авто", button);
                GUI.Box(BtnNext, "авто ▶", button);
                GUI.Box(BtnGarage, "Гараж", button);
            }
            GUI.Box(BtnReset, "Сброс", button);
            GUI.Box(BtnCam, "Камера", button);
            GUI.Box(BtnMenu, "Меню", button);
        }
    }
}
