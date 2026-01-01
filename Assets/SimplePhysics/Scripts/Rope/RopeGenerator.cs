using System.Collections.Generic;
using UnityEngine;

public class RopeGenerator : MonoBehaviour
{
    public GameObject spherePrefab;
    public int segments = 20;
    public float spacing = 0.25f;
    public Material ropeMaterial;

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
            {
                var j = seg.AddComponent<ConfigurableJoint>();
                j.connectedBody = prev;

                j.autoConfigureConnectedAnchor = true;
                j.anchor = Vector3.zero;

                j.xMotion = ConfigurableJointMotion.Locked;
                j.yMotion = ConfigurableJointMotion.Locked;
                j.zMotion = ConfigurableJointMotion.Locked;

                j.angularXMotion = ConfigurableJointMotion.Free;
                j.angularYMotion = ConfigurableJointMotion.Free;
                j.angularZMotion = ConfigurableJointMotion.Free;

                j.enableCollision = false;
                j.projectionMode = JointProjectionMode.PositionAndRotation;
                j.projectionDistance = 0.01f;
                j.projectionAngle = 1f;
            }

            prev = rb;
        }

        rope.Init(list, ropeMaterial);
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