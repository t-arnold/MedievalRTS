using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// One-component test harness. Empty scene + this component + Play = arena, player, enemies,
/// camera, debug overlay, duel tester. No art, prefabs, layers or assets required.
///
/// Hotkeys (number row):
///   1 overlay · 2 slow motion · 3 skirmish · 4 big battle · 5 god mode
///   6 start/stop duel series · 7 duel tier A · 8 duel tier B · 9 change starting-enemy tier · R reset
/// </summary>
public class TestArena : MonoBehaviour
{
    [Header("Attacks (leave empty to use built-in defaults)")]
    public AttackData playerLight;
    public AttackData playerHeavy;
    public AttackData[] soldierAttacks;

    [Header("Experience tiers, lowest first (leave empty to use built-in defaults)")]
    public CombatSkill[] skillLadder;

    [Header("Arena")]
    public Vector2 arenaSize = new Vector2(30f, 20f);
    public int startingEnemies = 4;
    [Tooltip("Index into the skill ladder for the enemies you fight at the start (9 cycles it).")]
    public int startingEnemyTier = 2;
    public int pillars = 6;

    [Header("Battles")]
    public int skirmishPerSide = 12;
    public int bigBattlePerSide = 100;
    [Tooltip("Relative chance of each tier when battles spawn (same order as the ladder).")]
    public float[] battleTierWeights = { 40f, 30f, 18f, 9f, 3f, 0f };

    [Header("Player")]
    public float playerHealth = 120f;
    public int maxSimultaneousAttackersOnPlayer = 2;

    public static readonly Color PlayerColor = new Color(0.35f, 0.65f, 1f);
    public static readonly Color AllyColor = new Color(0.2f, 0.35f, 0.75f);
    public static readonly Color EnemyColor = new Color(0.85f, 0.25f, 0.2f);

    public CombatStateMachine Player { get; private set; }
    public bool PlayerInvulnerable { get; private set; }
    public CombatSkill[] SkillLadder => skillLadder;
    public string StartingEnemyTierName => skillLadder[startingEnemyTier].tierName;

    readonly List<GameObject> pillarObjects = new List<GameObject>();
    int spawnCounter;

    void Awake()
    {
        EnsureAttacks();
        if (skillLadder == null || skillLadder.Length == 0) skillLadder = DefaultSkills.Ladder;
        startingEnemyTier = Mathf.Clamp(startingEnemyTier, 0, skillLadder.Length - 1);

        EnsureCamera();
        BuildArena();

        Player = SpawnPlayer(Vector2.zero);
        Camera.main.GetComponent<CameraRig2D>().Follow(Player);
        SpawnStartingEnemies();

        if (!GetComponent<DebugOverlay>()) gameObject.AddComponent<DebugOverlay>();
        if (!GetComponent<DuelTester>()) gameObject.AddComponent<DuelTester>();
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        var duel = GetComponent<DuelTester>();

        if (kb.digit1Key.wasPressedThisFrame) GetComponent<DebugOverlay>().show ^= true;
        if (kb.digit2Key.wasPressedThisFrame && !duel.Running)
        {
            Time.timeScale = Time.timeScale < 1f ? 1f : 0.25f;   // debug only
            Time.fixedDeltaTime = 0.02f * Time.timeScale;
        }
        if (kb.digit3Key.wasPressedThisFrame && !duel.Running) SpawnBattle(skirmishPerSide);
        if (kb.digit4Key.wasPressedThisFrame && !duel.Running) SpawnBattle(bigBattlePerSide);
        if (kb.digit5Key.wasPressedThisFrame) PlayerInvulnerable ^= true;
        if (kb.digit6Key.wasPressedThisFrame) duel.Toggle();
        if (kb.digit7Key.wasPressedThisFrame) duel.CycleA();
        if (kb.digit8Key.wasPressedThisFrame) duel.CycleB();
        if (kb.digit9Key.wasPressedThisFrame && !duel.Running)
        {
            startingEnemyTier = (startingEnemyTier + 1) % skillLadder.Length;
            ClearNpcs();
            SpawnStartingEnemies();
        }
        if (kb.rKey.wasPressedThisFrame)
        {
            Time.timeScale = 1f; Time.fixedDeltaTime = 0.02f;
            CombatExperience.GainEnabled = true;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        if (PlayerInvulnerable && Player) Player.Stats.Heal(9999f);
    }

    // ================= Spawning =================

    void EnsureAttacks()
    {
        bool needDefaults = !playerLight || !playerHeavy || soldierAttacks == null || soldierAttacks.Length == 0;
        if (!needDefaults) return;
        var d = DefaultAttacks.Create();
        if (!playerLight) playerLight = d.playerLight1;
        if (!playerHeavy) playerHeavy = d.playerHeavy;
        if (soldierAttacks == null || soldierAttacks.Length == 0) soldierAttacks = d.Soldier;
        Debug.Log("TestArena: using built-in default attacks. Run Tools > Combat > Create Default Attack Assets to make tunable copies.");
    }

    void EnsureCamera()
    {
        var cam = Camera.main;
        if (!cam)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            cam = go.AddComponent<Camera>();
            go.transform.position = new Vector3(0f, 0f, -10f);
        }
        cam.backgroundColor = new Color(0.08f, 0.08f, 0.1f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        if (!cam.GetComponent<CameraRig2D>()) cam.gameObject.AddComponent<CameraRig2D>();
    }

    CombatStateMachine SpawnPlayer(Vector2 pos)
    {
        var csm = CreateCombatant("Player", 0, pos, PlayerColor, playerHealth);
        csm.GetComponent<AttackTokens>().maxSimultaneousAttackers = maxSimultaneousAttackersOnPlayer;
        var pc = csm.gameObject.AddComponent<PlayerController>();
        pc.combat = csm;
        pc.lightAttack = playerLight;
        pc.heavyAttack = playerHeavy;
        return csm;
    }

    void SpawnStartingEnemies()
    {
        Vector2 centre = Player ? (Vector2)Player.transform.position : Vector2.zero;
        for (int i = 0; i < startingEnemies; i++)
        {
            float a = (i / (float)startingEnemies) * Mathf.PI * 2f;
            SpawnSoldier(1, centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 6f, EnemyColor, skillLadder[startingEnemyTier]);
        }
    }

    /// <summary>Spawns an AI soldier. No skill given = random tier from battleTierWeights.</summary>
    public CombatStateMachine SpawnSoldier(int team, Vector2 pos, Color color, CombatSkill skill = null)
    {
        var csm = CreateCombatant((team == 0 ? "Ally_" : "Enemy_") + (++spawnCounter), team, pos, color, 60f);
        csm.Stats.maxPoise = 40f;
        csm.Stats.ResetStats();

        var ai = csm.gameObject.AddComponent<SoldierAI>();
        ai.attacks = soldierAttacks;
        ai.skill = skill ? skill : RandomBattleTier();
        ai.moveSpeed = Random.Range(2.5f, 3.1f); // slight variety reads as more natural

        var xp = csm.gameObject.AddComponent<CombatExperience>();
        xp.ladder = skillLadder;
        return csm;
    }

    CombatSkill RandomBattleTier()
    {
        float total = 0f;
        int n = Mathf.Min(skillLadder.Length, battleTierWeights.Length);
        for (int i = 0; i < n; i++) total += Mathf.Max(0f, battleTierWeights[i]);
        if (total <= 0f) return skillLadder[0];
        float roll = Random.value * total;
        for (int i = 0; i < n; i++)
        {
            roll -= Mathf.Max(0f, battleTierWeights[i]);
            if (roll <= 0f) return skillLadder[i];
        }
        return skillLadder[0];
    }

    /// <summary>
    /// Two armies facing each other across the arena centre, allies on the left, enemies on the right.
    /// The player is moved BEHIND the allied line, so enemies meet your soldiers first.
    /// </summary>
    void SpawnBattle(int perSide)
    {
        const float spacing = 0.85f, gap = 1.5f;
        int rows = Mathf.CeilToInt(Mathf.Sqrt(perSide));
        int cols = Mathf.CeilToInt(perSide / (float)rows);
        float depth = (cols - 1) * spacing;

        for (int i = 0; i < perSide; i++)
        {
            int col = i / rows, row = i % rows;
            float y = (row - (rows - 1) / 2f) * spacing;
            float x = gap + col * spacing;
            SpawnSoldier(0, new Vector2(-x, y), AllyColor);
            SpawnSoldier(1, new Vector2(x, y), EnemyColor);
        }

        if (Player && Player.State != CombatState.Dead)
        {
            var rb = Player.GetComponent<Rigidbody2D>();
            Vector2 behind = new Vector2(Mathf.Max(-arenaSize.x / 2f + 1f, -(gap + depth + 2.5f)), 0f);
            rb.position = behind;
            Player.transform.position = behind;
        }
    }

    /// <summary>Removes every combatant except the player.</summary>
    public void ClearNpcs()
    {
        var all = new List<CombatStateMachine>(CombatStateMachine.All);
        foreach (var c in all)
            if (c && c != Player) Destroy(c.gameObject);
    }

    public void SetPillarsActive(bool active)
    {
        foreach (var p in pillarObjects) if (p) p.SetActive(active);
    }

    /// <summary>Builds a combatant from code: physics body, feet collider, stats, combat, tokens, placeholder visual.</summary>
    public static CombatStateMachine CreateCombatant(string name, int team, Vector2 pos, Color color, float health)
    {
        var go = new GameObject(name);
        go.transform.position = pos;

        var rb = go.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        var col = go.AddComponent<CircleCollider2D>();
        col.radius = 0.3f; // feet footprint; also the hurtbox for now

        var stats = go.AddComponent<CombatStats>();
        stats.maxHealth = health;
        stats.ResetStats();

        var csm = go.AddComponent<CombatStateMachine>(); // no Animator yet: animation calls are skipped safely
        csm.team = team;

        go.AddComponent<AttackTokens>();

        var vis = go.AddComponent<PlaceholderVisual>();
        vis.SetTeamColor(color);
        return csm;
    }

    // ================= Arena =================

    void BuildArena()
    {
        var root = new GameObject("Arena").transform;

        var ground = PlaceholderArt.MakeRenderer(root, "Ground", PlaceholderArt.Square, new Color(0.22f, 0.2f, 0.16f), arenaSize);
        ground.sortingOrder = -32000;

        Vector2 h = arenaSize / 2f;
        const float t = 1f;
        Wall(root, new Vector2(0, h.y + t / 2), new Vector2(arenaSize.x + 2 * t, t));
        Wall(root, new Vector2(0, -h.y - t / 2), new Vector2(arenaSize.x + 2 * t, t));
        Wall(root, new Vector2(h.x + t / 2, 0), new Vector2(t, arenaSize.y));
        Wall(root, new Vector2(-h.x - t / 2, 0), new Vector2(t, arenaSize.y));

        var rng = new System.Random(7); // same layout every run
        for (int i = 0; i < pillars; i++)
        {
            Vector2 p;
            do
            {
                p = new Vector2((float)(rng.NextDouble() * 2 - 1) * (h.x - 3), (float)(rng.NextDouble() * 2 - 1) * (h.y - 3));
            } while (p.magnitude < 4f); // keep the centre clear
            pillarObjects.Add(Wall(root, p, new Vector2(1.2f, 1.2f)));
        }
    }

    static GameObject Wall(Transform parent, Vector2 pos, Vector2 size)
    {
        var sr = PlaceholderArt.MakeRenderer(parent, "Wall", PlaceholderArt.Square, new Color(0.45f, 0.42f, 0.38f), size);
        sr.transform.position = pos;
        sr.sortingOrder = -Mathf.RoundToInt((pos.y - size.y / 2f) * 100f);
        var box = sr.gameObject.AddComponent<BoxCollider2D>();
        box.size = Vector2.one; // scaled by the transform
        return sr.gameObject;
    }
}
