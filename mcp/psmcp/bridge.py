"""Authenticated, bounded access to the existing protocol-2 game bridge."""

from __future__ import annotations

import asyncio
import json
import os
import re
from pathlib import Path
from typing import Any

import httpx

PROTOCOL_VERSION = 2
MOD_ID = "probably-stolen-mcp"
MAX_CONNECTION_BYTES = 16_384
MAX_REQUEST_BYTES = 16_384
MAX_RESPONSE_BYTES = 4 * 1024 * 1024


def default_connection_path() -> Path:
    return Path.home() / "AppData/LocalLow/Questing Goose Studio/Probably Stolen/ModData" / MOD_ID / "connection.json"


def connection_path(argument: str | None = None) -> Path:
    return Path(argument or os.environ.get("PSMCP_CONNECTION_FILE") or default_connection_path()).resolve()


def error(code: str, message: str, request_id: str | None = None) -> dict[str, Any]:
    return {
        "protocolVersion": PROTOCOL_VERSION,
        "success": False,
        "errorCode": code,
        "message": message,
        "requestId": request_id,
        "actionStatus": None,
        "state": None,
        "view": None,
    }


def _reject_constant(value: str) -> None:
    raise ValueError("Non-finite JSON number")


def decode_json(data: bytes) -> Any:
    return json.loads(data.decode("utf-8-sig"), parse_constant=_reject_constant)


class BridgeClient:
    def __init__(self, path: Path, *, timeout: float = 8, transport: httpx.AsyncBaseTransport | None = None):
        self.path = path
        self.timeout = timeout
        self.http = httpx.AsyncClient(
            timeout=timeout,
            trust_env=False,
            follow_redirects=False,
            transport=transport,
            headers={"Accept-Encoding": "identity"},
        )

    async def aclose(self) -> None:
        await self.http.aclose()

    def _connection(self) -> tuple[str, str] | dict[str, Any]:
        # Read every call: restarting the Mod rotates its token and may change its port.
        try:
            with self.path.open("rb") as stream:
                data = stream.read(MAX_CONNECTION_BYTES + 1)
            if len(data) > MAX_CONNECTION_BYTES:
                raise ValueError("Oversized connection file")
            connection = decode_json(data)
            if not isinstance(connection, dict):
                raise ValueError("Expected an object")
        except (OSError, ValueError, RecursionError):
            return error(
                "connection_file_unavailable",
                "Enable Probably Automated and restart the game. Set --connection or PSMCP_CONNECTION_FILE "
                "to the connection.json path reported by the Mod.",
            )
        version = connection.get("protocolVersion")
        if type(version) is not int or version != PROTOCOL_VERSION:
            return error("protocol_mismatch", "Update the Mod and MCP server to matching bridge protocol versions.")
        url, token = connection.get("baseUrl"), connection.get("token")
        match = re.fullmatch(r"http://127\.0\.0\.1:([0-9]{1,5})/?", url) if isinstance(url, str) else None
        if (
            not match
            or not 1024 <= int(match[1]) <= 65535
            or not isinstance(token, str)
            or not re.fullmatch(r"[!-~]{32,256}", token)
        ):
            return error("invalid_connection", "The connection file must contain a loopback HTTP URL and valid token.")
        return url.rstrip("/") + "/", token

    async def send(self, route: str, action: dict[str, Any] | None = None) -> dict[str, Any]:
        connection = self._connection()
        if isinstance(connection, dict):
            return connection
        url, token = connection
        body = None
        if action is not None:
            try:
                body = json.dumps(action, ensure_ascii=False, allow_nan=False, separators=(",", ":")).encode("utf-8")
            except (ValueError, UnicodeError, TypeError):
                return error("invalid_arguments", "Action arguments must be valid JSON values.")
            if len(body) > MAX_REQUEST_BYTES:
                return error("invalid_request", "The action body must not exceed 16 KiB.")
        headers = {"Authorization": "Bearer " + token}
        if body is not None:
            headers["Content-Type"] = "application/json; charset=utf-8"
        try:
            # An overall deadline also bounds a server which sends an endless trickle.
            async with asyncio.timeout(self.timeout):
                async with self.http.stream(
                    "GET" if action is None else "POST",
                    url + route,
                    content=body,
                    headers=headers,
                ) as response:
                    data = bytearray()
                    async for chunk in response.aiter_bytes(chunk_size=65_536):
                        data.extend(chunk)
                        if len(data) > MAX_RESPONSE_BYTES:
                            raise ValueError("Oversized bridge response")
                    result = decode_json(bytes(data))
                    if not isinstance(result, dict) or type(result.get("success")) is not bool:
                        raise ValueError("Invalid bridge envelope")
                    if type(result.get("protocolVersion")) is not int or result["protocolVersion"] != PROTOCOL_VERSION:
                        if action is not None:
                            return self._uncertain(action)
                        return error("protocol_mismatch", "The bridge returned an incompatible response.")
                    if not response.is_success and result["success"]:
                        if action is not None:
                            return self._uncertain(action)
                        return error("bridge_http_error", "The bridge returned an unsuccessful HTTP status.")
                    if result["success"]:
                        self._validate_payload(route, result)
                    return result
        except (TimeoutError, httpx.TimeoutException):
            return (
                self._uncertain(action)
                if action is not None
                else error(
                    "bridge_timeout",
                    "The game bridge timed out. Check whether the game is responsive.",
                )
            )
        except httpx.HTTPError:
            return (
                self._uncertain(action)
                if action is not None
                else error(
                    "bridge_unavailable",
                    "Cannot reach the Mod. Start the game with Probably Automated enabled.",
                )
            )
        except (ValueError, RecursionError):
            return (
                self._uncertain(action)
                if action is not None
                else error(
                    "invalid_bridge_response",
                    "The bridge did not return the expected JSON response.",
                )
            )

    @staticmethod
    def _uncertain(action: dict[str, Any]) -> dict[str, Any]:
        return error(
            "action_outcome_unknown",
            "The action may have executed. Query get_action_status and current state first. "
            "Retry only with the original action, snapshot, UUID and complete arguments unchanged.",
            action.get("requestId"),
        )

    @staticmethod
    def _validate_payload(route: str, result: dict[str, Any]) -> None:
        if route == "state":
            state = result.get("state")
            if not isinstance(state, dict) or not isinstance(state.get("snapshotId"), str):
                raise ValueError("Missing state")
            for name in ("inventory", "containers", "controls", "availableActions"):
                values = state.get(name)
                if not isinstance(values, list) or any(not isinstance(item, dict) for item in values):
                    raise ValueError("Invalid state collection")
            for container in state["containers"]:
                if not isinstance(container.get("items"), list):
                    raise ValueError("Invalid container items")
        if route == "view":
            view = result.get("view")
            if (
                not isinstance(view, dict)
                or not isinstance(view.get("imageBase64"), str)
                or view.get("mimeType") not in ("image/jpeg", "image/png")
                or any(type(view.get(key)) is not int or not 1 <= view[key] <= 1600 for key in ("width", "height"))
                or not isinstance(view.get("snapshotId"), str)
                or (result.get("state") is not None and not isinstance(result["state"], dict))
            ):
                raise ValueError("Invalid screenshot")
