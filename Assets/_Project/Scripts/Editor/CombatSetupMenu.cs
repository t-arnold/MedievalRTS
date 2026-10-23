#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor menu helpers. MUST live in a folder named "Editor" (Assets/_Project/Scripts/Editor/).
///   Tools > Combat > Create Test Arena In Scene
///   Tools > Combat > Create Default Attack Assets   (sword attacks)
///   Tools > Combat > Create Default Skill Tiers
///   Tools > Combat > Create Default Unit Assets     (arrows, axe/spear/bow/cavalry attacks, weapons, unit types)
/// None of these overwrite assets you've already tuned: existing files are kept and reused.
/// </summary>
public static class CombatSetupMenu
{
    const string AttackFolder = "Assets/_Project/Data/Attacks";
    const string SkillFolder = "Assets/_Project/Data/Skills";
    const string ProjectileFolder = "Assets/_Project/Data/Projectiles";
    const string WeaponFolder = "Assets/_Project/Data/Weapons";
    const string UnitFolder = "Assets/_Project/Data/Units";

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
        EnsureFolder(AttackFolder);
        var defaults = DefaultAttacks.Create();

        var light1 = Persist(defaults.playerLight1, AttackFolder);
        var light2 = Persist(defaults.playerLight2, AttackFolder);
        var heavy = Persist(defaults.playerHeavy, AttackFolder);
        var slash = Persist(defaults.soldierSlash, AttackFolder);
        var thrust = Persist(defaults.soldierThrust, AttackFolder);
        var overhead = Persist(defaults.soldierOverhead, AttackFolder);

        if (light1.nextInCombo != light2) { light1.nextInCombo = light2; EditorUtility.SetDirty(light1); }
        AssetDatabase.SaveAssets();

        var arena = Object.FindFirstObjectByType<TestArena>();
        if (arena)
        {
            Undo.RecordObject(arena, "Assign Attacks");
            arena.playerLight = light1;
            arena.playerHeavy = heavy;
            arena.soldierAttacks = new[] { slash, thrust, overhead };
            MarkDirty(arena);
            Debug.Log("Attack assets created and assigned to TestArena.");
        }
        else Debug.Log($"Attack assets created in {AttackFolder}. Add a TestArena and run this again to auto-assign.");

        Ping(light1);
    }

    [MenuItem("Tools/Combat/Create Default Skill Tiers")]
    static void CreateSkillAssets()
    {
        EnsureFolder(SkillFolder);
        var defaults = DefaultSkills.Create();
        var saved = new CombatSkill[defaults.Length];

        for (int i = 0; i < defaults.Length; i++)
        {
            defaults[i].name = $"{i}_{defaults[i].tierName}";
            saved[i] = Persist(defaults[i], SkillFolder);
        }
        AssetDatabase.SaveAssets();

        var arena = Object.FindFirstObjectByType<TestArena>();
        if (arena)
        {
            Undo.RecordObject(arena, "Assign Skill Tiers");
            arena.skillLadder = saved;
            MarkDirty(arena);
            Debug.Log("Skill tier assets created and assigned to TestArena.");
        }
        else Debug.Log($"Skill tier assets created in {SkillFolder}. Add a TestArena and run this again to auto-assign.");

        Ping(saved[0]);
    }

    [MenuItem("Tools/Combat/Create Default Unit Assets")]
    static void CreateUnitAssets()
    {
        EnsureFolder(AttackFolder);
        EnsureFolder(ProjectileFolder);
        EnsureFolder(WeaponFolder);
        EnsureFolder(UnitFolder);

        // Reuse the sword attacks already assigned to the arena, so your tuning carries over.
        var arena = Object.FindFirstObjectByType<TestArena>();
        var set = arena
            ? DefaultUnits.Create(arena.playerLight, arena.playerHeavy, arena.soldierAttacks)
            : DefaultUnits.Create();

        // Save leaves first, then the things that reference them, remapping to the saved copies.
        var arrow = Persist(set.arrow, ProjectileFolder);

        var attackMap = new Dictionary<AttackData, AttackData>();
        foreach (var a in set.attacks)
        {
            if (!a || attackMap.ContainsKey(a)) continue;
            if (a.projectile) a.projectile = arrow;
            attackMap[a] = Persist(a, AttackFolder);
        }
        AttackData MapA(AttackData a) => a && attackMap.TryGetValue(a, out var m) ? m : a;
        foreach (var saved in attackMap.Values)
        {
            var mapped = MapA(saved.nextInCombo);
            if (mapped != saved.nextInCombo) { saved.nextInCombo = mapped; EditorUtility.SetDirty(saved); }
        }

        var weaponMap = new Dictionary<WeaponData, WeaponData>();
        foreach (var w in set.Weapons)
        {
            if (!w) continue;
            if (w.aiAttacks != null) for (int i = 0; i < w.aiAttacks.Length; i++) w.aiAttacks[i] = MapA(w.aiAttacks[i]);
            w.playerLight = MapA(w.playerLight);
            w.playerHeavy = MapA(w.playerHeavy);
            w.rangedAttack = MapA(w.rangedAttack);
            w.braceAttack = MapA(w.braceAttack);
            weaponMap[w] = Persist(w, WeaponFolder);
        }
        WeaponData MapW(WeaponData w) => w && weaponMap.TryGetValue(w, out var m) ? m : w;

        var units = set.Units;
        for (int i = 0; i < units.Length; i++)
        {
            units[i].weapon = MapW(units[i].weapon);
            units[i] = Persist(units[i], UnitFolder);
        }
        var playerWeapons = set.PlayerWeapons;
        for (int i = 0; i < playerWeapons.Length; i++) playerWeapons[i] = MapW(playerWeapons[i]);

        AssetDatabase.SaveAssets();

        if (arena)
        {
            Undo.RecordObject(arena, "Assign Units");
            arena.unitTypes = units;
            arena.playerWeapons = playerWeapons;
            MarkDirty(arena);
            Debug.Log("Unit, weapon, attack and projectile assets created and assigned to TestArena.");
        }
        else Debug.Log($"Unit assets created in {UnitFolder}. Add a TestArena and run this again to auto-assign.");

        Ping(units[0]);
    }

    // ================= Helpers =================

    /// <summary>Saves a new asset, or returns the one already on disk with the same name (keeps your tuning).</summary>
    static T Persist<T>(T obj, string folder) where T : ScriptableObject
    {
        if (!obj) return null;
        if (AssetDatabase.Contains(obj)) return obj;
        string path = $"{folder}/{obj.name}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing) return existing;
        AssetDatabase.CreateAsset(obj, path);
        return obj;
    }

    static void MarkDirty(TestArena arena)
    {
        EditorUtility.SetDirty(arena);
        EditorSceneManager.MarkSceneDirty(arena.gameObject.scene);
    }

    static void Ping(Object o)
    {
        if (!o) return;
        Selection.activeObject = o;
        EditorGUIUtility.PingObject(o);
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
