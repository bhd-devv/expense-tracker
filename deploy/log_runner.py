"""Mirror a service's stdout/stderr to Docker and daily compressed persistent logs."""

import gzip
import json
import logging
import logging.handlers
import os
import selectors
import shutil
import signal
import subprocess
import sys
from datetime import UTC, date, datetime, timedelta
from pathlib import Path


class DailyLog(logging.handlers.TimedRotatingFileHandler):
    def getFilesToDelete(self) -> list[str]:
        # Count and calendar age both matter for infrequent jobs such as migrate.
        expired = set(super().getFilesToDelete())
        cutoff = datetime.now(UTC).date() - timedelta(days=self.backupCount)
        current = Path(self.baseFilename)
        prefix = current.name + "."
        for archive in current.parent.glob(current.name + ".*.gz"):
            try:
                day = date.fromisoformat(
                    archive.name.removeprefix(prefix).removesuffix(".gz")
                )
            except ValueError:
                continue
            if day < cutoff:
                expired.add(str(archive))
        return sorted(expired)

    def handleError(self, record: logging.LogRecord) -> None:
        # Never dump the failed record (it may be from a third-party runtime).
        error = sys.exception()
        print(
            f"ERROR log_archive_write_failed type={type(error).__name__} "
            f"errno={getattr(error, 'errno', None)}; console logging continues",
            file=sys.stderr,
            flush=True,
        )


def compress(source: str, destination: str) -> None:
    temporary = destination + ".tmp"
    with open(source, "rb") as original, gzip.open(temporary, "wb") as archive:
        shutil.copyfileobj(original, archive)
    os.replace(temporary, destination)
    os.remove(source)


def main() -> int:
    os.umask(0o077)
    directory = Path(os.environ.get("LOG_DIRECTORY", "/logs"))
    directory.mkdir(parents=True, exist_ok=True)
    service = os.environ.get("LOG_SERVICE", "service")
    if not service.replace("-", "").isalnum():
        raise ValueError("Invalid LOG_SERVICE")
    retention = int(os.environ.get("LOG_RETENTION_DAYS", "14"))
    if not 1 <= retention <= 365:
        raise ValueError("LOG_RETENTION_DAYS must be between 1 and 365")
    sink = DailyLog(
        directory / f"{service}.log",
        when="midnight",
        utc=True,
        backupCount=retention,
        encoding="utf-8",
    )
    sink.namer = lambda name: name + ".gz"
    sink.rotator = compress
    sink.setFormatter(logging.Formatter("%(message)s"))
    for expired in sink.getFilesToDelete():
        os.remove(expired)
    # Exact configured secrets are a second defence after application-level safe logging.
    secrets = sorted(
        {
            value
            for key, value in os.environ.items()
            if len(value) >= 8
            and any(
                part in key.upper()
                for part in (
                    "TOKEN",
                    "SECRET",
                    "PASSWORD",
                    "SERVICE_KEY",
                    "APIKEY",
                    "API_KEY",
                    "CONNECTIONSTRINGS",
                )
            )
        },
        key=len,
        reverse=True,
    )

    def emit(raw: bytes) -> None:
        line = raw.decode("utf-8", errors="replace").rstrip("\r\n")
        for secret in secrets:
            line = line.replace(secret, "[REDACTED]")
        try:
            data = json.loads(line)
        except ValueError:
            data = None
        if not isinstance(data, dict):
            data = {"message": line}
        data["service"] = service
        data.setdefault("timestamp", datetime.now(UTC).isoformat())
        line = json.dumps(data, ensure_ascii=False)
        # Synchronous writes provide backpressure instead of a lossy logging queue.
        print(line, flush=True)
        sink.handle(logging.LogRecord(service, logging.INFO, "", 0, line, (), None))

    command = sys.argv[1:]
    if not command:
        raise ValueError("A service command is required")
    with subprocess.Popen(
        command,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        start_new_session=True,
    ) as process:

        def forward(signum: int, _frame: object) -> None:
            try:
                os.killpg(process.pid, signum)
            except ProcessLookupError:
                pass

        for signum in (signal.SIGTERM, signal.SIGINT):
            signal.signal(signum, forward)
        assert process.stdout is not None
        pending = b""
        with selectors.DefaultSelector() as selector:
            selector.register(process.stdout, selectors.EVENT_READ)
            while True:
                if sink.shouldRollover(logging.makeLogRecord({})):
                    try:
                        sink.doRollover()
                    except OSError as error:
                        print(
                            f"ERROR log_archive_rotation_failed errno={error.errno}",
                            file=sys.stderr,
                            flush=True,
                        )
                if not selector.select(timeout=1):
                    continue
                chunk = os.read(process.stdout.fileno(), 65536)
                if not chunk:
                    break
                pending += chunk
                while b"\n" in pending:
                    line, pending = pending.split(b"\n", 1)
                    emit(line)
                # Bound memory even if a runtime produces a very long unterminated line.
                if len(pending) > 1024 * 1024:
                    emit(pending)
                    pending = b""
        if pending:
            emit(pending)
        code = process.wait()
        emit(f"service_stopped service={service} exit_code={code}".encode())
    sink.close()
    return code if code >= 0 else 128 - code


if __name__ == "__main__":
    sys.exit(main())
