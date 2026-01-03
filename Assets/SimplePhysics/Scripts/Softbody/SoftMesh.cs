using System.Collections.Generic;
using UnityEngine;
using System.Linq;


[DisallowMultipleComponent]
public class SoftMesh : MonoBehaviour
{
    [Header("Sphere")]
    public GameObject spherePrefab;

    [Header("Duplicate Vertex Merge")]
    public float mergeEpsilon = 1e-5f;

    [Header("Joint - Linear")]
    public float linearStrength = 2000f;
    public float linearDamping = 80f;
    public float linearMaxForce = 1e9f;

    [Header("Joint - Angular")]
    public float angularStrength = 200f;
    public float angularDamping = 10f;
    public float angularMaxForce = 1e9f;

    [Header("Joint Options")]
    public bool useProjection = true;
    public float projectionDistance = 0.01f;
    public float projectionAngle = 1f;

    [Header("Mesh Deformation")]
    public bool recalcNormals = true;

    [Header("Self-Collision Ignore (MeshCollider vs own spheres)")]
    public bool addMeshCollider = true;
    public bool meshColliderConvex = false;
    MeshCollider _meshCollider;

    public int impulseNearestN = 6;
    public float impulseScale = 1f;

    Rigidbody[] _bodies;


    public bool showRBs = true;


    // runtime
    Mesh _mesh;
    int[] _triangles;

    int[] _originalToUnique;
    Transform[] _uniqueToSphere;

    void Awake()
    {
        BuildSoftMesh();

        if(!showRBs)
            ApplyShowRBs(false);
    }


    private void LateUpdate()
    {
        DeformMesh();
        UpdateMeshCollider();
    }

    void BuildSoftMesh()
    {
        if (!spherePrefab)
        {
            Debug.LogError("SoftMesh: spherePrefab missing", this);
            enabled = false;
            return;
        }

        var mf = GetComponent<MeshFilter>();
        if (!mf || !mf.sharedMesh)
        {
            Debug.LogError("SoftMesh: MeshFilter missing", this);
            enabled = false;
            return;
        }

        // clone mesh (so original asset is untouched)
        _mesh = Instantiate(mf.sharedMesh);
        mf.sharedMesh = _mesh;


        // ensure MeshCollider exists and uses runtime mesh
        _meshCollider = GetComponent<MeshCollider>();
        if (!_meshCollider)
            _meshCollider = gameObject.AddComponent<MeshCollider>();

        _meshCollider.sharedMesh = _mesh;
        _meshCollider.convex = false;


        // --- ensure MeshCollider exists and uses the runtime mesh ---
        if (addMeshCollider)
        {
            var mc = GetComponent<MeshCollider>();
            if (!mc) mc = gameObject.AddComponent<MeshCollider>();

            mc.sharedMesh = _mesh;
            mc.convex = meshColliderConvex;
        }

        var baseVertices = _mesh.vertices;
        _triangles = _mesh.triangles;

        // merge duplicate vertices
        BuildUniqueVertices(baseVertices, out _originalToUnique, out var uniquePositions);

        _uniqueToSphere = new Transform[uniquePositions.Count];
        var bodies = new Rigidbody[uniquePositions.Count];

        // create spheres directly under THIS transform
        for (int i = 0; i < uniquePositions.Count; i++)
        {
            var s = Instantiate(spherePrefab, transform);
            s.name = $"Vtx_{i:0000}";

            s.transform.localPosition = uniquePositions[i];
            s.transform.localRotation = Quaternion.identity;

            // ---- keep sphere scale constant (same as prefab) even if parent is scaled ----
            Vector3 desiredWorldScale = spherePrefab.transform.lossyScale;
            Vector3 parentWorldScale = transform.lossyScale;

            // avoid divide-by-zero
            s.transform.localScale = new Vector3(
                parentWorldScale.x != 0f ? desiredWorldScale.x / parentWorldScale.x : desiredWorldScale.x,
                parentWorldScale.y != 0f ? desiredWorldScale.y / parentWorldScale.y : desiredWorldScale.y,
                parentWorldScale.z != 0f ? desiredWorldScale.z / parentWorldScale.z : desiredWorldScale.z
            );

            var rb = s.GetComponent<Rigidbody>();
            if (!rb) rb = s.AddComponent<Rigidbody>();

            // sync RB immediately
            rb.position = s.transform.position;
            rb.rotation = s.transform.rotation;

            _uniqueToSphere[i] = s.transform;
            bodies[i] = rb;
        }

        Physics.SyncTransforms();


        if (addMeshCollider)
        {
            // parent colliders (includes MeshCollider you just added)
            var parentCols = GetComponents<Collider>();

            for (int i = 0; i < _uniqueToSphere.Length; i++)
            {
                var t = _uniqueToSphere[i];
                if (!t) continue;

                // sphere prefab might have collider on child
                var sphereCols = t.GetComponentsInChildren<Collider>();

                for (int p = 0; p < parentCols.Length; p++)
                {
                    var pc = parentCols[p];
                    if (!pc) continue;

                    for (int s = 0; s < sphereCols.Length; s++)
                    {
                        var sc = sphereCols[s];
                        if (!sc) continue;

                        Physics.IgnoreCollision(sc, pc, true);
                    }
                }
            }
        }


        var edges = BuildEdges(_triangles, _originalToUnique);
        foreach (var e in edges)
            CreateJoint(bodies[e.b], bodies[e.a]);


        _bodies = bodies;
    }
    void DeformMesh()
    {
        if (_mesh == null) return;

        var verts = _mesh.vertices;

        for (int i = 0; i < verts.Length; i++)
            verts[i] = _uniqueToSphere[_originalToUnique[i]].localPosition;

        _mesh.vertices = verts;
        _mesh.RecalculateBounds();
        if (recalcNormals) _mesh.RecalculateNormals();
    }

    void UpdateMeshCollider()
    {
        if (_meshCollider == null || _mesh == null)
            return;

        // Force PhysX to rebuild collision mesh
        _meshCollider.sharedMesh = null;
        _meshCollider.sharedMesh = _mesh;
    }
    private void ApplyShowRBs(bool visible)
    {
        if (_uniqueToSphere == null || _uniqueToSphere.Length == 0) return;

        for (int i = 0; i < _uniqueToSphere.Length; i++)
        {
            var t = _uniqueToSphere[i];
            if (!t) continue;

            // Sphere prefab may have renderers on children
            var renderers = t.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
                renderers[r].enabled = visible;
        }
    }


    // ---------------- Joints ----------------
    void CreateJoint(Rigidbody self, Rigidbody other)
    {
        var j = self.gameObject.AddComponent<ConfigurableJoint>();
        j.connectedBody = other;

        j.autoConfigureConnectedAnchor = true;
        j.anchor = Vector3.zero;

        j.xMotion = ConfigurableJointMotion.Free;
        j.yMotion = ConfigurableJointMotion.Free;
        j.zMotion = ConfigurableJointMotion.Free;

        j.angularXMotion = ConfigurableJointMotion.Free;
        j.angularYMotion = ConfigurableJointMotion.Free;
        j.angularZMotion = ConfigurableJointMotion.Free;

        j.enableCollision = false;

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

        var lin = new JointDrive
        {
            positionSpring = linearStrength,
            positionDamper = linearDamping,
            maximumForce = linearMaxForce
        };

        j.xDrive = lin;
        j.yDrive = lin;
        j.zDrive = lin;

        // rest pose = current configuration
        j.targetPosition = Vector3.zero;

        j.rotationDriveMode = RotationDriveMode.Slerp;

        j.slerpDrive = new JointDrive
        {
            positionSpring = angularStrength,
            positionDamper = angularDamping,
            maximumForce = angularMaxForce
        };

        j.targetRotation = Quaternion.identity;
    }

    // ---------------- Mesh topology ----------------
    struct Edge
    {
        public int a, b;

        public Edge(int a, int b)
        {
            if (a < b) { this.a = a; this.b = b; }
            else { this.a = b; this.b = a; }
        }
    }
    sealed class EdgeComparer : IEqualityComparer<Edge>
    {
        public bool Equals(Edge x, Edge y) => x.a == y.a && x.b == y.b;

        public int GetHashCode(Edge e)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + e.a;
                h = h * 31 + e.b;
                return h;
            }
        }
    }
    HashSet<Edge> BuildEdges(int[] tris, int[] map)
    {
        var set = new HashSet<Edge>(new EdgeComparer());

        for (int i = 0; i < tris.Length; i += 3)
        {
            int a = map[tris[i]];
            int b = map[tris[i + 1]];
            int c = map[tris[i + 2]];

            if (a != b) set.Add(new Edge(a, b));
            if (b != c) set.Add(new Edge(b, c));
            if (c != a) set.Add(new Edge(c, a));
        }

        return set;
    }
    void BuildUniqueVertices(Vector3[] verts, out int[] originalToUnique, out List<Vector3> uniqueVerts)
    {
        originalToUnique = new int[verts.Length];
        uniqueVerts = new List<Vector3>();

        float epsSqr = mergeEpsilon * mergeEpsilon;

        for (int i = 0; i < verts.Length; i++)
        {
            int found = -1;
            for (int u = 0; u < uniqueVerts.Count; u++)
            {
                if ((uniqueVerts[u] - verts[i]).sqrMagnitude <= epsSqr)
                {
                    found = u;
                    break;
                }
            }

            if (found == -1)
            {
                found = uniqueVerts.Count;
                uniqueVerts.Add(verts[i]);
            }

            originalToUnique[i] = found;
        }
    }


    // --------------- Collision Response ----------------
    void OnCollisionEnter(Collision c) => RelayImpulse(c);
    //void OnCollisionStay(Collision c) => RelayImpulse(c);
    void RelayImpulse(Collision c)
    {
        if (_bodies == null || _bodies.Length == 0) return;
        if (c.contactCount == 0) return;

        Vector3 impulse = c.impulse * impulseScale;
        if (impulse.sqrMagnitude < 1e-8f) return;

        Vector3 p = c.contacts[0].point;

        int n = Mathf.Min(impulseNearestN, _bodies.Length);

        var nearest = _bodies
            .Where(rb => rb != null && !rb.isKinematic)
            .OrderBy(rb => (rb.worldCenterOfMass - p).sqrMagnitude)
            .Take(n);

        Vector3 per = impulse / n;

        foreach (var rb in nearest)
        {
            rb.AddForceAtPosition(-per, p, ForceMode.Impulse);
        }
    }


    // ---------------- Gizmos ----------------
    private void OnDrawGizmosSelected()
    {
        if (!spherePrefab) return;

        var mf = GetComponent<MeshFilter>();
        if (!mf) return;

        var m = mf.sharedMesh;
        if (!m) return;

        var verts = m.vertices;
        if (verts == null || verts.Length == 0) return;

        Gizmos.color = Color.cyan;

        // radius based purely on prefab lossyScale (world scale)
        float radius = spherePrefab.transform.lossyScale.x * 0.5f;

        // draw for every vertex (duplicates included)
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 worldPos = transform.TransformPoint(verts[i]);
            Gizmos.DrawWireSphere(worldPos, radius);
        }
    }


}
