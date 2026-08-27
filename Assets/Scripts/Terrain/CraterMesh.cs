using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class CraterMesh : MonoBehaviour
{
    public float radius = 5f;
    public float depth = 2f;

    private void Awake()
    {
        GenerateMesh();
    }

    public void GenerateMesh()
    {
        const int segments = 24;
        var vertices = new Vector3[1 + segments * 2];
        var tris = new List<int>();

        vertices[0] = Vector3.zero;

        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            vertices[1 + i] = new Vector3(
                Mathf.Cos(angle) * radius * 0.3f,
                -depth,
                Mathf.Sin(angle) * radius * 0.3f);
        }

        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            vertices[1 + segments + i] = new Vector3(
                Mathf.Cos(angle) * radius,
                0f,
                Mathf.Sin(angle) * radius);
        }

        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            tris.Add(0);
            tris.Add(1 + next);
            tris.Add(1 + i);
        }

        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            int inner = 1 + i;
            int innerNext = 1 + next;
            int outer = 1 + segments + i;
            int outerNext = 1 + segments + next;
            tris.Add(inner);    tris.Add(innerNext); tris.Add(outer);
            tris.Add(innerNext); tris.Add(outerNext); tris.Add(outer);
        }

        var mesh = new Mesh { name = "CraterMesh" };
        mesh.vertices = vertices;
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GetComponent<MeshFilter>().mesh = mesh;
        
        var col = GetComponent<MeshCollider>();
        if (col != null) col.sharedMesh = GetComponent<MeshFilter>().sharedMesh;
    }
}
