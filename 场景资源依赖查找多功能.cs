// Beecher@gsus.cc
// 2024.01.23

// 这段代码定义了一个Unity编辑器窗口工具，名为“场景资源依赖查找多功能”，提供了两个主要功能来增强内容创建和管理工作流程。
// 对象查找器：这个工具允许用户选择Unity场景中的任一对象，并根据这个对象找出其源文件。如果对象在场景中被重命名，它将以不同的风格显示，用来指出该对象和它的源文件不再有相同的名称。
// 依赖性探查器：这个功能允许用户探查和显示选中对象的所有依赖资源。提供了一个清除按钮来清除显示的依赖列表。
// 整体上，这个工具大大提高了美术资产管线中的工作效率，尤其是在管理资源和分析资源依赖关系方面。通过精确地显示资源间的关联，可以帮助团队成员明晰和优化资产间的结构，从而减少冗余并提高游戏的性能。

using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class PrefabAssetDependencyCheckTool : EditorWindow
{
    private Object selectedObject;
    private string objectSourcePath;
    private string objectFileName;
    private GUIStyle renamedLabelStyle;
    private Vector2 scrollPosition;
    private Dictionary<Object, Dictionary<string, HashSet<string>>> objectDependencies = 
          new Dictionary<Object, Dictionary<string, HashSet<string>>>();
    private Dictionary<string, bool> filterOptions = new Dictionary<string, bool>
    {
        {"mesh", true},
        {".fbx", true},
        {".mat", true},
        {"texture", true},
        {"shader", true},
        {".cs", true}
    };
    
    private GUIStyle matchingNameStyle;
    private GUIStyle nonMatchingNameStyle;

    private static readonly HashSet<string> TextureExtensions = new HashSet<string>
    {
        ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".psd", ".gif", ".hdr", ".exr", ".tif", ".tiff"
    };

    [MenuItem("Tools/美术工具/场景节点依赖检查工具")]
    private static void ShowWindow()
    {
        var window = GetWindow<PrefabAssetDependencyCheckTool>("场景节点依赖检查工具");
        window.Show();
    }

    private void OnEnable()
    {
        Selection.selectionChanged += Repaint;
        EditorApplication.delayCall += InitializeGUIStyles;
    }

    private void InitializeGUIStyles()
    {
        EditorApplication.delayCall -= InitializeGUIStyles;

        if (renamedLabelStyle == null)
        {
            renamedLabelStyle = new GUIStyle(EditorStyles.label)
            {
                normal = {textColor = new Color(1f, 0.2f, 0.2f)}
            };
        }

        if (matchingNameStyle == null)
        {
            matchingNameStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 14,
            };
        }

        if (nonMatchingNameStyle == null)
        {
            nonMatchingNameStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
            };
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.BeginVertical();
        GUILayout.Label("对象查找器", EditorStyles.boldLabel);
        DisplayObjectSourceFinder();

        EditorGUILayout.Space();
        
        GUILayout.Label("过滤选项", EditorStyles.boldLabel);
        DisplayDependencyExplorer();
        EditorGUILayout.EndVertical();
    }

    private void DisplayObjectSourceFinder()
    {
        if (renamedLabelStyle == null || matchingNameStyle == null || nonMatchingNameStyle == null)
        {
            InitializeGUIStyles();
        }

        EditorGUI.BeginChangeCheck();
        selectedObject = EditorGUILayout.ObjectField("被选物体", selectedObject, typeof(Object), true);
        if (EditorGUI.EndChangeCheck())
        {
            FindObjectSource();
        }

        if (selectedObject != null)
        {
            bool isRenamed = !string.IsNullOrEmpty(objectFileName) && selectedObject.name != objectFileName;
            EditorGUILayout.LabelField(isRenamed ? "场景内节点存在新命名" : "正常命名", selectedObject.name, isRenamed ? renamedLabelStyle : EditorStyles.label);

            if (!string.IsNullOrEmpty(objectFileName))
            {
                EditorGUILayout.TextField("源文件名", objectFileName);
            }
            if (!string.IsNullOrEmpty(objectSourcePath))
            {
                EditorGUILayout.TextField("路径", objectSourcePath);
                if (GUILayout.Button("定位文件"))
                {
                    EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(objectSourcePath));
                }
            }
            else
            {
                EditorGUILayout.HelpBox("该物体没有路径。", MessageType.Info);
            }
        }
        else
        {
            EditorGUILayout.HelpBox("从选择场景中的对象或预制件来查看其源文件。", MessageType.Info);
        }
    }

    private void DisplayDependencyExplorer()
    {
        EditorGUILayout.BeginHorizontal();
        List<string> copyOfFilterKeys = filterOptions.Keys.ToList();
        foreach (var filter in copyOfFilterKeys)
        {
            bool currentToggle = GUILayout.Toggle(filterOptions[filter], $"{filter.ToUpper()} 类型");
            if (filterOptions[filter] != currentToggle)
            {
                filterOptions[filter] = currentToggle;
            }
        }
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("查找选择的对象依赖", GUILayout.Height(30)))
        {
            FindDependenciesForSelectedObjects();
        }

        if (GUILayout.Button("清除", GUILayout.Height(20)))
        {
            objectDependencies.Clear();
        }

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        try
        {
            if (objectDependencies.Count == 0)
            {
                EditorGUILayout.HelpBox("没有显示的依赖。点击上面的按钮查找选中对象的依赖。", MessageType.Info);
            }
            else
            {
                DisplayDependencies();
            }
        }
        finally
        {
            EditorGUILayout.EndScrollView();
        }
    }

    private void DisplayDependencies()
    {
        if (renamedLabelStyle == null || matchingNameStyle == null || nonMatchingNameStyle == null)
        {
            InitializeGUIStyles();
        }

        float halfWidth = EditorGUIUtility.currentViewWidth / 2f - 10f;
        foreach (var objectEntry in objectDependencies)
        {
            Object parentObject = objectEntry.Key;
            if (parentObject != null)
            {
                EditorGUILayout.LabelField("选中对象名称: " + parentObject.name, EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
            }

            foreach (var dependencyEntry in objectEntry.Value)
            {
                string nodeName = dependencyEntry.Key;
                if (nonMatchingNameStyle != null)
                {
                    EditorGUILayout.LabelField("依赖节点: " + nodeName, nonMatchingNameStyle);
                }

                EditorGUI.indentLevel++;

                foreach (string dependencyPath in dependencyEntry.Value)
                {
                    string extension = Path.GetExtension(dependencyPath).ToLower();
                    if (ShouldDisplayDependency(extension))
                    {
                        EditorGUILayout.BeginHorizontal();
                        Object dependencyAsset = AssetDatabase.LoadAssetAtPath<Object>(dependencyPath);
                        EditorGUILayout.ObjectField(dependencyAsset, typeof(Object), false, GUILayout.Width(halfWidth));
                        EditorGUILayout.TextField(dependencyPath, GUILayout.Width(halfWidth));
                        EditorGUILayout.EndHorizontal();
                    }
                }

                EditorGUI.indentLevel--;
            }
            EditorGUI.indentLevel--;
            GUILayout.Space(10);
        }
    }

    private void FindObjectSource()
    {
        objectSourcePath = string.Empty;
        objectFileName = string.Empty;

        if (selectedObject == null) return;

        objectSourcePath = AssetDatabase.GetAssetPath(selectedObject);

        if (string.IsNullOrEmpty(objectSourcePath) && selectedObject is GameObject)
        {
            GameObject go = (GameObject)selectedObject;
            Object prefabSource = PrefabUtility.GetCorrespondingObjectFromSource(go);
            objectSourcePath = prefabSource != null ? AssetDatabase.GetAssetPath(prefabSource) : string.Empty;
        }

        if (!string.IsNullOrEmpty(objectSourcePath))
        {
            objectFileName = Path.GetFileNameWithoutExtension(objectSourcePath);
        }
    }

    private void FindDependenciesForSelectedObjects()
    {
        objectDependencies.Clear();
        foreach (var selectedObject in Selection.objects)
        {
            var path = AssetDatabase.GetAssetPath(selectedObject);
            if (!string.IsNullOrEmpty(path))
            {
                var dependencies = AssetDatabase.GetDependencies(path, true);
                var filteredDependencies = FilterDependencies(dependencies);

                if (filteredDependencies.Count > 0)
                {
                    objectDependencies[selectedObject] = filteredDependencies;
                }
            }
            else if (selectedObject is GameObject)
            {
                var leafDependencies = new Dictionary<string, HashSet<string>>();
                ProcessLeafGameObject(selectedObject as GameObject, leafDependencies);

                if (leafDependencies.Count > 0)
                {
                    objectDependencies[selectedObject] = leafDependencies;
                }
            }
        }
    }

    private Dictionary<string, HashSet<string>> FilterDependencies(string[] dependencies)
    {
        var filteredDependencies = new Dictionary<string, HashSet<string>>();
        foreach (string dependencyPath in dependencies)
        {
            string extension = Path.GetExtension(dependencyPath).ToLower();
            if (ShouldDisplayDependency(extension))
            {
                string fileName = Path.GetFileNameWithoutExtension(dependencyPath);
                if (!filteredDependencies.ContainsKey(fileName))
                {
                    filteredDependencies[fileName] = new HashSet<string>();
                }
                filteredDependencies[fileName].Add(dependencyPath);
            }
        }
        return filteredDependencies;
    }

    private bool ShouldDisplayDependency(string extension)
    {
        if (filterOptions.All(pair => !pair.Value))
            return true;

        if (filterOptions.ContainsKey("texture") && filterOptions["texture"] && TextureExtensions.Contains(extension))
            return true;

        return filterOptions.Any(pair => extension.EndsWith(pair.Key) && pair.Value);
    }

    private void ProcessLeafGameObject(GameObject gameObject, Dictionary<string, HashSet<string>> leafDependencies)
    {
        if (gameObject.transform.childCount == 0)
        {
            var dependenciesPaths = EditorUtility.CollectDependencies(new Object[] { gameObject })
                .Where(dep => dep != null && !(dep is GameObject) && !(dep is Component))
                .Select(AssetDatabase.GetAssetPath)
                .Distinct()
                .Where(path => !string.IsNullOrEmpty(path))
                .ToHashSet();

            if (dependenciesPaths.Count > 0)
            {
                leafDependencies[gameObject.name] = dependenciesPaths;
            }
        }
        else
        {
            foreach (Transform child in gameObject.transform)
            {
                ProcessLeafGameObject(child.gameObject, leafDependencies);
            }
        }
    }

    void OnSelectionChange()
    {
        selectedObject = Selection.activeObject;
        FindObjectSource();
        Repaint();
    }
}

