using Unity.Cinemachine;
using UnityEngine;

public class CameraController : MonoBehaviour
{
    [SerializeField]
    private GameObject self;

    [SerializeField]
    private GameObject target;

    [SerializeField]
    private CinemachineCamera cinemachineCamera;

    private GameObject lookAtObject;

    [SerializeField]
    private float focusRatio;

    private void Awake() {
        lookAtObject = new GameObject("Camera_LookAt");
        lookAtObject.transform.parent = transform;

        cinemachineCamera.LookAt = lookAtObject.transform;
    }

    private void Update() {
        if (self != null) {
            if(target != null) {
                lookAtObject.transform.position = ((1 - focusRatio) * self.transform.position) + (focusRatio * target.transform.position);
            } else {
                lookAtObject.transform.position = self.transform.position;
            }
        }
    }
}
