from __future__ import annotations

import argparse
import json
import logging
import signal
import sys

from wave_orchestrator.orchestrator import ConfigError, Orchestrator, OrchestratorError


def _setup_logging() -> None:
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s  %(levelname)-8s  %(name)s  %(message)s",
        datefmt="%Y-%m-%d %H:%M:%S",
    )


def _build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(
        prog="wave-orchestrator",
        description="Wave pipeline orchestrator — manages wave-gen/wave-osc/wave-fft processes",
    )
    sub = p.add_subparsers(dest="cmd", required=True)

    s_start = sub.add_parser("start", help="Start processes from a config file")
    s_start.add_argument("--config", required=True, help="Path to graph YAML config")
    s_start.add_argument("--serve-api", action="store_true", help="Also start HTTP API server")
    s_start.add_argument("--api-port", type=int, default=8099, help="HTTP API port (default 8099)")

    sub.add_parser("stop", help="Stop all running processes")

    s_reload = sub.add_parser("reload", help="Reload config (stop current, start new)")
    s_reload.add_argument("--config", required=True, help="Path to new graph YAML config")

    sub.add_parser("status", help="Show running processes and their liveness")

    return p


def cmd_start(args: argparse.Namespace, orch: Orchestrator) -> None:
    try:
        orch.start(args.config)
    except (ConfigError, OrchestratorError) as exc:
        print(f"Error: {exc}", file=sys.stderr)
        sys.exit(1)

    status = orch.status()
    print(f"Mode '{status['mode']}' started — {len(status['processes'])} process(es).")

    if not args.serve_api:
        return

    from wave_orchestrator.api import start_api_server

    server = start_api_server(orch, args.api_port)
    print(f"API listening on http://localhost:{args.api_port}")
    print("Press Ctrl-C to stop.")

    def _shutdown(sig: int, frame: object) -> None:
        print("\nShutting down…")
        server.shutdown()
        orch.stop()
        sys.exit(0)

    signal.signal(signal.SIGINT, _shutdown)
    signal.signal(signal.SIGTERM, _shutdown)
    signal.pause()


def cmd_stop(orch: Orchestrator) -> None:
    orch.stop()
    print("Stopped.")


def cmd_reload(args: argparse.Namespace, orch: Orchestrator) -> None:
    try:
        orch.reload(args.config)
    except (ConfigError, OrchestratorError) as exc:
        print(f"Error: {exc}", file=sys.stderr)
        sys.exit(1)

    status = orch.status()
    print(f"Mode '{status['mode']}' active — {len(status['processes'])} process(es).")


def cmd_status(orch: Orchestrator) -> None:
    status = orch.status()
    if status["mode"] is None:
        print("No processes running.")
        return
    print(f"Mode: {status['mode']}")
    print(f"{'NAME':<20} {'KIND':<10} {'TYPE':<10} {'PID':<8} {'ALIVE'}")
    print("-" * 58)
    for p in status["processes"]:
        alive = "yes" if p["alive"] else "DEAD"
        print(f"{p['name']:<20} {p['kind']:<10} {p['type']:<10} {p['pid']:<8} {alive}")


def main() -> None:
    _setup_logging()
    parser = _build_parser()
    args = parser.parse_args()
    orch = Orchestrator()

    if args.cmd == "start":
        cmd_start(args, orch)
    elif args.cmd == "stop":
        cmd_stop(orch)
    elif args.cmd == "reload":
        cmd_reload(args, orch)
    elif args.cmd == "status":
        cmd_status(orch)


if __name__ == "__main__":
    main()
