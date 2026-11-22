import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

SCRIPT = Path(__file__).resolve().parents[2] / "scripts/Configure-Mcp.py"


@pytest.fixture
def configuration(tmp_path, monkeypatch):
    root = tmp_path / "源码 with spaces"
    (root / "scripts").mkdir(parents=True)
    script = root / "scripts/Configure-Mcp.py"
    shutil.copyfile(SCRIPT, script)
    (root / "pyproject.toml").touch()
    (root / "uv.lock").touch()
    binary_dir = tmp_path / "bin with spaces"
    binary_dir.mkdir()
    uv = binary_dir / ("uv.exe" if os.name == "nt" else "uv")
    uv.touch()
    uv.chmod(0o755)
    monkeypatch.setenv("PATH", str(binary_dir))
    for key in ("PSMCP_CONNECTION_FILE", "STEAM_COMPAT_DATA_PATH", "WINEPREFIX", "XDG_DATA_HOME"):
        monkeypatch.delenv(key, raising=False)
    return root, script, uv


@pytest.mark.integration
def test_configure_uses_absolute_paths_and_preserves_prefix(configuration, tmp_path, monkeypatch):
    root, script, uv = configuration
    monkeypatch.setenv("STEAM_COMPAT_DATA_PATH", str(tmp_path / "custom prefix"))
    subprocess.run([sys.executable, str(script)], cwd=tmp_path, check=True, capture_output=True)
    entry = json.loads((root / "artifacts/mcp-config.json").read_text())["mcpServers"]["probably-stolen-mcp"]
    assert Path(entry["command"]) == uv
    assert entry["args"] == ["--directory", str(root), "run", "--locked", "--no-dev", "probably-stolen-mcp"]
    assert entry["env"] == {"STEAM_COMPAT_DATA_PATH": str(tmp_path / "custom prefix")}


@pytest.mark.integration
def test_configure_connection_overrides_environment(configuration, tmp_path, monkeypatch):
    root, script, _ = configuration
    monkeypatch.setenv("PSMCP_CONNECTION_FILE", str(tmp_path / "env.json"))
    explicit = tmp_path / "explicit connection.json"
    subprocess.run([sys.executable, str(script), "--connection", str(explicit)], check=True, capture_output=True)
    entry = json.loads((root / "artifacts/mcp-config.json").read_text())["mcpServers"]["probably-stolen-mcp"]
    assert entry["args"][-2:] == ["--connection", str(explicit)]
    assert "env" not in entry


@pytest.mark.integration
def test_configure_reports_missing_uv(configuration, monkeypatch):
    root, script, _ = configuration
    monkeypatch.setenv("PATH", "")
    result = subprocess.run([sys.executable, str(script)], capture_output=True, text=True)
    assert result.returncode != 0
    assert "Install uv" in result.stderr
    assert not (root / "artifacts/mcp-config.json").exists()
