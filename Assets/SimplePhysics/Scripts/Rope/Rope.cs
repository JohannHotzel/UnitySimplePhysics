using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class Rope : MonoBehaviour
{
    public List<Transform> segments = new List<Transform>();

    private LineRenderer lr;
    private Material ropeMaterial;

    public void Init(List<Transform> segs, Material material)
    {
        segments = segs;
        ropeMaterial = material;

        lr = GetComponent<LineRenderer>();
        lr.material = ropeMaterial;
        lr.startWidth = 0.15f;
        lr.endWidth = 0.15f;
        lr.generateLightingData = true;
    }

    private void Awake()
    {
        lr = GetComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = segments.Count;

        

    }

    private void LateUpdate()
    {
        if (lr == null || segments == null) return;

        int n = segments.Count;
        if (lr.positionCount != n) lr.positionCount = n;

        for (int i = 0; i < n; i++)
            lr.SetPosition(i, segments[i].position);
    }

   
}
