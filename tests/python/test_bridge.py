import json
from pathlib import Path

import httpx
import pytest
from psmcp.bridge import MAX_RESPONSE_BYTES, BridgeClient, connection_path

TOKEN = "a" * 64


@pytest.fixture
def connection(tmp_path):
    path = tmp_path / "connection.json"
    path.write_text(json.dumps({"protocolVersion": 2, "baseUrl": "http://127.0.0.1:15527/", "token": TOKEN}))
    return path


async def test_rotation_and_unicode_post(connection):
    requests = []

    def handle(request):
        requests.append(request)
        return httpx.Response(200, json={"protocolVersion": 2, "success": True})

    bridge = BridgeClient(connection, transport=httpx.MockTransport(handle))
    try:
        assert (await bridge.send("health"))["success"]
        config = json.loads(connection.read_text())
        config["token"] = "b" * 64
        config["baseUrl"] = "http://127.0.0.1:15528/"
        connection.write_text(json.dumps(config))
        action = {"requestId": "original-id", "arguments": {"text": "种子"}}
        assert (await bridge.send("actions", action))["success"]
        assert requests[0].headers["authorization"] == "Bearer " + TOKEN
        assert requests[1].headers["authorization"] == "Bearer " + "b" * 64
        assert requests[1].url.port == 15528
        assert json.loads(requests[1].content) == action
        assert int(requests[1].headers["content-length"]) == len(requests[1].content)
    finally:
        await bridge.aclose()


@pytest.mark.parametrize(
    "url",
    [
        "https://127.0.0.1:15527/",
        "http://example.com:15527/",
        "http://localhost:15527/",
        "http://127.0.0.1:80/",
        "http://127.0.0.1:65536/",
        "http://127.0.0.1:15527/secret",
        "http://user@127.0.0.1:15527/",
        "http://127.0.0.1:15527/?x=1",
        "http://127.0.0.1:15527/#x",
    ],
)
async def test_no_token_to_unapproved_url(connection, url):
    connection.write_text(json.dumps({"protocolVersion": 2, "baseUrl": url, "token": TOKEN}))
    requests = []
    bridge = BridgeClient(connection, transport=httpx.MockTransport(lambda request: requests.append(request)))
    try:
        assert (await bridge.send("health"))["errorCode"] == "invalid_connection"
        assert requests == []
    finally:
        await bridge.aclose()


@pytest.mark.parametrize(
    "data,code",
    [
        (b"bad json", "connection_file_unavailable"),
        (b"[]", "connection_file_unavailable"),
        (b"x" * 16_385, "connection_file_unavailable"),
        (b'{"protocolVersion": 1}', "protocol_mismatch"),
        (b'{"protocolVersion": 2, "baseUrl": "http://127.0.0.1:15527/", "token": "short"}', "invalid_connection"),
    ],
)
async def test_invalid_connection_files(connection, data, code):
    connection.write_bytes(data)
    bridge = BridgeClient(connection)
    try:
        assert (await bridge.send("health"))["errorCode"] == code
    finally:
        await bridge.aclose()


@pytest.mark.parametrize(
    "exception,query_code",
    [
        (httpx.ConnectError("private detail"), "bridge_unavailable"),
        (httpx.ReadTimeout("private detail"), "bridge_timeout"),
    ],
)
async def test_uncertain_posts_are_not_retried(connection, exception, query_code):
    calls = 0

    def handle(request):
        nonlocal calls
        calls += 1
        raise exception

    bridge = BridgeClient(connection, transport=httpx.MockTransport(handle))
    try:
        assert (await bridge.send("state"))["errorCode"] == query_code
        result = await bridge.send("actions", {"requestId": "original-id"})
        assert result["errorCode"] == "action_outcome_unknown"
        assert result["requestId"] == "original-id"
        assert "private detail" not in json.dumps(result)
        assert calls == 2
    finally:
        await bridge.aclose()


@pytest.mark.parametrize(
    "response,code",
    [
        (lambda: httpx.Response(200, content=b"not json"), "invalid_bridge_response"),
        (lambda: httpx.Response(200, content=b"x" * (MAX_RESPONSE_BYTES + 1)), "invalid_bridge_response"),
        (lambda: httpx.Response(200, json={"protocolVersion": 1, "success": True}), "protocol_mismatch"),
        (lambda: httpx.Response(500, json={"protocolVersion": 2, "success": True}), "bridge_http_error"),
        (lambda: httpx.Response(302, headers={"Location": "https://example.com/"}), "invalid_bridge_response"),
    ],
)
async def test_response_bounds_protocol_and_redirects(connection, response, code):
    requests = []

    def handle(request):
        requests.append(request)
        return response()

    bridge = BridgeClient(connection, transport=httpx.MockTransport(handle))
    try:
        assert (await bridge.send("health"))["errorCode"] == code
        result = await bridge.send("actions", {"requestId": "original-id"})
        assert result["errorCode"] == "action_outcome_unknown"
        assert len(requests) == 2
    finally:
        await bridge.aclose()


async def test_request_size_and_error_envelope(connection):
    requests = []
    expected = {"protocolVersion": 2, "success": False, "errorCode": "stale_snapshot", "requestId": "r"}

    def handle(request):
        requests.append(request)
        return httpx.Response(200, json=expected)

    bridge = BridgeClient(connection, transport=httpx.MockTransport(handle))
    try:
        assert (await bridge.send("actions", {"text": "x" * 16_385}))["errorCode"] == "invalid_request"
        assert requests == []
        assert await bridge.send("actions", {"requestId": "r"}) == expected
    finally:
        await bridge.aclose()


def test_connection_precedence(tmp_path, monkeypatch):
    monkeypatch.setenv("PSMCP_CONNECTION_FILE", str(tmp_path / "env.json"))
    assert connection_path() == tmp_path / "env.json"
    assert connection_path(str(tmp_path / "cli.json")) == tmp_path / "cli.json"
    monkeypatch.delenv("PSMCP_CONNECTION_FILE")
    assert connection_path().is_relative_to(Path.home())
