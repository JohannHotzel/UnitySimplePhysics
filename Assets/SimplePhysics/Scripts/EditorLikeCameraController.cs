using UnityEngine;
using UnityEngine.InputSystem;

public class EditorLikeCameraController : MonoBehaviour
{
    public Camera cam;

    [Header("Look (RMB)")]
    public float lookSensitivity = 0.1f;
    public float minPitch = -89f;
    public float maxPitch = 89f;

    [Header("Pan (MMB)")]
    public float panSpeed = 0.03f;

    [Header("Zoom (Scroll)")]
    public float zoomSpeed = 200.0f;


    float yaw;
    float pitch;

    void Start()
    {
        if (!cam) cam = GetComponentInChildren<Camera>();

        Vector3 euler = transform.rotation.eulerAngles;
        yaw = euler.y;
        pitch = euler.x;
    }

    void Update()
    {
        if (Mouse.current == null) return;

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();

        //  LOOK (RMB) 
        if (Mouse.current.rightButton.isPressed)
        {
            yaw += mouseDelta.x * lookSensitivity;
            pitch -= mouseDelta.y * lookSensitivity;
          //  pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

            cam.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        //  PAN (MMB) 
        if (Mouse.current.middleButton.isPressed)
        {
            Vector3 right = cam.transform.right;
            Vector3 up = cam.transform.up;

            Vector3 move =
                (-right * mouseDelta.x +
                 -up * mouseDelta.y) * panSpeed;

            cam.transform.position += move;
        }

        //  ZOOM (SCROLL) 
        float scroll = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            cam.transform.position += cam.transform.forward * (scroll * zoomSpeed * Time.deltaTime);
        }
    }
}
