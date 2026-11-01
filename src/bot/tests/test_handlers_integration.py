import os
import random
from collections.abc import AsyncGenerator
from datetime import UTC, datetime
from typing import Any

import httpx
import pytest
from aiogram import Bot
from aiogram.client.session.base import BaseSession
from aiogram.methods import AnswerCallbackQuery, EditMessageReplyMarkup, SendMessage
from aiogram.types import CallbackQuery, Chat, Message, Update, User

from expense_tracker_bot.api import Api
from expense_tracker_bot.handlers import Action, handle_callback, handle_message
from expense_tracker_bot.models import CatalogDto, ExpensePage


class FakeTelegram(BaseSession):
    def __init__(self) -> None:
        super().__init__()
        self.sent: list[Any] = []

    async def close(self) -> None:
        pass

    async def make_request(self, bot: Bot, method: Any, timeout: int | None = None) -> Any:  # noqa: ASYNC109 - aiogram contract
        self.sent.append(method)
        if isinstance(method, (SendMessage, EditMessageReplyMarkup)):
            return Message(
                message_id=len(self.sent),
                date=datetime.now(UTC),
                chat=Chat(id=int(method.chat_id), type="private"),
                text=getattr(method, "text", ""),
                reply_markup=method.reply_markup,
            ).as_(bot)
        if isinstance(method, AnswerCallbackQuery):
            return True
        raise AssertionError(type(method).__name__)

    async def stream_content(self, url: str, **kwargs: Any) -> AsyncGenerator[bytes]:
        raise AssertionError("No Telegram downloads in this test")
        yield b""


@pytest.mark.skipif(not os.getenv("EXPENSE_BOT_TEST_URL"), reason="Requires isolated running API")
async def test_real_backend_draft_edit_confirm_and_duplicate_callback() -> None:
    user_id = random.randint(10_000_000_000, 90_000_000_000)
    session = FakeTelegram()
    bot = Bot("123456:test-token-used-only-in-local-tests", session=session)
    async with httpx.AsyncClient(
        base_url=os.environ["EXPENSE_BOT_TEST_URL"],
        headers={"Authorization": "Bearer local-test-service-key-not-for-production-12345678"},
        timeout=10,
    ) as client:
        user = User(id=user_id, is_bot=False, first_name="Synthetic", language_code="uk")
        message = Message(
            message_id=1,
            date=datetime.now(UTC),
            chat=Chat(id=user_id, type="private"),
            from_user=user,
            text="Кава 75",
        ).as_(bot)
        update = Update(update_id=random.randint(10_000_000, 900_000_000), message=message)
        await handle_message(message, client, "https://example.com", update, bot)
        sent = session.sent[-1]
        assert isinstance(sent, SendMessage) and sent.reply_markup is not None
        draft_id = Action.unpack(sent.reply_markup.inline_keyboard[0][0].callback_data).entity
        api = Api(client, user_id)
        catalog = await api.get("/catalog", CatalogDto)
        from expense_tracker_bot.models import DraftDto

        draft = await api.get(f"/drafts/{draft_id}", DraftDto)
        draft = await api.mutate(
            "PUT",
            f"/drafts/{draft_id}",
            DraftDto,
            {
                **draft.model_dump(mode="json"),
                "amount": "75",
                "description": "Кава",
                "categoryId": str(catalog.categories[0].id),
            },
        )
        action = Action(action="save", entity=draft_id, field="d", version=draft.version)
        callback = CallbackQuery(
            id="synthetic-callback",
            from_user=user,
            chat_instance="test",
            message=message,
            data=action.pack(),
        ).as_(bot)
        await handle_callback(callback, action, client, "https://example.com")
        await handle_callback(callback, action, client, "https://example.com")
        expenses = await api.get("/expenses", ExpensePage)
        assert len(expenses.items) == 1
        assert expenses.items[0].amount == "75"
    await bot.session.close()
