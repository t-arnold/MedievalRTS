using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// One-component test harness. Empty scene + this component + Play = arena, player, enemies,
/// camera, debug overlay. No art, prefabs, layers or assets required.
///
/// Hotkeys (number row, so no fn key needed on a Mac):
///   1 = toggle debug overlay     2 = slow motion (25%)      3 = spawn skirmish (allies vs enemies)
///   4 = spawn big battle (stress test)   5 = toggle player invulnerable   R = reset arena
/// </summary>
public class TestArena : MonoBehaviour
{
    [Header("Attacks (leave empty to use built-in defaults)")]
    public AttackData playerLight;
    public AttackData playerHeavy;
    public AttackData[] soldierAttacks;

    [Header("Arena")]
    public Vector2 arenaSize = new Vector2(30f, 20f);
    public int startingEnemies = 4;
    public int pillars = 6;

    [Header("Spawning")]
    public int skirmishPerSide = 12;
    public int bigBattlePerSide = 100;

    [Header("Player")]
    public float playerHealth = 120f;
    public int maxSimultaneousAttackersOnPlayer = 2;

    public static readonly Color PlayerColor = new Color(0.35f, 0.65f, 1f);
    public static readonly Color AllyColor = new Color(0.2f, 0.35f, 0.75f);
    public static readonly Color EnemyColor = new Color(0.85f, 0.25f, 0.2f);

    public CombatStateMachine Player { get; private set; }
    public bool PlayerInvulnerable { get; private set; }
    int spawnCounter;

    void Awake()
    {
        EnsureAttacks();
        EnsureCamera();
        BuildArena();

        Player = SpawnPlayer(Vector2.zero);
        Camera.main.GetComponent<CameraRig2D>().Follow(Player);

        for (int i = 0; i < startingEnemies; i++)
        {
            float a = (i / (float)startingEnemies) * Mathf.PI * 2f;
            SpawnSoldier(1, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 6f, EnemyColor);
        }

        if (!GetComponent<DebugOverlay>()) gameObject.AddComponent<DebugOverlay>();
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.digit1Key.wasPressedThisFrame) GetComponent<DebugOverlay>().show ^= true;
        if (kb.digit2Key.wasPressedThisFrame)
        {
            Time.timeScale = Time.timeScale < 1f ? 1f : 0.25f;   // debug only
            Time.fixedDeltaTime = 0.02f * Time.timeScale;
        }
        if (kb.digit3Key.wasPressedThisFrame) SpawnBattle(skirmishPerSide, 4f);
        if (kb.digit4Key.wasPressedThisFrame) SpawnBattle(bigBattlePerSide, 3f);
        if (kb.digit5Key.wasPressedThisFrame) PlayerInvulnerable ^= true;
        if (kb.rKey.wasPressedThisFrame)
        {
            Time.timeScale = 1f; Time.fixedDeltaTime = 0.02f;
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

    public CombatStateMachine SpawnSoldier(int team, Vector2 pos, Color color)
    {
        var csm = CreateCombatant((team == 0 ? "Ally_" : "Enemy_") + (++spawnCounter), team, pos, color, 60f);
        csm.Stats.maxPoise = 40f;
        csm.Stats.ResetStats();
        var ai = csm.gameObject.AddComponent<SoldierAI>();
        ai.attacks = soldierAttacks;
        ai.moveSpeed = Random.Range(2.5f, 3.1f); // slight variety reads as more natural
        return csm;
    }

    void SpawnBattle(int perSide, float gapFromPlayer)
    {
        Vector2 centre = Player ? (Vector2)Player.transform.position : Vector2.zero;
        int cols = Mathf.CeilToInt(Mathf.Sqrt(perSide));
        const float spacing = 0.85f;
        for (int i = 0; i < perSide; i++)
        {
            Vector2 cell = new Vector2((i / cols) * spacing, (i % cols - cols / 2f) * spacing);
            SpawnSoldier(0, centre + new Vector2(-gapFromPlayer, 0f) - cell, AllyColor);
            SpawnSoldier(1, centre + new Vector2(gapFromPlayer, 0f) + cell, EnemyColor);
        }
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
            Wall(root, p, new Vector2(1.2f, 1.2f));
        }
    }

    static void Wall(Transform parent, Vector2 pos, Vector2 size)
    {
        var sr = PlaceholderArt.MakeRenderer(parent, "Wall", PlaceholderArt.Square, new Color(0.45f, 0.42f, 0.38f), size);
        sr.transform.position = pos;
        sr.sortingOrder = -Mathf.RoundToInt((pos.y - size.y / 2f) * 100f);
        var box = sr.gameObject.AddComponent<BoxCollider2D>();
        box.size = Vector2.one; // scaled by the transform
    }
}
