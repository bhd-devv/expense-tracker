import httpx
import pytest
from aiogram.types import Update

from expense_tracker_bot.api import Api
from expense_tracker_bot.main import minimal_update


def update(chat_type: str = "private") -> Update:
    return Update.model_validate(
        {
            "update_id": 1,
            "message": {
                "message_id": 3,
                "date": 1,
                "from": {"id": 12, "is_bot": False, "first_name": "Private name"},
                "chat": {"id": 12, "type": chat_type},
                "text": "/seed coffee 85",
                "voice": {"file_id": "private-file", "file_unique_id": "voice", "duration": 5},
            },
        }
    )


def test_ingress_keeps_only_private_launch_context() -> None:
    assert minimal_update(update("group")) is None
    payload = minimal_update(update())
    assert payload is not None
    message = payload["message"]
    assert message["from"]["id"] == 12
    assert message["from"]["first_name"] == ""
    assert "text" not in message and "voice" not in message
    Update.model_validate(payload)


def test_ingress_ignores_old_callbacks() -> None:
    callback = Update.model_validate(
        {
            "update_id": 2,
            "callback_query": {
                "id": "old",
                "from": {"id": 12, "is_bot": False, "first_name": "Synthetic"},
                "chat_instance": "test",
                "data": "l:save:expense:d:1",
            },
        }
    )
    assert minimal_update(callback) is None


async def test_inbox_transport_does_not_attach_actor_or_retry_unknown_results() -> None:
    calls = 0

    async def handler(request: httpx.Request) -> httpx.Response:
        nonlocal calls
        calls += 1
        assert request.url.path == "/internal/bot/inbox"
        assert "X-Telegram-User-Id" not in request.headers
        raise httpx.ReadTimeout("synthetic failure")

    async with httpx.AsyncClient(
        base_url="http://backend/internal/bot/", transport=httpx.MockTransport(handler)
    ) as client:
        with pytest.raises(httpx.ReadTimeout):
            await Api(client).call("POST", "/inbox", {})
    assert calls == 1
