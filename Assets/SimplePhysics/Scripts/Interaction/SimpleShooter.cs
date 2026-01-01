using UnityEngine;
using UnityEngine.InputSystem;

public class SimpleShooter : MonoBehaviour
{
    public Camera cam;
    public Rigidbody projectilePrefab;


    [Header("Projectile")]
    public float speed = 20f;
    public float rotationSpeed = 5f;
    public float mass = 1f;
    public Vector3 scale = Vector3.one;
    public bool randomRotation = true;


    void Update()
    {
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            Shoot();
        }
    }

    void Shoot()
    {
        if (!cam || !projectilePrefab) return;

        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());

        Vector3 spawnPos = cam.transform.position + cam.transform.forward * 0.2f;

        Quaternion rot = randomRotation ? Random.rotation : Quaternion.identity;

        Rigidbody rb = Instantiate(projectilePrefab, spawnPos, rot);

        rb.mass = mass;
        rb.transform.localScale = scale;

        rb.linearVelocity = ray.direction * speed;
        rb.angularVelocity = Random.onUnitSphere * rotationSpeed;

    }
}
