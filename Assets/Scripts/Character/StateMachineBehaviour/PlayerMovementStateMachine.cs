using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovementStateMachine : StateMachineBehaviour {

    private static readonly int MoveStateID = Animator.StringToHash("Move");

    private CharacterMovementStats characterMovementStats;

    private CharacterController characterController;

    private InputAction moveAction;

    public void Setup(InputAction moveAction, CharacterController characterController, CharacterMovementStats characterMovementStats) {
        this.moveAction = moveAction;
        this.characterController = characterController;
        this.characterMovementStats = characterMovementStats;
    }

    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) {

    }

    public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) {
        if (characterMovementStats == null || characterController == null || moveAction == null)
            return;


        Movement(animator.transform, animator, stateInfo);
        Gravity();
    }

    private void Movement(Transform transform, Animator animator, AnimatorStateInfo stateInfo) {

        Vector2 moveValue = moveAction.ReadValue<Vector2>();
        if (moveValue.sqrMagnitude <= 0) {
            animator.SetBool(MoveStateID, false);
            return;
        }
        else {
            animator.SetBool(MoveStateID, true);

            if(stateInfo.IsTag("Move")) { 
                Vector3 direction = new Vector3(moveValue.x, 0, moveValue.y);
                direction = Camera.main.transform.TransformDirection(direction);
                direction.y = 0;
                direction.Normalize();

                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), characterMovementStats.rotateSpeed * Time.deltaTime);

                characterController.Move(transform.forward * characterMovementStats.moveSpeed * Time.deltaTime);
            }
        }
    }

    public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) {

    }


    private void Gravity() {
        characterController.Move(Vector3.down * 50 * Time.deltaTime);
    }
}
