using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.XR.ARFoundation;


public class LandscapeFeaturesGeneration : MonoBehaviour
{
    public TerrainMeshGeneration m_TerrainMeshGeneration;
    public ARMeshManager m_MeshManager;

    public GameObject waterPrefab;


    private float minHeight;
    private float maxHeight;
    private float minMaxDiff;
    private float waterLevel = -1f;

    public void Generate()
    {
        minHeight = m_TerrainMeshGeneration.minHeight;
        maxHeight = m_TerrainMeshGeneration.maxHeight;
        minMaxDiff = maxHeight - minHeight;            

        AddWaterLevel();
    }

    private void AddWaterLevel()
    {
        if (waterLevel == -1f) waterLevel = minHeight + 0.3f * minMaxDiff;

        Vector3[] waterLayerVertices = new Vector3[m_TerrainMeshGeneration.verticesArray.Length];
        Array.Copy(m_TerrainMeshGeneration.verticesArray, waterLayerVertices, waterLayerVertices.Length);
        int[] waterLayerTriangles = (int[])m_TerrainMeshGeneration.trianglesArray.Clone();

        for (int i = 0; i < waterLayerVertices.Length; i++)
        {
            waterLayerVertices[i].y = waterLevel;
        }

        Mesh waterLevelMesh = new Mesh
        {
            vertices = waterLayerVertices,
            triangles = waterLayerTriangles
        };
        waterLevelMesh.Optimize();
        waterLevelMesh.RecalculateNormals();
        GameObject waterLayer = Instantiate(waterPrefab, transform.parent.GetChild(3));  //Add created terrain gameobject to AR Foundation's trackables
        waterLayer.GetComponent<MeshFilter>().mesh = waterLevelMesh;
        waterLayer.GetComponent<MeshCollider>().sharedMesh = waterLevelMesh;
        waterLayer.gameObject.SetActive(true);

    }

    public void UpdateWaterLevel(string input)
    {
        input = input.Replace(',', '.');

        if (float.TryParse(input, out float newValue))
            waterLevel = minHeight + newValue;
    }

}
