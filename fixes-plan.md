# Fixes Plan

Priority: stabilize single-node first, then preview expansion.

## Part 1: Single-Node Stability Gate (Independent)

Goal: no data surprises in single-node mode before preview.

### 1.1 Retention Policy (explicit contract)
- Default delete policy must stay OFF:
  - `-retention-bytes = -1`
  - `-retention-hours = 0`
- Retention deletion is opt-in only.
- Size-based retention is active only when `retention-bytes > 0`.
- Age-based retention is active only when `retention-hours > 0`.
- Topic-level retention overrides remain supported, but default behavior is no deletion.

### 1.2 Offset Store Durability
- Compact/recover/reopen path must remain crash-safe for single-node usage.
- `offsets.log` replace flow must be durability-safe enough for restart and power-loss windows.

### 1.3 Single-Node Acceptance
- No accidental segment deletion on default config.
- Offsets survive restart/recover cycles.
- Single-node smoke path remains stable with retention disabled by default.
- Replay/simple usage stays documented and usable for offset-based reruns during analysis.

## Part 2: Cluster and Replication Correctness (Independent)

Goal: fix cluster-grade correctness risks without blocking Part 1.

### 2.1 Delivery Semantics
- `P0`: ack currently happens before replication/ISR durability.
- Target policy already chosen: strict quorum durability semantics.

### 2.2 Divergence Handling
- `P1`: follower restart/failover path does not truncate divergent local tail.
- Replication alignment must handle stale suffix correctly.

### 2.3 Replication Worker Address Drift
- `P1`: running worker keeps stale leader endpoint if address changes.
- Assignment/update logic must include endpoint refresh criteria.

### 2.4 Controller Scope
- `single` mode is treated as single-node support path.
- Static multi-broker in `single` mode is not a release-gate path for preview.

## Part 3: SDK and Example Consistency (Independent)

Goal: make SDK behavior coherent across transports and easier for users.

### 3.1 HTTP/TCP Parity
- `P1`: HTTP transport currently breaks binary payload parity (`base64:` literal storage path).
- Chosen direction: HTTP is first-class transport, parity required.

### 3.2 Routing Contract
- `P2`: `auto_route=True` is exposed but not implemented end-to-end.
- Chosen direction: make `auto_route` real.

### 3.3 Fetch and Error Semantics
- `P2`: HTTP fetch ignores `max_bytes`.
- `P2`: HTTP error mapping is too coarse; examples can hide permanent failures behind polling loops.

### 3.4 Quality and Docs
- Strengthen transport-parity tests (binary payload, error mapping variants, cross-transport checks).
- Remove SDK README drift so docs match actual behavior.

## Execution Notes
- Parts 1, 2, and 3 are intentionally independent tracks.
- Current gate for preview is Part 1 completion.
- Parts 2 and 3 can proceed in parallel without changing Part 1 acceptance criteria.
