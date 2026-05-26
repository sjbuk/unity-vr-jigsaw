using UnityEngine;
using UnityEngine.InputSystem;

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

    void BindActions()
    {
        Bind(jigsawMap.FindAction("LeftLaserToggle"),  OnLeftLaserToggle);
        Bind(jigsawMap.FindAction("LeftTrigger"),      OnLeftTrigger);
        Bind(jigsawMap.FindAction("LeftReturn"),        OnLeftReturn);
        Bind(jigsawMap.FindAction("LeftGrip"),          OnLeftGripPressed, OnLeftGripReleased);

        Bind(jigsawMap.FindAction("RightLaserToggle"), OnRightLaserToggle);
        Bind(jigsawMap.FindAction("RightTrigger"),      OnRightTrigger);
        Bind(jigsawMap.FindAction("RightReturn"),       OnRightReturn);
        Bind(jigsawMap.FindAction("RightGrip"),         OnRightGripPressed, OnRightGripReleased);
    }

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
    void OnLeftReturn(InputAction.CallbackContext ctx)       { if (leftHolder != null) leftHolder.OnReturnButton(); }
    void OnLeftGripPressed(InputAction.CallbackContext ctx)  { }
    void OnLeftGripReleased(InputAction.CallbackContext ctx) { if (leftHolder != null) leftHolder.OnGripReleased(); }

    void OnRightLaserToggle(InputAction.CallbackContext ctx) { if (rightLaser != null) rightLaser.OnToggleButton(); }
    void OnRightTrigger(InputAction.CallbackContext ctx)     { if (rightLaser != null) rightLaser.OnTriggerButton(); }
    void OnRightReturn(InputAction.CallbackContext ctx)      { if (rightHolder != null) rightHolder.OnReturnButton(); }
    void OnRightGripPressed(InputAction.CallbackContext ctx) { }
    void OnRightGripReleased(InputAction.CallbackContext ctx){ if (rightHolder != null) rightHolder.OnGripReleased(); }
}
