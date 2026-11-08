import base64
import json
from uuid import uuid4

import httpx
import pytest
from psmcp.bridge import BridgeClient
from psmcp.server import create_server
from test_bridge import TOKEN

from mcp import Client

PNG = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a5XcAAAAASUVORK5CYII="


def snapshot():
    item = {"entityId": "item-1", "containerId": "bag", "statusText": "生长中 2/4", "count": 1}
    return {
        "snapshotId": "revision-1",
        "gameLoaded": True,
        "inventoryScope": "rendered",
        "inventory": [item],
        "containers": [{"entityId": "bag", "label": "种子输入", "items": [item]}],
        "controls": [{"entityId": "control-1", "value": 4, "nearbyText": "Day 1"}],
        "availableActions": [{"name": "set_value:1", "targetId": "item-1", "parameters": [{"name": "value"}]}],
        "dialogueChoices": [],
        "limitations": [],
        "interaction": {"kind": "ready"},
        "context": {},
        "trade": {"quotes": [{"amount": 5_000_000_000}]},
        "screenText": ["店铺"],
    }


@pytest.fixture
def tool_server(tmp_path):
    connection = tmp_path / "connection.json"
    connection.write_text(json.dumps({"protocolVersion": 2, "baseUrl": "http://127.0.0.1:15527/", "token": TOKEN}))
    calls = []

    def handle(request):
        calls.append(request)
        result = {"protocolVersion": 2, "success": True, "state": snapshot()}
        if request.url.path == "/view":
            result["view"] = {
                "snapshotId": "revision-1",
                "width": 1,
                "height": 1,
                "mimeType": "image/png",
                "imageBase64": PNG,
            }
        if request.method == "POST":
            result.update({"requestId": json.loads(request.content)["requestId"], "actionStatus": "dispatched"})
        return httpx.Response(200, json=result)

    return create_server(connection, bridge=BridgeClient(connection, transport=httpx.MockTransport(handle))), calls


async def test_tool_schemas_and_projections(tool_server):
    server, calls = tool_server
    async with Client(server) as client:
        tools = {t.name: t for t in (await client.list_tools()).tools}
        assert set(tools) == {
            "get_help",
            "get_bridge_status",
            "get_game_state",
            "get_trade_state",
            "get_inventory",
            "get_available_actions",
            "get_ui_state",
            "get_entity",
            "get_view",
            "get_action_status",
            "perform_action",
        }
        assert tools["get_help"].annotations.read_only_hint
        assert tools["perform_action"].annotations.destructive_hint
        assert set(tools["perform_action"].input_schema["required"]) == {"action", "expectedSnapshotId", "requestId"}
        state = (await client.call_tool("get_game_state")).structured_content["state"]
        trade = (await client.call_tool("get_trade_state")).structured_content
        assert trade["snapshotId"] == state["snapshotId"]
        assert trade["trade"]["quotes"][0]["amount"] == 5_000_000_000
        actions = (await client.call_tool("get_available_actions")).structured_content
        assert actions["availableActions"] == state["availableActions"]
        assert actions["usage"]["helpTopic"] == "actions"
        inventory = (await client.call_tool("get_inventory", {"containerId": "bag", "offset": 1})).structured_content
        assert inventory["total"] == 1 and inventory["inventory"] == []
        assert inventory["containers"][0]["label"] == "种子输入"
        assert inventory["containers"][0]["itemCount"] == 1
        entity = (await client.call_tool("get_entity", {"entityId": "item-1"})).structured_content
        assert entity["entity"]["statusText"] == "生长中 2/4"
        assert entity["availableActions"][0]["name"] == "set_value:1"
        assert (await client.call_tool("get_entity", {"entityId": "hidden"})).is_error
        assert (await client.call_tool("get_inventory", {"containerId": "hidden"})).is_error
        ui = (await client.call_tool("get_ui_state")).structured_content
        assert ui["controls"][0]["nearbyText"] == "Day 1"
        image = await client.call_tool("get_view")
        assert image.structured_content["snapshotId"] == "revision-1"
        assert image.content[1].type == "image" and base64.b64decode(image.content[1].data) == base64.b64decode(PNG)
        assert PNG not in image.content[0].text
        assert all(c.headers["authorization"] == "Bearer " + TOKEN for c in calls)


async def test_action_identity_and_argument_validation(tool_server):
    server, calls = tool_server
    async with Client(server) as client:
        args = {
            "action": "set_value:1",
            "expectedSnapshotId": "revision-1",
            "requestId": str(uuid4()),
            "arguments": {"value": 4, "text": "种子", "toggle": True},
        }
        assert not (await client.call_tool("perform_action", args)).is_error
        assert not (await client.call_tool("perform_action", args)).is_error
        assert json.loads(calls[-1].content) == args == json.loads(calls[-2].content)
        for bad in ({"unknown": 4}, {"quantity": True}, {"quantity": 1.2}, {"toggle": 1}, {"text": 4}):
            before = len(calls)
            result = await client.call_tool("perform_action", {**args, "arguments": bad})
            assert result.is_error and result.structured_content["errorCode"] == "invalid_arguments"
            assert len(calls) == before
        for bad in ({"requestId": "bad"}, {"action": " "}, {"expectedSnapshotId": "x" * 129}):
            assert (await client.call_tool("perform_action", {**args, **bad})).is_error
        assert (await client.call_tool("get_action_status", {"requestId": "../../state"})).is_error
        await client.call_tool("get_action_status", {"requestId": args["requestId"]})
        assert calls[-1].url.path == "/actions/" + args["requestId"]
