import json
from pathlib import Path

import pytest
from psmcp import paths
from psmcp.bridge import connection_path


@pytest.fixture
def linux_home(tmp_path, monkeypatch):
    home = tmp_path / "home"
    home.mkdir()
    monkeypatch.setattr(Path, "home", lambda: home)
    monkeypatch.setattr(paths.sys, "platform", "linux")
    for key in ("PSMCP_CONNECTION_FILE", "STEAM_COMPAT_DATA_PATH", "WINEPREFIX", "XDG_DATA_HOME"):
        monkeypatch.delenv(key, raising=False)
    return home


def proton_file(library):
    return library / "steamapps/compatdata/4348910/pfx/drive_c/users/steamuser" / paths.WINDOWS_DATA


def install_library(home, library):
    config = home / ".local/share/Steam/steamapps/libraryfolders.vdf"
    config.parent.mkdir(parents=True, exist_ok=True)
    config.write_text(
        '"libraryfolders"\n{\n "1"\n {\n "path" ' + json.dumps(str(library), ensure_ascii=False) + "\n }\n}\n",
        encoding="utf-8",
    )


def test_windows_default_unchanged(linux_home, monkeypatch):
    monkeypatch.setattr(paths.sys, "platform", "win32")
    assert paths.default_connection_path() == linux_home / paths.WINDOWS_DATA


def test_proton_default_before_first_launch(linux_home):
    assert paths.default_connection_path() == proton_file(linux_home / ".local/share/Steam")


@pytest.mark.parametrize(
    "root", [".local/share/Steam", ".steam/steam", ".steam/root", ".var/app/com.valvesoftware.Steam/.local/share/Steam"]
)
def test_existing_proton_connection(linux_home, root):
    connection = proton_file(linux_home / root)
    connection.parent.mkdir(parents=True)
    connection.write_text("not read during discovery", encoding="utf-8")
    assert connection_path() == connection


@pytest.mark.parametrize("stage", ["manifest", "prefix", "connection"])
def test_external_library_spaces_and_unicode(linux_home, tmp_path, stage):
    library = tmp_path / "游戏库 with spaces"
    install_library(linux_home, library)
    connection = proton_file(library)
    if stage == "manifest":
        manifest = library / "steamapps/appmanifest_4348910.acf"
        manifest.parent.mkdir(parents=True)
        manifest.touch()
    else:
        connection.parent.mkdir(parents=True)
        if stage == "connection":
            connection.touch()
    assert paths.default_connection_path() == connection


def test_xdg_data_home(linux_home, tmp_path, monkeypatch):
    data_home = tmp_path / "custom-data"
    monkeypatch.setenv("XDG_DATA_HOME", str(data_home))
    assert paths.default_connection_path() == proton_file(data_home / "Steam")


def test_explicit_paths_take_precedence(linux_home, tmp_path, monkeypatch):
    compat = tmp_path / "compat"
    wine = tmp_path / "wine"
    monkeypatch.setenv("STEAM_COMPAT_DATA_PATH", str(compat))
    monkeypatch.setenv("WINEPREFIX", str(wine))
    assert paths.default_connection_path() == compat / "pfx/drive_c/users/steamuser" / paths.WINDOWS_DATA
    monkeypatch.delenv("STEAM_COMPAT_DATA_PATH")
    assert paths.default_connection_path() == wine / "drive_c/users/steamuser" / paths.WINDOWS_DATA
    monkeypatch.setenv("PSMCP_CONNECTION_FILE", str(linux_home / "env.json"))
    assert connection_path() == linux_home / "env.json"
    assert connection_path(str(tmp_path / "cli.json")) == tmp_path / "cli.json"


def test_missing_explicit_prefix_does_not_fall_back(linux_home, tmp_path, monkeypatch):
    connection = proton_file(linux_home / ".local/share/Steam")
    connection.parent.mkdir(parents=True)
    connection.touch()
    monkeypatch.setenv("WINEPREFIX", str(tmp_path / "missing"))
    assert paths.default_connection_path() == tmp_path / "missing/drive_c/users/steamuser" / paths.WINDOWS_DATA


def test_ambiguous_installations_require_explicit_path(linux_home, tmp_path):
    other = tmp_path / "other-steam"
    install_library(linux_home, other)
    for library in (linux_home / ".local/share/Steam", other):
        connection = proton_file(library)
        connection.parent.mkdir(parents=True)
        connection.touch()
    with pytest.raises(ValueError, match="Multiple Proton connection files"):
        connection_path()
    assert connection_path(str(connection)) == connection


def test_duplicate_library_entries_do_not_create_ambiguity(linux_home):
    library = linux_home / ".local/share/Steam"
    install_library(linux_home, library)
    connection = proton_file(library)
    connection.parent.mkdir(parents=True)
    connection.touch()
    assert connection_path() == connection
    assert paths.steam_libraries().count(library) == 1


def test_unreadable_library_config_is_ignored(linux_home):
    config = linux_home / ".local/share/Steam/steamapps/libraryfolders.vdf"
    config.parent.mkdir(parents=True)
    config.write_bytes(b"\xff\xfe")
    assert paths.default_connection_path() == proton_file(linux_home / ".local/share/Steam")
