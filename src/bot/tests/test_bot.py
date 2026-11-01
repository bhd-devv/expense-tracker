import httpx
import pytest
from aiogram.types import Update
from pydantic import ValidationError

from expense_tracker_bot.api import Api
from expense_tracker_bot.copy import COPY
from expense_tracker_bot.handlers import Action, button, keyboard, menu
from expense_tracker_bot.main import minimal_update
from expense_tracker_bot.models import ProfileDto


def update(chat_type: str = "private") -> Update:
    return Update.model_validate(
        {
            "update_id": 1,
            "message": {
                "message_id": 3,
                "date": 1,
                "from": {"id": 12, "is_bot": False, "first_name": "Synthetic"},
                "chat": {"id": 12, "type": chat_type},
                "text": "coffee",
                "forward_origin": {
                    "type": "hidden_user",
                    "date": 1,
                    "sender_user_name": "Sensitive",
                },
            },
        }
    )


def test_ingress_keeps_only_supported_private_context() -> None:
    assert minimal_update(update("group")) is None
    payload = minimal_update(update())
    assert payload is not None
    assert payload["message"]["from"]["id"] == 12
    assert "forward_origin" not in payload["message"]
    Update.model_validate(payload)


def test_callback_identity_is_sender_and_all_callbacks_fit() -> None:
    value = Update.model_validate(
        {
            "update_id": 2,
            "callback_query": {
                "id": "cb",
                "from": {"id": 12, "is_bot": False, "first_name": "Synthetic"},
                "chat_instance": "test",
                "data": "l:open",
                "message": {
                    "message_id": 4,
                    "date": 1,
                    "from": {"id": 99, "is_bot": True, "first_name": "Bot"},
                    "chat": {"id": 12, "type": "private"},
                },
            },
        }
    )
    payload = minimal_update(value)
    assert payload is not None
    reconstructed = Update.model_validate(payload)
    assert reconstructed.callback_query is not None
    assert reconstructed.callback_query.from_user.id == 12
    for lang in COPY:
        t = COPY[lang]
        reply = menu(t, "https://example.com")
        assert reply.is_persistent and reply.resize_keyboard
        assert reply.keyboard[-1][0].web_app is not None
        for field in ("amount", "description", "accountId", "categoryId", "date"):
            packed = Action(
                action="field", entity="a" * 32, field="d." + field, version=100000
            ).pack()
            assert len(packed.encode()) <= 64
        assert keyboard([button(t, "save", "save", "a" * 32, "d", 3)]).inline_keyboard
    assert COPY["uk"].keys() == COPY["en"].keys()


async def test_api_attaches_actor_and_validates_generated_contract() -> None:
    async def handler(request: httpx.Request) -> httpx.Response:
        assert str(request.url) == "http://backend/internal/bot/actor/profile"
        assert request.headers["X-Telegram-User-Id"] == "12"
        return httpx.Response(
            200,
            json={
                "id": "00000000-0000-0000-0000-000000000001",
                "language": "uk",
                "theme": "auto",
                "timeZone": "Europe/Kyiv",
                "currency": "UAH",
                "defaultAccountId": None,
                "aiConsent": False,
                "version": 1,
                "today": "2026-11-01",
            },
        )

    async with httpx.AsyncClient(
        base_url="http://backend/internal/bot/", transport=httpx.MockTransport(handler)
    ) as client:
        profile = await Api(client, 12).get("/profile", ProfileDto)
        assert profile.version == 1
    with pytest.raises(ValidationError):
        ProfileDto.model_validate({"id": "not-a-uuid"})


async def test_unknown_http_failure_is_never_retried() -> None:
    calls = 0

    async def handler(request: httpx.Request) -> httpx.Response:
        nonlocal calls
        calls += 1
        raise httpx.ReadTimeout("unknown result")

    async with httpx.AsyncClient(
        base_url="http://backend/internal/bot/", transport=httpx.MockTransport(handler)
    ) as client:
        with pytest.raises(httpx.ReadTimeout):
            await Api(client, 12).call("POST", "/drafts/id/confirm", {})
    assert calls == 1
