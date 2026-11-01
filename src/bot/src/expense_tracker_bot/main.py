import asyncio
import logging
import secrets
import sys
from collections.abc import AsyncIterator
from contextlib import suppress
from datetime import UTC, datetime
from typing import Any

import httpx
from aiogram import Bot, Dispatcher
from aiogram.exceptions import TelegramRetryAfter
from aiogram.types import Chat, Update
from aiohttp import web
from pydantic import Field, ValidationError, field_validator
from pydantic_settings import BaseSettings, SettingsConfigDict

from .api import Api, ApiError
from .copy import COPY
from .handlers import router, show_record
from .models import LeaseDto, ProfileDto

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
    callback = update.callback_query
    if (
        message
        and message.chat.type == "private"
        and message.from_user
        and not message.from_user.is_bot
    ):
        return update.model_dump(
            mode="json",
            exclude_none=True,
            include={
                "update_id": True,
                "message": {
                    "message_id": True,
                    "date": True,
                    "chat": {"id", "type"},
                    "from_user": {"id", "is_bot", "first_name", "language_code"},
                    "text": True,
                    "voice": {"file_id", "file_unique_id", "duration", "file_size", "mime_type"},
                },
            },
            by_alias=True,
        )
    if (
        callback
        and callback.message
        and callback.message.chat.type == "private"
        and not callback.from_user.is_bot
    ):
        return update.model_dump(
            mode="json",
            exclude_none=True,
            include={
                "update_id": True,
                "callback_query": {
                    "id": True,
                    "from_user": {"id", "is_bot", "first_name", "language_code"},
                    "chat_instance": True,
                    "data": True,
                    "message": {"message_id": True, "date": True, "chat": {"id", "type"}},
                },
            },
            by_alias=True,
        )
    return None


async def deliver(bot: Bot, client: httpx.AsyncClient, payload: Any) -> None:
    if payload.get("kind") != "saved":
        return
    user_id = int(payload["telegramId"])
    api = Api(client, user_id)
    profile = await api.get("/profile", ProfileDto)
    t = COPY[profile.language]
    await bot.send_message(user_id, t["saved"])
    # Re-read current record; a notification never performs a financial mutation.
    from aiogram.types import Message

    message = Message(
        message_id=0, date=datetime.now(UTC), chat=Chat(id=user_id, type="private")
    ).as_(bot)
    try:
        await show_record(message, api, "e", str(payload["expenseId"]), t)
    except ApiError as error:
        if error.status != 404:
            raise


async def worker(
    kind: str, api: Api, bot: Bot, dispatcher: Dispatcher, client: httpx.AsyncClient, app_url: str
) -> None:
    while True:
        lease: LeaseDto | None = None
        try:
            response = await api.call("POST", f"/queues/{kind}/claim")
            if response.status_code == 204:
                await asyncio.sleep(1)
                continue
            lease = LeaseDto.model_validate(response.json())
            payload = lease.payload.root
            if kind == "inbox":
                await dispatcher.feed_update(
                    bot, Update.model_validate(payload), api_client=client, app_url=app_url
                )
            else:
                await deliver(bot, client, payload)
            await api.call(
                "POST", f"/queues/{lease.id}/result", {"token": str(lease.token), "success": True}
            )
        except asyncio.CancelledError:
            raise
        except TelegramRetryAfter as error:
            await asyncio.sleep(min(error.retry_after, 60))
        except Exception as error:
            # Log types only. Exceptions from Telegram/httpx may include token-bearing URLs.
            logger.warning("%s delivery failed: %s", kind, type(error).__name__)
            if lease:
                with suppress(Exception):
                    await api.call(
                        "POST",
                        f"/queues/{lease.id}/result",
                        {"token": str(lease.token), "success": False},
                    )
            await asyncio.sleep(3)


async def create_app(settings: Settings) -> web.Application:
    bot = Bot(settings.token)
    client = httpx.AsyncClient(
        base_url=settings.backend_url.rstrip("/") + "/",
        headers={"Authorization": "Bearer " + settings.service_key},
        timeout=httpx.Timeout(100, connect=5),
    )
    api = Api(client)
    dispatcher = Dispatcher()
    dispatcher.include_router(router)
    app = web.Application(client_max_size=65536)

    async def webhook(request: web.Request) -> web.Response:
        if not secrets.compare_digest(
            request.headers.get("X-Telegram-Bot-Api-Secret-Token", "").encode(),
            settings.webhook_secret.encode(),
        ):
            raise web.HTTPForbidden()
        try:
            update = Update.model_validate(await request.json())
        except (ValueError, ValidationError):
            raise web.HTTPBadRequest() from None
        payload = minimal_update(update)
        if payload is not None:
            try:
                # 2xx only after durable deduplicated storage. No background acknowledgement.
                async with asyncio.timeout(8):
                    await api.call(
                        "POST", "/inbox", {"updateId": update.update_id, "payload": payload}
                    )
            except Exception:
                raise web.HTTPServiceUnavailable() from None
        return web.Response(text="ok")

    async def health(_: web.Request) -> web.Response:
        return web.Response(text="ok")

    async def lifecycle(_: web.Application) -> AsyncIterator[None]:
        tasks = [
            asyncio.create_task(worker(kind, api, bot, dispatcher, client, settings.app_url))
            for kind in ("inbox", "outbox")
        ]
        try:
            yield
        finally:
            for task in tasks:
                task.cancel()
            await asyncio.gather(*tasks, return_exceptions=True)
            await client.aclose()
            await bot.session.close()

    app.router.add_post("/telegram/webhook", webhook)
    app.router.add_get("/health", health)
    app.cleanup_ctx.append(lifecycle)
    return app


async def register(settings: Settings) -> None:
    async with Bot(settings.token) as bot:
        await bot.set_webhook(
            settings.app_url + "/telegram/webhook",
            secret_token=settings.webhook_secret,
            allowed_updates=["message", "callback_query"],
            drop_pending_updates=False,
        )
        from aiogram.types import MenuButtonWebApp, WebAppInfo

        await bot.set_chat_menu_button(
            menu_button=MenuButtonWebApp(text="Lime", web_app=WebAppInfo(url=settings.app_url))
        )


def main() -> None:
    logging.basicConfig(level=logging.INFO)
    logging.getLogger("httpx").setLevel(logging.WARNING)
    logging.getLogger("aiogram.event").setLevel(logging.WARNING)
    settings = Settings()
    if len(sys.argv) == 2 and sys.argv[1] == "register-webhook":
        asyncio.run(register(settings))
    else:
        web.run_app(create_app(settings), host="0.0.0.0", port=settings.port, access_log=None)


if __name__ == "__main__":
    main()
