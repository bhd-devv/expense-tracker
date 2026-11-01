from typing import Any, TypeVar

import httpx
from pydantic import BaseModel

T = TypeVar("T", bound=BaseModel)


class ApiError(Exception):
    def __init__(self, code: str, status: int) -> None:
        self.code, self.status = code, status
        super().__init__(code)


class Api:
    def __init__(
        self, client: httpx.AsyncClient, user_id: int | None = None, language: str = "uk"
    ) -> None:
        self.client, self.user_id, self.language = client, user_id, language

    async def call(self, method: str, path: str, body: Any = None, **kwargs: Any) -> httpx.Response:
        headers = {}
        if self.user_id is not None:
            headers = {
                "X-Telegram-User-Id": str(self.user_id),
                "X-Telegram-Language": self.language,
            }
            path = "/actor" + path
        response = await self.client.request(
            method, path.lstrip("/"), json=body, headers=headers, **kwargs
        )
        if response.is_error:
            try:
                code = response.json().get("code", "unavailable")
            except ValueError:
                code = "unavailable"
            raise ApiError(code, response.status_code)
        return response

    async def get(self, path: str, model: type[T]) -> T:
        return model.model_validate((await self.call("GET", path)).json())

    async def mutate(
        self, method: str, path: str, model: type[T], body: BaseModel | dict[str, Any]
    ) -> T:
        data = body.model_dump(mode="json", by_alias=True) if isinstance(body, BaseModel) else body
        return model.model_validate((await self.call(method, path, data)).json())

    async def audio(self, request_id: str, data: bytes) -> httpx.Response:
        # Separate multipart call: never send Telegram download URLs or bot tokens.
        response = await self.client.post(
            "actor/drafts/audio",
            headers={"X-Telegram-User-Id": str(self.user_id)},
            data={"requestId": request_id},
            files={"audio": ("voice.ogg", data, "audio/ogg")},
        )
        if response.is_error:
            raise ApiError("audio_unavailable", response.status_code)
        return response
