claude:
Mình đã thêm mục 23 – Visualization Modes (Phase 2 addendum) vào cuối tài liệu. Nó gồm:

Các chế độ hiển thị (DisplayMode): lọc theo độ hiếm, ngưỡng giá trị và khoảng cách, bao gồm hoặc loại trừ, có màu và phím bật/tắt.
Năm template có sẵn: All, Rare+, Legendary only, Nearby, Best value.
Tracer: đường kẻ tới mục tiêu tốt nhất, có khoảng cách tối đa và giới hạn số đường vẽ cùng lúc.
HUD, cấu hình toàn cục, lưu JSON, giao diện cài đặt, interface IDisplayModeManager.
Danh sách nhiệm vụ Phase 2 và các test kèm theo.

Mục này ghi rõ chỉ mượn ý tưởng về trải nghiệm và cấu hình, không dùng bất kỳ kỹ thuật hook hay can thiệp render nào từ mod bên thứ ba. Mọi thứ chạy trên simulator, và các giới hạn ở mục 0 vẫn áp dụng.

23. Visualization Modes (Phase 2)
Configurable display modes for the overlay and minimap. They run only inside the simulator / Unity presentation layer and consume data from ITokenRepository; they never touch any external game.
23.1 Mode model
public enum FilterMode { Inclusive, Exclusive }   // show only listed rarities vs hide listed rarities

public class VisualizationMode
{
    public string Name;
    public Color Color;
    public KeyCode ToggleKey;
    public FilterMode Filter;
    public HashSet<TokenRarity> Rarities;
    public bool ShowTracer;
    public float MaxTracerDistance;
    public bool Enabled;
}
23.2 Built-in templates
Template
Filter
Rarities
Tracer
All tokens
Exclusive
none hidden
off
Valuable only
Inclusive
Rare, Epic, Legendary
on
Legendary hunt
Inclusive
Legendary
on
Nearby
Exclusive
none hidden (distance-limited)
off
Users can create, rename, recolor, rebind and delete modes; the config is persisted via the existing Configuration store (JSON).
23.3 Tracer
A line from the player to the current best target (and optionally to every token in the active mode). Respects MaxTracerDistance; colored by the mode or by rarity. Implemented in RouteRenderer / WorldOverlay using IVisualizer.
23.4 HUD and global config
• HUD lists active modes, token counts per mode, and the current target.
• Global settings: default tracer distance, marker limit (top N + nearest M), minimap zoom, label verbosity.
23.5 Acceptance criteria
1. Toggling a mode shows/hides only the tokens matching its filter.
2. Inclusive and Exclusive filters produce complementary sets for the same rarity list.
3. Mode config survives restart (JSON round-trip test).
4. Tracer never draws beyond MaxTracerDistance and disappears when the target is collected.
5. All of this works against SimGameApi only; no dependency on any real game.
23. Visualization Modes (Phase 2 addendum)
Design inspiration: configurable overlay modes as seen in generic highlight/ESP-style tools (filter modes, templates, hotkeys, tracer lines, HUD). Only the UX and configuration ideas are borrowed. No code, hooking, injection, or rendering interception techniques from any third-party game mod are to be used. Everything below is implemented inside Assistant.Unity against the simulator.
23.1 Display modes
A DisplayMode is a user-configurable preset that controls which tokens are shown and how.
public enum FilterMode { Inclusive, Exclusive }

public class DisplayMode
{
    public string Name;                 // e.g. "Legendary only"
    public Color Color;                 // marker/tracer color for this mode
    public KeyCode ToggleKey;           // hotkey to enable/disable the mode
    public FilterMode Filter;           // Inclusive: show only listed; Exclusive: hide listed
    public HashSet<TokenRarity> Rarities;
    public float MinValue;              // optional value threshold
    public float MaxDistance;           // optional distance limit
    public bool ShowTracer;             // draw a line to targets in this mode
    public bool Enabled;
}
Several modes can be active at once; a token is shown if any enabled mode accepts it, and the highest-priority matching mode decides its color.
23.2 Templates
Built-in presets the user can pick and then edit:
• All tokens: every available token, color by rarity.
• Rare+: Rare, Epic, Legendary.
• Legendary only: Legendary tokens only.
• Nearby: any rarity within MaxDistance.
• Best value: top N by target score.
Templates are plain JSON files so they can be shared and versioned.
23.3 Tracers
A tracer is a line from the player to a token or to the current best target.
• Modes: BestTargetOnly, AllInMode, Off.
• TracerMaxDistance in global config; tokens beyond it get no tracer (a minimap edge arrow is used instead).
• Rendering: pooled LineRenderer objects (limit the number of simultaneous tracers, e.g. 10).
• Color and thickness derive from the mode color and the target priority.
23.4 HUD and global config
public class OverlayConfig
{
    public float TracerMaxDistance = 300f;
    public bool ShowActiveModes = true;       // list currently enabled modes on the HUD
    public bool ShowPlayerCoordinates = true; // format configurable
    public string CoordinateFormat = "X: {x:0}  Z: {z:0}";
    public int MaxVisibleMarkers = 50;        // performance cap
}
The HUD shows the active modes, player coordinates, token counts per rarity and the current target (see section 16).
23.5 Persistence and settings UI
• Modes, templates and OverlayConfig are stored as JSON (see section 17).
• SettingsPanel lets the user: rename a mode and pick its color, edit the rarity list, switch Inclusive/Exclusive, bind a hotkey, apply a template, add or delete a mode.
23.6 Class and interface additions
public interface IDisplayModeManager
{
    IReadOnlyList<DisplayMode> Modes { get; }
    bool IsVisible(TokenLocation token, PlayerState player);   // evaluates all enabled modes
    DisplayMode GetMatchingMode(TokenLocation token, PlayerState player);
    void ApplyTemplate(string templateName);
    void Save(); void Load();
}
IVisualizer receives only the tokens that IDisplayModeManager.IsVisible accepts. Core stays unaware of rendering details; DisplayModeManager lives in Assistant.Unity (or Core if it has no Unity types).
23.7 Phase 2 tasks for the coding agent
1. Implement DisplayMode, OverlayConfig, IDisplayModeManager and DisplayModeManager with JSON persistence.
2. Add the five built-in templates.
3. Integrate filtering into the marker and minimap pipeline (capped by MaxVisibleMarkers).
4. Implement pooled tracer rendering with TracerMaxDistance.
5. Add a minimal SettingsPanel and the HUD elements from 23.4.
6. Unit tests: inclusive vs exclusive filtering, multiple modes active, distance/value thresholds, template loading, config round-trip (save then load).
All of this runs on the simulator only; the constraints in section 0 still apply.