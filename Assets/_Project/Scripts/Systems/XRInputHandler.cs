using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;

namespace JigSawVR
{
    public class XRInputHandler : MonoBehaviour
    {
        [Header("Left Hand")]
        [SerializeField] private LaserPointer _leftLaser;
        [SerializeField] private PieceHolder _leftHolder;

        [Header("Right Hand")]
        [SerializeField] private LaserPointer _rightLaser;
        [SerializeField] private PieceHolder _rightHolder;

        [Header("Locomotion")]
        [SerializeField] private ActionBasedSnapTurnProvider _snapTurnProvider;

        [Header("Input Actions")]
        [SerializeField] private InputActionReference _leftLaserToggle;
        [SerializeField] private InputActionReference _leftTrigger;
        [SerializeField] private InputActionReference _leftReturn;
        [SerializeField] private InputActionReference _rightLaserToggle;
        [SerializeField] private InputActionReference _rightTrigger;
        [SerializeField] private InputActionReference _rightReturn;
        [SerializeField] private InputActionReference _leftGrip;
        [SerializeField] private InputActionReference _rightGrip;

        private void OnEnable()
        {
            if (_leftLaserToggle != null)
                _leftLaserToggle.action.performed += OnLeftLaserToggle;

            if (_leftTrigger != null)
                _leftTrigger.action.performed += OnLeftTrigger;

            if (_leftReturn != null)
                _leftReturn.action.performed += OnLeftReturn;

            if (_rightLaserToggle != null)
                _rightLaserToggle.action.performed += OnRightLaserToggle;

            if (_rightTrigger != null)
                _rightTrigger.action.performed += OnRightTrigger;

            if (_rightReturn != null)
                _rightReturn.action.performed += OnRightReturn;

            if (_leftGrip != null)
            {
                _leftGrip.action.performed += OnLeftGripPressed;
                _leftGrip.action.canceled += OnLeftGripReleased;
            }

            if (_rightGrip != null)
            {
                _rightGrip.action.performed += OnRightGripPressed;
                _rightGrip.action.canceled += OnRightGripReleased;
            }
        }

        private void OnDisable()
        {
            if (_leftLaserToggle != null)
                _leftLaserToggle.action.performed -= OnLeftLaserToggle;

            if (_leftTrigger != null)
                _leftTrigger.action.performed -= OnLeftTrigger;

            if (_leftReturn != null)
                _leftReturn.action.performed -= OnLeftReturn;

            if (_rightLaserToggle != null)
                _rightLaserToggle.action.performed -= OnRightLaserToggle;

            if (_rightTrigger != null)
                _rightTrigger.action.performed -= OnRightTrigger;

            if (_rightReturn != null)
                _rightReturn.action.performed -= OnRightReturn;

            if (_leftGrip != null)
            {
                _leftGrip.action.performed -= OnLeftGripPressed;
                _leftGrip.action.canceled -= OnLeftGripReleased;
            }

            if (_rightGrip != null)
            {
                _rightGrip.action.performed -= OnRightGripPressed;
                _rightGrip.action.canceled -= OnRightGripReleased;
            }
        }

        private void OnLeftLaserToggle(InputAction.CallbackContext ctx) => _leftLaser?.Toggle();
        private void OnLeftTrigger(InputAction.CallbackContext ctx) => _leftLaser?.PullPiece();
        private void OnLeftReturn(InputAction.CallbackContext ctx) => _leftHolder?.ReturnPieceToWall();
        private void OnRightLaserToggle(InputAction.CallbackContext ctx) => _rightLaser?.Toggle();
        private void OnRightTrigger(InputAction.CallbackContext ctx) => _rightLaser?.PullPiece();
        private void OnRightReturn(InputAction.CallbackContext ctx) => _rightHolder?.ReturnPieceToWall();
        private void OnLeftGripPressed(InputAction.CallbackContext ctx) { /* Grip hold handled by XRI */ }
        private void OnLeftGripReleased(InputAction.CallbackContext ctx) => _leftHolder?.ReleasePiece();
        private void OnRightGripPressed(InputAction.CallbackContext ctx) { /* Grip hold handled by XRI */ }
        private void OnRightGripReleased(InputAction.CallbackContext ctx) => _rightHolder?.ReleasePiece();
    }
}
