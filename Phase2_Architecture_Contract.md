# Phase 2 Architecture Contract Design
## BrilliantCrypto Assistant — Simulator-First Foundation

---

## 0. Preliminary Analysis: `SpatialHashGrid.QueryCandidates`

### Question
> *Does `SpatialHashGrid.QueryCandidates` iterate by bounding box for small radii, or enumerate all buckets on every query? Fix first if it is the latter.*

### Verification & Finding
In [`Assistant.Core/Discovery/SpatialHashGrid.cs`](file:///c:/Users/phamt/System_Brill/Assistant.Core/Discovery/SpatialHashGrid.cs#L85-L123), `QueryCandidates` computes the bounding box indices:
```csharp
int minX = ToCellIndex(center.X - radius), maxX = ToCellIndex(center.X + radius);
int minZ = ToCellIndex(center.Z - radius), maxZ = ToCellIndex(center.Z + radius);
long span = ((long)maxX - minX + 1) * ((long)maxZ - minZ + 1);

if (span > _cells.Count) // Fewer real cells than candidate span -> iterate real cells
{
    foreach (var kv in _cells) { ... }
    return result;
}

for (int cx = minX; cx <= maxX; cx++)
{
    for (int cz = minZ; cz <= maxZ; cz++)
    {
        if (_cells.TryGetValue((cx, cz), out var set))
            result.UnionWith(set);
    }
}
return result;
```

**Conclusion:**
1. **For small/normal radii**, `span` (e.g., $1 \times 1 = 1$, $2 \times 2 = 4$, or $3 \times 3 = 9$ cells) is far smaller than `_cells.Count` in any populated grid. The code executes the nested loop over bounding box cells `[minX..maxX] × [minZ..maxZ]` via direct $O(1)$ dictionary lookups.
2. It does **NOT** enumerate all buckets on every query.
3. The `span > _cells.Count` branch acts as an adaptive optimization for massive/infinite radii to prevent iterating over millions of empty coordinate cells.
4. **No bug or performance regression exists here; no fix is required.**

---

## 1. File & Folder List with Project Placement

All new types maintain strict layering:
- `Assistant.Core` remains **100% dependency-free**, **Unity-free**, and **network-free**.
- `Assistant.Persistence` isolates file I/O and JSON serialization (`System.Text.Json`).
- `Assistant.Sim` provides mock implementations and simulation drivers.
- `Assistant.Tests` provides unit and property test suites.

```text
c:\Users\phamt\System_Brill\
├── Assistant.Core\
│   ├── Abstractions\
│   │   ├── IConfigStore.cs                 <-- Configuration persistence interface
│   │   ├── IDisplayModeManager.cs          <-- Display mode filtering & preset interface
│   │   ├── ITokenDiscoveryService.cs       <-- Discovery scan orchestrator interface
│   │   └── ITokenParser.cs                 <-- RawMapObject -> TokenLocation parser interface
│   ├── Data\
│   │   ├── ColorRgba.cs                    <-- Pure struct (R, G, B, A) with FromHex/ToHex
│   │   ├── DisplayMode.cs                  <-- Configuration model for visual filters (required Id, float? MaxDistance)
│   │   ├── FilterMode.cs                   <-- Enum: Inclusive vs Exclusive
│   │   ├── MapChangedEventArgs.cs          <-- Region-aware map change event args
│   │   ├── MinimapCoordinateResult.cs      <-- Output of minimap coordinate transformation
│   │   ├── MinimapViewport.cs              <-- Minimap geometry config (size, scale, zoom, center)
│   │   └── OverlayConfig.cs                <-- Global HUD/tracer config
│   ├── Discovery\
│   │   ├── DefaultTokenParser.cs           <-- Safe parser for RawMapObject properties
│   │   └── TokenDiscoveryService.cs        <-- Engine: coverage-based TTL, region invalidation, upsert
│   ├── Minimap\
│   │   └── MinimapMath.cs                  <-- Pure static functions: world -> minimap math & ray clamping
│   └── Visualization\
│       ├── BuiltInTemplates.cs             <-- 5 standard templates (All, Rare+, Legendary, Nearby, BestValue)
│       └── DisplayModeManager.cs           <-- Evaluates active modes, precedence, presentation filtering
│
├── Assistant.Persistence\                  <-- System.Text.Json persistence project
│   ├── Assistant.Persistence.csproj
│   ├── ColorRgbaHexJsonConverter.cs        <-- JSON converter serializing ColorRgba as "#RRGGBBAA"
│   └── JsonConfigStore.cs                  <-- File-based atomic JSON store with schemaVersion & key validation
│
├── Assistant.Sim\
│   └── SimMapChangeNotifier.cs             <-- Simulation helper raising region-specific MapChanged
│
└── Assistant.Tests\
    ├── Discovery\
    │   ├── TokenDiscoveryServiceTests.cs   <-- Coverage TTL, duplicate IDs, region invalidation
    │   └── TokenParserTests.cs             <-- RawMapObject boundary parsing
    ├── Minimap\
    │   ├── MinimapMathTests.cs             <-- Ray clamping, rotation, zoom, NaN/invalid bounds
    │   └── MinimapPropertyTests.cs         <-- Invariant tests with fixed RNG seeds
    ├── Visualization\
    │   ├── DisplayModeManagerTests.cs      <-- Inclusive/Exclusive, deterministic tie-breaking, cap ordering
    │   └── OverlayConfigTests.cs           <-- Config round-trip & precedence rules
    └── Persistence\
        └── JsonConfigStoreTests.cs         <-- Atomic save, schemaVersion, corrupt file handling, key validation
```

---

## 2. Explicit Changes to Phase 1 Contracts

To guarantee seamless integration without breaking existing systems, every modification to Phase 1 contracts is explicitly locked below:

### 2.1 `IGameApi.MapChanged` Signature Update
- **Phase 1 Signature:**
  ```csharp
  event Action? MapChanged;
  ```
- **Phase 2 Signature:**
  ```csharp
  event Action<MapChangedEventArgs>? MapChanged;
  ```
- **Rationale:** Enables fine-grained region invalidation (`MapChangedEventArgs.AffectedRegion`). A null `AffectedRegion` continues to signal whole-map invalidation.

### 2.2 `ITokenRepository` Additions
Add two methods to [`Assistant.Core/Abstractions/ITokenRepository.cs`](file:///c:/Users/phamt/System_Brill/Assistant.Core/Abstractions/ITokenRepository.cs):
```csharp
/// <summary>
/// Retrieves all active tokens whose positions fall within the specified region.
/// </summary>
IReadOnlyList<TokenLocation> GetInRegion(Region region);

/// <summary>
/// Purges all tokens and resets the spatial index.
/// </summary>
void Clear();
```
- **Implementation Note:** `GetInRegion` leverages `SpatialHashGrid.Query(region.Center, region.Radius)` and filters candidates using `region.Contains(t.Position)`. `Clear()` resets both the internal dictionary and the `SpatialHashGrid`.

### 2.3 Definition of `Region` (Circle vs Box)
- **Phase 1 Implementation:** Cylindrical / 2D circular boundary:
  ```csharp
  public class Region
  {
      public Vec3 Center { get; set; }
      public float Radius { get; set; }
      public bool Contains(Vec3 point) => Vec3.Distance2D(Center, point) <= Radius;
  }
  ```
- **Phase 2 Contract Lock:**
  - `Region` remains **strictly circular** ($Center, Radius$).
  - All discovery scanning, player radar ranges, and region invalidations use circular geometry.
  - Spatial bounding box queries (`[minX..maxX] × [minZ..maxZ]`) are strictly internal optimizations within `SpatialHashGrid` and do not alter the public contract of `Region`.

### 2.4 `IGameApi` Subscriptions by `TokenDiscoveryService`
- `TokenDiscoveryService` subscribes to:
  1. `IGameApi.TokenCollected` (`Action<string>`): Immediately marks token collected and removes it from `SpatialHashGrid`.
  2. `IGameApi.MapChanged` (`Action<MapChangedEventArgs>`): Enqueues dirty invalidation regions for re-scanning.
- `TokenDiscoveryService` **does NOT** subscribe to `TokenSpawned` or `TokenRemoved`. Discovery remains a pull-based scanning pipeline operating over `IMapProvider.GetObjects(Region)`.

### 2.5 Where `Tick()` Obtains the Scan Region
- `TokenDiscoveryService` receives `IGameApi` (or a player state provider) and `IMapProvider` via constructor injection.
- When `Tick()` executes scheduled scans:
  1. Calls `IGameApi.GetPlayerState()` to retrieve `PlayerState.Position`.
  2. Constructs the scan region: `new Region(playerState.Position, ScanRadius)`.
  3. Executes `ScanRegion(region)`.

### 2.6 Simulation & Test Updates
- `SimGameApi` updates `MapChanged` event and triggers:
  ```csharp
  public void TriggerMapChanged(Region? affectedRegion, string reason)
  {
      MapChanged?.Invoke(new MapChangedEventArgs(affectedRegion, reason, _clock.UtcNow));
  }
  ```
- Phase 1 tests in `Assistant.Tests` referencing `MapChanged` are updated to accept the new event parameter.

---

## 3. Interface and DTO Signatures

### 3.1 Core Data Types

#### `ColorRgba.cs`
```csharp
namespace Assistant.Core.Data
{
    /// <summary>
    /// Pure RGBA color representation (32-bit byte channel) with no external dependencies.
    /// </summary>
    public readonly struct ColorRgba : IEquatable<ColorRgba>
    {
        public byte R { get; }
        public byte G { get; }
        public byte B { get; }
        public byte A { get; }

        public ColorRgba(byte r, byte g, byte b, byte a = 255)
        {
            R = r; G = g; B = b; A = a;
        }

        public static ColorRgba FromHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return White;
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                return new ColorRgba(r, g, b, 255);
            }
            if (hex.Length == 8)
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                byte a = Convert.ToByte(hex.Substring(6, 2), 16);
                return new ColorRgba(r, g, b, a);
            }
            return White;
        }

        public string ToHex() => $"#{R:X2}{G:X2}{B:X2}{A:X2}";

        public static readonly ColorRgba White = new ColorRgba(255, 255, 255);
        public static readonly ColorRgba Yellow = new ColorRgba(255, 215, 0);
        public static readonly ColorRgba Purple = new ColorRgba(160, 32, 240);
        public static readonly ColorRgba Orange = new ColorRgba(255, 140, 0);
        public static readonly ColorRgba Cyan = new ColorRgba(0, 255, 255);
        public static readonly ColorRgba Red = new ColorRgba(255, 0, 0);

        public bool Equals(ColorRgba other) => R == other.R && G == other.G && B == other.B && A == other.A;
        public override bool Equals(object? obj) => obj is ColorRgba other && Equals(other);
        public override int GetHashCode() => (R << 24) | (G << 16) | (B << 8) | A;
        public override string ToString() => ToHex();
    }
}
```

#### `MapChangedEventArgs.cs`
```csharp
namespace Assistant.Core.Data
{
    public class MapChangedEventArgs
    {
        public Region? AffectedRegion { get; }
        public string Reason { get; }
        public DateTimeOffset Timestamp { get; }

        public MapChangedEventArgs(Region? affectedRegion, string reason, DateTimeOffset timestamp)
        {
            AffectedRegion = affectedRegion;
            Reason = reason ?? string.Empty;
            Timestamp = timestamp;
        }

        public static MapChangedEventArgs EntireMap(string reason, DateTimeOffset timestamp) =>
            new MapChangedEventArgs(null, reason, timestamp);

        public static MapChangedEventArgs SubRegion(Region region, string reason, DateTimeOffset timestamp) =>
            new MapChangedEventArgs(region, reason, timestamp);
    }
}
```

#### `ITokenParser.cs`
```csharp
namespace Assistant.Core.Abstractions
{
    /// <summary>
    /// Pure parsing interface: converts RawMapObject into TokenLocation without owning time.
    /// </summary>
    public interface ITokenParser
    {
        bool TryParseToken(RawMapObject raw, out TokenLocation? token, out string? failureReason);
        IReadOnlyList<TokenLocation> ParseMany(IEnumerable<RawMapObject> rawObjects);
    }
}
```

#### `ITokenDiscoveryService.cs`
```csharp
namespace Assistant.Core.Abstractions
{
    public interface ITokenDiscoveryService
    {
        IClock Clock { get; }
        float TtlSeconds { get; set; }
        TimeSpan ScanInterval { get; set; }
        float ScanRadius { get; set; }

        int ScanRegion(Region region);
        int ScanAll();
        void Tick();
        int PruneStaleTokens();
        void HandleMapChanged(MapChangedEventArgs args);

        /// <summary>
        /// Fires when NEW tokens are discovered or when existing tokens have changed properties (Position, Rarity, Value).
        /// Does NOT fire on timestamp-only confirmations.
        /// </summary>
        event Action<IReadOnlyList<TokenLocation>>? TokensDiscovered;

        /// <summary>
        /// Fires when stale tokens are evicted by coverage-based TTL pruning.
        /// </summary>
        event Action<IReadOnlyList<string>>? TokensExpired;
    }
}
```

---

### 3.2 Minimap Math Types & Conventions

#### Minimap Conventions
1. **World Coordinates:**
   - $+X$ = East
   - $+Z$ = North
   - $+Y$ = Up (vertical / altitude, ignored in 2D minimap projection)
2. **Heading & Yaw:**
   - Degrees measured **clockwise from North ($+Z$)**:
     - $0^\circ$ = North ($+Z$)
     - $90^\circ$ = East ($+X$)
     - $180^\circ$ = South ($-Z$)
     - $270^\circ$ = West ($-X$)
3. **Screen / Minimap Coordinates:**
   - Origin `(0, 0)` is at the **center** of the minimap UI widget.
   - Screen axes in local radar offset space: $+X_{\text{screen}}$ = Right, $+Y_{\text{screen}}$ = UP.
   - Counter-clockwise rotation rotates world offset by heading so the player heading points UP ($+Y_{\text{screen}}$).
4. **Single Source of Truth for Zoom:**
   - `OverlayConfig.MinimapZoom` is the authoritative user-facing setting (default `1.0f`).
   - `MinimapViewport.WorldUnitsPerPixel` is derived as:
     $$\text{WorldUnitsPerPixel} = \frac{\text{BaseWorldUnitsPerPixel}}{\text{OverlayConfig.MinimapZoom}}$$
   - `MinimapViewport.CreateFromConfig(OverlayConfig, ...)` and `OverlayConfig.CreateViewport(...)` enforce this single zoom source.
5. **Ray-Preserving Clamping:**
   - When a target lies strictly outside the padded viewport rectangle, it is clamped along the exact ray from the center to preserve directional bearing:
     $$s_x = \frac{\text{HalfWidth} - \text{EdgePaddingPx}}{|\Delta x|}, \quad s_y = \frac{\text{HalfHeight} - \text{EdgePaddingPx}}{|\Delta y|}$$
     $$\text{scale} = \min(s_x, s_y)$$
     $$\vec{P}_{\text{clamped}} = (\Delta x \cdot \text{scale}, \; \Delta y \cdot \text{scale})$$
   - Direction angle is preserved exactly: $\theta = \text{atan2}(\Delta x, \Delta y)$ from screen UP.
6. **Error Handling & Validation:**
   - Invalid viewport configurations (non-positive dimensions, invalid scale, negative padding, padding $\ge$ half-extents, non-finite values) throw `ArgumentException`.
   - `MinimapViewport.IsValid` and `MinimapMath.TryWorldToMinimap` provide safe UI-layer verification without throwing.
   - Non-finite target coordinates return a safe default off-screen result `new MinimapCoordinateResult(Vec2.Zero, true, 0f, 0f)` without throwing.

#### `MinimapViewport.cs`
```csharp
namespace Assistant.Core.Data
{
    public class MinimapViewport
    {
        public Vec2 CenterWorld { get; set; }           // Player world position (X, Z)
        public float ViewportWidthPx { get; set; } = 200f;
        public float ViewportHeightPx { get; set; } = 200f;
        public float WorldUnitsPerPixel { get; set; } = 0.5f;
        public float HeadingDegrees { get; set; } = 0f;
        public bool RotateWithPlayer { get; set; } = true;
        public float EdgePaddingPx { get; set; } = 4f;

        public bool IsValid =>
            ViewportWidthPx > 0f && !float.IsNaN(ViewportWidthPx) && !float.IsInfinity(ViewportWidthPx) &&
            ViewportHeightPx > 0f && !float.IsNaN(ViewportHeightPx) && !float.IsInfinity(ViewportHeightPx) &&
            WorldUnitsPerPixel > 0f && !float.IsNaN(WorldUnitsPerPixel) && !float.IsInfinity(WorldUnitsPerPixel) &&
            EdgePaddingPx >= 0f && !float.IsNaN(EdgePaddingPx) && !float.IsInfinity(EdgePaddingPx) &&
            EdgePaddingPx < (ViewportWidthPx * 0.5f) && EdgePaddingPx < (ViewportHeightPx * 0.5f) &&
            !float.IsNaN(HeadingDegrees) && !float.IsInfinity(HeadingDegrees) &&
            !float.IsNaN(CenterWorld.X) && !float.IsInfinity(CenterWorld.X) &&
            !float.IsNaN(CenterWorld.Y) && !float.IsInfinity(CenterWorld.Y);

        public static MinimapViewport CreateFromConfig(
            OverlayConfig config,
            Vec2 centerWorld,
            float headingDegrees = 0f,
            float baseWorldUnitsPerPixel = 0.5f,
            float widthPx = 200f,
            float heightPx = 200f,
            float edgePaddingPx = 4f,
            bool rotateWithPlayer = true)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (config.MinimapZoom <= 0f || float.IsNaN(config.MinimapZoom) || float.IsInfinity(config.MinimapZoom))
                throw new ArgumentException("MinimapZoom must be positive and finite.", nameof(config));

            return new MinimapViewport
            {
                CenterWorld = centerWorld,
                ViewportWidthPx = widthPx,
                ViewportHeightPx = heightPx,
                WorldUnitsPerPixel = baseWorldUnitsPerPixel / config.MinimapZoom,
                HeadingDegrees = headingDegrees,
                RotateWithPlayer = rotateWithPlayer,
                EdgePaddingPx = edgePaddingPx
            };
        }
    }
}
```

#### `MinimapCoordinateResult.cs`
```csharp
namespace Assistant.Core.Data
{
    public readonly struct MinimapCoordinateResult
    {
        public Vec2 ScreenPositionPx { get; }
        public bool IsOffScreen { get; }
        public float DirectionAngleDegrees { get; }     // Normalized [0..360) clockwise from screen UP
        public float DistanceWorld { get; }

        public MinimapCoordinateResult(Vec2 screenPos, bool isOffScreen, float angleDegrees, float distanceWorld)
        {
            ScreenPositionPx = screenPos;
            IsOffScreen = isOffScreen;
            DirectionAngleDegrees = angleDegrees;
            DistanceWorld = distanceWorld;
        }
    }
}
```

#### `MinimapMath.cs`
```csharp
namespace Assistant.Core.Minimap
{
    public static class MinimapMath
    {
        public static bool TryWorldToMinimap(Vec3 targetWorld, MinimapViewport? viewport, out MinimapCoordinateResult result)
        {
            if (viewport == null || !viewport.IsValid)
            {
                result = default;
                return false;
            }
            result = WorldToMinimap(targetWorld, viewport);
            return true;
        }

        public static MinimapCoordinateResult WorldToMinimap(Vec3 targetWorld, MinimapViewport viewport)
        {
            if (viewport == null) throw new ArgumentNullException(nameof(viewport));
            if (!viewport.IsValid) throw new ArgumentException("Viewport parameters are invalid or non-finite.", nameof(viewport));

            if (float.IsNaN(targetWorld.X) || float.IsInfinity(targetWorld.X) ||
                float.IsNaN(targetWorld.Y) || float.IsInfinity(targetWorld.Y) ||
                float.IsNaN(targetWorld.Z) || float.IsInfinity(targetWorld.Z))
            {
                return new MinimapCoordinateResult(Vec2.Zero, true, 0f, 0f);
            }

            double dx = (double)targetWorld.X - (double)viewport.CenterWorld.X;
            double dz = (double)targetWorld.Z - (double)viewport.CenterWorld.Y;
            double distWorldD = Math.Sqrt(dx * dx + dz * dz);
            float distanceWorld = double.IsInfinity(distWorldD) || distWorldD > float.MaxValue ? float.MaxValue : (float)distWorldD;

            if (dx == 0.0 && dz == 0.0) return new MinimapCoordinateResult(Vec2.Zero, false, 0f, 0f);

            double scale = (double)viewport.WorldUnitsPerPixel;
            double px = dx / scale;
            double py = dz / scale;

            double rx, ry;
            if (viewport.RotateWithPlayer)
            {
                double rad = (double)viewport.HeadingDegrees * (Math.PI / 180.0);
                double cos = Math.Cos(rad);
                double sin = Math.Sin(rad);
                rx = px * cos - py * sin;
                ry = px * sin + py * cos;
            }
            else
            {
                rx = px;
                ry = py;
            }

            double bearingDeg = Math.Atan2(rx, ry) * (180.0 / Math.PI);
            bearingDeg = (bearingDeg % 360.0 + 360.0) % 360.0;
            float bearing = (float)bearingDeg;

            double halfW = (double)viewport.ViewportWidthPx * 0.5;
            double halfH = (double)viewport.ViewportHeightPx * 0.5;
            double pad = (double)viewport.EdgePaddingPx;
            double limitX = halfW - pad;
            double limitY = halfH - pad;

            double absX = Math.Abs(rx);
            double absY = Math.Abs(ry);
            bool isOffScreen = absX > limitX || absY > limitY;

            double clampedX = rx;
            double clampedY = ry;
            if (isOffScreen)
            {
                double clampScale = absX == 0.0 ? limitY / absY : (absY == 0.0 ? limitX / absX : Math.Min(limitX / absX, limitY / absY));
                clampedX = Math.Clamp(rx * clampScale, -limitX, limitX);
                clampedY = Math.Clamp(ry * clampScale, -limitY, limitY);
            }

            return new MinimapCoordinateResult(new Vec2((float)clampedX, (float)clampedY), isOffScreen, bearing, distanceWorld);
        }
    }
}
```

---

### 3.3 Visualization & Persistence Types

#### `DisplayMode.cs`
```csharp
namespace Assistant.Core.Data
{
    public class DisplayMode
    {
        public string Id { get; }
        public string Name { get; set; } = string.Empty;
        public ColorRgba Color { get; set; } = ColorRgba.White;
        public string KeyBinding { get; set; } = string.Empty;
        public FilterMode Filter { get; set; } = FilterMode.Inclusive;
        public HashSet<TokenRarity> Rarities { get; set; } = new HashSet<TokenRarity>();
        public float MinValue { get; set; } = 0f;
        public float? MaxDistance { get; set; } = null; // null = unlimited (avoids Infinity in JSON)
        public bool ShowTracer { get; set; } = false;
        public bool Enabled { get; set; } = true;
        public int Priority { get; set; } = 0;

        public DisplayMode(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("DisplayMode Id must be non-empty and injected.", nameof(id));
            Id = id;
        }

        public bool Matches(TokenLocation token, Vec3 playerPosition)
        {
            if (!Enabled) return false;

            // Rarity filter
            bool rarityMatches = Filter == FilterMode.Inclusive
                ? Rarities.Contains(token.Rarity)
                : !Rarities.Contains(token.Rarity);
            if (!rarityMatches) return false;

            // MinValue threshold
            if (token.EstimatedValue < MinValue) return false;

            // MaxDistance threshold
            if (MaxDistance.HasValue)
            {
                float dist = Vec3.Distance2D(playerPosition, token.Position);
                if (dist > MaxDistance.Value) return false;
            }

            return true;
        }

        public DisplayMode Clone()
        {
            return new DisplayMode(Id)
            {
                Name = Name,
                Color = Color,
                KeyBinding = KeyBinding,
                Filter = Filter,
                Rarities = new HashSet<TokenRarity>(Rarities),
                MinValue = MinValue,
                MaxDistance = MaxDistance,
                ShowTracer = ShowTracer,
                Enabled = Enabled,
                Priority = Priority
            };
        }
    }
}
```

#### `OverlayConfig.cs`
```csharp
namespace Assistant.Core.Data
{
    public class OverlayConfig
    {
        public float TracerMaxDistance { get; set; } = 300f;
        public int MaxTracerCount { get; set; } = 10;
        public int MaxVisibleMarkers { get; set; } = 50;
        public bool ShowActiveModes { get; set; } = true;
        public bool ShowPlayerCoordinates { get; set; } = true;
        public string CoordinateFormat { get; set; } = "X: {0:F1}  Z: {1:F1}";
        public float MinimapZoom { get; set; } = 1.0f; // Single source of truth for zoom
    }
}
```

#### `IConfigStore.cs` & `JsonConfigStore.cs` (`Assistant.Persistence`)
```csharp
namespace Assistant.Core.Abstractions
{
    public interface IConfigStore
    {
        void Save<T>(string key, T data);
        T? Load<T>(string key);
        bool Exists(string key);
        bool Delete(string key);
    }
}
```
**`JsonConfigStore` Requirements:**
1. **Key Validation:** Rejects any key containing path separators (`/`, `\`), directory traversal (`..`), or illegal filename characters (`Path.GetInvalidFileNameChars()`). Throws `ArgumentException`.
2. **Schema Envelope:** Wraps saved JSON in an envelope:
   ```json
   {
     "schemaVersion": 1,
     "payload": { ... }
   }
   ```
3. **Corrupt File Resilience:** If a file does not exist or contains invalid/corrupt JSON, `Load<T>(key)` logs a warning and returns `null` (never throws).
4. **Atomic Writes:** Saves to a temporary file in the same directory (`$"{key}.tmp"`) and performs an atomic replace via `File.Move(tmp, target, overwrite: true)`.

---

## 4. Coverage-Based TTL, Invalidation, and Event Rules

### 4.1 Coverage-Based TTL Eviction (P1 #1)
- **The Rule:** A token is **ONLY** expired by TTL if its position lies inside a region that was actually scanned *after* its `LastConfirmedAt`.
- **Coverage Tracking Mechanism:**
  - `TokenDiscoveryService` maintains a history of scanned regions: `(Region region, DateTimeOffset scannedAt)`.
  - Stale check: A token is eligible for eviction when `Clock.UtcNow - token.LastConfirmedAt > TtlSeconds`.
  - Eviction check: The token is evicted if and only if:
    $$\exists \, (R, t_{\text{scan}}) \text{ in CoverageHistory} \text{ such that } R.\text{Contains}(token.\text{Position}) \text{ and } t_{\text{scan}} > token.\text{LastConfirmedAt}$$
  - **Result:** Tokens outside current scan coverage survive past TTL indefinitely. Tokens inside coverage that were scanned and not re-confirmed expire exactly at the threshold with `FakeClock`.

### 4.2 Duplicate ID Rule — First Occurrence Wins (P1 #5)
- If multiple `RawMapObject` instances in the **same scan batch** share an identical `Id`:
  - **The first valid occurrence is accepted and parsed.**
  - **All subsequent occurrences with that Id are rejected/skipped.**
  - A diagnostic record/warning is logged: `Duplicate ID '{id}' encountered in single scan batch; subsequent occurrence ignored.`
  - The repository receives exactly one token addition/refresh.

### 4.3 Discovery Event Semantics (`TokensDiscovered`) (P1 #2)
- `TokensDiscovered` fires **ONLY** when:
  1. A token is **NEW** (Id was not previously in repository), OR
  2. A token is **MUTATED** (its `Position`, `Rarity`, or `EstimatedValue` changed).
- A `LastConfirmedAt`-only refresh (token seen at same position/rarity/value) fires **NOTHING**.
- `TokensExpired` fires only when stale tokens are evicted by coverage-based TTL pruning.

### 4.4 Invalidation & Region Merging (P1 #2)
- If multiple `MapChanged` events occur within the same tick:
  - Events are **merged** by unioning or batching affected regions.
  - Events are **never dropped** due to timestamp equality.
  - A null `AffectedRegion` invalidates the entire map and supersedes pending sub-regions.

### 4.5 Lock Hygiene & Exception Handling (P1 #2)
- **Events Raised Outside Locks:** All repository mutations take internal locks, release them, and raise `TokensDiscovered` or `TokensExpired` outside of any lock boundary. Event payloads are immutable defensive copies (`IReadOnlyList<T>`).
- **Subscriber Exception Isolation:** Event invocation iterates subscriber delegates (`eventDelegate.GetInvocationList()`). Each handler is wrapped in a `try/catch`. If a handler throws:
  - The exception is captured/logged.
  - Execution continues to all remaining subscribers.
  - The discovery pipeline's internal state remains uncorrupted.

---

## 5. Behavior Tables for Edge Cases

### 5.1 Token Lifecycle State Transitions

| Scenario | State Before | Action / Event | Repository Action | New State | Event Fired |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **New Token Discovered** | None | `ScanRegion` finds new valid ID | `Add(token)` | Active | `TokensDiscovered` (1 item) |
| **Identical Refresh** | Active | `ScanRegion` confirms existing token (no pos/val change) | `token.LastConfirmedAt = Clock.UtcNow` | Active | None |
| **Mutated Refresh** | Active | `ScanRegion` finds token with moved position or new value | `Update(token)`, grid position updated | Active | `TokensDiscovered` (1 item) |
| **Token Outside Coverage** | Active (`age > TTL`) | `PruneStaleTokens()` runs, but region not scanned | None (survives) | Active | None |
| **Token Inside Coverage Not Found** | Active (`age > TTL`) | `PruneStaleTokens()` runs after region scanned | `Remove(id)` from repo and spatial hash | Expired | `TokensExpired` (1 ID) |
| **Token Collected** | Active | `TokenCollected(id)` received | `IsCollected = true`, removed from spatial grid | Collected | None (Sim event) |
| **Collected Rescanned** | Collected | Raw scan finds collected ID | Rejected by `TokenValidator` | Collected | None |

### 5.2 Conflicting Display Modes & Tie-Breaking (P2 #8)

| Conflict Type | Rules & Precedence | Outcome |
| :--- | :--- | :--- |
| **Multiple Matching Modes** | 1. Highest `Priority`<br>2. Tie-break: List order in `IDisplayModeManager.Modes`<br>3. Tie-break: Lexicographical order by `DisplayMode.Id` | Dominant mode decides **BOTH** color and tracer. |
| **Mode Matching Evaluation** | Criteria combined with **AND**: `RarityMatch && ValueMatch && DistanceMatch` | Token matches mode only if all conditions pass. |
| **Marker Cap (`MaxVisibleMarkers`)** | 1. `TokenRarity` descending (`Legendary` > `Epic` > `Rare` > `Common`)<br>2. `EstimatedValue` descending<br>3. Distance ascending<br>4. Tie-break: Lexicographical by `TokenLocation.Id` | Top $N$ tokens selected; ties broken deterministically. |

---

## 6. Complete Test Plan (Replacing 100% Coverage with Named Suites)

### 6.1 `TokenDiscoveryServiceTests`
1. `ScanRegion_ValidRawObjects_SuccessfullyUpsertsToRepository`
2. `ScanRegion_MalformedProperties_SkipsInvalidWithoutThrowing`
3. `ScanRegion_DuplicateIdsInSameScan_FirstOccurrenceWinsAndSubsequentRejected`
4. `ScanRegion_NewToken_FiresTokensDiscovered`
5. `ScanRegion_UnchangedToken_UpdatesLastConfirmedAtWithoutFiringTokensDiscovered`
6. `ScanRegion_MutatedToken_UpdatesAndFiresTokensDiscovered`
7. `PruneStaleTokens_TokenOutsideCoverage_SurvivesPastTtl`
8. `PruneStaleTokens_TokenInsideCoverage_ExpiresExactlyAtTtlThreshold`
9. `HandleMapChanged_WithAffectedRegion_PurgesOnlyTokensInsideRegion`
10. `HandleMapChanged_EntireMap_ClearsAllRepositoryTokens`
11. `HandleMapChanged_MultipleEventsSameTick_MergesRegionsWithoutDropping`
12. `TokenCollected_RemovesFromSpatialGrid_CannotBeQueriedAsAvailable`
13. `SubscriberException_DoesNotCorruptDiscoveryPipelineOrAbortOtherSubscribers`

### 6.2 `MinimapMathTests` & Property Tests
1. `WorldToMinimap_TargetAtPlayerPosition_ReturnsZeroOffsetAndNotOffScreen`
2. `WorldToMinimap_TargetOutsideBounds_ClampsPreservingRayDirection`
3. `WorldToMinimap_RotationActive_RotatesCoordinatesClockwiseByHeading`
4. `WorldToMinimap_NonRotated_MaintainsNorthUpOrientation`
5. `WorldToMinimap_ZoomFactor_ScalesPixelOffsetsProportionally`
6. `WorldToMinimap_InvalidScaleOrNegativeDimensions_ReturnsSafeOffScreenResult`
7. `WorldToMinimap_NaNCoordinates_ReturnsSafeDefaultWithoutThrowing`
8. `PropertyTest_FixedSeed_RandomPoints_DirectionAngleMatchesTrueBearing`
9. `PropertyTest_FixedSeed_RandomCoordinates_ClampedPositionNeverExceedsViewportPadding`

### 6.3 `DisplayModeManagerTests`
1. `IsVisible_InclusiveFilter_ReturnsTrueOnlyForConfiguredRarities`
2. `IsVisible_ExclusiveFilter_ReturnsFalseOnlyForConfiguredRarities`
3. `FilterTokens_InclusiveAndExclusiveWithSameList_ProduceExactComplementSets`
4. `GetDominantMode_MultipleMatchingModes_ReturnsHighestPriority`
5. `GetDominantMode_PriorityTie_SelectsFirstByListOrderThenId`
6. `FilterTokens_ExceedingCap_RespectsMaxVisibleMarkersLimitWithDeterministicTieBreaker`
7. `BuiltInTemplates_AllFiveTemplates_HaveValidDistinctConfigurationsAndRequiredIds`
8. `ToggleModeByKey_ValidKey_TogglesEnabledState`

### 6.4 `JsonConfigStoreTests`
1. `SaveAndLoad_DisplayModeList_RoundTripsAccurately`
2. `SaveAndLoad_ColorRgba_SerializesAsHexAndPreservesExactByteValues`
3. `Save_WrapsPayloadWithSchemaVersionEnvelope`
4. `Load_NonExistentOrCorruptFile_ReturnsNullWithoutThrowing`
5. `Save_AtomicWrite_WritesToTempAndMovesOverwritingTarget`
6. `ValidateKey_PathSeparatorsOrDirectoryTraversal_ThrowsArgumentException`

---

## 7. Definition of Done & C# LangVersion (P2 #10)

### 7.1 C# Language Version Support
- `Assistant.Core.csproj` targets `netstandard2.1` with `<Nullable>enable</Nullable>`.
- To support nullable reference types, init-only setters, and pattern matching across build environments, explicitly lock:
  ```xml
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>10.0</LangVersion>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  ```

### 7.2 Phase 2 Gate Criteria
1. **Compilation**: `dotnet build` succeeds with zero errors and zero warnings.
2. **Deterministic Time**: No ambient `DateTime.Now` or `DateTime.UtcNow` calls in `Assistant.Core` or `Assistant.Sim`. All time queries flow through `IClock`.
3. **Pure Core**: `Assistant.Core` has zero references to Unity, serialization packages, or external network libraries.
4. **Isolated Persistence**: All JSON file I/O is isolated in `Assistant.Persistence`.
5. **Phase 2 Gate Test Suite**: All 36 named test cases across the 4 test suites pass in `dotnet test`.

---

## 8. Phase 3 Integration Bridge (P1 #4)

### 8.1 Clean Hand-off to Phase 3 (Ranking & Pathfinding)
- **CRITICAL ARCHITECTURAL CONTRACT:**
  - **Phase 3 `ITargetRanker` will read directly from `ITokenRepository.GetAvailable()`, NOT from `DisplayModeManager.FilterTokens`.**
  - `DisplayModeManager.FilterTokens` is strictly a presentation-layer filter for the HUD, minimap, and overlay rendering.
  - Target ranking and collection intelligence must operate on the complete set of valid, available domain tokens in `ITokenRepository`.
- **Phase 3 `IPathFinder`** will consume `PlayerState.Position` and selected `TokenLocation.Position`.
- Phase 2 outputs data containers (`ShowTracer`, `DominantMode.Color`, `MinimapCoordinateResult`); Phase 3 will populate path nodes and route waypoints.
