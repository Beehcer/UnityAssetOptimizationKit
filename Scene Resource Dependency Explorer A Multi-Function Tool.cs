// Beecher@gsus.cc
// 2024.01.23

// This code defines a Unity Editor window tool named "Scene Resource Dependency Lookup Multifunction," providing two main features to enhance content creation and management workflows.
// Object Finder: This tool allows users to select any object in a Unity scene and find its source file. If the object has been renamed in the scene, it will be displayed in a different style, indicating that the object and its source file no longer share the same name.
// Dependency Explorer: This feature allows users to explore and display all dependency assets of the selected object. A clear button is provided to clear the displayed list of dependencies.
// Overall, this tool greatly improves work efficiency in the art asset pipeline, especially in managing resources and analyzing asset dependencies. By precisely displaying the relationships between assets, it can help team members clarify and optimize asset structures, thereby reducing redundancy and improving the performance of the game.

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

    [MenuItem("Tools/Art Tools/Scene Node Dependency Check Tool")]
    private static void ShowWindow()
    {
        var window = GetWindow<PrefabAssetDependencyCheckTool>("Scene Node Dependency Check Tool");
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
        GUILayout.Label("Object Finder", EditorStyles.boldLabel);
        DisplayObjectSourceFinder();

        EditorGUILayout.Space();
        
        GUILayout.Label("Filter Options", EditorStyles.boldLabel);
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
        selectedObject = EditorGUILayout.ObjectField("Selected Object", selectedObject, typeof(Object), true);
        if (EditorGUI.EndChangeCheck())
        {
            FindObjectSource();
        }

        if (selectedObject != null)
        {
            bool isRenamed = !string.IsNullOrEmpty(objectFileName) && selectedObject.name != objectFileName;
            EditorGUILayout.LabelField(isRenamed ? "Scene object has been renamed" : "Normal Naming", selectedObject.name, isRenamed ? renamedLabelStyle : EditorStyles.label);

            if (!string.IsNullOrEmpty(objectFileName))
            {
                EditorGUILayout.TextField("Source File Name", objectFileName);
            }
            if (!string.IsNullOrEmpty(objectSourcePath))
            {
                EditorGUILayout.TextField("Path", objectSourcePath);
                if (GUILayout.Button("Locate File"))
                {
                    EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(objectSourcePath));
                }
            }
            else
            {
                EditorGUILayout.HelpBox("This object does not have a path.", MessageType.Info);
            }
        }
        else
        {
            EditorGUILayout.HelpBox("Select an object from the scene or a prefab to view its source file.", MessageType.Info);
        }
    }

    private void DisplayDependencyExplorer()
    {
        EditorGUILayout.BeginHorizontal();
        List<string> copyOfFilterKeys = filterOptions.Keys.ToList();
        foreach (var filter in copyOfFilterKeys)
        {
            bool currentToggle = GUILayout.Toggle(filterOptions[filter], $"{filter.ToUpper()} Type");
            if (filterOptions[filter] != currentToggle)
            {
                filterOptions[filter] = currentToggle;
            }
        }
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("Find Dependencies of the Selected Object", GUILayout.Height(30)))
        {
            FindDependenciesForSelectedObjects();
        }

        if (GUILayout.Button("Clear", GUILayout.Height(20)))
        {
            objectDependencies.Clear();
        }

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        try
        {
            if (objectDependencies.Count == 0)
            {
                EditorGUILayout.HelpBox("No dependencies displayed. Click the button above to find dependencies for the selected object.", MessageType.Info);
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
                EditorGUILayout.LabelField("Selected Object Name: " + parentObject.name, EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
            }

            foreach (var dependencyEntry in objectEntry.Value)
            {
                string nodeName = dependencyEntry.Key;
                if (nonMatchingNameStyle != null)
                {
                    EditorGUILayout.LabelField("Dependency Node: " + nodeName, nonMatchingNameStyle);
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
