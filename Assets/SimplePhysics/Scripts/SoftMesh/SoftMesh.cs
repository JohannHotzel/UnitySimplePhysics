using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshCollider))]
public class SoftMesh : MonoBehaviour
{
    [Header("Compute")]
    public ComputeShader compute;

    [Header("Impact")]
    public float minImpactImpulse = 0.5f;
    public float impactVelocity = 1.5f;
    public float maxDistance = 1.0f;
    public LayerMask deformLayers;

    [Header("Spring")]
    public float springK = 50f;
    public float damping = 8f;

    [Header("CPU Apply / Collider")]
    public bool updateCollider = true;
    public int cpuApplyEveryNFrames = 1;   

    [Header("Debug")]
    public bool reset = false;

    MeshFilter mf;
    MeshCollider mc;
    Mesh mesh;

    ComputeBuffer restBuf, posBuf, velBuf;

    int kSimulate, kImpact;
    int vertexCount;

    Vector3[] cpuVerts; 

    int frameCounter;

    void Awake()
    {
        mf = GetComponent<MeshFilter>();
        mc = GetComponent<MeshCollider>();

        // WICHTIG: eigene Shader-Instanz pro Objekt!
        compute = Instantiate(compute);

        mesh = Instantiate(mf.sharedMesh);
        mf.sharedMesh = mesh;
        mc.sharedMesh = mesh;

        var rest = mesh.vertices;
        vertexCount = rest.Length;

        restBuf = new ComputeBuffer(vertexCount, sizeof(float) * 3);
        posBuf = new ComputeBuffer(vertexCount, sizeof(float) * 3);
        velBuf = new ComputeBuffer(vertexCount, sizeof(float) * 3);

        restBuf.SetData(rest);
        posBuf.SetData(rest);
        velBuf.SetData(new Vector3[vertexCount]);

        cpuVerts = new Vector3[vertexCount];

        kSimulate = compute.FindKernel("Simulate");
        kImpact = compute.FindKernel("ApplyImpact");

        BindCommon(kSimulate);
        BindCommon(kImpact);
    }
    void BindCommon(int kernel)
    {
        compute.SetBuffer(kernel, "_Rest", restBuf);
        compute.SetBuffer(kernel, "_Pos", posBuf);
        compute.SetBuffer(kernel, "_Vel", velBuf);
        compute.SetInt("_VertexCount", vertexCount);
    }

    void OnDestroy()
    {
        restBuf?.Release();
        posBuf?.Release();
        velBuf?.Release();
    }

    void Update()
    {
        if (reset)
        {
            ResetState();
            reset = false;
        }

        compute.SetFloat("_DT", Time.deltaTime);
        compute.SetFloat("_SpringK", springK);
        compute.SetFloat("_Damping", damping);

        Dispatch(kSimulate);


        frameCounter++;
        if (frameCounter % Mathf.Max(1, cpuApplyEveryNFrames) != 0) return;

        posBuf.GetData(cpuVerts);

        if (mesh == null) return;

        mesh.vertices = cpuVerts;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        if (updateCollider && mc != null)
        {
            mc.sharedMesh = null;
            mc.sharedMesh = mesh;
        }
    }
    void ResetState()
    {
        var rest = mesh.vertices; // Achtung: wenn Mesh schon deformiert ist, nimm lieber cached Rest!
        // Besser: Rest einmal aus Awake cachen. Hier einfach robust:
        restBuf.GetData(cpuVerts);
        posBuf.SetData(cpuVerts);
        velBuf.SetData(new Vector3[vertexCount]);

        mesh.vertices = cpuVerts;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mc.sharedMesh = null;
        mc.sharedMesh = mesh;
    }

    void Dispatch(int kernel)
    {
        int groups = Mathf.CeilToInt(vertexCount / 256f);
        compute.Dispatch(kernel, groups, 1, 1);
    }

    void OnCollisionEnter(Collision c)
    {
        if (c.contactCount == 0) return;
        if (c.impulse.magnitude < minImpactImpulse) return;
        if ((deformLayers.value & (1 << c.gameObject.layer)) == 0) return;

        var cp = c.GetContact(0);

        Vector3 impactPointWS = cp.point;
        Vector3 pushDirWS = -cp.normal.normalized;

        //float kick = impactVelocity * c.impulse.magnitude;
        float kick = impactVelocity;

        ApplyImpactGPU(impactPointWS, pushDirWS, kick);
    }

    void ApplyImpactGPU(Vector3 impactPointWS, Vector3 pushDirWS, float kick)
    {
        // Matrices an GPU geben (Impact braucht world-distances)
        Matrix4x4 l2w = transform.localToWorldMatrix;
        Matrix4x4 w2l = transform.worldToLocalMatrix;

        compute.SetMatrix("_L2W", l2w);
        compute.SetMatrix("_W2L", w2l);

        compute.SetVector("_ImpactPointWS", impactPointWS);
        compute.SetVector("_PushDirWS", pushDirWS);
        compute.SetFloat("_Kick", kick);
        compute.SetFloat("_MaxDistance", maxDistance);

        Dispatch(kImpact);
    }
}
