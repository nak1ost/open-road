using RaceGame.Cars;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RaceGame.Input
{
    /// <summary>
    /// Reads keyboard / gamepad (for testing in the editor) plus on-screen touch values
    /// (set by the touch controls) and passes them to the CarController.
    /// </summary>
    public class PlayerCarInput : MonoBehaviour
    {
        public CarController car;

        // Values from on-screen touch controls.
        public float touchThrottle;
        public float touchSteer;
        public bool touchHandbrake;

        public bool ResetPressed { get; private set; }
        public bool NextCarPressed { get; private set; }
        public bool PrevCarPressed { get; private set; }
        public bool CameraPressed { get; private set; }
        public bool GaragePressed { get; private set; }
        public bool MenuPressed { get; private set; }

        float steerSmoothed;

        void Update()
        {
            float throttle = 0f, steer = 0f;
            bool handbrake = false;
            ResetPressed = NextCarPressed = PrevCarPressed = CameraPressed = GaragePressed = MenuPressed = false;

#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) throttle += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) throttle -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) steer += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) steer -= 1f;
                handbrake |= kb.spaceKey.isPressed;
                ResetPressed |= kb.rKey.wasPressedThisFrame;
                NextCarPressed |= kb.eKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame;
                PrevCarPressed |= kb.qKey.wasPressedThisFrame;
                CameraPressed |= kb.cKey.wasPressedThisFrame;
                GaragePressed |= kb.gKey.wasPressedThisFrame;
                MenuPressed |= kb.escapeKey.wasPressedThisFrame;
            }
            var pad = Gamepad.current;
            if (pad != null)
            {
                throttle += pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
                steer += pad.leftStick.x.ReadValue();
                handbrake |= pad.buttonSouth.isPressed;
                ResetPressed |= pad.buttonNorth.wasPressedThisFrame;
                NextCarPressed |= pad.rightShoulder.wasPressedThisFrame;
                PrevCarPressed |= pad.leftShoulder.wasPressedThisFrame;
                CameraPressed |= pad.buttonWest.wasPressedThisFrame;
                GaragePressed |= pad.selectButton.wasPressedThisFrame;
                MenuPressed |= pad.startButton.wasPressedThisFrame;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            throttle += UnityEngine.Input.GetAxisRaw("Vertical");
            steer += UnityEngine.Input.GetAxisRaw("Horizontal");
            handbrake |= UnityEngine.Input.GetKey(KeyCode.Space);
            ResetPressed |= UnityEngine.Input.GetKeyDown(KeyCode.R);
            NextCarPressed |= UnityEngine.Input.GetKeyDown(KeyCode.E) || UnityEngine.Input.GetKeyDown(KeyCode.Tab);
            PrevCarPressed |= UnityEngine.Input.GetKeyDown(KeyCode.Q);
            CameraPressed |= UnityEngine.Input.GetKeyDown(KeyCode.C);
            GaragePressed |= UnityEngine.Input.GetKeyDown(KeyCode.G);
            MenuPressed |= UnityEngine.Input.GetKeyDown(KeyCode.Escape);
#endif
            throttle += touchThrottle;
            steer += touchSteer;
            handbrake |= touchHandbrake;

            // keyboard steering is digital, smooth it a little
            steerSmoothed = Mathf.MoveTowards(steerSmoothed, Mathf.Clamp(steer, -1f, 1f), Time.deltaTime * 4f);

            if (car == null) return;
            car.throttleInput = Mathf.Clamp(throttle, -1f, 1f);
            car.steerInput = steerSmoothed;
            car.handbrakeInput = handbrake;
        }
    }
}
