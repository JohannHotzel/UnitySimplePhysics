using System.Collections.Generic;
using UnityEngine;

public class Rope : MonoBehaviour
{
    public List<Transform> segments = new List<Transform>();
    private LineRenderer lr;

    [Header("Appearance")]
    public Material lineMaterial;
    public bool showLineRenderers = true;

    public void Init(List<Transform> segs, Material material)
    {
        lineMaterial = material;
        segments = segs;
    }
    private void Awake()
    {
        if (!showLineRenderers)
            return;

        lr = gameObject.AddComponent<LineRenderer>();

        lr.material = lineMaterial;
        lr.positionCount = segments.Count;

        lr.useWorldSpace = true;

        lr.startWidth = 0.15f;
        lr.endWidth = 0.15f;
        lr.generateLightingData = true;
    }
    void LateUpdate()
    {
        if (!showLineRenderers || lr == null || segments == null || segments.Count == 0)
            return;

        UpdateLine();
    }
    
    void UpdateLine()
    {
        for (int i = 0; i < segments.Count; i++)
            lr.SetPosition(i, segments[i].position);
    }
    
}
