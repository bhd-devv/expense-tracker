from collections.abc import AsyncGenerator
from datetime import UTC, datetime
from typing import Any

import pytest
from aiogram import Bot
from aiogram.client.session.base import BaseSession
from aiogram.methods import DeleteMyCommands, SendMessage, SetChatMenuButton, SetWebhook
from aiogram.types import (
    Chat,
    InlineKeyboardMarkup,
    Message,
    ReplyKeyboardRemove,
    User,
    Voice,
)

from expense_tracker_bot.copy import COPY
from expense_tracker_bot.handlers import handle_message
from expense_tracker_bot.main import Settings, register


class FakeTelegram(BaseSession):
    def __init__(self) -> None:
        super().__init__()
        self.sent: list[Any] = []

    async def close(self) -> None:
        pass

    async def make_request(self, bot: Bot, method: Any, timeout: int | None = None) -> Any:  # noqa: ASYNC109 - aiogram contract
        self.sent.append(method)
        if isinstance(method, SendMessage):
            return Message(
                message_id=len(self.sent),
                date=datetime.now(UTC),
                chat=Chat(id=int(method.chat_id), type="private"),
                text=method.text,
            ).as_(bot)
        if isinstance(method, (SetWebhook, DeleteMyCommands, SetChatMenuButton)):
            return True
        raise AssertionError(type(method).__name__)

    async def stream_content(self, url: str, **kwargs: Any) -> AsyncGenerator[bytes]:
        raise AssertionError("The launcher must never download files")
        yield b""


@pytest.mark.parametrize("language", ["uk", "en", "en-GB"])
@pytest.mark.parametrize(
    "entry", ["/start", "/seed", "/anything", "Кава 85", "➕ Додати витрату", "voice"]
)
async def test_every_message_only_shows_two_inline_web_apps(language: str, entry: str) -> None:
    session = FakeTelegram()
    bot = Bot("123456:test-token-used-only-in-local-tests", session=session)
    message = Message(
        message_id=1,
        date=datetime.now(UTC),
        chat=Chat(id=42, type="private"),
        from_user=User(id=42, is_bot=False, first_name="Synthetic", language_code=language),
        text=None if entry == "voice" else entry,
        voice=Voice(file_id="never-download", file_unique_id="voice", duration=5)
        if entry == "voice"
        else None,
    ).as_(bot)
    await handle_message(message, "https://example.com/")
    assert len(session.sent) == 2
    assert isinstance(session.sent[0].reply_markup, ReplyKeyboardRemove)
    launch = session.sent[1]
    assert isinstance(launch, SendMessage)
    assert isinstance(launch.reply_markup, InlineKeyboardMarkup)
    buttons = [button for row in launch.reply_markup.inline_keyboard for button in row]
    t = COPY["en" if language.startswith("en") else "uk"]
    assert [button.text for button in buttons] == [t["expenses"], t["tasks"]]
    assert [button.web_app.url for button in buttons if button.web_app] == [
        "https://example.com",
        "https://example.com/tasks",
    ]
    assert all(button.callback_data is None for button in buttons)
    await bot.session.close()


async def test_explicit_registration_removes_command_menus_and_extra_web_app_button(
    monkeypatch: Any,
) -> None:
    import expense_tracker_bot.main as main

    session = FakeTelegram()
    bot = Bot("123456:test-token-used-only-in-local-tests", session=session)
    monkeypatch.setattr(main, "Bot", lambda _: bot)
    settings = Settings(
        token=bot.token,
        service_key="synthetic-service-key-for-tests-123456789",
        webhook_secret="synthetic-webhook-secret-for-tests-123456",
        app_url="https://example.com",
    )
    await register(settings)
    webhook = session.sent[0]
    assert isinstance(webhook, SetWebhook)
    assert webhook.allowed_updates == ["message"]
    assert webhook.drop_pending_updates is False
    cleared = [method for method in session.sent if isinstance(method, DeleteMyCommands)]
    assert len(cleared) == 6
    assert all(method.scope is not None for method in cleared)
    assert {(method.scope.type, method.language_code) for method in cleared if method.scope} == {
        (scope, language)
        for scope in ("default", "all_private_chats")
        for language in ("", "uk", "en")
    }
    menu = session.sent[-1]
    assert isinstance(menu, SetChatMenuButton)
    assert menu.menu_button is not None
    assert menu.menu_button.type == "default"
