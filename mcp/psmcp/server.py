"""Stdio MCP tools over the game's authenticated HTTP interface.

Architecture inspired by Gennadiyev/STS2MCP's Python bridge. This implementation
uses our protocol, snapshot and retry contracts, with the official MCP SDK v2.
"""

from __future__ import annotations

import argparse
import base64
import json
import math
import re
import sys
from contextlib import asynccontextmanager
from pathlib import Path
from typing import Any

from mcp.server import MCPServer
from mcp.types import CallToolResult, ImageContent, TextContent, ToolAnnotations
from pydantic import StrictInt, StrictStr

from . import __version__
from .bridge import BridgeClient, connection_path, error
from .help import HelpCatalog

INSTRUCTIONS = (
    "Observe with get_game_state, choose an exact availableActions[].name, then call perform_action with "
    "that observation's snapshotId and a new UUID. Supply only arguments listed in the chosen action's "
    "parameters, using its current choices and ranges. Re-read state after actions. For uncertain retries "
    "preserve the original action, snapshot, UUID and arguments. Use get_help when unsure; omit topic for "
    "the introduction and topic list. Help works while the game is offline."
)
USAGE = {
    "tool": "perform_action",
    "action": "Copy availableActions[].name exactly; kind, description and targetId are not action names.",
    "expectedSnapshotId": "Use snapshotId from the observation on which this decision is based.",
    "requestId": "New UUID for a new decision. Retry only with the original action, snapshot, UUID and arguments.",
    "arguments": "Use only the chosen action's parameters. Respect required, min/max/step and choices. "
    "action.targetId is the source; arguments.targetId may be a different legal target.",
    "afterDispatch": "Re-read state to verify effects; dispatched does not mean animations or confirmations completed.",
    "emptyList": "Inspect interaction.waitingFor, blockers and limitations. Wait or report the unsupported input.",
    "helpTopic": "actions",
}


def reply(value: dict[str, Any]) -> CallToolResult:
    return CallToolResult(
        is_error=value.get("success") is False,
        structured_content=value,
        content=[TextContent(text=json.dumps(value, ensure_ascii=False, allow_nan=False, separators=(",", ":")))],
    )


def valid_uuid(value: str) -> bool:
    return re.fullmatch(r"[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}", value) is not None


def valid_arguments(arguments: dict[str, Any]) -> bool:
    string_fields = {"targetId", "destinationId", "text"}
    int_fields = {"quantity", "x", "y", "rotation", "index"}
    bool_fields = {"flipped", "toggle"}
    number_fields = {"value", "upperValue"}
    for name, value in arguments.items():
        if name not in string_fields | int_fields | bool_fields | number_fields:
            return False
        if value is None:
            continue
        if name in string_fields and not isinstance(value, str):
            return False
        if name in int_fields and (type(value) is not int or not -(2**31) <= value < 2**31):
            return False
        if name in bool_fields and type(value) is not bool:
            return False
        if name in number_fields:
            if type(value) not in (int, float):
                return False
            try:
                if not math.isfinite(value):
                    return False
            except OverflowError:
                return False
    return True


def create_server(path: Path, *, bridge: BridgeClient | None = None) -> MCPServer:
    bridge = bridge or BridgeClient(path)
    help_catalog = HelpCatalog()

    @asynccontextmanager
    async def lifespan(server):
        try:
            yield {}
        finally:
            await bridge.aclose()

    server = MCPServer(
        "probably-stolen-mcp", version=__version__, instructions=INSTRUCTIONS, lifespan=lifespan, log_level="WARNING"
    )

    def read_tool(fn):
        return server.tool(
            annotations=ToolAnnotations(
                read_only_hint=True,
                destructive_hint=False,
                idempotent_hint=True,
                open_world_hint=False,
            )
        )(fn)

    @read_tool
    def get_help(topic: StrictStr | None = None) -> CallToolResult:
        """Read built-in help without a game connection. Omit topic for overview and the catalog.

        Topics are case-insensitive catalog IDs, never file paths or URLs. Use actions for snapshots,
        arguments and retries; items/appraisal/machines explain workflows and current support.
        Only live availableActions determine which actions can execute.
        """
        return reply(help_catalog.get(topic))

    @read_tool
    async def get_bridge_status() -> CallToolResult:
        """Check the authenticated Mod connection. success means connected, not that a save is loaded."""
        return reply(await bridge.send("health"))

    @read_tool
    async def get_game_state() -> CallToolResult:
        """Read the main-thread observation under state, including snapshotId and availableActions.

        Includes phase, cash, customer, inventory, trade, controls and interaction. public_claim features
        are unverified and quoted prices are not true values. Re-read after actions and animations.
        """
        return reply(await bridge.send("state"))

    async def state_result():
        result = await bridge.send("state")
        return result, result.get("state") if result["success"] else None

    @read_tool
    async def get_trade_state() -> CallToolResult:
        """Read current trade, counter, UI quotes, blockers and actions at the top level.

        Directions are from the player's perspective. Quotes apply to offered stacks; do not multiply
        by count. Wholesale totals may include items outside the counter. Use current actions to trade.
        """
        result, state = await state_result()
        if state is None:
            return reply(result)
        return reply(
            {
                "protocolVersion": result["protocolVersion"],
                **{
                    k: state.get(k)
                    for k in (
                        "snapshotId",
                        "gameLoaded",
                        "customer",
                        "trade",
                        "interaction",
                        "controls",
                        "availableActions",
                        "limitations",
                    )
                },
            }
        )

    @read_tool
    async def get_inventory(
        containerId: StrictStr | None = None, offset: StrictInt = 0, limit: StrictInt = 100
    ) -> CallToolResult:
        """Read rendered containers and opened contents, optionally filtering a container entityId.

        offset is nonnegative, limit is 1..200. Includes shape, ownership, known features and native
        statusText. Labels can be empty. Unopened contents are not read. Use returned entity references
        in current actions, not numeric item IDs. This query never moves or uses items; see get_help(items).
        """
        if offset < 0 or not 1 <= limit <= 200:
            return reply(error("invalid_request", "offset must be nonnegative; limit must be 1..200."))
        result, state = await state_result()
        if state is None:
            return reply(result)
        containers = [c for c in state["containers"] if containerId is None or c.get("entityId") == containerId]
        if containerId is not None and not containers:
            return reply(error("entity_unavailable", "This container is not currently rendered. Open it first."))
        items = [i for i in state["inventory"] if containerId is None or i.get("containerId") == containerId]
        keys = (
            "entityId",
            "label",
            "role",
            "parentItemId",
            "windowTitle",
            "kind",
            "insertLocked",
            "removeLocked",
            "shape",
        )
        return reply(
            {
                "protocolVersion": result["protocolVersion"],
                "snapshotId": state["snapshotId"],
                "gameLoaded": state.get("gameLoaded"),
                "inventoryScope": state.get("inventoryScope"),
                "containers": [{**{k: c.get(k) for k in keys}, "itemCount": len(c["items"])} for c in containers],
                "total": len(items),
                "offset": offset,
                "inventory": items[offset : offset + limit],
            }
        )

    @read_tool
    async def get_available_actions() -> CallToolResult:
        """Return snapshotId, availableActions, interaction, limitations and usage at the top level.

        Copy exact action.name into perform_action. kind is a category; targetId identifies the source.
        Fill arguments only from parameters, respecting required, ranges, steps and choices. For
        item_use_on, arguments.targetId is the destination, distinct from the tool's action.targetId.
        Never invent references. Empty lists require inspecting interaction. See get_help(actions).
        """
        result, state = await state_result()
        if state is None:
            return reply(result)
        return reply(
            {
                "protocolVersion": result["protocolVersion"],
                **{
                    k: state.get(k)
                    for k in (
                        "snapshotId",
                        "gameLoaded",
                        "availableActions",
                        "dialogueChoices",
                        "interaction",
                        "limitations",
                    )
                },
                "usage": USAGE,
            }
        )

    @read_tool
    async def get_ui_state() -> CallToolResult:
        """Read visible text, controls, values, disabled reasons and available actions.

        Match control.actions to exact availableActions names. Distinguish duplicate labels using
        windowTitle, nearbyText, bounds and get_view. Disabled or occluded controls cannot be invoked.
        """
        result, state = await state_result()
        if state is None:
            return reply(result)
        return reply(
            {
                k: state.get(k)
                for k in (
                    "snapshotId",
                    "context",
                    "interaction",
                    "screenText",
                    "controls",
                    "availableActions",
                )
            }
        )

    @read_tool
    async def get_entity(entityId: StrictStr) -> CallToolResult:
        """Inspect an exposed item, container or control by entityId. Never opens contents or performs scans."""
        result, state = await state_result()
        if state is None:
            return reply(result)
        entity = next(
            (
                e
                for group in ("inventory", "containers", "controls")
                for e in state[group]
                if e.get("entityId") == entityId
            ),
            None,
        )
        if entity is None:
            return reply(error("entity_unavailable", "This entity is not currently exposed; read current state."))
        return reply(
            {
                "snapshotId": state["snapshotId"],
                "entity": entity,
                "availableActions": [a for a in state["availableActions"] if a.get("targetId") == entityId],
            }
        )

    @read_tool
    async def get_view() -> CallToolResult:
        """Capture the rendered game frame as an MCP image and its observation. Longest edge is at most 1600.

        Includes inspection artwork and visible panels. Does not reveal hidden item truth.
        """
        result = await bridge.send("view")
        if not result["success"]:
            return reply(result)
        view = result["view"]
        try:
            if not base64.b64decode(view["imageBase64"], validate=True):
                raise ValueError("Empty image")
        except ValueError:
            return reply(error("invalid_bridge_response", "The bridge returned invalid image data."))
        state = result.get("state") or {}
        response = reply(
            {
                "protocolVersion": result["protocolVersion"],
                **{k: view[k] for k in ("snapshotId", "width", "height", "mimeType")},
                "context": state.get("context"),
                "interaction": state.get("interaction"),
            }
        )
        response.content.append(ImageContent(data=view["imageBase64"], mime_type=view["mimeType"]))
        return response

    @read_tool
    async def get_action_status(requestId: StrictStr) -> CallToolResult:
        """Read the original retained result without replay. Last 256 commands are kept until Mod restart.

        dispatched means handler invoked, not completion. action_not_found does not prove it never ran.
        """
        if not valid_uuid(requestId):
            return reply(error("invalid_request", "requestId must be a UUID."))
        return reply(await bridge.send("actions/" + requestId))

    @server.tool(
        annotations=ToolAnnotations(
            read_only_hint=False,
            destructive_hint=True,
            idempotent_hint=True,
            open_world_hint=False,
        )
    )
    async def perform_action(
        action: StrictStr, expectedSnapshotId: StrictStr, requestId: StrictStr, arguments: dict[str, Any] | None = None
    ) -> CallToolResult:
        """Dispatch one exact availableActions[].name with that observation's snapshotId and a new UUID.

        arguments contains only the chosen action's parameters; omit for parameterless actions.
        Retry uncertainty only after querying status/state, preserving action, snapshot, UUID and all
        arguments. On stale_snapshot make a new decision with a new UUID. dispatched means handler ran,
        not that animation or confirmation finished. Actions affect progress and may be saved.
        """
        if (
            not valid_uuid(requestId)
            or not action.strip()
            or len(action) > 128
            or not expectedSnapshotId.strip()
            or len(expectedSnapshotId) > 128
        ):
            return reply(
                error("invalid_request", "Provide a UUID and nonempty action/snapshot, at most 128 characters.")
            )
        if arguments is not None and not valid_arguments(arguments):
            return reply(error("invalid_arguments", "Use only recognized argument names and their documented types."))
        return reply(
            await bridge.send(
                "actions",
                {
                    "action": action,
                    "expectedSnapshotId": expectedSnapshotId,
                    "requestId": requestId,
                    "arguments": arguments,
                },
            )
        )

    return server


def main() -> None:
    parser = argparse.ArgumentParser(description="Probably Stolen MCP (Python stdio server)")
    parser.add_argument("--connection", help="connection.json path; overrides PSMCP_CONNECTION_FILE")
    parser.add_argument("--version", action="version", version=__version__)
    args = parser.parse_args()
    # MCP protocol exclusively owns stdout, including on non-UTF-8 Windows terminals.
    sys.stderr.reconfigure(encoding="utf-8")
    create_server(connection_path(args.connection)).run(transport="stdio")


if __name__ == "__main__":
    main()
