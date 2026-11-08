"""One help source: repository Markdown, included as resources in built wheels."""

from importlib.resources import files
from pathlib import Path

from . import __version__
from .bridge import PROTOCOL_VERSION


class HelpCatalog:
    def __init__(self) -> None:
        packaged = files("psmcp").joinpath("help")
        if packaged.is_dir():
            sources = [p for p in packaged.iterdir() if p.name.endswith(".md")]
        else:
            root = Path(__file__).resolve().parents[2]
            sources = [*root.joinpath("docs/help").glob("*.md"), root / "docs/capabilities.md"]
        self.topics = {}
        for source in sorted(sources, key=lambda p: p.name):
            topic = source.name.removesuffix(".md")
            content = source.read_text(encoding="utf-8-sig")
            title = content.splitlines()[0]
            if topic != topic.lower() or not title.startswith("# "):
                raise ValueError("Help topics require lowercase filenames and a Markdown title")
            self.topics[topic] = {"topic": topic, "title": title[2:], "content": content}
        if "overview" not in self.topics:
            raise ValueError("Bundled overview help is missing")

    def get(self, topic: str | None = None) -> dict:
        topic = (topic or "").strip().lower() or "overview"
        catalog = [{"topic": t["topic"], "title": t["title"]} for t in self.topics.values()]
        if topic not in self.topics:
            return {
                "success": False,
                "errorCode": "unknown_help_topic",
                "message": "Choose a topic from topics, or omit topic for the introduction.",
                "topics": catalog,
            }
        return {
            "success": True,
            "protocolVersion": PROTOCOL_VERSION,
            "serverVersion": __version__,
            **self.topics[topic],
            "mimeType": "text/markdown",
            "topics": catalog,
        }
