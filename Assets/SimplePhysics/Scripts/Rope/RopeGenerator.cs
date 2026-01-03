using System.Collections.Generic;
using UnityEngine;


public class RopeGenerator : MonoBehaviour
{
    [Header("Rope")]
    public GameObject spherePrefab;
    public int segments = 20;
    public float spacing = 0.25f;

    [Header("Appearance")]
    public Material ropeMaterial;

    [Header("Joint - Angular")]
    public float angularStrength = 200f;
    public float angularDamping = 10f;
    public float angularMaxForce = Mathf.Infinity;

    [Header("Joint - Projection")]
    public bool useProjection = true;
    public float projectionDistance = 0.01f;
    public float projectionAngle = 1f;


    [ContextMenu("Generate Rope")]
    public void GenerateRope()
    {
        if (!spherePrefab)
        {
            Debug.LogError("Sphere Prefab not assigned");
            return;
        }

        var parent = new GameObject("Rope");
        parent.transform.SetParent(transform, false);

        var rope = parent.AddComponent<Rope>();
        var list = new List<Transform>();

        Rigidbody prev = null;

        for (int i = 0; i < segments; i++)
        {
            var seg = Instantiate(spherePrefab, parent.transform);
            seg.name = $"Seg_{i:00}";
            seg.transform.localPosition = Vector3.forward * (i * spacing);
            seg.transform.localRotation = Quaternion.identity;

            list.Add(seg.transform);

            var rb = seg.GetComponent<Rigidbody>();
            if (!rb) rb = seg.AddComponent<Rigidbody>();


            if (prev)
                CreateJoint(rb, prev);
            
            prev = rb;
        }

        rope.Init(list, ropeMaterial);
    }
    void CreateJoint(Rigidbody a, Rigidbody b)
    {
        var j = a.gameObject.AddComponent<ConfigurableJoint>();
        j.connectedBody = b;
        j.autoConfigureConnectedAnchor = true;
        j.anchor = Vector3.zero;


        if (useProjection)
        {
            j.projectionMode = JointProjectionMode.PositionAndRotation;
            j.projectionDistance = projectionDistance;
            j.projectionAngle = projectionAngle;
        }
        else
        {
            j.projectionMode = JointProjectionMode.None;
        }


        j.xMotion = ConfigurableJointMotion.Locked;
        j.yMotion = ConfigurableJointMotion.Locked;
        j.zMotion = ConfigurableJointMotion.Locked;


        j.angularXMotion = ConfigurableJointMotion.Free;
        j.angularYMotion = ConfigurableJointMotion.Free;
        j.angularZMotion = ConfigurableJointMotion.Free;

        j.rotationDriveMode = RotationDriveMode.Slerp;

        j.slerpDrive = new JointDrive
        {
            positionSpring = angularStrength,
            positionDamper = angularDamping,
            maximumForce = angularMaxForce
        };

        j.targetRotation = Quaternion.identity;

    }


    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;

        Vector3 prevWorldPos = Vector3.zero;
        bool hasPrev = false;

        for (int i = 0; i < segments; i++)
        {
            float localRadius = spherePrefab.transform.localScale.x * 0.5f;

            Vector3 localPos = Vector3.forward * (i * spacing);
            Vector3 worldPos = transform.TransformPoint(localPos);

            Gizmos.DrawWireSphere(worldPos, localRadius);

            if (hasPrev)
            {
                Gizmos.DrawLine(prevWorldPos, worldPos);
            }

            prevWorldPos = worldPos;
            hasPrev = true;
        }
    }

}