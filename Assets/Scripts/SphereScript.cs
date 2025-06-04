using UnityEngine;

public class SphereScript : MonoBehaviour
{
    public float moveSpeed = 5f;
    public float resizeSpeed = 1f;
    public float minScale = 0.1f;
    public float maxScale = 10f;

    private Transform sphereTransform;

    void Start()
    {
        sphereTransform = transform;
    }

    void Update()
    {
        Movement();
        Resize();
    }

    void Movement()
    {
        Vector3 move = Vector3.zero;

        if (Input.GetKey(KeyCode.W)) move += Vector3.forward;
        if (Input.GetKey(KeyCode.S)) move += Vector3.back;
        if (Input.GetKey(KeyCode.A)) move += Vector3.left;
        if (Input.GetKey(KeyCode.D)) move += Vector3.right;
        if (Input.GetKey(KeyCode.Space)) move += Vector3.up;
        if (Input.GetKey(KeyCode.LeftShift)) move += Vector3.down;

        transform.Translate(move * moveSpeed * Time.deltaTime, Space.World);
    }

    void Resize()
    {
        float scaleChange = 0f;

        if (Input.GetKey(KeyCode.Q)) scaleChange -= resizeSpeed * Time.deltaTime;
        if (Input.GetKey(KeyCode.E)) scaleChange += resizeSpeed * Time.deltaTime;

        Vector3 newScale = sphereTransform.localScale + Vector3.one * scaleChange;
        newScale = Vector3.Max(Vector3.one * minScale, Vector3.Min(Vector3.one * maxScale, newScale));

        sphereTransform.localScale = newScale;
    }
}
