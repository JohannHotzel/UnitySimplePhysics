using System.Collections.Generic;
using UnityEngine;

public class ClothGenerator : MonoBehaviour
{
    [Header("Cloth")]
    public GameObject prefab;
    public int rows = 10;          
    public int cols = 20;          
    public float spacing = 0.25f;

    [Header("Appearance")]
    public Material lineMaterial;
    public Material clothMaterial;

    [Header("Joint - Angular")]
    public float angularStrength = 200f;
    public float angularDamping = 10f;
    public float angularMaxForce = Mathf.Infinity;

    [Header("Joint - Projection")]
    public bool useProjection = true;
    public float projectionDistance = 0.01f;
    public float projectionAngle = 1f;


    [ContextMenu("Generate Cloth")]
    public void GenerateCloth()
    {
        if (!prefab)
        {
            Debug.LogError("Sphere Prefab not assigned");
            return;
        }

        var parent = new GameObject("Cloth");
        parent.transform.SetParent(transform, false);

        var cloth = parent.AddComponent<Cloth>();
        var list = new List<Transform>(rows * cols);

        Rigidbody[,] bodies = new Rigidbody[rows, cols];

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                var seg = Instantiate(prefab, parent.transform);
                seg.name = $"Seg_{r:00}_{c:00}";

                seg.transform.localPosition = new Vector3(c * spacing, -r * spacing, 0f);
                seg.transform.localRotation = Quaternion.identity;

                list.Add(seg.transform);

                var rb = seg.GetComponent<Rigidbody>();
                if (!rb) rb = seg.AddComponent<Rigidbody>();


                bodies[r, c] = rb;

                if (c > 0)
                {
                    CreateJoint(seg.gameObject, bodies[r, c - 1]);
                }

                if (r > 0)
                {
                    CreateJoint(seg.gameObject, bodies[r - 1, c]);
                }
            }
        }

        cloth.Init(list, rows, cols, lineMaterial, clothMaterial);
    }
    private void CreateJoint(GameObject a, Rigidbody b)
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

    // Visualize in Editor
    private void OnDrawGizmosSelected()
    {
        if (!prefab) return;

        Gizmos.color = Color.cyan;

        float localRadius = prefab.transform.localScale.x * 0.5f;

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                Vector3 localPos = new Vector3(c * spacing, -r * spacing, 0f);
                Vector3 worldPos = transform.TransformPoint(localPos);

                Gizmos.DrawWireSphere(worldPos, localRadius);

                if (c > 0)
                {
                    Vector3 leftLocal = new Vector3((c - 1) * spacing, -r * spacing, 0f);
                    Gizmos.DrawLine(transform.TransformPoint(leftLocal), worldPos);
                }

                if (r > 0)
                {
                    Vector3 upLocal = new Vector3(c * spacing, -(r - 1) * spacing, 0f);
                    Gizmos.DrawLine(transform.TransformPoint(upLocal), worldPos);
                }
            }
        }
    }

}
