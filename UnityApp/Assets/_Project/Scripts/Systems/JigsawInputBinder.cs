using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Central input binding manager that discovers LaserPointer and PieceHolder components
/// on child GameObjects and wires them up to the XRI_Jigsaw input action map.
/// Provides a single point of input configuration for both hands.
/// </summary>
public class JigsawInputBinder : MonoBehaviour
{
    [SerializeField] private InputActionAsset inputActions;
    private InputActionMap jigsawMap;

    private LaserPointer leftLaser;
    private LaserPointer rightLaser;
    private PieceHolder leftHolder;
    private PieceHolder rightHolder;

    void Awake()
    {
        if (inputActions == null)
        {
            var jsonAsset = Resources.Load<TextAsset>("XRI_Jigsaw");
            if (jsonAsset != null)
                inputActions = InputActionAsset.FromJson(jsonAsset.text);
        }

        if (inputActions == null)
        {
            Debug.LogError("[JigsawInputBinder] XRI_Jigsaw input actions not found. Ensure XRI_Jigsaw.inputactions is in a Resources folder or assigned.");
            return;
        }

        jigsawMap = inputActions.FindActionMap("Jigsaw");
        if (jigsawMap == null)
        {
            Debug.LogError("[JigsawInputBinder] Action map 'Jigsaw' not found.");
            return;
        }

        FindComponents();
        BindActions();
    }

    /// <summary>Finds LaserPointer and PieceHolder components on child objects and assigns them by hand side.</summary>
    void FindComponents()
    {
        var lasers = GetComponentsInChildren<LaserPointer>(true);
        var holders = GetComponentsInChildren<PieceHolder>(true);

        foreach (var laser in lasers)
        {
            string parentName = laser.transform.parent != null ? laser.transform.parent.name : "";
            if (parentName.Contains("Left"))
            {
                leftLaser = laser;
                laser.Hand = LaserPointer.HandSide.Left;
            }
            else if (parentName.Contains("Right"))
            {
                rightLaser = laser;
                laser.Hand = LaserPointer.HandSide.Right;
            }
        }

        foreach (var holder in holders)
        {
            string parentName = holder.transform.parent != null ? holder.transform.parent.name : "";
            if (parentName.Contains("Left"))
                leftHolder = holder;
            else if (parentName.Contains("Right"))
                rightHolder = holder;
        }
    }

    /// <summary>Binds all left and right hand input actions to their respective handlers.</summary>
    void BindActions()
    {
        Bind(jigsawMap.FindAction("LeftLaserToggle"),  OnLeftLaserToggle);
        Bind(jigsawMap.FindAction("LeftTrigger"),      OnLeftTrigger,      OnLeftTriggerReleased);
        Bind(jigsawMap.FindAction("LeftReturn"),        OnLeftReturn);
        Bind(jigsawMap.FindAction("LeftGrip"),          OnLeftGripPressed, OnLeftGripReleased);

        Bind(jigsawMap.FindAction("RightLaserToggle"), OnRightLaserToggle);
        Bind(jigsawMap.FindAction("RightTrigger"),      OnRightTrigger,     OnRightTriggerReleased);
        Bind(jigsawMap.FindAction("RightReturn"),       OnRightReturn);
        Bind(jigsawMap.FindAction("RightGrip"),         OnRightGripPressed, OnRightGripReleased);
    }

    /// <summary>Wires an input action's performed and optional canceled events to the given callbacks.</summary>
    /// <param name="action">The input action to bind.</param>
    /// <param name="performed">Callback for the performed event.</param>
    /// <param name="canceled">Optional callback for the canceled event.</param>
    void Bind(InputAction action, System.Action<InputAction.CallbackContext> performed,
              System.Action<InputAction.CallbackContext> canceled = null)
    {
        if (action == null) return;
        action.performed += performed;
        if (canceled != null)
            action.canceled += canceled;
    }

    void OnEnable()
    {
        if (jigsawMap != null)
            jigsawMap.Enable();
    }

    void OnDisable()
    {
        if (jigsawMap != null)
            jigsawMap.Disable();
    }

    void OnDestroy()
    {
        if (jigsawMap == null) return;

        var actions = jigsawMap.actions;
        foreach (var a in actions)
        {
            a.performed -= null;
            a.canceled -= null;
        }
    }

    void OnLeftLaserToggle(InputAction.CallbackContext ctx)  { if (leftLaser != null) leftLaser.OnToggleButton(); }
    void OnLeftTrigger(InputAction.CallbackContext ctx)      { if (leftLaser != null) leftLaser.OnTriggerButton(); }
    void OnLeftTriggerReleased(InputAction.CallbackContext ctx) { if (leftLaser != null) leftLaser.OnTriggerReleased(); }
    void OnLeftReturn(InputAction.CallbackContext ctx)       { if (leftHolder != null) leftHolder.OnReturnButton(); }
    void OnLeftGripPressed(InputAction.CallbackContext ctx)  { }
    void OnLeftGripReleased(InputAction.CallbackContext ctx) { if (leftHolder != null) leftHolder.OnGripReleased(); }

    void OnRightLaserToggle(InputAction.CallbackContext ctx) { if (rightLaser != null) rightLaser.OnToggleButton(); }
    void OnRightTrigger(InputAction.CallbackContext ctx)     { if (rightLaser != null) rightLaser.OnTriggerButton(); }
    void OnRightTriggerReleased(InputAction.CallbackContext ctx) { if (rightLaser != null) rightLaser.OnTriggerReleased(); }
    void OnRightReturn(InputAction.CallbackContext ctx)      { if (rightHolder != null) rightHolder.OnReturnButton(); }
    void OnRightGripPressed(InputAction.CallbackContext ctx) { }
    void OnRightGripReleased(InputAction.CallbackContext ctx){ if (rightHolder != null) rightHolder.OnGripReleased(); }
}
