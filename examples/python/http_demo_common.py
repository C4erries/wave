"""Shared HTTP helpers for wave-mq demo scripts.

These helpers intentionally stay thin and use only stdlib HTTP + JSON.
"""

from __future__ import annotations

import base64
import json
import time
from dataclasses import dataclass
from typing import Any
from urllib.error import HTTPError, URLError
from urllib.parse import quote
from urllib.request import Request, urlopen


DEFAULT_TIMEOUT = 10.0
DEFAULT_PARTITIONS = 1
DEFAULT_REPLICATION_FACTOR = 1


@dataclass(slots=True)
class HttpError(RuntimeError):
    status: int
    url: str
    body: str
    data: dict[str, Any] | None = None

    def __str__(self) -> str:  # pragma: no cover - trivial
        if self.data and isinstance(self.data, dict):
            err = self.data.get("error")
            msg = self.data.get("message")
            parts = [f"HTTP {self.status}"]
            if err:
                parts.append(str(err))
            if msg:
                parts.append(str(msg))
            return ": ".join(parts)
        return f"HTTP {self.status} for {self.url}: {self.body}"


class TransportError(RuntimeError):
    pass


def normalize_base_url(addr: str) -> str:
    value = addr.strip()
    if not value:
        raise ValueError("addr is required")
    if "://" not in value:
        value = "http://" + value
    return value.rstrip("/")


def api_url(addr: str, path: str) -> str:
    return f"{normalize_base_url(addr)}{path}"


def _decode_body(raw: bytes) -> tuple[str, dict[str, Any] | None]:
    text = raw.decode("utf-8", errors="replace")
    if not text.strip():
        return text, None
    try:
        return text, json.loads(text)
    except json.JSONDecodeError:
        return text, None


def request_json(method: str, url: str, payload: dict[str, Any] | None = None, timeout: float = DEFAULT_TIMEOUT) -> dict[str, Any]:
    data = None if payload is None else json.dumps(payload).encode("utf-8")
    headers = {"Accept": "application/json"}
    if data is not None:
        headers["Content-Type"] = "application/json"

    req = Request(url, data=data, headers=headers, method=method)
    try:
        with urlopen(req, timeout=timeout) as resp:
            raw = resp.read()
    except HTTPError as exc:
        body_text, body_json = _decode_body(exc.read())
        raise HttpError(exc.code, url, body_text, body_json) from exc
    except URLError as exc:
        raise TransportError(f"{url}: {exc.reason}") from exc

    text, data_json = _decode_body(raw)
    if data_json is None:
        raise TransportError(f"{url}: expected JSON response, got {text!r}")
    return data_json


def ensure_topic(
    addr: str,
    topic: str,
    partitions: int = DEFAULT_PARTITIONS,
    replication_factor: int = DEFAULT_REPLICATION_FACTOR,
    timeout: float = DEFAULT_TIMEOUT,
) -> dict[str, Any]:
    payload = {
        "name": topic,
        "partitions": partitions,
        "replicationFactor": replication_factor,
    }
    url = api_url(addr, "/api/topics")
    try:
        return request_json("POST", url, payload, timeout=timeout)
    except HttpError as exc:
        data = exc.data or {}
        if exc.status == 409 and data.get("error") == "topic_exists":
            return data
        raise


def produce_message(
    addr: str,
    topic: str,
    value: str,
    key: str,
    timeout: float = DEFAULT_TIMEOUT,
) -> dict[str, Any]:
    payload = {"key": key, "value": value}
    url = api_url(addr, f"/api/topics/{quote(topic, safe='')}/messages")
    return request_json("POST", url, payload, timeout=timeout)


def produce_partition_message(
    addr: str,
    topic: str,
    partition: int,
    value: str,
    key: str | None = None,
    timeout: float = DEFAULT_TIMEOUT,
) -> dict[str, Any]:
    payload: dict[str, Any] = {"value": value}
    if key is not None:
        payload["key"] = key
    url = api_url(addr, f"/api/topics/{quote(topic, safe='')}/partitions/{partition}/messages")
    return request_json("POST", url, payload, timeout=timeout)


def fetch_messages(
    addr: str,
    topic: str,
    offset: int = 0,
    limit: int = 50,
    timeout: float = DEFAULT_TIMEOUT,
) -> list[dict[str, Any]]:
    url = api_url(addr, f"/api/topics/{quote(topic, safe='')}/messages?offset={offset}&limit={limit}")
    data = request_json("GET", url, timeout=timeout)
    if not isinstance(data, list):
        raise TransportError(f"{url}: expected list response")
    return data


def fetch_partition_messages(
    addr: str,
    topic: str,
    partition: int,
    offset: int = 0,
    limit: int = 50,
    timeout: float = DEFAULT_TIMEOUT,
) -> list[dict[str, Any]]:
    path = f"/api/topics/{quote(topic, safe='')}/partitions/{partition}/messages?offset={offset}&limit={limit}"
    url = api_url(addr, path)
    data = request_json("GET", url, timeout=timeout)
    if not isinstance(data, list):
        raise TransportError(f"{url}: expected list response")
    return data


def get_topic_detail(
    addr: str,
    topic: str,
    timeout: float = DEFAULT_TIMEOUT,
) -> dict[str, Any] | None:
    url = api_url(addr, f"/api/topics/{quote(topic, safe='')}")
    try:
        data = request_json("GET", url, timeout=timeout)
    except HttpError as exc:
        if exc.status == 404:
            return None
        raise
    if not isinstance(data, dict):
        raise TransportError(f"{url}: expected object response")
    return data


def partition_detail(
    addr: str,
    topic: str,
    partition: int,
    timeout: float = DEFAULT_TIMEOUT,
) -> dict[str, Any] | None:
    detail = get_topic_detail(addr, topic, timeout=timeout)
    if detail is None:
        return None
    partitions = detail.get("partitions")
    if not isinstance(partitions, list):
        raise TransportError("topic detail missing partitions")
    for item in partitions:
        if int(item.get("id", -1)) == partition:
            return item
    return None


def get_committed_offset(
    addr: str,
    group: str,
    topic: str,
    partition: int,
    timeout: float = DEFAULT_TIMEOUT,
) -> int | None:
    path = f"/api/consumers/{quote(group, safe='')}/topics/{quote(topic, safe='')}/partitions/{partition}/offset"
    url = api_url(addr, path)
    try:
        data = request_json("GET", url, timeout=timeout)
    except HttpError as exc:
        if exc.status == 404:
            return None
        raise
    offset = data.get("offset") if isinstance(data, dict) else None
    if offset is None:
        return None
    return int(offset)


def commit_offset(
    addr: str,
    group: str,
    topic: str,
    partition: int,
    offset: int,
    timeout: float = DEFAULT_TIMEOUT,
) -> dict[str, Any]:
    path = f"/api/consumers/{quote(group, safe='')}/topics/{quote(topic, safe='')}/partitions/{partition}/offset"
    url = api_url(addr, path)
    return request_json("POST", url, {"offset": offset}, timeout=timeout)


def resolve_start_offset(
    addr: str,
    topic: str,
    partition: int,
    start_from: str,
    fallback_offset: int,
    timeout: float = DEFAULT_TIMEOUT,
) -> int:
    if start_from == "offset":
        return fallback_offset

    detail = partition_detail(addr, topic, partition, timeout=timeout)
    if detail is None:
        return 0 if start_from == "latest" else fallback_offset

    if start_from == "earliest":
        return int(detail.get("startOffset", fallback_offset))

    high_watermark = int(detail.get("highWatermark", -1))
    return max(0, high_watermark + 1)


def decode_record_field(value: Any) -> str:
    if value is None:
        return ""
    if not isinstance(value, str):
        return str(value)
    if value.startswith("base64:"):
        raw = base64.b64decode(value.removeprefix("base64:"))
        return raw.decode("utf-8", errors="replace")
    return value


def sort_records_ascending(records: list[dict[str, Any]]) -> list[dict[str, Any]]:
    return sorted(records, key=lambda item: int(item.get("offset", 0)))


def record_text(record: dict[str, Any]) -> str:
    offset = int(record.get("offset", 0))
    key = decode_record_field(record.get("key"))
    value = decode_record_field(record.get("value"))
    timestamp = str(record.get("timestamp", ""))
    return f"offset={offset} ts={timestamp} key={key} value={value}"


def sleep_interval(seconds: float) -> None:
    time.sleep(seconds)
