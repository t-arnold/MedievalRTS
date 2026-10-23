using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// One-component test harness. Empty scene + this component + Play = field, player, enemies,
/// camera, debug overlay, duel tester. No art, prefabs, layers or assets required.
///
/// Hotkeys (number row):
///   1 overlay · 2 slow motion · 3 skirmish · 4 big battle · 5 god mode
///   6 start/stop duel series · 7/8 duel tier A/B · Shift+7/Shift+8 duel unit A/B
///   9 starting-enemy tier · 0 starting-enemy unit type · L unit labels · Q (player) switch weapon · R reset
/// </summary>
public class TestArena : MonoBehaviour
{
    [Header("Sword attacks (leave empty to use built-in defaults)")]
    public AttackData playerLight;
    public AttackData playerHeavy;
    public AttackData[] soldierAttacks;

    [Header("Units & weapons (leave empty to use built-in defaults)")]
    [Tooltip("Order: Swordsman, Axeman, Spearman, Archer, Light Cavalry, Heavy Cavalry (armyMix follows it).")]
    public UnitType[] unitTypes;
    [Tooltip("Weapons the player cycles with Q.")]
    public WeaponData[] playerWeapons;

    [Header("Experience tiers, lowest first (leave empty to use built-in defaults)")]
    public CombatSkill[] skillLadder;

    [Header("Field")]
    [Tooltip("Room for cavalry to wheel.")]
    public Vector2 fieldSize = new Vector2(52f, 32f);
    public int startingEnemies = 4;
    [Tooltip("Index into the skill ladder for the enemies you fight at the start (9 cycles it).")]
    public int startingEnemyTier = 2;
    [Tooltip("Index into Unit Types for the starting enemies (0 cycles it).")]
    public int startingEnemyUnit = 0;
    public int pillars = 6;

    [Header("Battles")]
    public int skirmishSize = 20;
    public int bigBattlePerSide = 100;
    [Tooltip("Share of each unit type in battles (same order as Unit Types).")]
    public float[] armyMix = { 25f, 15f, 20f, 25f, 8f, 7f };
    [Tooltip("Relative chance of each tier when battles spawn (same order as the ladder).")]
    public float[] battleTierWeights = { 40f, 30f, 18f, 9f, 3f, 0f };

    [Header("Player")]
    public float playerHealth = 120f;
    public int maxSimultaneousAttackersOnPlayer = 2;
    [Range(0f, 1f), Tooltip("Damage multiplier when your own soldiers hit you by accident.")]
    public float allyHitsOnPlayer = 0.3f;

    public static readonly Color PlayerColor = new Color(0.35f, 0.65f, 1f);
    public static readonly Color AllyColor = new Color(0.2f, 0.35f, 0.75f);
    public static readonly Color EnemyColor = new Color(0.85f, 0.25f, 0.2f);

    public CombatStateMachine Player { get; private set; }
    public bool PlayerInvulnerable { get; private set; }
    public CombatSkill[] SkillLadder => skillLadder;
    public UnitType[] UnitTypes => unitTypes;
    public string StartingEnemyTierName => skillLadder[startingEnemyTier].tierName;
    public string StartingEnemyUnitName => unitTypes[startingEnemyUnit].displayName;

    readonly List<GameObject> pillarObjects = new List<GameObject>();
    int spawnCounter;

    void Awake()
    {
        EnsureContent();
        CombatStateMachine.AllyHitsOnPlayerMultiplier = allyHitsOnPlayer;

        EnsureCamera();
        BuildField();

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
        var overlay = GetComponent<DebugOverlay>();
        bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;

        if (kb.digit1Key.wasPressedThisFrame) overlay.Toggle();
        if (kb.lKey.wasPressedThisFrame) overlay.CycleLabels();
        if (kb.digit2Key.wasPressedThisFrame && !duel.Running)
        {
            Time.timeScale = Time.timeScale < 1f ? 1f : 0.25f;   // debug only
            Time.fixedDeltaTime = 0.02f * Time.timeScale;
        }
        if (kb.digit3Key.wasPressedThisFrame && !duel.Running) SpawnBattle(skirmishSize);
        if (kb.digit4Key.wasPressedThisFrame && !duel.Running) SpawnBattle(bigBattlePerSide);
        if (kb.digit5Key.wasPressedThisFrame) PlayerInvulnerable ^= true;
        if (kb.digit6Key.wasPressedThisFrame) duel.Toggle();
        if (kb.digit7Key.wasPressedThisFrame) { if (shift) duel.CycleUnitA(); else duel.CycleA(); }
        if (kb.digit8Key.wasPressedThisFrame) { if (shift) duel.CycleUnitB(); else duel.CycleB(); }
        if (kb.digit9Key.wasPressedThisFrame && !duel.Running)
        {
            startingEnemyTier = (startingEnemyTier + 1) % skillLadder.Length;
            ClearNpcs();
            SpawnStartingEnemies();
        }
        if (kb.digit0Key.wasPressedThisFrame && !duel.Running)
        {
            startingEnemyUnit = (startingEnemyUnit + 1) % unitTypes.Length;
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

    // ================= Content =================

    void EnsureContent()
    {
        if (skillLadder == null || skillLadder.Length == 0) skillLadder = DefaultSkills.Ladder;
        startingEnemyTier = Mathf.Clamp(startingEnemyTier, 0, skillLadder.Length - 1);

        bool needUnits = unitTypes == null || unitTypes.Length == 0 || playerWeapons == null || playerWeapons.Length == 0;
        if (needUnits)
        {
            var set = DefaultUnits.Create(playerLight, playerHeavy, soldierAttacks);
            if (unitTypes == null || unitTypes.Length == 0) unitTypes = set.Units;
            if (playerWeapons == null || playerWeapons.Length == 0) playerWeapons = set.PlayerWeapons;
            Debug.Log("TestArena: using built-in units and weapons. Run Tools > Combat > Create Default Unit Assets to make tunable copies.");
        }
        startingEnemyUnit = Mathf.Clamp(startingEnemyUnit, 0, unitTypes.Length - 1);

        // Fallback melee list for any unit whose weapon has no AI attacks.
        if (soldierAttacks == null || soldierAttacks.Length == 0)
            soldierAttacks = unitTypes[0] && unitTypes[0].weapon ? unitTypes[0].weapon.aiAttacks : DefaultAttacks.Create().Soldier;
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

    // ================= Spawning =================

    CombatStateMachine SpawnPlayer(Vector2 pos)
    {
        var csm = CreateCombatant("Player", 0, pos, PlayerColor, playerHealth, 0.3f);
        csm.isPlayer = true;
        csm.Tokens.maxSimultaneousAttackers = maxSimultaneousAttackersOnPlayer;
        csm.gameObject.AddComponent<UnitLoadout>();
        var pc = csm.gameObject.AddComponent<PlayerController>();
        pc.combat = csm;
        pc.weapons = playerWeapons;
        pc.lightAttack = playerLight;
        pc.heavyAttack = playerHeavy;
        pc.Equip(0);
        return csm;
    }

    void SpawnStartingEnemies()
    {
        Vector2 centre = Player ? (Vector2)Player.transform.position : Vector2.zero;
        var type = unitTypes[startingEnemyUnit];
        float radius = type.mounted ? 10f : type.role == UnitRole.Archer ? 9f : 6f;
        for (int i = 0; i < startingEnemies; i++)
        {
            float a = (i / (float)startingEnemies) * Mathf.PI * 2f;
            Vector2 p = ClampToField(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, 2f);
            SpawnUnit(type, 1, p, EnemyColor, skillLadder[startingEnemyTier]);
        }
    }

    /// <summary>Spawns an AI unit of a given type. No skill given = random tier from battleTierWeights.</summary>
    public CombatStateMachine SpawnUnit(UnitType type, int team, Vector2 pos, Color color, CombatSkill skill = null)
    {
        string prefix = team == 0 ? "Ally_" : "Enemy_";
        var csm = CreateCombatant(prefix + type.shortName + "_" + (++spawnCounter), team, pos, color, type.health, type.bodyRadius);
        csm.Stats.maxPoise = type.poise;
        csm.Stats.maxStamina = type.stamina;
        csm.Stats.ResetStats();
        csm.knockbackResistance = type.knockbackResistance;
        csm.isMounted = type.mounted;
        csm.movesWhileAttacking = type.mounted;
        if (csm.Body) csm.Body.mass = type.mass;

        var lo = csm.gameObject.AddComponent<UnitLoadout>();
        lo.unit = type;
        lo.Equip(type.weapon);

        UnitBrain brain;
        if (type.mounted)
        {
            var cav = csm.gameObject.AddComponent<CavalryAI>();
            cav.Configure(type);
            cav.SetHeading(team == 0 ? Vector2.right : Vector2.left);
            brain = cav;
        }
        else
        {
            var ai = csm.gameObject.AddComponent<SoldierAI>();
            brain = ai;
        }
        brain.moveSpeed = type.moveSpeed * Random.Range(0.92f, 1.08f); // slight variety reads as more natural
        brain.attacks = soldierAttacks;
        brain.skill = skill ? skill : RandomBattleTier();

        var xp = csm.gameObject.AddComponent<CombatExperience>();
        xp.ladder = skillLadder;
        return csm;
    }

    /// <summary>Kept for older callers: spawns the first unit type (Swordsman).</summary>
    public CombatStateMachine SpawnSoldier(int team, Vector2 pos, Color color, CombatSkill skill = null) =>
        SpawnUnit(unitTypes[0], team, pos, color, skill);

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
    /// Two combined-arms armies: spears in the front ranks, other foot behind them, archers at the back,
    /// cavalry on both wings. Allies on the left, enemies on the right. The player is placed between the
    /// allied foot and the archers.
    /// </summary>
    void SpawnBattle(int perSide)
    {
        int[] counts = Composition(perSide);
        float alliedDepth = SpawnArmy(0, counts);
        SpawnArmy(1, counts);

        if (Player && Player.State != CombatState.Dead)
        {
            Vector2 behind = ClampToField(new Vector2(-(2f + alliedDepth + 1.3f), 0f), 1f);
            if (Player.Body) Player.Body.position = behind;
            Player.transform.position = behind;
        }
    }

    /// <summary>Splits a head count across unit types by armyMix (largest remainder).</summary>
    int[] Composition(int total)
    {
        int n = unitTypes.Length;
        var counts = new int[n];
        float sum = 0f;
        for (int i = 0; i < n; i++) sum += i < armyMix.Length ? Mathf.Max(0f, armyMix[i]) : 0f;
        if (sum <= 0f) { counts[0] = total; return counts; }

        var remainders = new float[n];
        int assigned = 0;
        for (int i = 0; i < n; i++)
        {
            float exact = total * (i < armyMix.Length ? Mathf.Max(0f, armyMix[i]) : 0f) / sum;
            counts[i] = Mathf.FloorToInt(exact);
            remainders[i] = exact - counts[i];
            assigned += counts[i];
        }
        while (assigned < total)
        {
            int best = 0;
            for (int i = 1; i < n; i++) if (remainders[i] > remainders[best]) best = i;
            counts[best]++; remainders[best] = -1f; assigned++;
        }
        return counts;
    }

    /// <summary>Returns the depth (x extent) of the foot block, so the player can stand behind it.</summary>
    float SpawnArmy(int team, int[] counts)
    {
        float side = team == 0 ? -1f : 1f;
        Color color = team == 0 ? AllyColor : EnemyColor;

        var spears = new List<UnitType>();
        var foot = new List<UnitType>();
        var archers = new List<UnitType>();
        var cavalry = new List<UnitType>();
        for (int i = 0; i < unitTypes.Length; i++)
        {
            var t = unitTypes[i];
            if (!t) continue;
            for (int k = 0; k < counts[i]; k++)
            {
                if (t.mounted) cavalry.Add(t);
                else if (t.role == UnitRole.Archer || (t.weapon && t.weapon.IsRanged)) archers.Add(t);
                else if (t.weapon && t.weapon.bracesVsCavalry) spears.Add(t);
                else foot.Add(t);
            }
        }
        Shuffle(foot);
        var melee = new List<UnitType>(spears);
        melee.AddRange(foot);

        const float spacing = 0.85f, gap = 2f;
        int maxRows = Mathf.Max(4, Mathf.FloorToInt((fieldSize.y - 10f) / spacing));
        float meleeDepth = PlaceBlock(melee, team, side, gap, maxRows, spacing, color, out int meleeRows);
        PlaceBlock(archers, team, side, gap + meleeDepth + 2.6f, maxRows, spacing * 1.1f, color, out _);

        float wingY = Mathf.Max(3f, meleeRows * spacing * 0.5f + 2.5f);
        for (int i = 0; i < cavalry.Count; i++)
        {
            float wing = i % 2 == 0 ? 1f : -1f;
            int idx = i / 2;
            float x = side * (gap + 1f + (idx % 3) * 1.5f);
            float y = wing * (wingY + (idx / 3) * 1.5f);
            SpawnUnit(cavalry[i], team, ClampToField(new Vector2(x, y), 2f), color);
        }
        return meleeDepth;
    }

    float PlaceBlock(List<UnitType> units, int team, float side, float startX, int maxRows, float spacing, Color color, out int rows)
    {
        rows = 0;
        if (units.Count == 0) return 0f;
        rows = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(units.Count * 3f)), 1, maxRows); // wider than deep
        int cols = Mathf.CeilToInt(units.Count / (float)rows);
        for (int i = 0; i < units.Count; i++)
        {
            int col = i / rows, row = i % rows;
            float y = (row - (rows - 1) / 2f) * spacing;
            float x = side * (startX + col * spacing);
            SpawnUnit(units[i], team, ClampToField(new Vector2(x, y), 1.5f), color);
        }
        return (cols - 1) * spacing;
    }

    static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    Vector2 ClampToField(Vector2 p, float margin)
    {
        Vector2 h = fieldSize / 2f - Vector2.one * margin;
        return new Vector2(Mathf.Clamp(p.x, -h.x, h.x), Mathf.Clamp(p.y, -h.y, h.y));
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
    public static CombatStateMachine CreateCombatant(string name, int team, Vector2 pos, Color color, float health, float radius = 0.3f)
    {
        var go = new GameObject(name);
        go.transform.position = pos;

        var rb = go.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        var col = go.AddComponent<CircleCollider2D>();
        col.radius = radius; // feet footprint; also the hurtbox for now

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

    // ================= Field =================

    void BuildField()
    {
        var root = new GameObject("Arena").transform;

        var ground = PlaceholderArt.MakeRenderer(root, "Ground", PlaceholderArt.Square, new Color(0.22f, 0.2f, 0.16f), fieldSize);
        ground.sortingOrder = -32000;

        Vector2 h = fieldSize / 2f;
        const float t = 1f;
        Wall(root, new Vector2(0, h.y + t / 2), new Vector2(fieldSize.x + 2 * t, t));
        Wall(root, new Vector2(0, -h.y - t / 2), new Vector2(fieldSize.x + 2 * t, t));
        Wall(root, new Vector2(h.x + t / 2, 0), new Vector2(t, fieldSize.y));
        Wall(root, new Vector2(-h.x - t / 2, 0), new Vector2(t, fieldSize.y));

        var rng = new System.Random(7); // same layout every run
        for (int i = 0; i < pillars; i++)
        {
            Vector2 p;
            do
            {
                p = new Vector2((float)(rng.NextDouble() * 2 - 1) * (h.x - 3), (float)(rng.NextDouble() * 2 - 1) * (h.y - 3));
            } while (p.magnitude < 6f); // keep the centre clear
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
