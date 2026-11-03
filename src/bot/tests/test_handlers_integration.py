import json
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
    Voice,
)

from expense_tracker_bot.api import Api
from expense_tracker_bot.copy import COPY
from expense_tracker_bot.handlers import (
    Action,
    browse,
    catalogs,
    handle_callback,
    handle_message,
    router,
)
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
@pytest.mark.parametrize("entry", ["menu", "start", "tasks", "old_tasks_language"])
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
    text = "/start app"
    if entry == "menu":
        text = COPY[language]["app"]
    elif entry == "tasks":
        text = COPY[language]["tasks"]
    elif entry == "old_tasks_language":
        text = COPY["en" if language == "uk" else "uk"]["tasks"]
    message = Message(
        message_id=1,
        date=datetime.now(UTC),
        chat=Chat(id=42, type="private"),
        from_user=User(id=42, is_bot=False, first_name="Synthetic", language_code=language),
        text=text,
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
    tasks = entry in ("tasks", "old_tasks_language")
    assert sent.text == COPY[language]["openTasksPrompt" if tasks else "openAppPrompt"]
    assert isinstance(sent.reply_markup, InlineKeyboardMarkup)
    launch = sent.reply_markup.inline_keyboard[0][0]
    assert launch.web_app is not None
    assert launch.web_app.url == "https://example.com" + ("/tasks" if tasks else "")
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
async def test_obsolete_currency_button_cannot_change_record(language: str, kind: str) -> None:
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
    assert changes == []
    response = session.sent[-1]
    assert isinstance(response, SendMessage)
    assert response.text == COPY[language]["stale"]
    await bot.session.close()


@pytest.mark.parametrize("language", ["uk", "en"])
@pytest.mark.parametrize("entry", ["menu", "text", "voice"])
async def test_manual_entry_uses_fields_without_ai_or_audio_download(
    language: str, entry: str
) -> None:
    draft_id = "00000000-0000-0000-0000-000000000043"
    draft = {
        "id": draft_id,
        "status": "ready",
        "amount": None,
        "currency": "UAH",
        "description": "Coffee" if entry == "text" else "",
        "transcript": "",
        "date": "2026-11-02",
        "categoryId": None,
        "errorCode": None,
        "version": 1,
        "expenseId": None,
    }
    calls: list[httpx.Request] = []

    async def backend(request: httpx.Request) -> httpx.Response:
        calls.append(request)
        path = request.url.path.rsplit("/", 1)[-1]
        if path == "profile":
            return httpx.Response(
                200,
                json={
                    "id": "00000000-0000-0000-0000-000000000042",
                    "language": language,
                    "theme": "auto",
                    "timeZone": "Europe/Kyiv",
                    "currency": "UAH",
                    "aiConsent": False,
                    "inputMode": "manual",
                    "version": 1,
                    "today": "2026-11-02",
                },
            )
        if path == "conversation":
            return httpx.Response(
                200,
                json={
                    "state": "idle",
                    "entityId": None,
                    "field": None,
                    "version": 1,
                },
            )
        if path == "drafts":
            assert request.method == "POST"
            payload = json.loads(request.content)
            assert payload["mode"] == "manual"
            assert payload["text"] == ("Coffee" if entry == "text" else "")
            return httpx.Response(200, json=draft)
        if path == draft_id:
            return httpx.Response(200, json=draft)
        if path == "catalog":
            return httpx.Response(200, json={"categories": []})
        raise AssertionError(f"Unexpected API request: {request.method} {request.url.path}")

    session = FakeTelegram()
    bot = Bot("123456:test-token-used-only-in-local-tests", session=session)
    message = Message(
        message_id=1,
        date=datetime.now(UTC),
        chat=Chat(id=42, type="private"),
        from_user=User(id=42, is_bot=False, first_name="Synthetic", language_code=language),
        text=COPY[language]["add"] if entry == "menu" else "Coffee" if entry == "text" else None,
        voice=Voice(file_id="synthetic", file_unique_id="synthetic", duration=1)
        if entry == "voice"
        else None,
    ).as_(bot)
    async with httpx.AsyncClient(
        base_url="http://backend/internal/bot/", transport=httpx.MockTransport(backend)
    ) as client:
        await handle_message(
            message, client, "https://example.com", Update(update_id=12, message=message), bot
        )
    if entry == "voice":
        assert all(request.method == "GET" for request in calls)
        assert session.sent[-1].text == COPY[language]["manualVoice"]
    else:
        result = session.sent[-1]
        assert isinstance(result, SendMessage)
        assert isinstance(result.reply_markup, InlineKeyboardMarkup)
        actions = [
            Action.unpack(button.callback_data)
            for row in result.reply_markup.inline_keyboard
            for button in row
            if button.callback_data
        ]
        assert {action.field for action in actions if action.action == "field"} == {
            "d.amount",
            "d.description",
            "d.date",
            "d.categoryId",
        }
        assert "2026-11-02" in result.text
        assert COPY[language]["manual"] not in result.text
        assert sum(request.method == "POST" for request in calls) == 1
    await bot.session.close()


@pytest.mark.parametrize("language", ["uk", "en"])
async def test_entry_mode_switch_preserves_ai_consent(language: str) -> None:
    profile = {
        "id": "00000000-0000-0000-0000-000000000042",
        "language": language,
        "theme": "auto",
        "timeZone": "Europe/Kyiv",
        "currency": "UAH",
        "aiConsent": False,
        "inputMode": "ai",
        "version": 1,
        "today": "2026-11-02",
    }
    conversation = {"state": "preference", "entityId": None, "field": "inputMode.1", "version": 3}

    async def backend(request: httpx.Request) -> httpx.Response:
        if request.url.path.endswith("/profile"):
            if request.method == "PUT":
                payload = json.loads(request.content)
                assert payload["inputMode"] == "manual"
                assert payload["aiConsent"] is False
                profile.update(payload)
                profile["version"] = 2
            return httpx.Response(200, json=profile)
        assert request.url.path.endswith("/conversation")
        if request.method == "PUT":
            conversation.update(json.loads(request.content))
            conversation["version"] = 4
        return httpx.Response(200, json=conversation)

    session = FakeTelegram()
    bot = Bot("123456:test-token-used-only-in-local-tests", session=session)
    user = User(id=42, is_bot=False, first_name="Synthetic", language_code=language)
    message = Message(message_id=1, date=datetime.now(UTC), chat=Chat(id=42, type="private")).as_(
        bot
    )
    action = Action(action="preference", entity="manual", field="inputMode", version=3)
    callback = CallbackQuery(
        id="synthetic-mode",
        from_user=user,
        chat_instance="test",
        message=message,
        data=action.pack(),
    ).as_(bot)
    async with httpx.AsyncClient(
        base_url="http://backend/internal/bot/", transport=httpx.MockTransport(backend)
    ) as client:
        await handle_callback(callback, action, client, "https://example.com")
    assert profile["inputMode"] == "manual"
    assert profile["aiConsent"] is False
    settings_message = session.sent[-1]
    assert isinstance(settings_message, SendMessage)
    assert isinstance(settings_message.reply_markup, InlineKeyboardMarkup)
    assert any(
        COPY[language]["manualMode"] in button.text
        for row in settings_message.reply_markup.inline_keyboard
        for button in row
    )
    await bot.session.close()


@pytest.mark.parametrize("language", ["uk", "en"])
async def test_category_icon_picker_groups_selection_and_stale_buttons(language: str) -> None:
    category_id = "00000000-0000-0000-0000-000000000043"
    item = {
        "id": category_id,
        "name": "Їжа та напої",
        "code": "food",
        "icon": "utensils-crossed",
        "archived": False,
        "version": 1,
    }
    conversation = {"state": "idle", "entityId": None, "field": None, "version": 0}
    mutations: list[dict[str, object]] = []
    library = {
        "groups": [
            {
                "id": "food",
                "uk": "Їжа та напої",
                "en": "Food & drinks",
                "icons": [
                    {"id": "coffee", "uk": "Кава", "en": "Coffee", "symbol": "☕"},
                    {"id": "utensils-crossed", "uk": "Їжа", "en": "Meals", "symbol": "🍽"},
                ],
            }
        ]
    }

    async def backend(request: httpx.Request) -> httpx.Response:
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
                    "aiConsent": False,
                    "inputMode": "manual",
                    "version": 1,
                    "today": "2026-11-02",
                },
            )
        if path.endswith("/conversation"):
            if request.method == "PUT":
                payload = json.loads(request.content)
                conversation.update(payload)
                conversation["version"] = payload["version"] + 1
            return httpx.Response(200, json=conversation)
        if path.endswith("/catalog"):
            return httpx.Response(200, json={"categories": [item]})
        if path.endswith("/category-icons"):
            return httpx.Response(200, json=library)
        assert path.endswith(f"/categories/{category_id}")
        assert request.method == "PUT"
        payload = json.loads(request.content)
        mutations.append(payload)
        assert payload["name"] == item["name"]
        assert payload["code"] == "food"
        assert payload["icon"] == "coffee"
        assert payload["version"] == 1
        item.update(payload)
        item["version"] = 2
        return httpx.Response(200, json=item)

    session = FakeTelegram()
    bot = Bot("123456:test-token-used-only-in-local-tests", session=session)
    user = User(id=42, is_bot=False, first_name="Synthetic", language_code=language)
    message = Message(message_id=1, date=datetime.now(UTC), chat=Chat(id=42, type="private")).as_(
        bot
    )
    async with httpx.AsyncClient(
        base_url="http://backend/internal/bot/", transport=httpx.MockTransport(backend)
    ) as client:
        for action in [
            Action(
                action="icon", entity=category_id.replace("-", ""), field="categories", version=1
            ),
            Action(action="icongroup", entity="food", version=1),
            Action(action="iconset", entity="coffee", version=1),
            Action(action="iconset", entity="coffee", version=1),
        ]:
            assert len(action.pack().encode()) <= 64
            callback = CallbackQuery(
                id="synthetic-icon",
                from_user=user,
                chat_instance="test",
                message=message,
                data=action.pack(),
            ).as_(bot)
            await handle_callback(callback, action, client, "https://example.com")
            if action.action == "icongroup":
                result = session.sent[-1]
                assert isinstance(result, SendMessage)
                assert isinstance(result.reply_markup, InlineKeyboardMarkup)
                assert any(
                    "☕" in button.text
                    for row in result.reply_markup.inline_keyboard
                    for button in row
                )
                assert any(
                    "✓" in button.text
                    for row in result.reply_markup.inline_keyboard
                    for button in row
                )
    assert len(mutations) == 1
    assert item["icon"] == "coffee"
    assert session.sent[-1].text == COPY[language]["stale"]
    await bot.session.close()


@pytest.mark.parametrize("language", ["uk", "en"])
@pytest.mark.parametrize(
    "kind,in_use", [("categories", False), ("categories", True), ("expenses", False)]
)
async def test_inline_deletion_requires_confirmation_and_preserves_used_categories(
    language: str, kind: str, in_use: bool
) -> None:
    entity_id = "00000000-0000-0000-0000-000000000043"
    item = {
        "id": entity_id,
        "name": "Custom",
        "code": None,
        "icon": "tags",
        "archived": False,
        "version": 7,
    }
    expense = {
        "id": entity_id,
        "amount": "12.50",
        "currency": "UAH",
        "description": "Lunch",
        "date": "2026-11-02",
        "categoryId": entity_id,
        "source": "bot",
        "version": 7,
    }
    conversation = {"state": "idle", "entityId": None, "field": None, "version": 0}
    removed = False
    delete_calls = 0

    async def backend(request: httpx.Request) -> httpx.Response:
        nonlocal removed, delete_calls
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
                    "aiConsent": False,
                    "inputMode": "manual",
                    "version": 1,
                    "today": "2026-11-02",
                },
            )
        if path.endswith("/catalog"):
            return httpx.Response(
                200, json={"categories": [] if removed and kind == "categories" else [item]}
            )
        if path.endswith("/conversation"):
            if request.method == "PUT":
                payload = json.loads(request.content)
                conversation.update(payload)
                conversation["version"] = payload["version"] + 1
            return httpx.Response(200, json=conversation)
        if path.endswith("/expenses"):
            return httpx.Response(
                200, json={"items": [] if removed else [expense], "page": 0, "hasMore": False}
            )
        assert path.endswith(f"/{kind}/{entity_id}")
        if request.method == "GET":
            return (
                httpx.Response(404, json={"code": "not_found"})
                if removed
                else httpx.Response(200, json=expense)
            )
        assert request.method == "DELETE"
        assert request.url.params["version"] == "7"
        delete_calls += 1
        if in_use:
            return httpx.Response(422, json={"code": "category_in_use"})
        removed = True
        return httpx.Response(204)

    session = FakeTelegram()
    bot = Bot("123456:test-token-used-only-in-local-tests", session=session)
    user = User(id=42, is_bot=False, first_name="Synthetic", language_code=language)
    message = Message(message_id=1, date=datetime.now(UTC), chat=Chat(id=42, type="private")).as_(
        bot
    )
    async with httpx.AsyncClient(
        base_url="http://backend/internal/bot/", transport=httpx.MockTransport(backend)
    ) as client:
        api = Api(client, 42, language)
        if kind == "categories":
            await catalogs(message, api, COPY[language])
        else:
            await browse(message, api, COPY[language])
        listing = session.sent[-1]
        assert isinstance(listing, SendMessage)
        assert isinstance(listing.reply_markup, InlineKeyboardMarkup)
        first_row = listing.reply_markup.inline_keyboard[0]
        assert len(first_row) == 2
        assert first_row[1].callback_data is not None
        deletion = Action.unpack(first_row[1].callback_data)
        assert deletion.action == ("catdelete" if kind == "categories" else "delete")
        assert deletion.version == 7
        assert all(
            Action.unpack(button.callback_data or "").action != "archive"
            for row in listing.reply_markup.inline_keyboard
            for button in row
        )

        async def click(action: Action) -> None:
            assert len(action.pack().encode()) <= 64
            callback = CallbackQuery(
                id="synthetic-delete",
                from_user=user,
                chat_instance="test",
                message=message,
                data=action.pack(),
            ).as_(bot)
            await handle_callback(callback, action, client, "https://example.com")

        await click(deletion)
        assert delete_calls == 0
        confirmation = session.sent[-1]
        assert isinstance(confirmation, SendMessage)
        assert isinstance(confirmation.reply_markup, InlineKeyboardMarkup)
        confirm, cancel = confirmation.reply_markup.inline_keyboard[0]
        assert confirm.callback_data is not None and cancel.callback_data is not None
        await click(Action.unpack(cancel.callback_data))
        assert delete_calls == 0
        await click(Action.unpack(confirm.callback_data))
        assert delete_calls == 1
        assert removed == (not in_use)
        if in_use:
            assert session.sent[-1].text == COPY[language]["categoryInUse"]
        else:
            await click(Action.unpack(confirm.callback_data))
            assert delete_calls == 1
            assert session.sent[-1].text == COPY[language]["stale"]
    await bot.session.close()


async def test_inline_category_actions_paginate_full_catalog_within_telegram_button_limit() -> None:
    items = [
        {
            "id": f"00000000-0000-0000-0000-{index:012d}",
            "name": f"Category {index}",
            "code": None,
            "archived": False,
            "version": 1,
        }
        for index in range(50)
    ]

    async def backend(request: httpx.Request) -> httpx.Response:
        assert request.url.path.endswith("/catalog")
        return httpx.Response(200, json={"categories": items})

    session = FakeTelegram()
    bot = Bot("123456:test-token-used-only-in-local-tests", session=session)
    message = Message(message_id=1, date=datetime.now(UTC), chat=Chat(id=42, type="private")).as_(
        bot
    )
    seen = []
    async with httpx.AsyncClient(
        base_url="http://backend/internal/bot/", transport=httpx.MockTransport(backend)
    ) as client:
        for page in range(3):
            await catalogs(message, Api(client, 42), COPY["uk"], page)
            result = session.sent[-1]
            assert isinstance(result, SendMessage)
            assert isinstance(result.reply_markup, InlineKeyboardMarkup)
            actions = [
                Action.unpack(button.callback_data or "")
                for row in result.reply_markup.inline_keyboard
                for button in row
            ]
            assert len(actions) <= 100
            seen.extend(action.entity for action in actions if action.action == "catdelete")
    assert len(seen) == len(set(seen)) == 50
    await bot.session.close()


@pytest.mark.parametrize("language", ["uk", "en"])
async def test_category_limit_uses_profile_currency_and_checks_stale_callbacks(
    language: str,
) -> None:
    entity_id = "00000000-0000-0000-0000-000000000043"
    item = {
        "id": entity_id,
        "name": "Custom",
        "code": None,
        "icon": "tags",
        "archived": False,
        "version": 3,
        "limit": None,
    }
    conversation = {"state": "idle", "entityId": None, "field": None, "version": 0}
    mutations: list[dict[str, object]] = []

    async def backend(request: httpx.Request) -> httpx.Response:
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
                    "aiConsent": False,
                    "inputMode": "manual",
                    "version": 1,
                    "today": "2026-11-02",
                },
            )
        if path.endswith("/catalog"):
            return httpx.Response(200, json={"categories": [item]})
        if path.endswith("/conversation"):
            if request.method == "PUT":
                payload = json.loads(request.content)
                conversation.update(payload)
                conversation["version"] = payload["version"] + 1
            return httpx.Response(200, json=conversation)
        assert path.endswith(f"/categories/{entity_id}/limit")
        assert request.method == "PUT"
        payload = json.loads(request.content)
        mutations.append(payload)
        assert payload["version"] == item["version"]
        if payload["amount"] is None:
            assert payload["currency"] is None
            item["limit"] = None
        else:
            assert payload["amount"] == "80.00"
            assert payload["currency"] == "UAH"
            item["limit"] = {
                "amount": "80",
                "currency": "UAH",
                "spent": "100",
                "usedPercent": 125,
                "exceeded": True,
                "from": "2026-11-01",
                "to": "2026-11-30",
            }
        item["version"] = payload["version"] + 1
        return httpx.Response(200, json=item)

    session = FakeTelegram()
    bot = Bot("123456:test-token-used-only-in-local-tests", session=session)
    user = User(id=42, is_bot=False, first_name="Synthetic", language_code=language)
    message = Message(message_id=1, date=datetime.now(UTC), chat=Chat(id=42, type="private")).as_(
        bot
    )
    async with httpx.AsyncClient(
        base_url="http://backend/internal/bot/", transport=httpx.MockTransport(backend)
    ) as client:

        async def click(action: Action) -> None:
            assert len(action.pack().encode()) <= 64
            callback = CallbackQuery(
                id="synthetic-limit",
                from_user=user,
                chat_instance="test",
                message=message,
                data=action.pack(),
            ).as_(bot)
            await handle_callback(callback, action, client, "https://example.com")

        original = Action(
            action="catlimit", entity=entity_id.replace("-", ""), field="categories", version=3
        )
        await click(original)
        assert conversation["state"] == "category_limit"
        prompt = session.sent[-1]
        assert isinstance(prompt, SendMessage)
        assert "UAH" in prompt.text
        assert isinstance(prompt.reply_markup, InlineKeyboardMarkup)
        assert all(
            Action.unpack(button.callback_data).action != "limitcurrency"
            for row in prompt.reply_markup.inline_keyboard
            for button in row
            if button.callback_data
        )
        await click(Action(action="limitcurrency", entity="BTC", version=1))
        assert not mutations
        assert session.sent[-1].text == COPY[language]["stale"]
        typed = Message(
            message_id=2,
            date=datetime.now(UTC),
            chat=Chat(id=42, type="private"),
            from_user=user,
            text="80,00",
        ).as_(bot)
        await handle_message(typed, bot, client, "https://example.com")
        assert len(mutations) == 1
        assert conversation["state"] == "idle"
        assert "125%" in session.sent[-1].text
        assert "██████████" in session.sent[-1].text
        assert COPY[language]["limitExceeded"] in session.sent[-1].text
        assert "100 / 80 UAH" in session.sent[-1].text
        await click(original)
        assert len(mutations) == 1
        assert session.sent[-1].text == COPY[language]["stale"]
        await click(
            Action(action="catlimit", entity=original.entity, field="categories", version=4)
        )
        clear = Action(action="limitclear", entity=original.entity, field="categories", version=4)
        await click(clear)
        assert len(mutations) == 2
        assert item["limit"] is None
        await click(clear)
        assert len(mutations) == 2
        assert session.sent[-1].text == COPY[language]["stale"]
    await bot.session.close()
