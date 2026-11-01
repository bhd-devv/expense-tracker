#!/usr/bin/env python3
"""Export a local Development API contract and regenerate both consumers."""
import argparse
import json
import subprocess
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
subprocess.run(["npx", "--no-install", "prettier", "--write", "src/lib/api-schema.d.ts"], cwd=ROOT / "src/frontend", check=True)
subprocess.run([
    "uv", "run", "--frozen", "datamodel-codegen", "--input", "../../contracts/openapi.json",
    "--input-file-type", "openapi", "--output", "src/expense_tracker_bot/models.py",
    "--output-model-type", "pydantic_v2.BaseModel", "--target-python-version", "3.13",
    "--use-standard-collections", "--use-union-operator", "--disable-timestamp",
    "--use-annotated", "--field-constraints", "--formatters", "ruff-format",
], cwd=ROOT / "src/bot", check=True)
subprocess.run([
    "uv", "run", "--frozen", "ruff", "check", "--select", "I", "--fix",
    "src/expense_tracker_bot/models.py",
], cwd=ROOT / "src/bot", check=True)
subprocess.run([
    "uv", "run", "--frozen", "ruff", "format", "src/expense_tracker_bot/models.py",
], cwd=ROOT / "src/bot", check=True)
