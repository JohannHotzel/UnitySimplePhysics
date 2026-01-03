using System.Collections.Generic;
using UnityEngine;

public class Rope : MonoBehaviour
{
    public List<Transform> segments = new List<Transform>();

    private LineRenderer lr;
    public Material ropeMaterial;

    public void Init(List<Transform> segs, Material material)
    {
        ropeMaterial = material;
        segments = segs;
    }

    private void Awake()
    {
        lr = gameObject.AddComponent<LineRenderer>();

        lr.material = ropeMaterial;
        lr.positionCount = segments.Count;

        lr.useWorldSpace = true;

        lr.startWidth = 0.15f;
        lr.endWidth = 0.15f;
        lr.generateLightingData = true;
    }

    void LateUpdate()
    {
        if (lr == null || segments == null || segments.Count == 0)
            return;

        UpdateLine();
    }

    void UpdateLine()
    {
        for (int i = 0; i < segments.Count; i++)
            lr.SetPosition(i, segments[i].position);
    }




}
