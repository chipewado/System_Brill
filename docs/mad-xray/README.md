# MAD X-Ray

MAD X-Ray is a research and visualization layer for evidence-backed GEM observations in BrilliantCrypto. The project must distinguish what is visibly observed, what is merely suggested by a signal, and what has been independently verified.

## Current repository baseline

The existing solution is simulator-first. It contains `Assistant.Core`, `Assistant.Persistence`, `Assistant.Runner`, `Assistant.Sim`, and `Assistant.Tests`. The current simulator token model is useful for deterministic testing, but simulated tokens are not evidence about a live game session.

This document starts a separate MAD X-Ray workstream. It does not claim live-game compatibility or that any GEM has been detected in BrilliantCrypto.

## Goal

Present a trustworthy map/list of GEM evidence using four independently evaluated source families:

1. **Radar/HUD** — observations from user-provided or otherwise permitted recordings/screenshots and documented, authorized interfaces.
2. **Audio** — time-aligned features from permitted recordings, evaluated against independently labeled events.
3. **Visual** — visible pixels and temporal visual cues from permitted recordings. It cannot detect an object that is fully occluded and absent from the rendered image.
4. **Official API/SDK** — only documented interfaces whose terms and permissions explicitly allow the intended use.

## Non-goals

- No kernel driver, process hiding, anti-cheat bypass, GameGuard evasion, process injection, unauthorized memory reading, or private-protocol interception.
- No claim that an audio tone, HUD marker, color blob, or model score proves a GEM exists.
- No underground/world-coordinate inference from screen pixels without a valid, independently established coordinate transform.
- No automated gameplay or actions that violate the game's rules.

## Evidence-first workflow

`Source capture → timestamp normalization → source-specific observation → evidence validation → cross-source correlation → verification state → presentation`

Every observation must retain provenance. Fusion must not erase disagreements between sources. Missing or unavailable sources are represented as unavailable, not as negative evidence.

## Trust states

- `ConfirmedGem`: independently verified by an authorized ground-truth source.
- `ObservedGem`: GEM is directly visible in a permitted visual source, but world identity/location is not independently verified.
- `UnverifiedSignal`: one or more cues suggest a GEM, but evidence is insufficient.
- `RejectedSignal`: reviewed evidence shows the cue is not a GEM.
- `Unknown`: insufficient, missing, or conflicting evidence.

Only `ConfirmedGem` may be presented as confirmed. All other states must be visibly labeled.

## Development rule

Build deterministic contracts, replayable offline tests, and evaluation reports before connecting any live source. A source adapter may be added only after its permission, data contract, timestamp semantics, and failure behavior are documented.
