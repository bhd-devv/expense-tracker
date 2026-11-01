FROM ghcr.io/astral-sh/uv:0.12.5 AS uv
FROM python:3.14.7-slim
COPY --from=uv /uv /uvx /bin/
WORKDIR /app
COPY src/bot/pyproject.toml src/bot/uv.lock ./
COPY src/bot/src ./src
RUN uv sync --frozen --no-dev --no-cache && useradd --create-home --uid 10001 bot
ENV PATH="/app/.venv/bin:$PATH" PYTHONUNBUFFERED=1
USER bot
EXPOSE 8081
CMD ["python", "-m", "expense_tracker_bot.main"]
