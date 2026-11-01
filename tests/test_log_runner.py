"""Stdlib checks for the container logging boundary; no Docker or live credentials."""

import gzip
import importlib.util
import json
import logging
import os
import signal
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path

RUNNER = Path(__file__).resolve().parents[1] / "deploy" / "log_runner.py"
spec = importlib.util.spec_from_file_location("log_runner", RUNNER)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class LogRunnerTests(unittest.TestCase):
    def test_console_file_stderr_redaction_and_exit_code(self):
        with tempfile.TemporaryDirectory() as directory:
            secret = "synthetic-credential-for-test"
            process = subprocess.run(
                [
                    sys.executable,
                    str(RUNNER),
                    sys.executable,
                    "-u",
                    "-c",
                    (
                        "import os,sys; print(os.environ['BOT_TOKEN']); "
                        "print('stderr-event',file=sys.stderr); sys.exit(7)"
                    ),
                ],
                env={
                    **os.environ,
                    "LOG_SERVICE": "test",
                    "LOG_DIRECTORY": directory,
                    "BOT_TOKEN": secret,
                },
                capture_output=True,
                check=False,
                text=True,
                timeout=10,
            )
            self.assertEqual(7, process.returncode)
            saved = (Path(directory) / "test.log").read_text()
            self.assertEqual(process.stdout, saved)
            self.assertNotIn(secret, saved)
            records = [json.loads(line) for line in saved.splitlines()]
            self.assertEqual("[REDACTED]", records[0]["message"])
            self.assertEqual("stderr-event", records[1]["message"])
            self.assertTrue(all(record["service"] == "test" for record in records))
            self.assertTrue(all("timestamp" in record for record in records))

    def test_daily_rotation_gzip_retention_and_resume(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "test.log"
            sink = module.DailyLog(path, when="midnight", utc=True, backupCount=2)
            sink.namer = lambda name: name + ".gz"
            sink.rotator = module.compress
            record = logging.makeLogRecord({"msg": "before-midnight"})
            sink.handle(record)
            for suffix in ("2000-01-01", "2000-01-02"):
                (Path(directory) / f"test.log.{suffix}.gz").write_bytes(b"old")
            sink.rolloverAt = int(time.time()) - 1
            self.assertTrue(sink.shouldRollover(record))
            sink.doRollover()
            archives = sorted(Path(directory).glob("*.gz"))
            self.assertEqual(1, len(archives))
            self.assertFalse((Path(directory) / "test.log.2000-01-01.gz").exists())
            self.assertFalse((Path(directory) / "test.log.2000-01-02.gz").exists())
            with gzip.open(archives[-1], "rt") as archive:
                self.assertEqual("before-midnight\n", archive.read())
            sink.handle(logging.makeLogRecord({"msg": "after-midnight"}))
            sink.close()
            self.assertEqual("after-midnight\n", path.read_text())
            sink = module.DailyLog(path, when="midnight", utc=True, backupCount=2)
            sink.handle(logging.makeLogRecord({"msg": "after-restart"}))
            sink.close()
            self.assertEqual("after-midnight\nafter-restart\n", path.read_text())

    def test_sigterm_is_forwarded_and_final_logs_are_drained(self):
        with tempfile.TemporaryDirectory() as directory:
            process = subprocess.Popen(
                [
                    sys.executable,
                    str(RUNNER),
                    sys.executable,
                    "-u",
                    "-c",
                    (
                        "import signal,time,sys; "
                        "signal.signal(signal.SIGTERM, lambda *_: "
                        "(print('shutdown-complete'),sys.exit(0))); "
                        "print('ready'); time.sleep(30)"
                    ),
                ],
                env={**os.environ, "LOG_SERVICE": "test", "LOG_DIRECTORY": directory},
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True,
            )
            try:
                self.assertEqual(
                    "ready", json.loads(process.stdout.readline())["message"]
                )
                process.send_signal(signal.SIGTERM)
                stdout, stderr = process.communicate(timeout=10)
                self.assertEqual(0, process.returncode, stderr)
                self.assertIn("shutdown-complete", stdout)
                self.assertIn(
                    "shutdown-complete", (Path(directory) / "test.log").read_text()
                )
            finally:
                if process.poll() is None:
                    process.kill()
                    process.communicate()


if __name__ == "__main__":
    unittest.main()
