using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// On-screen debug info (IMGUI, no setup): FPS, unit counts, controls, the player's weapon and
/// health/stamina/poise/state, starting enemies, duel tester status and history, a combat log
/// of hits involving the player, and bars + labels over nearby units.
/// Added automatically by TestArena. Toggle with 1; L cycles unit labels (off / near mouse / all).
/// </summary>
public class DebugOverlay : MonoBehaviour
{
    public enum LabelMode { Off, NearMouse, All }

    public bool show = true;
    public LabelMode labels = LabelMode.NearMouse;
    public int maxUnitBars = 80;
    [Tooltip("Hide the controls help after this many seconds (press 1 twice to see it again).")]
    public float helpSeconds = 20f;

    TestArena arena;
    DuelTester duel;
    float fps = 60f;
    float shownAt;
    GUIStyle label, small;
    readonly Queue<string> log = new Queue<string>();
    readonly List<string> lines = new List<string>();
    CombatStateMachine logged;

    void Awake() => arena = GetComponent<TestArena>();
    void OnEnable() => shownAt = Time.unscaledTime;

    public void CycleLabels() => labels = (LabelMode)(((int)labels + 1) % 3);

    /// <summary>Show/hide. Showing again brings the controls help back for a while.</summary>
    public void Toggle()
    {
        show = !show;
        if (show) shownAt = Time.unscaledTime;
    }

    void Update()
    {
        fps = Mathf.Lerp(fps, 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime), 0.05f);
        if (!duel) duel = GetComponent<DuelTester>();

        // Hook the combat log onto the current player (re-hooks after a reset).
        var p = arena ? arena.Player : null;
        if (p && p != logged)
        {
            logged = p;
            p.OnHitLanded += r => Log($"You → {r.defender.name} [{Describe(r.defender)}] ({r.attack.name}): {r.type}");
            p.OnHitReceived += r => Log($"{(r.attacker ? r.attacker.name : "?")} [{Describe(r.attacker)}] → you ({r.attack.name}): {r.type}");
        }
    }

    static string Describe(CombatStateMachine c)
    {
        if (!c) return "?";
        var lo = c.Loadout;
        var brain = c.GetComponent<UnitBrain>();
        string s = lo ? lo.Label : "";
        if (brain && brain.skill) s = (s.Length > 0 ? s + " " : "") + brain.skill.tierName;
        if (lo && lo.weapon && lo.weapon.IsRanged && lo.ammo >= 0) s += $" ({lo.ammo})";
        return s.Length > 0 ? s : "?";
    }

    void Log(string line)
    {
        log.Enqueue($"{Time.time:0.0}s  {line}");
        while (log.Count > 6) log.Dequeue();
    }

    void OnGUI()
    {
        if (!show) return;

        float scale = Mathf.Clamp(Screen.height / 1000f, 0.8f, 2.5f);
        label ??= new GUIStyle(GUI.skin.label);
        small ??= new GUIStyle(GUI.skin.label);
        label.fontSize = Mathf.RoundToInt(12 * scale);
        small.fontSize = Mathf.RoundToInt(10 * scale);
        label.normal.textColor = small.normal.textColor = Color.white;

        float x = 8 * scale, y = 8 * scale, w = 560 * scale, line = 16 * scale;

        int allies = 0, enemies = 0;
        foreach (var c in CombatStateMachine.All)
        {
            if (c.State == CombatState.Dead) continue;
            if (c.team == 0) allies++; else enemies++;
        }

        // ---- text lines ----
        lines.Clear();
        lines.Add($"FPS {fps:0}   units {allies} blue / {enemies} red   arrows {Projectile.Live}   x{Time.timeScale:0.##}");
        bool help = Time.unscaledTime - shownAt < helpSeconds;
        if (help)
        {
            lines.Add("WASD move · mouse aim · LMB light · E heavy · RMB block · Space dodge · Shift sprint · Q weapon");
            lines.Add("1 overlay · 2 slow-mo · 3 skirmish · 4 battle · 5 god · 9 enemy tier · 0 enemy unit · L labels · R reset");
            lines.Add("Duels: 6 start/stop · 7/8 tier A/B · Shift+7/Shift+8 unit A/B");
        }
        if (arena) lines.Add($"Starting enemies: {arena.StartingEnemyTierName} {arena.StartingEnemyUnitName}");
        if (duel)
        {
            lines.Add($"Duel: {duel.NameA} vs {duel.NameB}{(duel.Running ? "   (running, 6 to stop)" : "")}");
            if (duel.Running) lines.Add("  " + duel.Status);
            for (int i = 0; i < Mathf.Min(3, duel.History.Count); i++) lines.Add("  done: " + duel.History[i]);
        }

        var p = arena ? arena.Player : null;
        var pc = p ? p.GetComponent<PlayerController>() : null;
        int barRows = p ? 4 : 0;

        GUI.color = new Color(0, 0, 0, 0.55f);
        GUI.DrawTexture(new Rect(x - 4, y - 4, w, line * (lines.Count + barRows + 0.5f) + line * 0.8f * log.Count + 8), Texture2D.whiteTexture);
        GUI.color = Color.white;

        foreach (var l in lines) Text(ref y, x, line, l);
        y += line * 0.5f;

        if (p)
        {
            string weaponName = pc && pc.CurrentWeapon ? pc.CurrentWeapon.weaponName : "";
            Text(ref y, x, line, $"{weaponName}  ·  {p.State} ({p.StateTime:0.00}s){(arena.PlayerInvulnerable ? "  [GOD MODE]" : "")}{(p.State == CombatState.Dead ? "   YOU DIED — press R" : "")}");
            float bw = 280 * scale;
            Bar(ref y, x, bw, line, "HP", p.Stats.Health, p.Stats.maxHealth, new Color(0.85f, 0.2f, 0.2f));
            Bar(ref y, x, bw, line, "Stamina", p.Stats.Stamina, p.Stats.maxStamina, new Color(0.3f, 0.8f, 0.3f));
            Bar(ref y, x, bw, line, "Poise", p.Stats.Poise, p.Stats.maxPoise, new Color(0.9f, 0.75f, 0.2f));
        }

        foreach (var l in log) { GUI.Label(new Rect(x, y, w, line), l, small); y += line * 0.8f; }

        DrawUnitBars(scale);
    }

    void DrawUnitBars(float scale)
    {
        var cam = Camera.main;
        if (!cam) return;
        Vector3 centre = cam.transform.position;
        float radius = cam.orthographicSize * cam.aspect + 2f; // roughly what's on screen

        Vector2 mouse = Event.current != null ? Event.current.mousePosition : Vector2.zero;
        float labelRadius = 90f * scale; // pixels around the mouse

        int drawn = 0;
        foreach (var c in CombatStateMachine.All)
        {
            if (c.isPlayer || c.State == CombatState.Dead) continue;
            if (drawn >= maxUnitBars) break;
            Vector2 d = c.transform.position - centre;
            if (Mathf.Abs(d.x) > radius || Mathf.Abs(d.y) > cam.orthographicSize + 2f) continue;
            drawn++;

            float top = c.isMounted ? 1.25f : 1.0f;
            Vector3 sp = cam.WorldToScreenPoint(c.transform.position + Vector3.up * top);
            if (sp.z < 0) continue;
            float bw = 30 * scale, bh = 3 * scale;
            float bx = sp.x - bw / 2, by = Screen.height - sp.y;

            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(bx - 1, by - 1, bw + 2, bh * 2 + 3), Texture2D.whiteTexture);
            GUI.color = new Color(0.85f, 0.2f, 0.2f);
            GUI.DrawTexture(new Rect(bx, by, bw * c.Stats.Health / c.Stats.maxHealth, bh), Texture2D.whiteTexture);
            GUI.color = new Color(0.9f, 0.75f, 0.2f);
            GUI.DrawTexture(new Rect(bx, by + bh + 1, bw * c.Stats.Poise / c.Stats.maxPoise, bh), Texture2D.whiteTexture);
            GUI.color = Color.white;

            bool showLabel = labels == LabelMode.All ||
                             (labels == LabelMode.NearMouse && (new Vector2(sp.x, Screen.height - sp.y) - mouse).magnitude < labelRadius);
            if (!showLabel) continue;

            string tag = Describe(c);
            if (c.State != CombatState.Idle) tag += " · " + c.State;
            GUI.Label(new Rect(bx, by - 13 * scale, 200 * scale, 15 * scale), tag, small);
        }
    }

    void Text(ref float y, float x, float line, string s)
    {
        GUI.Label(new Rect(x, y, 3000, line), s, label);
        y += line;
    }

    void Bar(ref float y, float x, float w, float line, string name, float v, float max, Color col)
    {
        float labelW = 60 * (line / 16f);
        GUI.Label(new Rect(x, y, labelW, line), name, label);
        float bx = x + labelW, bw = w - labelW, bh = line * 0.6f, by = y + line * 0.2f;
        GUI.color = new Color(0.15f, 0.15f, 0.15f);
        GUI.DrawTexture(new Rect(bx, by, bw, bh), Texture2D.whiteTexture);
        GUI.color = col;
        GUI.DrawTexture(new Rect(bx, by, bw * Mathf.Clamp01(v / max), bh), Texture2D.whiteTexture);
        GUI.color = Color.white;
        y += line;
    }
}
