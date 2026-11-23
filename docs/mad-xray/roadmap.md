# MAD X-Ray implementation roadmap

## Stage 0 — Repository and evaluation baseline

- Keep the existing simulator behavior intact.
- Audit project target frameworks and current test conventions before adding assemblies.
- Keep MAD X-Ray components isolated from simulated gameplay logic.
- Add deterministic fixtures and source provenance.
- Do not label simulator-generated tokens as live detections.

**Exit criteria:** baseline build/test results are recorded; new contracts have unit tests; no live-game integration is implied.

## Stage 1 — Phase B.1: metric and dataset audit

- Recompute all metrics from raw labels and predictions.
- Verify train/dev/test separation by recording session, not adjacent frames.
- Report both multiclass metrics and explicitly defined binary GEM-cue metrics.
- Publish confusion matrices, per-class precision/recall, macro-F1, and false-positive examples.
- Ensure all percentages can be reproduced by a checked-in script or test.
- Record detector and dataset versions.

**Exit criteria:** all displayed metrics reconcile mathematically with the confusion matrix; no test-session leakage; false-positive review is documented.

## Stage 2 — Four offline source adapters

### 2A Radar/HUD
- Define accepted input formats and coordinate/time metadata.
- Begin with manual annotation or user-supplied recordings.
- Treat a HUD cue as a cue unless an authorized documented source establishes its meaning.

### 2B Audio
- Analyze permitted recordings offline.
- Extract time-aligned features (for example, energy bands or spectrogram events) without assuming a sound uniquely identifies a GEM.
- Compare events against independently labeled sessions.
- Report false positives such as unrelated sound effects.

### 2C Visual
- Use temporal context and session-level splits.
- Separate visible GEMs, GEM-related effects, ambiguous cues, and no observable cue.
- Evaluate precision/recall and false-positive rates by scene and recording session.
- State clearly that fully occluded objects cannot be recovered from absent pixels.

### 2D Official API/SDK
- Research public documentation and permitted use.
- Record the exact API/SDK version, authorization requirements, terms, rate limits, and data semantics.
- If no suitable documented interface exists, mark the source unavailable. Do not reverse-engineer private protocols or bypass protections.

**Exit criteria:** each source has an input contract, provenance, timestamp semantics, offline fixtures, evaluation metrics, and documented limitations.

## Stage 3 — Evidence correlation and verification

- Normalize timestamps and correlate observations within documented tolerances.
- Preserve raw observations and disagreements.
- Prevent duplicate/correlated cues from being counted as independent evidence.
- Require independent authorized ground truth for `ConfirmedGem`.
- Keep unknown coordinates null.

**Exit criteria:** deterministic fusion tests cover missing sources, conflicting sources, duplicate events, timestamp drift, and unverified cues.

## Stage 4 — MAD X-Ray presentation

- Build a standalone overlay or review UI that consumes the evidence model.
- Show status labels, source badges, timestamps, and provenance.
- Use distinct styling for confirmed, observed, unverified, rejected, and unknown.
- Provide a review panel for false positives and source disagreements.
- Do not display invented world positions.

**Exit criteria:** UI is tested with replay fixtures, DPI/scaling changes, missing inputs, and high event volume.

## Stage 5 — Authorized integration gate

Before connecting any live source, require:
- documented permission and terms;
- a stable, documented interface;
- an explicit user-controlled opt-in;
- no anti-cheat evasion, process hiding, injection, unauthorized memory access, or private-protocol interception;
- a safe failure mode that disables the adapter when authorization or interface assumptions are not met.

## Priority order

1. Correctness of Phase B.1 metrics.
2. Evidence contracts and provenance.
3. Offline Radar/HUD, Audio, and Visual adapters.
4. Official API/SDK documentation review.
5. Evidence fusion.
6. Overlay/review UI.

Do not optimize for a high apparent GEM count. Optimize for low false-positive rate, reproducibility, and honest verification states.
