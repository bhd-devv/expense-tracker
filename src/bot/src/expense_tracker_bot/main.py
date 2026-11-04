import asyncio
import logging
import secrets
import sys
from collections.abc import AsyncIterator
from time import monotonic
from typing import Any

import httpx
from aiogram import Bot, Dispatcher
from aiogram.exceptions import TelegramRetryAfter
from aiogram.types import (
    BotCommandScopeAllPrivateChats,
    BotCommandScopeDefault,
    MenuButtonDefault,
    Update,
)
from aiohttp import web
from pydantic import Field, ValidationError, field_validator
from pydantic_settings import BaseSettings, SettingsConfigDict

from .api import Api
from .handlers import router
from .models import LeaseDto
from .observability import (
    TelegramLogging,
    configure_logging,
    context,
    log_failure,
    request_completed,
    request_started,
)

logger = logging.getLogger(__name__)


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_prefix="BOT_")
    token: str = Field(min_length=20)
    service_key: str = Field(min_length=32)
    webhook_secret: str = Field(min_length=32, pattern=r"^[A-Za-z0-9_-]+$")
    backend_url: str = "http://backend:8080/internal/bot/"
    app_url: str
    port: int = 8081

    @field_validator("app_url")
    @classmethod
    def https_url(cls, value: str) -> str:
        if not value.startswith("https://"):
            raise ValueError("Mini App requires HTTPS")
        return value.rstrip("/")


def minimal_update(update: Update) -> dict[str, Any] | None:
    message = update.message
    if not (
        message
        and message.chat.type == "private"
        and message.from_user
        and not message.from_user.is_bot
    ):
        return None
    # No command, purchase text, audio ID, callback data or display name is needed
    # to show the launcher. Do not persist them in the durable inbox.
    return {
        "update_id": update.update_id,
        "message": {
            "message_id": message.message_id,
            "date": int(message.date.timestamp()),
            "chat": {"id": message.chat.id, "type": "private"},
            "from": {
                "id": message.from_user.id,
                "is_bot": False,
                "first_name": "",
                "language_code": message.from_user.language_code,
            },
        },
    }


async def worker(api: Api, bot: Bot, dispatcher: Dispatcher, app_url: str) -> None:
    while True:
        lease: LeaseDto | None = None
        started = monotonic()
        token = context.set({"queue": "inbox"})
        try:
            response = await api.call("POST", "/queues/inbox/claim")
            if response.status_code == 204:
                await asyncio.sleep(1)
                continue
            lease = LeaseDto.model_validate(response.json())
            payload = lease.payload.root
            context.set(
                {
                    "queue": "inbox",
                    "deliveryId": str(lease.id),
                    "updateId": payload.get("update_id"),
                    "attempt": lease.attempts,
                }
            )
            logger.info("Delivery processing started")
            # Old queued callbacks are ignored; old messages get the launcher only.
            update = Update.model_validate(payload)
            if update.message is not None:
                await dispatcher.feed_update(bot, update, app_url=app_url)
            await api.call(
                "POST", f"/queues/{lease.id}/result", {"token": str(lease.token), "success": True}
            )
            logger.info(
                "Delivery completed",
                extra={"details": {"elapsedMs": round((monotonic() - started) * 1000, 2)}},
            )
        except asyncio.CancelledError:
            if lease:
                logger.warning("Delivery interrupted during shutdown; durable lease retained")
            raise
        except TelegramRetryAfter as error:
            logger.warning(
                "Telegram rate limited delivery",
                extra={"details": {"retryAfterSeconds": error.retry_after}},
            )
            await asyncio.sleep(min(error.retry_after, 60))
        except Exception as error:
            log_failure(logger, "Delivery failed", error)
            if lease and lease.attempts >= 8:
                logger.error("Delivery attempts exhausted; manual review required")
            if lease:
                try:
                    await api.call(
                        "POST",
                        f"/queues/{lease.id}/result",
                        {"token": str(lease.token), "success": False},
                    )
                except Exception as result_error:
                    log_failure(logger, "Delivery failure acknowledgement failed", result_error)
            await asyncio.sleep(3)
        finally:
            context.reset(token)


async def create_app(settings: Settings) -> web.Application:
    bot = Bot(settings.token)
    bot.session.middleware(TelegramLogging())
    client = httpx.AsyncClient(
        base_url=settings.backend_url.rstrip("/") + "/",
        headers={"Authorization": "Bearer " + settings.service_key},
        timeout=httpx.Timeout(100, connect=5),
        event_hooks={"request": [request_started], "response": [request_completed]},
    )
    api = Api(client)
    dispatcher = Dispatcher()
    dispatcher.include_router(router)
    app = web.Application(client_max_size=65536)

    @web.middleware
    async def request_logging(request: web.Request, handler: Any) -> web.StreamResponse:
        started = monotonic()
        status = 500
        try:
            response: web.StreamResponse = await handler(request)
            status = response.status
            return response
        except web.HTTPException as error:
            status = error.status
            raise
        except Exception as error:
            log_failure(logger, "Webhook request failed", error)
            raise
        finally:
            logger.log(
                logging.WARNING if status >= 400 else logging.INFO,
                "HTTP request completed",
                extra={
                    "details": {
                        "method": request.method,
                        "route": request.match_info.route.resource.canonical
                        if request.match_info.route.resource
                        else "unmatched",
                        "status": status,
                        "elapsedMs": round((monotonic() - started) * 1000, 2),
                    }
                },
            )

    app.middlewares.append(request_logging)

    async def webhook(request: web.Request) -> web.Response:
        if not secrets.compare_digest(
            request.headers.get("X-Telegram-Bot-Api-Secret-Token", "").encode(),
            settings.webhook_secret.encode(),
        ):
            logger.warning("Webhook secret rejected")
            raise web.HTTPForbidden()
        try:
            update = Update.model_validate(await request.json())
        except (ValueError, ValidationError) as error:
            log_failure(logger, "Webhook payload rejected", error)
            raise web.HTTPBadRequest() from None
        payload = minimal_update(update)
        if payload is not None:
            token = context.set({"updateId": update.update_id})
            logger.info(
                "Webhook update received",
                extra={
                    "details": {
                        "updateId": update.update_id,
                        "kind": "message",
                    }
                },
            )
            try:
                # 2xx only after durable deduplicated storage. No background acknowledgement.
                async with asyncio.timeout(8):
                    await api.call(
                        "POST", "/inbox", {"updateId": update.update_id, "payload": payload}
                    )
                logger.info(
                    "Webhook update stored", extra={"details": {"updateId": update.update_id}}
                )
            except Exception as error:
                log_failure(
                    logger, "Webhook durable storage failed", error, updateId=update.update_id
                )
                raise web.HTTPServiceUnavailable() from None
            finally:
                context.reset(token)
        else:
            logger.info("Webhook update ignored", extra={"details": {"updateId": update.update_id}})
        return web.Response(text="ok")

    async def health(_: web.Request) -> web.Response:
        return web.Response(text="ok")

    async def lifecycle(_: web.Application) -> AsyncIterator[None]:
        logger.info("Bot started")
        task = asyncio.create_task(worker(api, bot, dispatcher, settings.app_url))
        try:
            yield
        finally:
            logger.info("Bot stopping")
            task.cancel()
            await asyncio.gather(task, return_exceptions=True)
            await client.aclose()
            await bot.session.close()

    app.router.add_post("/telegram/webhook", webhook)
    app.router.add_get("/health", health)
    app.cleanup_ctx.append(lifecycle)
    return app


async def register(settings: Settings) -> None:
    async with Bot(settings.token) as bot:
        bot.session.middleware(TelegramLogging())
        await bot.set_webhook(
            settings.app_url + "/telegram/webhook",
            secret_token=settings.webhook_secret,
            allowed_updates=["message"],
            drop_pending_updates=False,
        )
        # Explicit registration also removes previously published command menus.
        for scope in (BotCommandScopeDefault(), BotCommandScopeAllPrivateChats()):
            for language in ("", "uk", "en"):
                await bot.delete_my_commands(scope=scope, language_code=language)
        await bot.set_chat_menu_button(menu_button=MenuButtonDefault())
        logger.info("Webhook registered; command menus cleared")


def main() -> None:
    configure_logging()
    try:
        settings = Settings()
        if len(sys.argv) == 2 and sys.argv[1] == "register-webhook":
            asyncio.run(register(settings))
        else:
            web.run_app(create_app(settings), host="0.0.0.0", port=settings.port, access_log=None)
    except Exception as error:
        log_failure(logger, "Bot terminated", error)
        raise SystemExit(1) from None


if __name__ == "__main__":
    main()
