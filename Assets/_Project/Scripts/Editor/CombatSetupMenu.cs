#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor menu helpers. MUST live in a folder named "Editor" (e.g. Assets/_Project/Scripts/Editor/).
///   Tools > Combat > Create Test Arena In Scene
///   Tools > Combat > Create Default Attack Assets  (never overwrites assets you've already tuned)
/// </summary>
public static class CombatSetupMenu
{
    const string Folder = "Assets/_Project/Data/Attacks";

    [MenuItem("Tools/Combat/Create Test Arena In Scene")]
    static void CreateArena()
    {
        var existing = Object.FindFirstObjectByType<TestArena>();
        if (existing) { Selection.activeObject = existing; Debug.Log("TestArena already in scene."); return; }

        var go = new GameObject("TestArena");
        go.AddComponent<TestArena>();
        Undo.RegisterCreatedObjectUndo(go, "Create Test Arena");
        Selection.activeObject = go;
        EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("TestArena created. Press Play.");
    }

    [MenuItem("Tools/Combat/Create Default Attack Assets")]
    static void CreateAttackAssets()
    {
        EnsureFolder(Folder);
        var defaults = DefaultAttacks.Create();

        AttackData Save(AttackData a)
        {
            string path = $"{Folder}/{a.name}.asset";
            var onDisk = AssetDatabase.LoadAssetAtPath<AttackData>(path);
            if (onDisk) return onDisk; // keep the user's tuning
            var copy = Object.Instantiate(a);
            copy.name = a.name;
            copy.nextInCombo = null; // re-linked below to the saved asset
            AssetDatabase.CreateAsset(copy, path);
            return copy;
        }

        var light1 = Save(defaults.playerLight1);
        var light2 = Save(defaults.playerLight2);
        var heavy = Save(defaults.playerHeavy);
        var slash = Save(defaults.soldierSlash);
        var thrust = Save(defaults.soldierThrust);
        var overhead = Save(defaults.soldierOverhead);

        if (!light1.nextInCombo) { light1.nextInCombo = light2; EditorUtility.SetDirty(light1); }
        AssetDatabase.SaveAssets();

        var arena = Object.FindFirstObjectByType<TestArena>();
        if (arena)
        {
            Undo.RecordObject(arena, "Assign Attacks");
            arena.playerLight = light1;
            arena.playerHeavy = heavy;
            arena.soldierAttacks = new[] { slash, thrust, overhead };
            EditorUtility.SetDirty(arena);
            EditorSceneManager.MarkSceneDirty(arena.gameObject.scene);
            Debug.Log("Attack assets created and assigned to TestArena.");
        }
        else
        {
            Debug.Log($"Attack assets created in {Folder}. Add a TestArena and run this again to auto-assign.");
        }

        Selection.activeObject = light1;
        EditorGUIUtility.PingObject(light1);
    }

    static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0]; // "Assets"
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
#endif
