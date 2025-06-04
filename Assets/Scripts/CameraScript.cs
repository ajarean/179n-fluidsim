using UnityEngine;

public class CameraScript : MonoBehaviour
{
    public Transform cameraTransform;
    public float lookSpeed = 3f;
    public float orbitSpeed = 5f;
    public float panSpeed = 0.5f;
    public float zoomSpeed = 10f;
    public float minDistance = 1f;
    public float maxDistance = 100f;

    private Vector3 focusPoint;
    private float distance;

    private float yaw = 0f;
    private float pitch = 0f;

    void Start()
    {
        if (cameraTransform == null)
        {
            cameraTransform = Camera.main.transform;
        }
        focusPoint = cameraTransform.position + cameraTransform.forward * 10f;
        distance = Vector3.Distance(cameraTransform.position, focusPoint);
        Vector3 angles = cameraTransform.eulerAngles;
        yaw = angles.y;
        pitch = angles.x;
    }

    void Update()
    {
        // rotate: lmb
        if (Input.GetMouseButton(0) )
        {
            // float rotX = Input.GetAxis("Mouse X") * orbitSpeed;
            // float rotY = -Input.GetAxis("Mouse Y") * orbitSpeed;

            // Quaternion rot = Quaternion.Euler(rotY, rotX, 0);
            // Vector3 direction = cameraTransform.position - focusPoint;
            // direction = rot * direction;

            // cameraTransform.position = focusPoint + direction;
            // cameraTransform.LookAt(focusPoint);
            float mouseX = Input.GetAxis("Mouse X") * lookSpeed;
            float mouseY = Input.GetAxis("Mouse Y") * lookSpeed;

            yaw += mouseX;
            pitch -= mouseY;
            pitch = Mathf.Clamp(pitch, -89f, 89f); // Prevent flipping

            cameraTransform.rotation = Quaternion.Euler(pitch, yaw, 0f);
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