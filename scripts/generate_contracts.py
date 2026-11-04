#!/usr/bin/env python3
"""Export a local Development API contract and regenerate both consumers."""

import argparse
import json
import subprocess
import tempfile
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument("--url", default="http://127.0.0.1:5085/openapi/v1.json")
args = parser.parse_args()
with urllib.request.urlopen(args.url, timeout=15) as response:
    schema = json.load(response)
contract = ROOT / "contracts/openapi.json"
contract.parent.mkdir(exist_ok=True)
contract.write_text(json.dumps(schema, ensure_ascii=False, indent=2) + "\n")
subprocess.run(["npm", "run", "generate"], cwd=ROOT / "src/frontend", check=True)
subprocess.run(
    ["npx", "--no-install", "prettier", "--write", "src/lib/api-schema.d.ts"],
    cwd=ROOT / "src/frontend",
    check=True,
)
# The bot consumes only delivery DTOs, never browser workspace contracts.
bot_paths = {
    path: value for path, value in schema["paths"].items() if path.startswith("/internal/bot/")
}
models = schema["components"]["schemas"]
required: set[str] = set()


def collect(value: object) -> None:
    if isinstance(value, dict):
        reference = value.get("$ref", "")
        prefix = "#/components/schemas/"
        if isinstance(reference, str) and reference.startswith(prefix):
            name = reference.removeprefix(prefix)
            if name not in required:
                required.add(name)
                collect(models[name])
        for child in value.values():
            collect(child)
    elif isinstance(value, list):
        for child in value:
            collect(child)


collect(bot_paths)
with tempfile.TemporaryDirectory() as temporary:
    bot_contract = Path(temporary) / "bot-openapi.json"
    bot_contract.write_text(
        json.dumps(
            {
                "openapi": schema["openapi"],
                "info": schema["info"],
                "paths": bot_paths,
                "components": {
                    "schemas": {name: model for name, model in models.items() if name in required}
                },
            },
            ensure_ascii=False,
            indent=2,
        )
        + "\n"
    )
    subprocess.run(
        [
            "uv",
            "run",
            "--frozen",
            "datamodel-codegen",
            "--input",
            str(bot_contract),
            "--input-file-type",
            "openapi",
            "--output",
            "src/expense_tracker_bot/models.py",
            "--output-model-type",
            "pydantic_v2.BaseModel",
            "--target-python-version",
            "3.13",
            "--use-standard-collections",
            "--use-union-operator",
            "--disable-timestamp",
            "--use-annotated",
            "--field-constraints",
            "--formatters",
            "ruff-format",
        ],
        cwd=ROOT / "src/bot",
        check=True,
    )
subprocess.run(
    [
        "uv",
        "run",
        "--frozen",
        "ruff",
        "check",
        "--select",
        "I",
        "--fix",
        "src/expense_tracker_bot/models.py",
    ],
    cwd=ROOT / "src/bot",
    check=True,
)
subprocess.run(
    [
        "uv",
        "run",
        "--frozen",
        "ruff",
        "format",
        "src/expense_tracker_bot/models.py",
    ],
    cwd=ROOT / "src/bot",
    check=True,
)
