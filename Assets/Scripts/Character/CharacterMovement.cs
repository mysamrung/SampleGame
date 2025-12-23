using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.InputSystem;

public class CharacterMovement : MonoBehaviour
{
    private CharacterController characterController;

    public float moveSpeed;
    public float rotateSpeed;

    private InputAction moveAction;

    private void Start() {
        characterController = GetComponent<CharacterController>();

        // "Move" ‚Æ "Jump" ‚ÌƒŠƒtƒ@ƒŒƒ“ƒX‚ð’T‚·
        moveAction = InputSystem.actions.FindAction("Move");
    }


    private void Update() {
        Movement();
        Gravity();
    }

    private void Movement() {
        
        Vector2 moveValue = moveAction.ReadValue<Vector2>();
        if (moveValue.sqrMagnitude <= 0)
            return;

        Vector3 direction = new Vector3(moveValue.x, 0, moveValue.y);
        direction = Camera.main.transform.TransformDirection(direction);
        direction.y = 0;
        direction.Normalize();

        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), rotateSpeed * Time.deltaTime);

        characterController.Move(transform.forward * moveSpeed * Time.deltaTime);
    }

    private void Gravity() {

        characterController.Move(Vector3.down * 10 * Time.deltaTime);
    }
}
