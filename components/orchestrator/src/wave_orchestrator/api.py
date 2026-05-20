from __future__ import annotations

import json
import logging
import threading
from http.server import BaseHTTPRequestHandler, HTTPServer
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from wave_orchestrator.orchestrator import Orchestrator

logger = logging.getLogger(__name__)

_CORS_HEADERS = {
    "Access-Control-Allow-Origin": "*",
    "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
    "Access-Control-Allow-Headers": "Content-Type",
}


def _make_handler(orchestrator: "Orchestrator") -> type:
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, fmt: str, *args: object) -> None:
            logger.debug("HTTP %s", fmt % args)

        def _send_cors(self) -> None:
            for k, v in _CORS_HEADERS.items():
                self.send_header(k, v)

        def _send_json(self, code: int, body: dict) -> None:
            data = json.dumps(body).encode()
            self.send_response(code)
            self.send_header("Content-Type", "application/json")
            self._send_cors()
            self.end_headers()
            self.wfile.write(data)

        def do_OPTIONS(self) -> None:
            self.send_response(204)
            self._send_cors()
            self.end_headers()

        def do_GET(self) -> None:
            if self.path != "/status":
                self._send_json(404, {"error": f"Not found: {self.path}"})
                return
            try:
                status = orchestrator.status()
                self._send_json(200, status)
            except Exception as exc:
                logger.exception("Error in GET /status")
                self._send_json(500, {"error": str(exc)})

        def do_POST(self) -> None:
            if self.path != "/reload":
                self._send_json(404, {"error": f"Not found: {self.path}"})
                return
            try:
                length = int(self.headers.get("Content-Length", 0))
                raw = self.rfile.read(length) if length else b"{}"
                body = json.loads(raw)
            except (json.JSONDecodeError, ValueError) as exc:
                self._send_json(400, {"error": f"Invalid JSON: {exc}"})
                return

            config = body.get("config")
            if not config:
                self._send_json(400, {"error": "'config' field is required"})
                return

            try:
                orchestrator.reload(config)
                status = orchestrator.status()
                self._send_json(200, {"ok": True, "mode": status["mode"]})
            except Exception as exc:
                logger.exception("Error in POST /reload")
                self._send_json(400, {"error": str(exc)})

    return Handler


def start_api_server(orchestrator: "Orchestrator", port: int = 8099) -> HTTPServer:
    handler_cls = _make_handler(orchestrator)
    server = HTTPServer(("", port), handler_cls)
    thread = threading.Thread(target=server.serve_forever, daemon=True, name="wave-orchestrator-api")
    thread.start()
    logger.info("API server listening on http://localhost:%d", port)
    return server
