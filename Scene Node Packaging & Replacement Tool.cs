// Beecher@gsus.cc
// 2024.01.23

// This code is part of a Unity Editor extension, implementing a custom editor window "Scene Node Pack & Replace Tool," which provides functionality for operating on Unity GameObject nodes and Prefabs.
// Node Packaging Tool ("Scene Node Pack & Replace Tool")
// One-click Node (File) Replacement Tool
// The existence of this tool significantly improves the efficiency of the art team when handling resource and object replacements, especially useful for large-scale scene reconstructions or resource structure adjustments. It simplifies the typically tedious manual processing workflow.

using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;

public class AssetAndPrefabTool : EditorWindow
{
    private string targetPath;
    private GameObject selectedPrefab;
    private GameObject prefabToReplace;
    private GameObject prefabToRemove;
    private bool copyAllComponents = true;
    private bool keepName = true;

    [MenuItem("Tools/Art Tools/Scene Node Pack & Replace Tool")]
    private static void Init()
    {
        GetWindow<AssetAndPrefabTool>(true, "Scene Node Pack & Replace Tool", true);
    }

    void OnGUI()
    {
        GUILayout.Label("Node Packaging Tool", EditorStyles.boldLabel);
        targetPath = EditorGUILayout.TextField("Package to Target Path", targetPath);
        selectedPrefab = EditorGUILayout.ObjectField("File to Package", selectedPrefab, typeof(GameObject), false) as GameObject;

        if (GUILayout.Button("Package & Copy Rebind"))
        {
            RebindPrefabDependencies();
        }

        EditorGUILayout.Space();

        GUILayout.Label("One-click Node (File) Replacement Tool", EditorStyles.boldLabel);
        prefabToReplace = EditorGUILayout.ObjectField("File to Replace", prefabToReplace, typeof(GameObject), false) as GameObject;
        prefabToRemove = EditorGUILayout.ObjectField("Remove Scene Node", prefabToRemove, typeof(GameObject), true) as GameObject;

        copyAllComponents = EditorGUILayout.ToggleLeft("Copy All Components", copyAllComponents);

        keepName = EditorGUILayout.ToggleLeft("Keep Name (Maintain Node Naming)", keepName);

        if (GUILayout.Button("Execute Replacement Replace"))
        {
            ReplacePrefab();
        }
    }

    private void RebindPrefabDependencies()
    {
        if (selectedPrefab != null && !string.IsNullOrEmpty(targetPath))
        {
            RebindPrefabAssets(selectedPrefab, targetPath);
        }
        else
        {
            EditorUtility.DisplayDialog("Error", "You must specify both a valid prefab and target path.", "OK");
        }
    }

    private void RebindPrefabAssets(GameObject prefab, string folderPath)
    {
        string prefabPath = AssetDatabase.GetAssetPath(prefab);
        string[] allDependencies = AssetDatabase.GetDependencies(prefabPath, true).Where(p => !p.EndsWith(".cs") && !p.EndsWith(".shader")).ToArray();

        CheckAndCreateFolder(targetPath);
        AssetDatabase.Refresh();
        string newPrefabPath = Path.Combine(folderPath, Path.GetFileName(prefabPath));

        if (!File.Exists(newPrefabPath))
        {
            AssetDatabase.CopyAsset(prefabPath, newPrefabPath);
        }

        AssetDatabase.Refresh();

        foreach (string dependencyPath in allDependencies)
        {
            string fileName = Path.GetFileName(dependencyPath);
            string copyPath = Path.Combine(folderPath, fileName);

            if (!File.Exists(copyPath))
            {
                AssetDatabase.CopyAsset(dependencyPath, copyPath);
            }
        }

        AssetDatabase.Refresh();
        GameObject newPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(newPrefabPath);
        Renderer[] renderers = newPrefab.GetComponentsInChildren<Renderer>(true);
        MeshFilter[] meshFilters = newPrefab.GetComponentsInChildren<MeshFilter>(true);

        foreach (Renderer renderer in renderers)
        {
            Material[] newMaterials = renderer.sharedMaterials.Select(m => CopyAsset<Material>(m, folderPath)).ToArray();
            renderer.sharedMaterials = newMaterials;
        }

        foreach (MeshFilter meshFilter in meshFilters)
        {
            meshFilter.sharedMesh = CopyAsset<Mesh>(meshFilter.sharedMesh, folderPath);
        }

        PrefabUtility.SavePrefabAsset(newPrefab);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"{prefab.name} and its dependencies have been rebound to {folderPath}.");
    }

    private T CopyAsset<T>(T original, string folderPath) where T : Object
    {
        if (!original) return null;

        string assetPath = AssetDatabase.GetAssetPath(original);
        string newAssetPath = Path.Combine(folderPath, Path.GetFileName(assetPath));

        if (!File.Exists(newAssetPath))
        {
            AssetDatabase.CopyAsset(assetPath, newAssetPath);
            AssetDatabase.Refresh();
        }

        if (typeof(T) == typeof(Material))
        {
            Material newMaterial = AssetDatabase.LoadAssetAtPath<Material>(newAssetPath);
            UpdateMaterialTextures(original as Material, newMaterial, folderPath);
            return newMaterial as T;
        }

        return AssetDatabase.LoadAssetAtPath<T>(newAssetPath);
    }

    private void UpdateMaterialTextures(Material originalMaterial, Material newMaterial, string folderPath)
    {
        Shader shader = newMaterial.shader;
        int propertyCount = ShaderUtil.GetPropertyCount(shader);

        for (int i = 0; i < propertyCount; i++)
        {
            if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
            {
                string propertyName = ShaderUtil.GetPropertyName(shader, i);
                Texture originalTexture = originalMaterial.GetTexture(propertyName);

                if (originalTexture != null)
                {
                    string originalTexturePath = AssetDatabase.GetAssetPath(originalTexture);
                    string newTexturePath = Path.Combine(folderPath, Path.GetFileName(originalTexturePath));

                    if (!File.Exists(newTexturePath))
                    {
                        AssetDatabase.CopyAsset(originalTexturePath, newTexturePath);
                        AssetDatabase.Refresh();
                    }

                    Texture newTexture = AssetDatabase.LoadAssetAtPath<Texture>(newTexturePath);
                    newMaterial.SetTexture(propertyName, newTexture);
                }
            }
        }
    }

    private void CheckAndCreateFolder(string folderPath)
    {
        string[] folderNames = folderPath.Split('/');
        string parentFolder = "Assets";
        foreach (string folder in folderNames)
        {
            string tempPath = Path.Combine(parentFolder, folder);
            if (!AssetDatabase.IsValidFolder(tempPath))
            {
                AssetDatabase.CreateFolder(parentFolder, folder);
            }
            parentFolder = tempPath;
        }
    }

    private void ReplacePrefab()
    {
        if (prefabToReplace == null || prefabToRemove == null)
        {
            EditorUtility.DisplayDialog("Error", "Please specify the objects to replace and to remove.", "OK");
            return;
        }

        string originalName = keepName ? prefabToRemove.name : null;

        Undo.RecordObject(prefabToRemove, "Replace Prefab");
        Vector3 position = prefabToRemove.transform.position;
        Quaternion rotation = prefabToRemove.transform.rotation;
        Vector3 scale = prefabToRemove.transform.localScale;

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabToReplace);
        instance.transform.position = position;
        instance.transform.rotation = rotation;
        instance.transform.localScale = scale;
        instance.transform.SetParent(prefabToRemove.transform.parent, true);

        if (copyAllComponents)
        {
            CopyComponents(prefabToRemove, instance);
        }

        if (keepName)
        {
            instance.name = originalName;
        }

        Undo.RegisterCreatedObjectUndo(instance, "Create Prefab");

        DestroyPrefab(prefabToRemove);
    }

    private void CopyComponents(GameObject original, GameObject destination)
    {
        foreach (Component originalComponent in original.GetComponents<Component>())
        {
            if (!(originalComponent is Transform))
            {
                UnityEditorInternal.ComponentUtility.CopyComponent(originalComponent);
                UnityEditorInternal.ComponentUtility.PasteComponentAsNew(destination);
            }
        }
    }

    private void DestroyPrefab(GameObject prefabToRemove)
    {
        Undo.DestroyObjectImmediate(prefabToRemove);
    }
}
