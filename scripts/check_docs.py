"""Validate and generate the expense-tracker documentation index."""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path
from urllib.parse import unquote, urlsplit


ROOT = Path(__file__).resolve().parents[1]
CHANGELOG_DIR = ROOT / ".agents" / "changelog"
INDEX = ROOT / "docs" / "changes.md"
RECORD_NAME = re.compile(r"^(\d+)-([a-z0-9]+(?:-[a-z0-9]+)*)\.md$")
LINK = re.compile(r"!?\[[^\]]*\]\((<[^>]+>|[^\s)]+)")
HEADING = re.compile(r"^# (.+)$", re.MULTILINE)


def markdown_files() -> list[Path]:
    paths = list(ROOT.glob("*.md"))
    for directory in (".agents", "docs", "scripts"):
        paths.extend((ROOT / directory).rglob("*.md"))
    return sorted(paths)


def records() -> tuple[list[tuple[int, Path, str]], list[str]]:
    errors: list[str] = []
    result: list[tuple[int, Path, str]] = []
    seen: dict[int, Path] = {}
    if not CHANGELOG_DIR.is_dir():
        return result, ["Missing .agents/changelog directory"]
    for path in sorted(CHANGELOG_DIR.glob("*.md")):
        if path.name == "README.md":
            continue
        match = RECORD_NAME.fullmatch(path.name)
        if match is None:
            errors.append(f"Invalid changelog filename: {path.relative_to(ROOT)}")
            continue
        number = int(match.group(1))
        if number in seen:
            errors.append(
                f"Duplicate changelog number {number}: "
                f"{seen[number].name} and {path.name}"
            )
        seen[number] = path
        heading = HEADING.search(path.read_text())
        if heading is None:
            errors.append(f"Missing level-one heading: {path.relative_to(ROOT)}")
            continue
        result.append((number, path, heading.group(1).strip()))
    return sorted(result, reverse=True), errors


def generated_index(items: list[tuple[int, Path, str]]) -> str:
    lines = [
        "# Зміни",
        "",
        "Цей файл генерується командою `python3 scripts/check_docs.py --write-index`.",
        "Детальні записи зберігаються в `.agents/changelog/`.",
        "",
    ]
    if not items:
        lines.append("Наразі завершених змістовних змін не зафіксовано.")
    else:
        lines.extend(
            f"- {number:03d} — [{title}](../.agents/changelog/{path.name})"
            for number, path, title in items
        )
    return "\n".join(lines) + "\n"


def check_links(path: Path) -> list[str]:
    errors: list[str] = []
    for raw_target in LINK.findall(path.read_text()):
        target = raw_target.strip("<>")
        parts = urlsplit(target)
        if parts.scheme or target.startswith("#"):
            continue
        destination = (path.parent / unquote(parts.path)).resolve()
        if not destination.is_relative_to(ROOT):
            errors.append(f"{path.relative_to(ROOT)}: link escapes repository: {target}")
        elif not destination.exists():
            errors.append(f"{path.relative_to(ROOT)}: missing link target: {target}")
    return errors


def check_secret_filenames() -> list[str]:
    errors: list[str] = []
    for path in (ROOT / ".agents").rglob("*"):
        if not path.is_file():
            continue
        lowered = path.name.lower()
        if lowered in {".env", ".env.local", ".env.production"} or lowered.endswith(
            (".pem", ".key", ".p12", ".pfx")
        ):
            errors.append(f"Secret-like file must not be documented: {path.relative_to(ROOT)}")
    return errors


def check() -> list[str]:
    required = (
        "AGENTS.md",
        ".agents/README.md",
        ".agents/STATUS.md",
        ".agents/changelog/README.md",
        ".agents/tasks/README.md",
        ".agents/templates/change.md",
        ".agents/templates/task.md",
        ".agents/templates/decision.md",
        ".agents/knowledge/README.md",
        ".agents/knowledge/decisions.md",
        ".agents/guides/sdlc.md",
        ".agents/guides/documentation.md",
        "docs/README.md",
        "docs/changes.md",
    )
    errors = [
        f"Missing required document: {path}"
        for path in required
        if not (ROOT / path).is_file()
    ]
    for path in markdown_files():
        errors.extend(check_links(path))
        if not path.read_text().endswith("\n"):
            errors.append(f"Missing final newline: {path.relative_to(ROOT)}")
    items, record_errors = records()
    errors.extend(record_errors)
    errors.extend(check_secret_filenames())
    if INDEX.is_file() and INDEX.read_text() != generated_index(items):
        errors.append("docs/changes.md is stale; run --write-index")
    return errors


def main() -> int:
    parser = argparse.ArgumentParser()
    actions = parser.add_mutually_exclusive_group(required=True)
    actions.add_argument("--check", action="store_true")
    actions.add_argument("--write-index", action="store_true")
    args = parser.parse_args()
    if args.write_index:
        items, errors = records()
        if errors:
            print("\n".join(errors), file=sys.stderr)
            return 1
        INDEX.parent.mkdir(parents=True, exist_ok=True)
        INDEX.write_text(generated_index(items))
        print(f"Updated {INDEX.relative_to(ROOT)}")
        return 0
    errors = check()
    if errors:
        print("\n".join(errors), file=sys.stderr)
        return 1
    print("Documentation links, changelog format, and required files: OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
