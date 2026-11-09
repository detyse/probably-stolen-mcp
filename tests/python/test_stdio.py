import json
import os
import sys

import pytest

from mcp import Client, StdioServerParameters


@pytest.mark.integration
async def test_stdio_help_without_game_or_working_directory(tmp_path):
    (tmp_path / "overview.md").write_text("LOCAL_FILE_MUST_NOT_BE_READ")
    python = os.environ.get("PSMCP_TEST_PYTHON", sys.executable)
    server = StdioServerParameters(
        command=python, args=["-m", "psmcp", "--connection", str(tmp_path / "missing.json")], cwd=tmp_path
    )
    async with Client(server, read_timeout_seconds=15) as client:
        tools = (await client.list_tools()).tools
        assert len(tools) == 11
        unavailable = await client.call_tool("get_bridge_status")
        assert unavailable.is_error and unavailable.structured_content["errorCode"] == "connection_file_unavailable"
        help_result = await client.call_tool("get_help")
        help_body = help_result.structured_content
        assert help_body["topic"] == "overview" and help_body["protocolVersion"] == 2
        assert json.loads(help_result.content[0].text) == help_body
        assert "LOCAL_FILE_MUST_NOT_BE_READ" not in help_body["content"]
        assert len(help_body["topics"]) == 9
        for topic in help_body["topics"]:
            result = await client.call_tool("get_help", {"topic": topic["topic"]})
            assert not result.is_error
            assert result.structured_content["content"].startswith("# " + topic["title"])
        normalized = await client.call_tool("get_help", {"topic": " ACTIONS "})
        assert normalized.structured_content["topic"] == "actions"
        for invalid in ("missing", "../overview.md", str(tmp_path / "overview.md"), "https://example.com/"):
            result = await client.call_tool("get_help", {"topic": invalid})
            assert result.is_error and result.structured_content["errorCode"] == "unknown_help_topic"
        assert not (await client.call_tool("get_help")).is_error
