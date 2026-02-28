using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter))]
public class EmbeddedSoftBody : MonoBehaviour
{
    [Header("Poisson Disk")]
    [Min(0.0001f)] public float radius = 0.25f;
    [Min(1)] public int targetCount = 500;
    [Min(1)] public int maxAttempts = 200000;

    [Header("Prefab")]
    public GameObject prefab;

    [Header("Neighbors")]
    [Min(1)] public int neighborCount = 6;

    [Header("Joints (ConfigurableJoint)")]
    public float linearStrength = 500f;
    public float linearDamping = 10f;
    public float linearMaxForce = Mathf.Infinity;

    public float angularStrength = 750f;
    public float angularDamping = 10f;
    public float angularMaxForce = Mathf.Infinity;

    public bool useProjection = true;
    public float projectionDistance = 0.01f;
    public float projectionAngle = 1f;

    [Header("Embedding / Skinning")]
    [Range(1, 4)] public int maxInfluences = 4;
    public float weightFalloffEps = 1e-4f;

    [Header("Hierarchy")]
    public string pointsContainerName = "SoftBodyPoints";

    [Header("MeshCollider & Collision Response")]
    public bool addMeshCollider = true;
    public bool meshColliderConvex = false;
    public bool relayCollisionImpulse = true;
    public int impulseNearestN = 3;
    public float impulseScale = 0.2f;

    MeshCollider _meshCollider;

    public List<Vector3> pointsLocal = new List<Vector3>();
    public List<Transform> pointTransforms = new List<Transform>();
    public HashSet<Edge> edges = new HashSet<Edge>();

    [Serializable]
    public struct Edge : IEquatable<Edge>
    {
        public int a, b;
        public Edge(int i0, int i1)
        {
            if (i0 < i1) { a = i0; b = i1; }
            else { a = i1; b = i0; }
        }
        public bool Equals(Edge other) => a == other.a && b == other.b;
        public override bool Equals(object obj) => obj is Edge e && Equals(e);
        public override int GetHashCode() { unchecked { return (a * 397) ^ b; } }
    }

    Transform _pointsRoot;
    Rigidbody[] _bodies;

    Mesh _mesh;
    Vector3[] _restVertsLocal;

    Vector3[] _restParticlePosW;
    Quaternion[] _restParticleRotW;

    int[] _bindIdx;
    float[] _bindW;

    // ============================================================
    // Context menus
    // ============================================================

    [ContextMenu("Instantiate Prefabs")]
    void CM_InstantiatePrefabs()
    {
        if (!prefab)
        {
            Debug.LogError("EmbeddedSoftBody: Prefab not assigned.", this);
            return;
        }

        EnsureMeshClone();
        ClearExistingPointsAndJoints();

        BuildPoissonPointsLocal();
        InstantiatePrefabs(); // creates pointTransforms + _bodies

        if (addMeshCollider)
        {
            CreateMeshCollider();
            UpdateMeshCollider();
        }
        else
        {
            RemoveMeshColliderIfAny();
        }

        Debug.Log($"Instantiate Prefabs done: points={pointTransforms.Count}, verts={_restVertsLocal?.Length ?? 0}");
    }

    [ContextMenu("Instantiate ConfigurableJoints")]
    void CM_InstantiateJoints()
    {
        EnsureMeshClone();
        GatherExistingPoints(); // in case prefabs already exist

        if (_bodies == null || _bodies.Length == 0)
        {
            Debug.LogWarning("EmbeddedSoftBody: No points/bodies found. Run 'Instantiate Prefabs' first.");
            return;
        }

        ClearExistingJointsOnly();

        BuildEdgesByKNN();
        CreateJointsFromEdges();

        Debug.Log($"Instantiate Joints done: edges={edges.Count}");
    }

    [ContextMenu("Clear Points and Joints")]
    void CM_ClearPointsAndJoints()
    {
        ClearExistingPointsAndJoints();
        RemoveMeshColliderIfAny();
        Debug.Log("Cleared all points and joints.");
    }


    // ============================================================
    // Runtime
    // ============================================================
    void Start()
    {
        EnsureMeshClone();

        GatherExistingPoints();

        if (pointTransforms.Count > 0)
        {
            CacheParticleRestPose();
            BakeBinding();
        }

        if (addMeshCollider)
            CreateMeshCollider();
        else
            RemoveMeshColliderIfAny();

        int jointCount = 0;
        if (_pointsRoot != null)
        {
            for (int i = 0; i < _pointsRoot.childCount; i++)
                jointCount += _pointsRoot.GetChild(i).GetComponents<ConfigurableJoint>().Length;
        }

        Debug.Log($"EmbeddedSoftBody Start: points={pointTransforms.Count}, edges={jointCount}, verts={_restVertsLocal?.Length ?? 0}");
    }

    void LateUpdate()
    {
        if (_mesh == null || _restVertsLocal == null) return;
        if (_bindIdx == null || _bindW == null) return;
        if (pointTransforms == null || pointTransforms.Count == 0) return;
        if (_restParticlePosW == null || _restParticleRotW == null) return;

        DeformMeshSkinned();

        if (addMeshCollider)
            UpdateMeshCollider();
    }


    // ============================================================
    // Helpers
    // ============================================================
    Transform GetOrCreatePointsRoot()
    {
        var existing = transform.Find(pointsContainerName);
        if (existing) return existing;

        var go = new GameObject(pointsContainerName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        return go.transform;
    }

    void EnsureMeshClone()
    {
        var mf = GetComponent<MeshFilter>();
        if (!mf || !mf.sharedMesh)
        {
            Debug.LogError("EmbeddedSoftBody: MeshFilter missing or has no mesh.", this);
            return;
        }

        if (_mesh == null)
        {
            _mesh = Instantiate(mf.sharedMesh);
            _mesh.name = mf.name;
            mf.sharedMesh = _mesh;
            _restVertsLocal = _mesh.vertices;
        }
        else if (_restVertsLocal == null || _restVertsLocal.Length == 0)
        {
            _restVertsLocal = _mesh.vertices;
        }
    }

    void GatherExistingPoints()
    {
        _pointsRoot = GetOrCreatePointsRoot();

        pointTransforms.Clear();
        pointsLocal.Clear();

        var bodies = new List<Rigidbody>();
        for (int i = 0; i < _pointsRoot.childCount; i++)
        {
            var ch = _pointsRoot.GetChild(i);
            var rb = ch.GetComponent<Rigidbody>();
            if (!rb) continue;

            pointTransforms.Add(ch);
            pointsLocal.Add(ch.localPosition);
            bodies.Add(rb);
        }

        _bodies = bodies.ToArray();
    }

    void ClearExistingPointsAndJoints()
    {
        _pointsRoot = GetOrCreatePointsRoot();

        for (int i = _pointsRoot.childCount - 1; i >= 0; i--)
        {
            var ch = _pointsRoot.GetChild(i);
            if (!Application.isPlaying) DestroyImmediate(ch.gameObject);
            else Destroy(ch.gameObject);
        }

        pointTransforms.Clear();
        pointsLocal.Clear();
        edges.Clear();

        _bodies = null;
        _restParticlePosW = null;
        _restParticleRotW = null;
        _bindIdx = null;
        _bindW = null;
    }

    void ClearExistingJointsOnly()
    {
        for (int i = 0; i < pointTransforms.Count; i++)
        {
            var t = pointTransforms[i];
            if (!t) continue;

            var joints = t.GetComponents<ConfigurableJoint>();
            for (int j = 0; j < joints.Length; j++)
            {
                if (!Application.isPlaying) DestroyImmediate(joints[j]);
                else Destroy(joints[j]);
            }
        }
    }


    // ------------------------------------------------------------
    // Step 1: Poisson on surface -> pointsLocal
    // ------------------------------------------------------------
    void BuildPoissonPointsLocal()
    {
        pointsLocal.Clear();

        var mf = GetComponent<MeshFilter>();
        var mesh = mf.sharedMesh;
        var v = mesh.vertices;
        var t = mesh.triangles;

        if (t == null || t.Length < 3)
        {
            Debug.LogWarning("EmbeddedSoftBody: Mesh has no triangles.");
            return;
        }

        float[] cdf = BuildTriangleAreaCDF(v, t, out float totalArea);
        if (totalArea <= 0f)
        {
            Debug.LogWarning("EmbeddedSoftBody: Total mesh surface area is zero.");
            return;
        }

        Transform tr = mf.transform;

        var pointsWorld = new List<Vector3>(targetCount);
        float r2 = radius * radius;

        int attempts = 0;
        while (attempts < maxAttempts && pointsWorld.Count < targetCount)
        {
            attempts++;

            int triIndex = PickTriangleIndex(cdf, UnityEngine.Random.value);

            int i0 = t[triIndex * 3 + 0];
            int i1 = t[triIndex * 3 + 1];
            int i2 = t[triIndex * 3 + 2];

            Vector3 aL = v[i0];
            Vector3 bL = v[i1];
            Vector3 cL = v[i2];

            Vector3 pL = SamplePointInTriangle(aL, bL, cL);
            Vector3 pW = tr.TransformPoint(pL);

            bool ok = true;
            for (int i = 0; i < pointsWorld.Count; i++)
            {
                if ((pointsWorld[i] - pW).sqrMagnitude < r2) { ok = false; break; }
            }

            if (ok) pointsWorld.Add(pW);
        }

        for (int i = 0; i < pointsWorld.Count; i++)
            pointsLocal.Add(tr.InverseTransformPoint(pointsWorld[i]));
    }

    static float[] BuildTriangleAreaCDF(Vector3[] verts, int[] tris, out float totalArea)
    {
        int triCount = tris.Length / 3;
        float[] cdf = new float[triCount];

        totalArea = 0f;
        for (int k = 0; k < triCount; k++)
        {
            Vector3 a = verts[tris[k * 3 + 0]];
            Vector3 b = verts[tris[k * 3 + 1]];
            Vector3 c = verts[tris[k * 3 + 2]];

            float area = 0.5f * Vector3.Cross(b - a, c - a).magnitude;
            totalArea += area;
            cdf[k] = totalArea;
        }

        if (totalArea > 0f)
            for (int k = 0; k < triCount; k++)
                cdf[k] /= totalArea;

        return cdf;
    }

    static int PickTriangleIndex(float[] cdf, float u01)
    {
        int idx = Array.BinarySearch(cdf, u01);
        if (idx < 0) idx = ~idx;
        if (idx >= cdf.Length) idx = cdf.Length - 1;
        return idx;
    }

    static Vector3 SamplePointInTriangle(Vector3 a, Vector3 b, Vector3 c)
    {
        float u = UnityEngine.Random.value;
        float v = UnityEngine.Random.value;
        if (u + v > 1f) { u = 1f - u; v = 1f - v; }
        return a + u * (b - a) + v * (c - a);
    }


    // ------------------------------------------------------------
    // Step 2: Instantiate prefabs + rigidbodies
    // ------------------------------------------------------------
    void InstantiatePrefabs()
    {
        _pointsRoot = GetOrCreatePointsRoot();

        pointTransforms.Clear();
        pointTransforms.Capacity = pointsLocal.Count;
        _bodies = new Rigidbody[pointsLocal.Count];

        Vector3 desiredWorldScale = prefab.transform.lossyScale;
        Vector3 parentWorldScale = transform.lossyScale;

        for (int i = 0; i < pointsLocal.Count; i++)
        {
            var go = Instantiate(prefab, _pointsRoot);
            go.name = $"P_{i:0000}";
            go.transform.localPosition = pointsLocal[i];
            go.transform.localRotation = Quaternion.identity;

            go.transform.localScale = new Vector3(
                parentWorldScale.x != 0f ? desiredWorldScale.x / parentWorldScale.x : desiredWorldScale.x,
                parentWorldScale.y != 0f ? desiredWorldScale.y / parentWorldScale.y : desiredWorldScale.y,
                parentWorldScale.z != 0f ? desiredWorldScale.z / parentWorldScale.z : desiredWorldScale.z
            );

            var rb = go.GetComponent<Rigidbody>();
            if (!rb) rb = go.AddComponent<Rigidbody>();

            rb.position = go.transform.position;
            rb.rotation = go.transform.rotation;

            pointTransforms.Add(go.transform);
            _bodies[i] = rb;
        }

        Physics.SyncTransforms();
    }


    // ------------------------------------------------------------
    // Step 3: Rest pose for skinning
    // ------------------------------------------------------------
    void CacheParticleRestPose()
    {
        int n = pointTransforms.Count;
        _restParticlePosW = new Vector3[n];
        _restParticleRotW = new Quaternion[n];

        for (int i = 0; i < n; i++)
        {
            _restParticlePosW[i] = pointTransforms[i].position;
            _restParticleRotW[i] = pointTransforms[i].rotation;
        }
    }


    // ------------------------------------------------------------
    // Step 4: Bind mesh verts -> up to 4 nearest particles
    // ------------------------------------------------------------
    void BakeBinding()
    {
        if (_restVertsLocal == null || _restVertsLocal.Length == 0) return;
        if (pointTransforms == null || pointTransforms.Count == 0) return;

        int vCount = _restVertsLocal.Length;
        int pCount = pointTransforms.Count;
        int k = Mathf.Clamp(maxInfluences, 1, 4);

        _bindIdx = new int[vCount * k];
        _bindW = new float[vCount * k];

        for (int vi = 0; vi < vCount; vi++)
        {
            Vector3 vRestW = transform.TransformPoint(_restVertsLocal[vi]);

            int[] bestIdx = new int[k];
            float[] bestD2 = new float[k];
            for (int a = 0; a < k; a++) { bestIdx[a] = -1; bestD2[a] = float.PositiveInfinity; }

            for (int pi = 0; pi < pCount; pi++)
            {
                float d2 = (vRestW - _restParticlePosW[pi]).sqrMagnitude;

                int worst = 0;
                for (int a = 1; a < k; a++)
                    if (bestD2[a] > bestD2[worst]) worst = a;

                if (d2 < bestD2[worst])
                {
                    bestD2[worst] = d2;
                    bestIdx[worst] = pi;
                }
            }

            float sum = 0f;
            for (int a = 0; a < k; a++)
            {
                int bi = bestIdx[a];
                float w = 0f;

                if (bi >= 0)
                {
                    float d = Mathf.Sqrt(bestD2[a]);
                    w = 1f / Mathf.Max(d, weightFalloffEps);
                }

                _bindIdx[vi * k + a] = bi;
                _bindW[vi * k + a] = w;
                sum += w;
            }

            if (sum <= 0f)
            {
                _bindIdx[vi * k + 0] = 0;
                _bindW[vi * k + 0] = 1f;
                for (int a = 1; a < k; a++) { _bindIdx[vi * k + a] = -1; _bindW[vi * k + a] = 0f; }
            }
            else
            {
                float inv = 1f / sum;
                for (int a = 0; a < k; a++) _bindW[vi * k + a] *= inv;
            }
        }
    }


    // ------------------------------------------------------------
    // Step 5: Build edges by KNN (index-based)
    // ------------------------------------------------------------
    void BuildEdgesByKNN()
    {
        edges.Clear();
        int count = pointTransforms.Count;
        if (count < 2) return;

        int n = Mathf.Clamp(neighborCount, 1, count - 1);

        for (int i = 0; i < count; i++)
        {
            Vector3 pi = pointTransforms[i].localPosition;

            int[] bestIdx = new int[n];
            float[] bestD2 = new float[n];
            for (int a = 0; a < n; a++) { bestIdx[a] = -1; bestD2[a] = float.PositiveInfinity; }

            for (int j = 0; j < count; j++)
            {
                if (j == i) continue;

                Vector3 pj = pointTransforms[j].localPosition;
                float d2 = (pj - pi).sqrMagnitude;

                int worst = 0;
                for (int a = 1; a < n; a++)
                    if (bestD2[a] > bestD2[worst]) worst = a;

                if (d2 < bestD2[worst])
                {
                    bestD2[worst] = d2;
                    bestIdx[worst] = j;
                }
            }

            for (int a = 0; a < n; a++)
            {
                int j = bestIdx[a];
                if (j >= 0) edges.Add(new Edge(i, j));
            }
        }
    }


    // ------------------------------------------------------------
    // Step 6: ConfigurableJoints from edges
    // ------------------------------------------------------------
    void CreateJointsFromEdges()
    {
        if (_bodies == null || _bodies.Length == 0) return;

        foreach (var e in edges)
        {
            var a = _bodies[e.a];
            var b = _bodies[e.b];
            if (!a || !b) continue;
            CreateJoint(a, b);
        }

        Physics.SyncTransforms();
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

        j.xMotion = ConfigurableJointMotion.Free;
        j.yMotion = ConfigurableJointMotion.Free;
        j.zMotion = ConfigurableJointMotion.Free;

        j.angularXMotion = ConfigurableJointMotion.Free;
        j.angularYMotion = ConfigurableJointMotion.Free;
        j.angularZMotion = ConfigurableJointMotion.Free;

        var lin = new JointDrive
        {
            positionSpring = linearStrength,
            positionDamper = linearDamping,
            maximumForce = linearMaxForce
        };

        j.xDrive = lin;
        j.yDrive = lin;
        j.zDrive = lin;
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


    // ------------------------------------------------------------
    // MeshCollider
    // ------------------------------------------------------------
    void CreateMeshCollider()
    {
        if (_mesh == null) return;

        _meshCollider = GetComponent<MeshCollider>();
        if (!_meshCollider)
            _meshCollider = gameObject.AddComponent<MeshCollider>();

        _meshCollider.sharedMesh = _mesh;
        _meshCollider.convex = meshColliderConvex;

        // parent colliders (includes MeshCollider that was just added)
        var parentCols = GetComponents<Collider>();

        for (int i = 0; i < pointTransforms.Count; i++)
        {
            var t = pointTransforms[i];
            if (!t) continue;

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

    void UpdateMeshCollider()
    {
        if (_meshCollider == null || _mesh == null)
            return;

        _meshCollider.sharedMesh = null;
        _meshCollider.sharedMesh = _mesh;
    }

    void RemoveMeshColliderIfAny()
    {
        var mc = GetComponent<MeshCollider>();
        if (!mc) { _meshCollider = null; return; }

        if (!Application.isPlaying) DestroyImmediate(mc);
        else Destroy(mc);

        _meshCollider = null;
    }

    // Collision Response for MeshCollider
    void OnCollisionEnter(Collision c)
    {
        if (relayCollisionImpulse) RelayImpulse(c);
    }

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
            rb.AddForceAtPosition(-per, p, ForceMode.Impulse);
    }


    // ------------------------------------------------------------
    // Runtime deformation
    // ------------------------------------------------------------
    void DeformMeshSkinned()
    {
        int vCount = _restVertsLocal.Length;
        int k = Mathf.Clamp(maxInfluences, 1, 4);

        var deformed = new Vector3[vCount];

        for (int vi = 0; vi < vCount; vi++)
        {
            Vector3 vRestW = transform.TransformPoint(_restVertsLocal[vi]);
            Vector3 vOutW = Vector3.zero;

            for (int a = 0; a < k; a++)
            {
                int pi = _bindIdx[vi * k + a];
                float w = _bindW[vi * k + a];
                if (pi < 0 || w <= 0f) continue;

                Matrix4x4 restM = Matrix4x4.TRS(_restParticlePosW[pi], _restParticleRotW[pi], Vector3.one);
                Matrix4x4 curM = Matrix4x4.TRS(pointTransforms[pi].position, pointTransforms[pi].rotation, Vector3.one);
                Matrix4x4 delta = curM * restM.inverse;

                vOutW += delta.MultiplyPoint3x4(vRestW) * w;
            }

            deformed[vi] = transform.InverseTransformPoint(vOutW);
        }

        _mesh.vertices = deformed;
        if ((Time.frameCount & 3) == 0) _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
    }


    // ------------------------------------------------------------
    // Debug visualization of joints in editor
    // ------------------------------------------------------------
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;

        var root = transform.Find(pointsContainerName);
        if (!root) return;

        for (int i = 0; i < root.childCount; i++)
        {
            var child = root.GetChild(i);

            var joints = child.GetComponents<ConfigurableJoint>();
            for (int j = 0; j < joints.Length; j++)
            {
                var joint = joints[j];
                if (!joint) continue;
                if (!joint.connectedBody) continue;

                Gizmos.DrawLine(
                    joint.transform.position,
                    joint.connectedBody.transform.position
                );
            }
        }
    }



}