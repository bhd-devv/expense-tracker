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
from aiogram.types import (
    CallbackQuery,
    Chat,
    InlineKeyboardMarkup,
    Message,
    ReplyKeyboardMarkup,
    Update,
    User,
)

from expense_tracker_bot.api import Api
from expense_tracker_bot.copy import COPY
from expense_tracker_bot.handlers import Action, handle_callback, handle_message, router
from expense_tracker_bot.main import minimal_update
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
                # Telegram Message exposes only inline markup, never a reply keyboard.
                reply_markup=method.reply_markup
                if isinstance(method.reply_markup, InlineKeyboardMarkup)
                else None,
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


@pytest.mark.parametrize("language", ["uk", "en"])
@pytest.mark.parametrize("entry", ["menu", "start"])
async def test_app_launch_uses_inline_web_app_and_preserves_reply_menu(
    language: str, entry: str
) -> None:
    calls: list[str] = []

    async def backend(request: httpx.Request) -> httpx.Response:
        calls.append(request.url.path)
        assert request.headers["X-Telegram-User-Id"] == "42"
        if request.url.path == "/internal/bot/actor/profile":
            return httpx.Response(
                200,
                json={
                    "id": "00000000-0000-0000-0000-000000000042",
                    "language": language,
                    "theme": "auto",
                    "timeZone": "Europe/Kyiv",
                    "currency": "UAH",
                    "aiConsent": False,
                    "version": 1,
                    "today": "2026-11-01",
                },
            )
        assert request.url.path == "/internal/bot/actor/conversation"
        return httpx.Response(
            200,
            json={
                "state": "idle",
                "entityId": None,
                "field": None,
                "version": 1 if request.method == "PUT" else 0,
            },
        )

    session = FakeTelegram()
    bot = Bot("123456:test-token-used-only-in-local-tests", session=session)
    message = Message(
        message_id=1,
        date=datetime.now(UTC),
        chat=Chat(id=42, type="private"),
        from_user=User(id=42, is_bot=False, first_name="Synthetic", language_code=language),
        text=COPY[language]["app"] if entry == "menu" else "/start app",
    ).as_(bot)
    async with httpx.AsyncClient(
        base_url="http://backend/internal/bot/", transport=httpx.MockTransport(backend)
    ) as client:
        await handle_message(
            message, client, "https://example.com", Update(update_id=1, message=message), bot
        )
    assert calls == [
        "/internal/bot/actor/profile",
        "/internal/bot/actor/conversation",
        "/internal/bot/actor/conversation",
    ]
    sent = session.sent[-1]
    assert isinstance(sent, SendMessage)
    assert sent.text == COPY[language]["openAppPrompt"]
    assert isinstance(sent.reply_markup, InlineKeyboardMarkup)
    launch = sent.reply_markup.inline_keyboard[0][0]
    assert launch.web_app is not None
    assert launch.web_app.url == "https://example.com"
    if entry == "start":
        assert len(session.sent) == 2
        reply = session.sent[0].reply_markup
        assert isinstance(reply, ReplyKeyboardMarkup)
        assert reply.is_persistent
        assert all(button.web_app is None for row in reply.keyboard for button in row)
    await bot.session.close()


@pytest.mark.parametrize("language", ["uk", "en"])
@pytest.mark.parametrize("outcome", ["created", "exists", "limit", "timeout"])
async def test_seed_routes_through_ingress_without_ai_or_implicit_retry(
    language: str, outcome: str
) -> None:
    calls: list[httpx.Request] = []

    async def backend(request: httpx.Request) -> httpx.Response:
        calls.append(request)
        assert request.headers["X-Telegram-User-Id"] == "42"
        assert request.headers["X-Telegram-Language"] == language
        if request.method == "GET" and request.url.path == "/internal/bot/actor/profile":
            return httpx.Response(
                200,
                json={
                    "id": "00000000-0000-0000-0000-000000000042",
                    "language": language,
                    "theme": "auto",
                    "timeZone": "Europe/Kyiv",
                    "currency": "UAH",
                    "aiConsent": False,
                    "version": 1,
                    "today": "2026-11-01",
                },
            )
        assert request.method == "POST" and request.url.path == "/internal/bot/actor/seed"
        if outcome == "timeout":
            raise httpx.ReadTimeout("Result unknown", request=request)
        if outcome == "limit":
            return httpx.Response(422, json={"code": "catalog_limit"})
        return httpx.Response(
            200,
            json={
                "createdCount": 70 if outcome == "created" else 0,
                "totalCount": 70,
                "from": "2026-10-01",
                "to": "2026-10-31",
            },
        )

    session = FakeTelegram()
    bot = Bot("123456:test-token-used-only-in-local-tests", session=session)
    update = Update(
        update_id=1,
        message=Message(
            message_id=1,
            date=datetime.now(UTC),
            chat=Chat(id=42, type="private"),
            from_user=User(id=42, is_bot=False, first_name="Synthetic", language_code=language),
            text="/seed",
        ),
    )
    payload = minimal_update(update)
    assert payload is not None
    message = Update.model_validate(payload).message
    assert message is not None
    async with httpx.AsyncClient(
        base_url="http://backend/internal/bot/", transport=httpx.MockTransport(backend)
    ) as client:
        await router.propagate_event(
            "message",
            message.as_(bot),
            bot=bot,
            api_client=client,
            app_url="https://example.com",
            event_update=update,
        )
    assert len(calls) == 2
    assert len(session.sent) == 1
    response = session.sent[0]
    assert isinstance(response, SendMessage)
    key = {
        "created": "seedAdded",
        "exists": "seedExists",
        "limit": "seedLimit",
        "timeout": "seedUnknown",
    }[outcome]
    assert COPY[language][key].format(count=70) in response.text
    await bot.session.close()


@pytest.mark.parametrize("language", ["uk", "en"])
@pytest.mark.parametrize("entry", ["legacy_menu", "removed_callback"])
async def test_removed_catalogue_actions_refresh_menu_without_creating_expenses(
    language: str, entry: str
) -> None:
    calls: list[str] = []

    async def backend(request: httpx.Request) -> httpx.Response:
        calls.append(request.url.path)
        if request.url.path.endswith("/profile"):
            return httpx.Response(
                200,
                json={
                    "id": "00000000-0000-0000-0000-000000000042",
                    "language": language,
                    "theme": "auto",
                    "timeZone": "Europe/Kyiv",
                    "currency": "UAH",
                    "aiConsent": False,
                    "version": 1,
                    "today": "2026-11-02",
                },
            )
        assert request.url.path.endswith("/conversation")
        return httpx.Response(
            200, json={"state": "idle", "entityId": None, "field": None, "version": 1}
        )

    session = FakeTelegram()
    bot = Bot("123456:test-token-used-only-in-local-tests", session=session)
    user = User(id=42, is_bot=False, first_name="Synthetic", language_code=language)
    message = Message(
        message_id=1,
        date=datetime.now(UTC),
        chat=Chat(id=42, type="private"),
        from_user=user,
        text="💳 Accounts" if language == "en" else "💳 Рахунки",
    ).as_(bot)
    async with httpx.AsyncClient(
        base_url="http://backend/internal/bot/", transport=httpx.MockTransport(backend)
    ) as client:
        if entry == "legacy_menu":
            await handle_message(
                message, client, "https://example.com", Update(update_id=1, message=message), bot
            )
            assert len(calls) == 3
            assert isinstance(session.sent[-1].reply_markup, ReplyKeyboardMarkup)
        else:
            action = Action(action="new", entity="accounts")
            callback = CallbackQuery(
                id="synthetic",
                from_user=user,
                chat_instance="test",
                message=message,
                data=action.pack(),
            ).as_(bot)
            await handle_callback(callback, action, client, "https://example.com")
            assert len(calls) == 1
    response = session.sent[-1]
    assert isinstance(response, SendMessage)
    assert response.text == COPY[language]["stale"]
    assert all("/drafts" not in path for path in calls)
    await bot.session.close()


@pytest.mark.parametrize("language", ["uk", "en"])
@pytest.mark.parametrize("kind", ["d", "e"])
async def test_currency_button_updates_record_without_a_financial_account(
    language: str, kind: str
) -> None:
    record_id = "00000000-0000-0000-0000-000000000043"
    category_id = "00000000-0000-0000-0000-000000000044"
    record = {
        "id": record_id,
        "amount": "12.5",
        "currency": "UAH",
        "description": "Synthetic lunch",
        "date": "2026-10-12",
        "categoryId": category_id,
        "version": 3,
        **(
            {"status": "ready", "transcript": "", "errorCode": None, "expenseId": None}
            if kind == "d"
            else {"source": "bot"}
        ),
    }
    changes: list[dict[str, object]] = []

    async def backend(request: httpx.Request) -> httpx.Response:
        import json

        path = request.url.path
        if path.endswith("/profile"):
            return httpx.Response(
                200,
                json={
                    "id": "00000000-0000-0000-0000-000000000042",
                    "language": language,
                    "theme": "auto",
                    "timeZone": "Europe/Kyiv",
                    "currency": "UAH",
                    "aiConsent": True,
                    "version": 1,
                    "today": "2026-11-02",
                },
            )
        if path.endswith("/conversation"):
            return httpx.Response(
                200,
                json={
                    "state": "record",
                    "entityId": record_id,
                    "field": f"{kind}.currency.3",
                    "version": 5,
                },
            )
        if path.endswith("/catalog"):
            return httpx.Response(
                200,
                json={
                    "categories": [
                        {
                            "id": category_id,
                            "name": "Food",
                            "code": "food",
                            "archived": False,
                            "version": 1,
                        }
                    ]
                },
            )
        assert path.endswith(f"/{'drafts' if kind == 'd' else 'expenses'}/{record_id}")
        if request.method == "PUT":
            body = json.loads(request.content)
            changes.append(body)
            record.update({"currency": body["currency"], "version": 4})
        return httpx.Response(200, json=record)

    session = FakeTelegram()
    bot = Bot("123456:test-token-used-only-in-local-tests", session=session)
    user = User(id=42, is_bot=False, first_name="Synthetic", language_code=language)
    message = Message(
        message_id=1, date=datetime.now(UTC), chat=Chat(id=42, type="private"), from_user=user
    ).as_(bot)
    action = Action(action="select", entity="USD", field="record", version=5)
    callback = CallbackQuery(
        id="synthetic", from_user=user, chat_instance="test", message=message, data=action.pack()
    ).as_(bot)
    async with httpx.AsyncClient(
        base_url="http://backend/internal/bot/", transport=httpx.MockTransport(backend)
    ) as client:
        await handle_callback(callback, action, client, "https://example.com")
    assert changes == [
        {
            "amount": "12.5",
            "currency": "USD",
            "description": "Synthetic lunch",
            "date": "2026-10-12",
            "categoryId": category_id,
            "version": 3,
        }
    ]
    response = session.sent[-1]
    assert isinstance(response, SendMessage)
    assert "12.5 USD" in response.text
    await bot.session.close()
