# Project Plan

## Status
- Single-node preview is ready: retention is off by default, WAL reopen is stable, and `make bb-full` passes.
- The preview entrypoints are documented in the root `README.md`, and the module READMEs now match the current broker/UI/SDK behavior.
- `wave-ui` builds and lints, and the main preview screens are aligned with the current broker contract.
- `wave-python-sdk` provides the TCP SDK and helper API for simple scenarios.

## Remaining Work

### 1. `wave-mq`: cluster / replication correctness
- Make quorum discipline explicit for multi-node scenarios so acks do not outrun durable replication.
- Finish truncation and alignment behavior for divergent tails after restart or failover.
- Ensure the replication worker does not keep a stale advertised leader endpoint after address changes.
- Keep single-node as the current preview gate; treat cluster preview as the next stage.

### 2. `wave-python-sdk`: transport parity, routing, and producer UX
- Make `auto_route` actually work instead of being a public no-op flag.
- Align HTTP and TCP transport behavior, especially for `max_bytes` and binary payload handling.
- Keep typed error mapping consistent across both transports.
- Keep keyed produce as the default high-level write path everywhere user-facing:
  SDK, examples, and other convenience clients should publish by key and let the broker choose the partition.
- Keep explicit partition produce available only as an advanced / low-level path for diagnostics,
  replay, and manual control.
- Review all client-facing surfaces for this rule:
  `wave-python-sdk`, examples, `mbctl`, and UI produce flows should not present explicit partition choice
  as the primary path.

### 3. `wave-ui`: browser-level preview smoke
- Add one browser smoke path for Dashboard, Topics, Topic Details, Consumer Groups, and Metrics.
- Verify the UI against a dockerized preview stack and reachable endpoint assumptions.
- If any screen is still experimental, mark it clearly in the UI.

### 4. Documentation / release hygiene
- Keep the root README as the preview entrypoint and keep the module READMEs aligned with it.
- Keep experimental cluster and transport limitations clearly labeled in the docs.
- Keep the example README files in sync with runnable scripts, compose files, and current default flows.
- Document the producer contract clearly:
  default produce is keyed and broker-routed; explicit partition produce is optional and secondary.

## Principle
- This file is the only source of truth for remaining work.
- Old sub-plans and developer notes are intentionally retired.
