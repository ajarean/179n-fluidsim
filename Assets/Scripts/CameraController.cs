using UnityEngine;

public class CameraController : MonoBehaviour
{
    public Transform cameraTransform;
    public float orbitSpeed = 5f;
    public float panSpeed = 0.5f;
    public float zoomSpeed = 10f;
    public float minDistance = 1f;
    public float maxDistance = 100f;

    private Vector3 focusPoint;
    private float distance;

    void Start()
    {
        if (cameraTransform == null) { 
            cameraTransform = Camera.main.transform;
        }
        focusPoint = cameraTransform.position + cameraTransform.forward * 10f;
        distance = Vector3.Distance(cameraTransform.position, focusPoint);
    }

    void Update()
    {
        // orbit: alt + lmb
        if (Input.GetMouseButton(0) && Input.GetKey(KeyCode.LeftAlt))
        {
            float rotX = Input.GetAxis("Mouse X") * orbitSpeed;
            float rotY = -Input.GetAxis("Mouse Y") * orbitSpeed;

            Quaternion rot = Quaternion.Euler(rotY, rotX, 0);
            Vector3 direction = cameraTransform.position - focusPoint;
            direction = rot * direction;

            cameraTransform.position = focusPoint + direction;
            cameraTransform.LookAt(focusPoint);
        }

        // pan: rmb
        if (Input.GetMouseButton(1) && !Input.GetKey(KeyCode.LeftAlt))
        {
            Vector3 right = cameraTransform.right;
            Vector3 up = cameraTransform.up;

            Vector3 movement = -right * Input.GetAxis("Mouse X") * panSpeed
                               - up * Input.GetAxis("Mouse Y") * panSpeed;

            cameraTransform.position += movement;
            focusPoint += movement;
        }

        // zoom: scroll
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            distance = Mathf.Clamp(distance - scroll * zoomSpeed, minDistance, maxDistance);
            Vector3 dir = (cameraTransform.position - focusPoint).normalized;
            cameraTransform.position = focusPoint + dir * distance;
        }
    }
}
