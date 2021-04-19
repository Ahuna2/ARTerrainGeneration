using System;
using System.Collections.Generic;
using System.Threading;
using System.Linq;

using TriangleNet.Geometry;
using TriangleNet.Topology;
using TriangleNet.Meshing;

using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class TerrainMeshGeneration : MonoBehaviour
{
    public MeshClassification m_MeshClassification;
    public ARMeshManager m_MeshManager;
    public GameObject terrainPrefab;
    public AnimationCurve heightCurve;

    private List<Vector3> vertices;
    public Vector3[] verticesArray;
    private List<int> triangles;
    public int[] trianglesArray;

    private bool debug1 = true;
    private bool debug2 = true;
    private bool debug3 = true;

    private bool displayingGenerateButton = true;

    public float minHeight = float.PositiveInfinity;
    public float maxHeight = float.NegativeInfinity;
    private float smallestUpgrade = float.PositiveInfinity;

    #region Parameters
    //TODO readonlyks
    private float precision = 0.011f;
    private float noiseModifier = 0.18f;
    private float snowHeightThreshold = 0.45f;
    private float sandHeightThreshold = 0.31f;
    private int degrees = 8;
    private float sedimentCapacity = 0.0023f;
    private int initialDropletLifespan = 20;
    #endregion

    public void ResetMesh()
    {
        if (displayingGenerateButton) return;

        //Works by reloading scene; couldn't figure out any other way to pass the messege to ARKit
        var xrManagerSettings = XRGeneralSettings.Instance.Manager;
        xrManagerSettings.DeinitializeLoader();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        xrManagerSettings.InitializeLoaderSync();
    }

    public void Generate()
    {
        if (!displayingGenerateButton) return;

        DateTime start = DateTime.Now;

        //Get all vertices of ground meshes
        GetGroundMeshVertices();

        //Retain only top-layer vertices
        DateTime beforeHiddenVertices = DateTime.Now;
        vertices.Sort((o1, o2) => o1.y.CompareTo(o2.y));
        RemoveHiddenVertices(precision);
        DateTime afterHiddenVertices = DateTime.Now;

        //Apply Delaunay's triangulation to generate mesh from retained vertices
        DateTime beforeTriangulation = DateTime.Now;
        TriangulateMesh();
        DateTime afterTriangulation = DateTime.Now;

        //Remove jarring cliffs
        DateTime beforeSmoothing = DateTime.Now;
        verticesArray = vertices.ToArray();
        trianglesArray = triangles.ToArray();
        SmoothMesh();
        DateTime afterSmoothing = DateTime.Now;

        //Add noise
        DateTime beforeNoise = DateTime.Now;
        ApplyNoise(verticesArray);
        DateTime afterNoise = DateTime.Now;

        //Apply erosion
        DateTime beforeErosion = DateTime.Now;
        if (debug1) Erosion();
        DateTime afterErosion = DateTime.Now;

        //Instantiate new mesh
        InstantiateMesh();

        if (debug2)
        {
            DateTime end = DateTime.Now;
            Debug.Log("VERTICES:" + verticesArray.Length);
            Debug.Log("Total length: " + (end - start).TotalMilliseconds);
            Debug.Log("HiddenVertices length: " + (afterHiddenVertices - beforeHiddenVertices).TotalMilliseconds);
            Debug.Log("Triangulation length: " + (afterTriangulation - beforeTriangulation).TotalMilliseconds);
            Debug.Log("Smoothing length: " + (afterSmoothing - beforeSmoothing).TotalMilliseconds);
            Debug.Log("Noise length: " + (afterNoise - beforeSmoothing).TotalMilliseconds);
            Debug.Log("Erosion length: " + (afterErosion - beforeSmoothing).TotalMilliseconds);
        }
    }

    private void GetGroundMeshVertices()
    {
        //Stop updating detected meshes
        if (m_MeshManager.subsystem is XRMeshSubsystem meshSubsystem)
        {
            meshSubsystem.Stop();
        }

        //Fetch detected ground meshes
        List<MeshFilter> groundMeshes = m_MeshClassification.GetGroundMeshes();
        if (groundMeshes.Count == 0)
        {
            Debug.Log("No groundmeshes found.");
            return;
        }

        //Copy the vertices and disable meshes
        vertices = new List<Vector3>();
        foreach (MeshFilter el in groundMeshes)
        {
            vertices.AddRange(el.mesh.vertices);
            el.gameObject.SetActive(false);
        }
    }

    private void RemoveHiddenVertices(float precision)
    {
        //Remove any vertex that is predated by a higher one at similar coordinates
        for (int i = vertices.Count-1; i >= 0; i--)
        {
            for (int j = i+1; j < vertices.Count; j++)
            {
                if (Mathf.Abs(vertices[i].x - vertices[j].x) < precision && Mathf.Abs(vertices[i].z - vertices[j].z) < precision)
                {
                    vertices.RemoveAt(i);
                    i++;
                }
            }
        }
    }

    private void TriangulateMesh()
    {
        //TODO: Default to a heigher MinimumAngle (30); create a timeout loop that lowers (30 -> 15 -> 5 -> 0) it

        Polygon polygon = new Polygon();
        foreach (Vector3 el in vertices)
        {
            polygon.Add(new Vertex(el.x, el.z, el.y, -1));
        }

        TriangleNet.Mesh mesh = (TriangleNet.Mesh)polygon.Triangulate(new QualityOptions() { SteinerPoints = -1, MinimumAngle = degrees } );

        //Update Steiner points heights to the average of all its neighbours
        RecalculateSteinerPoints(mesh);

        //Use having ordered triangles to do preliminary smoothing
        List<Triangle> trianglesNet = mesh.Triangles.ToList();
        trianglesNet.Sort((o2, o1) =>
        (
            o1.GetVertex(0).y + o1.GetVertex(1).y + o1.GetVertex(2).y).CompareTo(o2.GetVertex(0).y + o2.GetVertex(1).y + o2.GetVertex(2).y)
        );

        //Vertex list needs re-creation from TriangleNet data
        vertices = new List<Vector3>();
        triangles = new List<int>();
        
        foreach (Triangle el in trianglesNet)
        {
            Vertex v0 = el.GetVertex(2), v1 = el.GetVertex(1), v2 = el.GetVertex(0);
            float averageHeight = (v0.up + v1.up + v2.up) / 3;

            if (averageHeight > v0.up) v0.up = averageHeight;
            if (averageHeight > v1.up) v1.up = averageHeight;
            if (averageHeight > v2.up) v2.up = averageHeight;

            triangles.Add(GetIndexByVertex2DCoords((float)v0.x, (float)v0.y, v0.up));
            triangles.Add(GetIndexByVertex2DCoords((float)v1.x, (float)v1.y, v1.up));
            triangles.Add(GetIndexByVertex2DCoords((float)v2.x, (float)v2.y, v2.up));
        }
    }

    private void SmoothMesh()
    {
        List<float> trianglesAverageHeights = new List<float>();
        for (int i = 0; i < triangles.Count; i += 3)
            trianglesAverageHeights.Add((vertices[triangles[i + 2]].y + vertices[triangles[i + 1]].y + vertices[triangles[i]].y) / 3);

        while (trianglesAverageHeights.Count > 0)
        {
            float highestAverageLeft = float.NegativeInfinity;
            int hightestAverageI = -1;
            for (int j = 0; j < trianglesAverageHeights.Count; j++)
            {
                if (trianglesAverageHeights[j] > highestAverageLeft)
                {
                    highestAverageLeft = trianglesAverageHeights[j];
                    hightestAverageI = j;
                }
            }
            if (highestAverageLeft < -10000f) return;

            int i = hightestAverageI * 3;
            Vector3 v0 = verticesArray[triangles[i + 2]], v1 = verticesArray[triangles[i + 1]], v2 = verticesArray[triangles[i]];
            float averageHeight = (v0.y + v1.y + v2.y) / 3;

            if (v0.y < averageHeight) verticesArray[triangles[i + 2]].y = averageHeight;
            if (v1.y < averageHeight) verticesArray[triangles[i + 1]].y = averageHeight;
            if (v2.y < averageHeight) verticesArray[triangles[i]].y = averageHeight;

            trianglesAverageHeights[hightestAverageI] = float.NegativeInfinity;
        }
    }

    private void ApplyNoise(Vector3[] vertices)
    {
        //Define normal distribution floor and ceiling
        foreach (Vector3 el in vertices)
        {
            float height = el.y;
            if (height < minHeight) minHeight = height;
            if (height > maxHeight) maxHeight = height;
        }
        float floorToCeilingDifference = maxHeight - minHeight;

        //Generate n heightareas and n corresponding normal distribution multipliers
        float[] multipliers = new float[]
        {   
            0.02f, 0.047f, 0.1f, 0.18f, 0.3f, 0.5f, 0.69f, 0.9f, 1f, 1.2f,
            1.2f, 1f, 0.9f, 0.69f, 0.5f, 0.3f, 0.18f, 0.1f, 0.047f, 0.02f
        };
        float[] breakpoints = new float[multipliers.Length];
        float step = 1f / breakpoints.Length;
        for (int i = 0; i < breakpoints.Length; i++)
        {
            breakpoints[i] = minHeight + (i + 1f) * step * floorToCeilingDifference;
        }

        //Apply (noise * its strength) to each vertex
        for (int i = 0; i < vertices.Length; i++)
        {
            float height = vertices[i].y;
            float mul = 0;
            for (int j = 0; j < breakpoints.Length; j++)
            {
                if (height <= breakpoints[j])
                {
                    mul = multipliers[j];
                    break;
                }
            }
            float noise = 0.3f * Mathf.PerlinNoise(0.5f * vertices[i].x * 300, vertices[i].z * 300) + 2f * Mathf.PerlinNoise(vertices[i].x * 10, vertices[i].z * 10);
            float heightCurveModifier = heightCurve.Evaluate((vertices[i].y - minHeight) / (maxHeight - minHeight));
            float totalNoise = noiseModifier * heightCurveModifier * noise;
            if (totalNoise < smallestUpgrade) smallestUpgrade = totalNoise;
            vertices[i].y += totalNoise;
        }

        //Subtract the smallest added noise from all vertices to avoid raising the terrain too high
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i].y -= (smallestUpgrade + 0.2f);
        }

        SmoothMesh();
    }

    private void Erosion()
    {
        int numberOfDroplets = verticesArray.Length;

        for (int i = 0; i < numberOfDroplets; i++)
        {
            float sediment = 0;
            float dropletLifespan = initialDropletLifespan;

            //Spawn the initial droplet
            int dropletPos = UnityEngine.Random.Range(0, verticesArray.Length);

            //Emulate its path
            while (true)
            {
                //Find the lowest neighbour
                int target = ErosionNextTarget(dropletPos);
                float heightDifference = verticesArray[dropletPos].y - verticesArray[target].y;

                //Avoid getting stuck on flat terrain
                if (heightDifference == 0 && sediment == 0) break;

                //If the droplet has been going for long enough to evaporate then deposit the remaining sediment
                if (dropletLifespan-- == 0)
                {
                    verticesArray[dropletPos].y += sediment;
                    break;
                }

                //If the current position is the lowest of its neighbours
                if (heightDifference < 0)
                {
                    //If gathered sediment isn't enough to carry on then deposit it and break
                    if (sediment < (-heightDifference + (-heightDifference / 10)))
                    {
                        verticesArray[dropletPos].y += sediment;
                        break;
                    }

                    //Else deposit the necessary sediment to overcome difference and carry on
                    verticesArray[dropletPos].y += (-heightDifference + (-heightDifference / 10));
                    sediment -= (-heightDifference + (-heightDifference / 10));
                }

                //Else there is a lower neighbour - erode what's possible and add it to carried sediment
                else
                {
                    float remainingErosionCapacity = sedimentCapacity - sediment;
                    float erosionAmount = heightDifference < remainingErosionCapacity ? heightDifference : remainingErosionCapacity;
                    verticesArray[dropletPos].y -= erosionAmount;
                    sediment += erosionAmount;
                }

                //The droplet loses more sediment as it evaporates
                verticesArray[dropletPos].y += sediment / (dropletLifespan + 5);
                sediment /= (dropletLifespan + 1);

                //Iterate over target
                dropletPos = target;
            }
        }
    }

    private void InstantiateMesh()
    {
        minHeight = float.MaxValue;
        maxHeight = float.MinValue;
        foreach (Vector3 el in verticesArray)
        {
            if (el.y < minHeight) minHeight = el.y;
            if (el.y > maxHeight) maxHeight = el.y;
        }

        Mesh terrainMesh = new Mesh
        {
            vertices = verticesArray,
            triangles = trianglesArray
        };
        terrainMesh.Optimize();
        terrainMesh.RecalculateNormals();
        GameObject terrain = Instantiate(terrainPrefab, transform.parent.GetChild(3));  //Add created terrain gameobject to AR Foundation's trackables
        terrain.GetComponent<MeshFilter>().mesh = terrainMesh;
        terrain.GetComponent<MeshCollider>().sharedMesh = terrainMesh;
        terrain.GetComponent<MeshRenderer>().material.SetFloat("_SnowHeightThreshold", maxHeight - snowHeightThreshold * (maxHeight - minHeight));
        terrain.GetComponent<MeshRenderer>().material.SetFloat("_SandHeightThreshold", minHeight + sandHeightThreshold * (maxHeight - minHeight));
        terrain.gameObject.SetActive(true);

    }

    private int ErosionNextTarget(int dropletPos)
    {
        int targetPos = -1;
        float targetHeight = float.MaxValue;

        for (int i = 0; i < triangles.Count; i += 3)
        {
            if (triangles[i] == dropletPos || triangles[i + 1] == dropletPos || triangles[i + 2] == dropletPos)
            {
                for (int j = i; j < i + 3; j++)
                {
                    float tempTargetHeight = verticesArray[triangles[j]].y;
                    if (triangles[j] != dropletPos && tempTargetHeight < targetHeight)
                    {
                        targetPos = triangles[j];
                        targetHeight = tempTargetHeight;
                    }
                }
            }
        }

        return targetPos;
    }

    private int GetIndexByVertex2DCoords(float x, float z, float up)
    {
        //Check if vertix was already added; if so, return its index
        for (int i = 0; i < vertices.Count; i++)
        {
            if (vertices[i].x == x && vertices[i].z == z) return i;
        }

        //No match - add new vertex with coords
        vertices.Add(new Vector3(x, up, z));
        return vertices.Count - 1;
    }

    private void RecalculateSteinerPoints(TriangleNet.Mesh mesh)
    {
        //Steiner points are added througout Delanunay to meet quality expectations 
        //Add a height to these that is equal to the average of all its neighbouring triangles

        bool reverse = false;   //Traverse backwards to account for Steiners affecting neighbouring Steiners
        for (int n = 0; n < 2; n++)
        {
            foreach (Triangle triangle in (reverse ? mesh.triangles : mesh.triangles.Reverse()))
            {
                Vertex[] vertices = triangle.vertices;
                float averageHeight = 0;
                List<int> steinersI = new List<int>();
                int averageHeightWeight = 3;

                for (int i = 2; i >= 0; i--)
                {
                    //If the point isn't Steiner then use its height for average
                    if (vertices[i].steinerPointWeight == -1)       
                        averageHeight += vertices[i].up;

                    //Else if the point is Steiner and hasn't been updated then update it
                    else if (vertices[i].steinerPointWeight == 0)   
                    {
                        steinersI.Add(i);
                        averageHeightWeight--;
                    }

                    //Else the point is Steiner that has been updated then update it and use its height for average
                    else
                    {
                        steinersI.Add(i);
                        averageHeight += vertices[i].up;
                    }
                }

                if (averageHeightWeight != 0)
                {
                    averageHeight /= averageHeightWeight;

                    foreach (int el in steinersI)
                    {
                        vertices[el].up = (vertices[el].up * vertices[el].steinerPointWeight + averageHeight * averageHeightWeight) / (vertices[el].steinerPointWeight + averageHeightWeight);
                        vertices[el].steinerPointWeight += averageHeightWeight;
                    }
                }
            }
            reverse = true;
        }
    }

    private void IncreaseVertexFrequency()
    {
        //Replace all triangles with 3 subdivided ones
        int trianglesOriginalLength = triangles.Count;
        List<int> newTriangles = new List<int>();
        for (int i = trianglesOriginalLength - 1; i >= 0; i -= 3)
        {
            int v0 = triangles[i - 2], v1 = triangles[i - 1], v2 = triangles[i];
            newTriangles.Add(v0); newTriangles.Add(v1); newTriangles.Add(vertices.Count);
            newTriangles.Add(v1); newTriangles.Add(v2); newTriangles.Add(vertices.Count);
            newTriangles.Add(v2); newTriangles.Add(v0); newTriangles.Add(vertices.Count);

            Vector3 newVertex = (vertices[v0] + vertices[v1] + vertices[v2]) / 3;
            vertices.Add(newVertex);
        }
        triangles = newTriangles;
    }

    #region UI controllers
    public void UpdateFloat1(string input)
    {
        input = input.Replace(',', '.');

        if (float.TryParse(input, out float newValue))
            sedimentCapacity = newValue;
    }

    public void UpdateFloat2(string input)
    {
        input = input.Replace(',', '.');

        if (float.TryParse(input, out float newValue))
            initialDropletLifespan = (int)newValue;
    }

    public void SwitchGenerateAndResetButtons()
    {
        displayingGenerateButton = false;
        Text buttonText = GameObject.FindGameObjectWithTag("GameController").GetComponentInChildren<Text>();
        buttonText.text = "Reset";
    }

    public void ToggleDebug1(bool value)
    {
        this.debug1 = value;
    }

    public void ToggleDebug2(bool value)
    {
        this.debug2 = value;
    }

    public void ToggleDebug3(bool value)
    {
        this.debug3 = value;
    }
    #endregion 
}
