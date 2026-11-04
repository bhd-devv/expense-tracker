from aiogram import F, Router
from aiogram.types import (
    InlineKeyboardButton,
    InlineKeyboardMarkup,
    Message,
    ReplyKeyboardRemove,
    WebAppInfo,
)

from .copy import COPY

router = Router()


def app_keyboard(t: dict[str, str], url: str) -> InlineKeyboardMarkup:
    origin = url.rstrip("/")
    return InlineKeyboardMarkup(
        inline_keyboard=[
            [InlineKeyboardButton(text=t["expenses"], web_app=WebAppInfo(url=origin))],
            [InlineKeyboardButton(text=t["tasks"], web_app=WebAppInfo(url=origin + "/tasks"))],
        ]
    )


@router.message(F.chat.type == "private")
async def handle_message(message: Message, app_url: str) -> None:
    if not message.from_user or message.from_user.is_bot:
        return
    language = message.from_user.language_code or "uk"
    t = COPY["en" if language.lower().startswith("en") else "uk"]
    # Remove reply keyboards left by older bot versions. All messages, including
    # Telegram's initial Start message, only return the two Web App launchers.
    await message.answer(t["welcome"], reply_markup=ReplyKeyboardRemove())
    await message.answer(t["open"], reply_markup=app_keyboard(t, app_url))
