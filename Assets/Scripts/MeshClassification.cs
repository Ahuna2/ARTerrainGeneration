using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.ARKit;

using Object = UnityEngine.Object;

public class MeshClassification : MonoBehaviour
{
    /// <summary>
    /// The number of mesh classifications detected.
    /// </summary>
    const int k_NumClassifications = 8;

    /// <summary>
    /// The mesh manager for the scene.
    /// </summary>
    public ARMeshManager m_MeshManager;

    /// <summary>
    /// Mesh prefab for all excluding walls and ceiling.
    /// </summary>
    public MeshFilter m_Ground;

    /// <summary>
    /// A mapping from tracking ID to instantiated mesh filters.
    /// </summary>
    readonly Dictionary<TrackableId, MeshFilter[]> m_MeshFrackingMap = new Dictionary<TrackableId, MeshFilter[]>();

    /// <summary>
    /// The delegate to call to breakup a mesh.
    /// </summary>
    Action<MeshFilter> m_BreakupMeshAction;

    /// <summary>
    /// The delegate to call to update a mesh.
    /// </summary>
    Action<MeshFilter> m_UpdateMeshAction;

    /// <summary>
    /// The delegate to call to remove a mesh.
    /// </summary>
    Action<MeshFilter> m_RemoveMeshAction;

    /// <summary>
    /// An array to store the triangle vertices of the base mesh.
    /// </summary>
    readonly List<int> m_BaseTriangles = new List<int>();

    /// <summary>
    /// An array to store the triangle vertices of the classified mesh.
    /// </summary>
    readonly List<int> m_ClassifiedTriangles = new List<int>();

    /// <summary>
    /// On awake, set up the mesh filter delegates.
    /// </summary>
    void Awake()
    {
        m_BreakupMeshAction = new Action<MeshFilter>(BreakupMesh);
        m_UpdateMeshAction = new Action<MeshFilter>(UpdateMesh);
        m_RemoveMeshAction = new Action<MeshFilter>(RemoveMesh);
    }

    /// <summary>
    /// On enable, subscribe to the meshes changed event.
    /// </summary>
    void OnEnable()
    {
        Debug.Assert(m_MeshManager != null, "mesh manager cannot be null");
        if (m_MeshManager.subsystem is XRMeshSubsystem meshSubsystem)
        {
            meshSubsystem.SetClassificationEnabled(true);
        }
        m_MeshManager.meshesChanged += OnMeshesChanged;
    }

    /// <summary>
    /// On disable, unsubscribe from the meshes changed event.
    /// </summary>
    void OnDisable()
    {
        Debug.Assert(m_MeshManager != null, "mesh manager cannot be null");
        if (m_MeshManager.subsystem is XRMeshSubsystem meshSubsystem)
        {
            meshSubsystem.SetClassificationEnabled(false);
        }
        m_MeshManager.meshesChanged -= OnMeshesChanged;
    }

    /// <summary>
    /// When the meshes change, update the scene meshes.
    /// </summary>
    void OnMeshesChanged(ARMeshesChangedEventArgs args)
    {
        if (args.added != null)
        {
            args.added.ForEach(m_BreakupMeshAction);
        }

        if (args.updated != null)
        {
            args.updated.ForEach(m_UpdateMeshAction);
        }

        if (args.removed != null)
        {
            args.removed.ForEach(m_RemoveMeshAction);
        }

    }

    /// <summary>
    /// Get all created meshes with the tag 'Ground'.
    /// </summary>
    public List<MeshFilter> GetGroundMeshes()
    {
        var trackablesTransform = transform.parent.GetChild(3);
        List<MeshFilter> groundMeshFilters = new List<MeshFilter>();
        int meshLayerMask = 3; 

        for (int i = 0; i < trackablesTransform.childCount; i++)
        {
            Transform trackable = trackablesTransform.GetChild(i);
            try
            {
                if (trackable.gameObject.activeSelf && trackable.gameObject.layer == meshLayerMask && trackable.GetComponent<MeshFilter>().mesh.vertexCount > 200)
                {
                    groundMeshFilters.Add(trackable.GetComponent<MeshFilter>());
                }
            } 
            catch (NullReferenceException) { }    //Happens once in a hundred. Cannot figure out why.
        }

        return groundMeshFilters;
    }

    /// <summary>
    /// Parse the trackable ID from the mesh filter name.
    /// </summary>
    /// <param name="meshFilterName">The mesh filter name containing the trackable ID.</param>
    /// <returns>
    /// The trackable ID parsed from the string.
    /// </returns>
    TrackableId ExtractTrackableId(string meshFilterName)
    {
        string[] nameSplit = meshFilterName.Split(' ');
        return new TrackableId(nameSplit[1]);
    }

    /// <summary>
    /// Given a base mesh, the face classifications for all faces in the mesh, and a single face classification to
    /// extract, extract into a new mesh only the faces that have the selected face classification.
    /// </summary>
    /// <param name="baseMesh">The original base mesh.</param>
    /// <param name="faceClassifications">The array of face classifications for each triangle in the
    /// <paramref name="baseMesh"/></param>
    /// <param name="selectedMeshClassification">A single classification to extract the faces from the
    /// <paramref="baseMesh"/>into the <paramref name="classifiedMesh"/></param>
    /// <param name="classifiedMesh">The output mesh to be updated with the extracted mesh.</param>
    void ExtractClassifiedMesh(Mesh baseMesh, NativeArray<ARMeshClassification> faceClassifications, ARMeshClassification selectedMeshClassification, Mesh classifiedMesh)
    {
        // Count the number of faces matching the selected classification.
        int classifiedFaceCount = 0;
        for (int i = 0; i < faceClassifications.Length; ++i)
        {
            if (faceClassifications[i] == selectedMeshClassification)
            {
                ++classifiedFaceCount;
            }
        }

        // Clear the existing mesh.
        classifiedMesh.Clear();

        // If there were matching face classifications, build a new mesh from the base mesh.
        if (classifiedFaceCount > 0)
        {
            baseMesh.GetTriangles(m_BaseTriangles, 0);
            Debug.Assert(m_BaseTriangles.Count == (faceClassifications.Length * 3),
                         "unexpected mismatch between triangle count and face classification count");

            m_ClassifiedTriangles.Clear();
            m_ClassifiedTriangles.Capacity = classifiedFaceCount * 3;

            for (int i = 0; i < faceClassifications.Length; ++i)
            {
                if (faceClassifications[i] == selectedMeshClassification)
                {
                    int baseTriangleIndex = i * 3;

                    m_ClassifiedTriangles.Add(m_BaseTriangles[baseTriangleIndex + 0]);
                    m_ClassifiedTriangles.Add(m_BaseTriangles[baseTriangleIndex + 1]);
                    m_ClassifiedTriangles.Add(m_BaseTriangles[baseTriangleIndex + 2]);
                }
            }

            classifiedMesh.vertices = baseMesh.vertices;
            classifiedMesh.normals = baseMesh.normals;
            classifiedMesh.SetTriangles(m_ClassifiedTriangles, 0);
        }

    }

    /// <summary>
    /// Break up a single mesh with multiple face classifications into submeshes, each with an unique and uniform mesh
    /// classification.
    /// </summary>
    /// <param name="meshFilter">The mesh filter for the base mesh with multiple face classifications.</param>
    void BreakupMesh(MeshFilter meshFilter)
    {
        XRMeshSubsystem meshSubsystem = m_MeshManager.subsystem as XRMeshSubsystem;
        if (meshSubsystem == null)
        {
            return;
        }

        var meshId = ExtractTrackableId(meshFilter.name);
        
        var faceClassifications = meshSubsystem.GetFaceClassifications(meshId, Allocator.Persistent);

        if (!faceClassifications.IsCreated)
        {
            return;
        }

        using (faceClassifications)
        {
            if (faceClassifications.Length <= 0)
            {
                return;
            }

            var parent = meshFilter.transform.parent;

            //Discard wall/ceiling meshes
            MeshFilter[] meshFilters = new MeshFilter[] { Instantiate(m_Ground, parent), null, Instantiate(m_Ground, parent), null, Instantiate(m_Ground, parent), Instantiate(m_Ground, parent), null, null };
            m_MeshFrackingMap[meshId] = meshFilters;

            var baseMesh = meshFilter.sharedMesh;
            for (int i = 0; i < k_NumClassifications; ++i)
            {
                var classifiedMeshFilter = meshFilters[i];
                if (classifiedMeshFilter != null)
                {
                    var classifiedMesh = classifiedMeshFilter.mesh;
                    ExtractClassifiedMesh(baseMesh, faceClassifications, (ARMeshClassification)i, classifiedMesh);
                    meshFilters[i].mesh = classifiedMesh;
                }
            }
        }
    }

    /// <summary>
    /// Update the submeshes corresponding to the single mesh with multiple face classifications into submeshes.
    /// </summary>
    /// <param name="meshFilter">The mesh filter for the base mesh with multiple face classifications.</param>
    void UpdateMesh(MeshFilter meshFilter)
    {
        XRMeshSubsystem meshSubsystem = m_MeshManager.subsystem as XRMeshSubsystem;
        if (meshSubsystem == null)
        {
            return;
        }

        var meshId = ExtractTrackableId(meshFilter.name);
        var faceClassifications = meshSubsystem.GetFaceClassifications(meshId, Allocator.Persistent);

        if (!faceClassifications.IsCreated)
        {
            return;
        }

        using (faceClassifications)
        {
            if (faceClassifications.Length <= 0)
            {
                return;
            }

            var meshFilters = m_MeshFrackingMap[meshId];

            var baseMesh = meshFilter.sharedMesh;
            for (int i = 0; i < k_NumClassifications; ++i)
            {
                var classifiedMeshFilter = meshFilters[i];
                if (classifiedMeshFilter != null)
                {
                    var classifiedMesh = classifiedMeshFilter.mesh;
                    ExtractClassifiedMesh(baseMesh, faceClassifications, (ARMeshClassification)i, classifiedMesh);
                    meshFilters[i].mesh = classifiedMesh;
                }
            }
        }
    }

    /// <summary>
    /// Remove the submeshes corresponding to the single mesh.
    /// </summary>
    /// <param name="meshFilter">The mesh filter for the base mesh with multiple face classifications.</param>
    void RemoveMesh(MeshFilter meshFilter)
    {
        var meshId = ExtractTrackableId(meshFilter.name);
        var meshFilters = m_MeshFrackingMap[meshId];
        for (int i = 0; i < k_NumClassifications; ++i)
        {
            var classifiedMeshFilter = meshFilters[i];
            if (classifiedMeshFilter != null)
            {
                Object.Destroy(classifiedMeshFilter);
            }
        }

        m_MeshFrackingMap.Remove(meshId);
    }
}