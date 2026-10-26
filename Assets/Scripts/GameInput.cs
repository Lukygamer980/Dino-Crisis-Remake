using UnityEngine;
using UnityEngine.InputSystem;

// The game's controls, from the Input Actions asset in Assets/Resources/Controls.inputactions.
// Every action binds keyboard and mouse and the standard gamepad layout, so a DualSense and an
// Xbox pad work alike: buttonSouth is Cross / A, buttonWest is Square / X, start is Options / Menu.
public static class GameInput
{
    static InputActionMap map;

    // Mouse movement, scaled like the legacy "Mouse X/Y" axes, and the right stick, from -1 to 1.
    public static InputAction Look => Action("Look");
    public static InputAction LookStick => Action("Look Stick");
    public static InputAction Move => Action("Move");
    public static InputAction Run => Action("Run");
    public static InputAction Aim => Action("Aim");
    public static InputAction Fire => Action("Fire");
    public static InputAction Interact => Action("Interact");
    public static InputAction Restart => Action("Restart");
    public static InputAction Reload => Action("Reload");
    public static InputAction Crouch => Action("Crouch");
    public static InputAction Jump => Action("Jump");
    public static InputAction Holster => Action("Holster");

    static InputAction Action(string name)
    {
        if (map == null)
        {
            map = Resources.Load<InputActionAsset>("Controls").FindActionMap("Player", true);
            map.Enable();
        }
        return map.FindAction(name, true);
    }

    // Whether the button that last pressed `action` is on the mouse.
    public static bool FromMouse(InputAction action) => action.activeControl?.device is Mouse;
}
