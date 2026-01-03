using System.Collections.Generic;
using UnityEngine;

public class Rope : MonoBehaviour
{
    [Header("RigidBodies")]
    public List<Transform> points = new List<Transform>();
    
    [Header("Appearance")]
    public Material lineMaterial;
    public bool showLineRenderers = true;

    private LineRenderer lr;

    public void Init(List<Transform> segs, Material material)
    {
        lineMaterial = material;
        points = segs;
    }
    private void Awake()
    {
        if (!showLineRenderers)
            return;

        lr = gameObject.AddComponent<LineRenderer>();

        lr.material = lineMaterial;
        lr.positionCount = points.Count;

        lr.useWorldSpace = true;

        lr.startWidth = 0.15f;
        lr.endWidth = 0.15f;
        lr.generateLightingData = true;
    }
    void LateUpdate()
    {
        if (!showLineRenderers || lr == null || points == null || points.Count == 0)
            return;

        UpdateLine();
    }
    
    void UpdateLine()
    {
        for (int i = 0; i < points.Count; i++)
            lr.SetPosition(i, points[i].position);
    }
    
}
