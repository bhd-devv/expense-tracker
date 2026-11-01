import asyncio
from typing import Any

import httpx
from aiohttp.test_utils import TestClient, TestServer

import expense_tracker_bot.main as main
from expense_tracker_bot.main import Settings


async def test_webhook_acknowledges_only_after_durable_acceptance(monkeypatch: Any) -> None:
    accepted: list[dict[str, Any]] = []
    fail = False

    async def backend(request: httpx.Request) -> httpx.Response:
        nonlocal fail
        if fail:
            raise httpx.ConnectError("synthetic outage")
        import json

        accepted.append(json.loads(request.content))
        return httpx.Response(204)

    client = httpx.AsyncClient(
        base_url="http://synthetic/internal/bot/", transport=httpx.MockTransport(backend)
    )
    monkeypatch.setattr(main.httpx, "AsyncClient", lambda **kwargs: client)

    async def idle(*args: Any, **kwargs: Any) -> None:
        await asyncio.Event().wait()

    monkeypatch.setattr(main, "worker", idle)
    settings = Settings(
        token="123456:test-token-used-only-in-local-tests",
        service_key="synthetic-service-key-for-tests-123456789",
        webhook_secret="synthetic-webhook-secret-for-tests-123456",
        app_url="https://example.com",
    )
    payload = {
        "update_id": 12,
        "message": {
            "message_id": 3,
            "date": 1,
            "from": {"id": 12, "is_bot": False, "first_name": "Synthetic"},
            "chat": {"id": 12, "type": "private"},
            "text": "coffee",
        },
    }
    async with TestClient(TestServer(await main.create_app(settings))) as web:
        assert (await web.post("/telegram/webhook", json=payload)).status == 403
        headers = {"X-Telegram-Bot-Api-Secret-Token": settings.webhook_secret}
        assert (await web.post("/telegram/webhook", json=payload, headers=headers)).status == 200
        assert accepted[0]["updateId"] == 12
        fail = True
        assert (await web.post("/telegram/webhook", json=payload, headers=headers)).status == 503
