import json
import logging
import sys
import traceback
from contextvars import ContextVar
from datetime import UTC, datetime
from time import monotonic
from typing import Any

import httpx
from aiogram import Bot
from aiogram.client.session.middlewares.base import BaseRequestMiddleware, NextRequestMiddlewareType
from aiogram.methods import TelegramMethod
from aiogram.methods.base import Response, TelegramType

context: ContextVar[dict[str, Any] | None] = ContextVar("log_context", default=None)
logger = logging.getLogger(__name__)


def failure(error: BaseException, depth: int = 0) -> dict[str, Any]:
    # No exception messages, local variables or source lines: they can contain user data.
    cause = error.__cause__ or (None if error.__suppress_context__ else error.__context__)
    return {
        "type": type(error).__name__,
        "stack": [
            {"file": frame.filename, "line": frame.lineno, "function": frame.name}
            for frame in traceback.extract_tb(error.__traceback__)
        ],
        "inner": failure(cause, depth + 1) if cause is not None and depth < 8 else None,
    }


class JsonFormatter(logging.Formatter):
    def format(self, record: logging.LogRecord) -> str:
        data = {
            "timestamp": datetime.fromtimestamp(record.created, UTC).isoformat(),
            "level": record.levelname,
            "category": record.name,
            "message": "Operation failed" if record.exc_info else record.getMessage(),
            **(context.get() or {}),
            **getattr(record, "details", {}),
        }
        if record.exc_info and record.exc_info[1]:
            data["failure"] = failure(record.exc_info[1])
        if hasattr(record, "failure"):
            data["failure"] = record.failure
        return json.dumps(data, ensure_ascii=False)


def log_failure(logger: logging.Logger, event: str, error: BaseException, **details: Any) -> None:
    logger.error(event, extra={"failure": failure(error), "details": details})


def configure_logging() -> None:
    handler = logging.StreamHandler(sys.stdout)
    handler.setFormatter(JsonFormatter())
    logging.basicConfig(level=logging.INFO, handlers=[handler], force=True)
    # HTTP client URLs may carry credentials; API transport is logged explicitly.
    logging.getLogger("httpx").setLevel(logging.WARNING)
    logging.getLogger("httpcore").setLevel(logging.WARNING)


async def request_started(request: httpx.Request) -> None:
    request.extensions["log_started"] = monotonic()
    logger.info(
        "Backend request started",
        extra={"details": {"method": request.method, "path": request.url.path}},
    )


async def request_completed(response: httpx.Response) -> None:
    started = response.request.extensions.get("log_started", monotonic())
    logger.log(
        logging.WARNING if response.is_error else logging.INFO,
        "Backend request completed",
        extra={
            "details": {
                "method": response.request.method,
                "path": response.request.url.path,
                "status": response.status_code,
                "requestId": response.headers.get("X-Request-Id"),
                "elapsedMs": round((monotonic() - started) * 1000, 2),
            }
        },
    )


class TelegramLogging(BaseRequestMiddleware):
    async def __call__(
        self,
        make_request: NextRequestMiddlewareType[TelegramType],
        bot: Bot,
        method: TelegramMethod[TelegramType],
    ) -> Response[TelegramType]:
        started = monotonic()
        name = type(method).__name__
        logger.info("Telegram request started", extra={"details": {"method": name}})
        try:
            result = await make_request(bot, method)
        except Exception as error:
            log_failure(logger, "Telegram request failed", error, method=name)
            raise
        logger.info(
            "Telegram request completed",
            extra={
                "details": {"method": name, "elapsedMs": round((monotonic() - started) * 1000, 2)}
            },
        )
        return result
