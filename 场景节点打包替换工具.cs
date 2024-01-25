// Beecher@gsus.cc
// 2024.01.23

// 该代码是 Unity 编辑器扩展的一部分，实现了一个自定义编辑器窗口 "场景节点打包替换工具"，提供了一些操作 Unity 对象节点（GameObject） 和 预制体（Prefabs）的功能。
// 节点打包工具 ("节点打包替换工具")
// 节点（文件）一键替换工具
// 这个工具的存在显著地提高了美术团队在处理资源和对象替换时的效率，尤其有助于大规模的场景重构或资源结构调整的工作。它简化了平时繁琐的手动处理流程。

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

    [MenuItem("Tools/美术工具/场景节点打包替换工具")]
    private static void Init()
    {
        GetWindow<AssetAndPrefabTool>(true, "场景节点打包替换工具", true);
    }

    void OnGUI()
    {
        GUILayout.Label("节点打包工具", EditorStyles.boldLabel);
        targetPath = EditorGUILayout.TextField("打包到目标路径", targetPath);
        selectedPrefab = EditorGUILayout.ObjectField("被打包 文件", selectedPrefab, typeof(GameObject), false) as GameObject;

        if (GUILayout.Button("打包复制 Rebind"))
        {
            RebindPrefabDependencies();
        }

        EditorGUILayout.Space();

        GUILayout.Label("节点（文件）一键替换工具", EditorStyles.boldLabel);
        prefabToReplace = EditorGUILayout.ObjectField("替换文件", prefabToReplace, typeof(GameObject), false) as GameObject;
        prefabToRemove = EditorGUILayout.ObjectField("移除场景节点", prefabToRemove, typeof(GameObject), true) as GameObject;

        copyAllComponents = EditorGUILayout.ToggleLeft("复制所有组件", copyAllComponents);

        keepName = EditorGUILayout.ToggleLeft("不改名（保持节点命名）", keepName);

        if (GUILayout.Button("执行替换 Replace"))
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
            EditorUtility.DisplayDialog("错误", "你必须同时指定有效的预制和目标路径。", "确定");
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
            EditorUtility.DisplayDialog("错误", "请指定替换对象和移除对象。", "确定");
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
