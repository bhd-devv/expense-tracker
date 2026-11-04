from typing import Any

import httpx


class ApiError(Exception):
    def __init__(self, code: str, status: int) -> None:
        self.code, self.status = code, status
        super().__init__(code)


class Api:
    """Private durable inbox transport; no access to user workspace operations."""

    def __init__(self, client: httpx.AsyncClient) -> None:
        self.client = client

    async def call(self, method: str, path: str, body: Any = None) -> httpx.Response:
        response = await self.client.request(method, path.lstrip("/"), json=body)
        if response.is_error:
            try:
                code = response.json().get("code", "unavailable")
            except ValueError:
                code = "unavailable"
            raise ApiError(code, response.status_code)
        return response
