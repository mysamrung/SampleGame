using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerBehaviour : MonoBehaviour
{
    private CharacterController characterController;
    private Animator animator;

    private InputAction moveAction;

    [SerializeField]
    private CharacterMovementStats moveStats;


    private void Start() {
        characterController = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();

        moveAction = InputSystem.actions.FindAction("Move");

        var playerMovementStateMachines = animator.GetBehaviours<PlayerMovementStateMachine>();
        foreach(var playerMovementStateMachine in playerMovementStateMachines) {
            playerMovementStateMachine.Setup(moveAction, characterController, moveStats);
        }
    }
}
