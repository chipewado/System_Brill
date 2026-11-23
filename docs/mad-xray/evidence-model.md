# MAD X-Ray evidence model

## Design principles

1. **Provenance first:** every observation identifies its source, session, timestamp, and input artifact.
2. **Observation is not truth:** a detector output is an observation, not a verified GEM.
3. **No false precision:** do not invent world coordinates or calibrated probabilities.
4. **Independent verification:** confirmation requires an authorized ground-truth source independent of the cue being evaluated.
5. **Replayability:** the same input and versioned detector configuration should produce reproducible output.
6. **Disagreement is data:** preserve conflicting observations rather than silently averaging them away.

## Core concepts

### SourceKind

- `RadarHud`
- `Audio`
- `Visual`
- `OfficialApi`
- `HumanAnnotation`
- `GroundTruth`

`GroundTruth` is reserved for an independently authorized verification source; it is not a detector output.

### VerificationStatus

- `ConfirmedGem`
- `ObservedGem`
- `UnverifiedSignal`
- `RejectedSignal`
- `Unknown`

### Observation record

A future C# contract should carry at least:

| Field | Meaning |
|---|---|
| `ObservationId` | Stable unique identifier |
| `SessionId` | Recording/replay session |
| `SourceKind` | Radar/HUD, audio, visual, official API, annotation, or ground truth |
| `ObservedAt` | Normalized UTC timestamp |
| `TimeOffsetMs` | Offset from the session timeline |
| `InputArtifactId` | Reference to source media/data; avoid copying large payloads |
| `DetectorId` | Detector/adapter name and version |
| `Label` | Source-specific observation label |
| `Score` | Optional raw score; never call it probability unless calibrated |
| `Region` | Optional screen-space region with coordinate-space metadata |
| `WorldPosition` | Null unless supplied by an authorized source with documented coordinate semantics |
| `VerificationStatus` | Current evidence state |
| `GroundTruthReference` | Required to support `ConfirmedGem` |
| `Notes` | Human-readable caveats or review notes |

### Coordinate rules

- Screen-space regions must declare their coordinate space, dimensions, scaling, and capture transform.
- A screen-space point is not a world-space location.
- `WorldPosition` remains null for visual-only detections unless a valid mapping is independently established and documented.
- Do not infer underground coordinates from color, glow, sound, or HUD-like visual effects.

## Fusion rules

- Correlate observations by session and normalized time, with source-specific tolerances recorded in configuration.
- Preserve each source observation and its raw score.
- Do not treat multiple frames from the same detector as independent confirmations.
- Do not treat correlated channels as independent evidence. For example, a HUD animation and its corresponding screen pixels may be two representations of one underlying event.
- A weighted sum of uncalibrated detector scores must not be exposed as a probability.
- A fusion result may raise or lower review priority, but it cannot become `ConfirmedGem` without an independent ground-truth reference.
- If sources conflict or timestamps cannot be aligned, use `Unknown` or `UnverifiedSignal` and record why.

## Suggested C# shape (contract sketch)

The following is a design sketch, not a committed runtime API:

```csharp
public enum SourceKind
{
    RadarHud,
    Audio,
    Visual,
    OfficialApi,
    HumanAnnotation,
    GroundTruth
}

public enum VerificationStatus
{
    ConfirmedGem,
    ObservedGem,
    UnverifiedSignal,
    RejectedSignal,
    Unknown
}

public sealed record GemEvidence(
    string ObservationId,
    string SessionId,
    SourceKind SourceKind,
    DateTimeOffset ObservedAt,
    long TimeOffsetMs,
    string InputArtifactId,
    string DetectorId,
    string Label,
    double? RawScore,
    VerificationStatus Status,
    string? GroundTruthReference,
    string? Notes);
```

Before implementation, align naming, nullable conventions, target frameworks, and serialization with the repository's existing C# style.
