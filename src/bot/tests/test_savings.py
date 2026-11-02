from datetime import UTC, date, datetime
from uuid import uuid4

import httpx
import pytest
from aiogram import Bot
from aiogram.types import CallbackQuery, Chat, Message, User
from test_handlers_integration import FakeTelegram

from expense_tracker_bot.copy import COPY
from expense_tracker_bot.models import ConversationDto, SavingsEntryRequest
from expense_tracker_bot.savings import SavingsAction, SavingsFlow, handle_callback


async def test_confirmation_reuses_durable_key_after_unknown_response() -> None:
    account_id, request_id = uuid4(), uuid4()
    flow = SavingsFlow(
        requestId=request_id,
        accountId=account_id,
        name="Trip",
        currency="USD",
        kind="deposit",
        amount="12.30",
        date=date(2026, 11, 2),
    )
    conversation = ConversationDto(
        state="savings_confirm", entityId=None, field=flow.model_dump_json(), version=7
    )
    submitted: list[SavingsEntryRequest] = []
    account = {
        "id": str(account_id),
        "name": "Trip",
        "currency": "USD",
        "balance": "12.3",
        "added": "12.3",
        "spent": "0",
    }

    async def transport(request: httpx.Request) -> httpx.Response:
        nonlocal conversation
        path = request.url.path
        assert request.headers["X-Telegram-User-Id"] == "12"
        if path.endswith("/profile"):
            return httpx.Response(
                200,
                json={
                    "id": str(uuid4()),
                    "language": "en",
                    "theme": "auto",
                    "timeZone": "Europe/Kyiv",
                    "currency": "UAH",
                    "today": "2026-11-02",
                    "aiConsent": False,
                    "version": 1,
                },
            )
        if path.endswith("/conversation"):
            if request.method == "PUT":
                conversation = ConversationDto.model_validate_json(request.content)
                conversation.version += 1
            return httpx.Response(200, json=conversation.model_dump(mode="json"))
        if path.endswith("/entries"):
            if request.method == "GET":
                return httpx.Response(
                    200, json={"account": account, "items": [], "page": 0, "hasMore": False}
                )
            submitted.append(SavingsEntryRequest.model_validate_json(request.content))
            if len(submitted) == 1:
                raise httpx.ReadTimeout("Synthetic lost response", request=request)
            return httpx.Response(
                200,
                json={
                    "id": str(request_id),
                    "kind": "deposit",
                    "amount": "12.3",
                    "date": "2026-11-02",
                    "description": "",
                },
            )
        raise AssertionError(path)

    session = FakeTelegram()
    bot = Bot("123456:synthetic-test-token", session=session)
    message = Message(message_id=1, date=datetime.now(UTC), chat=Chat(id=12, type="private")).as_(
        bot
    )
    data = SavingsAction(action="save", version=7)
    callback = CallbackQuery(
        id="save-cb",
        chat_instance="test",
        message=message,
        from_user=User(id=12, is_bot=False, first_name="Synthetic"),
        data=data.pack(),
    ).as_(bot)
    async with httpx.AsyncClient(
        base_url="http://backend/internal/bot/", transport=httpx.MockTransport(transport)
    ) as client:
        await handle_callback(callback, data, client)
        assert conversation.state == "savings_confirm"
        await handle_callback(callback, data, client)
        assert len(submitted) == 2
        assert submitted[0] == submitted[1]
        assert submitted[0].requestId == request_id
        assert conversation.state == "idle"
        await handle_callback(callback, data, client)
        assert len(submitted) == 2
    await bot.session.close()


@pytest.mark.parametrize("lang", ["uk", "en"])
def test_savings_keyboard_has_localized_actions_and_bounded_callbacks(lang: str) -> None:
    assert COPY[lang]["savings"]
    for action, value in [("entry", "withdrawal"), ("history", "10000"), ("currency", "USD")]:
        data = SavingsAction(action=action, entity="a" * 32, value=value, version=2147483647)
        assert len(data.pack().encode()) <= 64
