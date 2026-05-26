using UnityEngine;
using UnityEngine.InputSystem;

public class EditorFlyCamera : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 3f;
    public float fastSpeed = 10f;

    [Header("Look")]
    public float lookSensitivity = 2f;
    public bool invertY = false;

    private float yaw;
    private float pitch;
    private Vector2 moveInput;
    private Vector2 lookDelta;
    private bool isLooking;

    void Start()
    {
        if (!Application.isEditor)
        {
            Destroy(this);
            return;
        }

        yaw = transform.eulerAngles.y;
        pitch = transform.eulerAngles.x;

        var mouse = Mouse.current;
        if (mouse != null)
        {
            isLooking = mouse.rightButton.isPressed;
        }
    }

    void Update()
    {
        if (!Application.isEditor) return;

        ReadInput();
        HandleLook();
        HandleMovement();
    }

    void ReadInput()
    {
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;

        if (keyboard == null) return;

        moveInput = Vector2.zero;
        if (keyboard.wKey.isPressed) moveInput.y += 1f;
        if (keyboard.sKey.isPressed) moveInput.y -= 1f;
        if (keyboard.aKey.isPressed) moveInput.x -= 1f;
        if (keyboard.dKey.isPressed) moveInput.x += 1f;

        if (mouse != null)
        {
            isLooking = mouse.rightButton.isPressed;
            lookDelta = isLooking ? mouse.delta.ReadValue() : Vector2.zero;
        }
    }

    void HandleLook()
    {
        if (!isLooking || lookDelta == Vector2.zero) return;

        float mx = lookDelta.x * lookSensitivity * 0.1f;
        float my = lookDelta.y * lookSensitivity * 0.1f * (invertY ? 1f : -1f);

        yaw += mx;
        pitch = Mathf.Clamp(pitch + my, -89f, 89f);

        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    void HandleMovement()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        bool fast = keyboard.leftShiftKey.isPressed;
        float speed = fast ? fastSpeed : moveSpeed;
        bool hasInput = moveInput != Vector2.zero;

        if (hasInput)
        {
            Vector3 forward = transform.forward;
            Vector3 right = transform.right;
            forward.y = 0f;
            right.y = 0f;
            forward.Normalize();
            right.Normalize();

            Vector3 move = (forward * moveInput.y + right * moveInput.x) * speed * Time.deltaTime;
            transform.position += move;
        }

        float vertical = 0f;
        if (keyboard.eKey.isPressed) vertical += 1f;
        if (keyboard.qKey.isPressed) vertical -= 1f;
        if (vertical != 0f)
            transform.position += Vector3.up * vertical * speed * Time.deltaTime;
    }

    void OnGUI()
    {
        if (!Application.isEditor) return;

        GUILayout.BeginArea(new Rect(10, 10, 400, 150));
        GUILayout.Box("EDITOR FLY CAMERA (Input System)");
        GUILayout.Label("WASD = Move    E/Q = Up/Down    Shift = Fast");
        GUILayout.Label("Right-Click + Mouse = Look");
        GUILayout.EndArea();
    }
}
