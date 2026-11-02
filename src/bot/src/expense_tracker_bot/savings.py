"""Savings UX. The .NET API owns money, access and idempotency rules."""

from datetime import date
from typing import Literal
from uuid import NAMESPACE_URL, UUID, uuid5

import httpx
from aiogram import Router
from aiogram.filters.callback_data import CallbackData
from aiogram.types import CallbackQuery, InlineKeyboardMarkup, Message
from aiogram.utils.keyboard import InlineKeyboardBuilder
from pydantic import BaseModel

from .api import Api, ApiError
from .copy import COPY, CURRENCIES
from .models import (
    ConversationDto,
    CreateSavingsRequest,
    ProfileDto,
    SavingsAccountDto,
    SavingsAccountsDto,
    SavingsEntryDto,
    SavingsEntryPage,
    SavingsEntryRequest,
)

router = Router()


class SavingsAction(CallbackData, prefix="sv"):
    action: str
    entity: str = "_"
    value: str = "_"
    version: int = 0


class SavingsFlow(BaseModel):
    requestId: UUID
    accountId: UUID | None = None
    name: str = ""
    currency: str = ""
    kind: Literal["deposit", "withdrawal"] = "deposit"
    amount: str = ""
    date: date
    description: str = ""


def markup(rows: list[tuple[str, SavingsAction]]) -> InlineKeyboardMarkup:
    builder = InlineKeyboardBuilder()
    for text, action in rows:
        builder.button(text=text, callback_data=action.pack())
    builder.adjust(1)
    return builder.as_markup()


async def update_state(
    api: Api, previous: ConversationDto, step: str, flow: SavingsFlow | None = None
) -> ConversationDto:
    return await api.mutate(
        "PUT",
        "/conversation",
        ConversationDto,
        {
            "state": step,
            "entityId": None,
            "field": flow.model_dump_json() if flow else None,
            "version": previous.version,
        },
    )


async def show_accounts(message: Message, api: Api, t: dict[str, str], page: int = 0) -> None:
    result = await api.get("/savings", SavingsAccountsDto)
    if page < 0:
        raise ApiError("conflict", 409)
    page = min(page, max(0, (len(result.items) - 1) // 20))
    rows = [
        (
            f"{item.name} · {item.balance} {item.currency}",
            SavingsAction(action="open", entity=item.id.hex),
        )
        for item in result.items[page * 20 : (page + 1) * 20]
    ]
    if page:
        rows.append((t["previous"], SavingsAction(action="list", value=str(page - 1))))
    if len(result.items) > (page + 1) * 20:
        rows.append((t["next"], SavingsAction(action="list", value=str(page + 1))))
    rows.append((t["newSavings"], SavingsAction(action="new")))
    await message.answer(
        t["savings"] if result.items else t["emptySavings"], reply_markup=markup(rows)
    )


async def show_account(
    message: Message, api: Api, t: dict[str, str], account_id: UUID, page: int = 0
) -> None:
    result = await api.get(f"/savings/{account_id}/entries?page={page}", SavingsEntryPage)
    account = result.account
    rows = [
        (t["deposit"], SavingsAction(action="entry", entity=account_id.hex, value="deposit")),
        (t["withdrawal"], SavingsAction(action="entry", entity=account_id.hex, value="withdrawal")),
    ]
    if page:
        rows.append(
            (
                t["previous"],
                SavingsAction(action="history", entity=account_id.hex, value=str(page - 1)),
            )
        )
    if result.hasMore:
        rows.append(
            (t["next"], SavingsAction(action="history", entity=account_id.hex, value=str(page + 1)))
        )
    rows.append((t["savingsBack"], SavingsAction(action="list")))
    lines = [
        account.name,
        f"{t['savingsBalance']}: {account.balance} {account.currency}",
        f"{t['savingsAdded']}: {account.added} {account.currency}",
        f"{t['savingsSpent']}: {account.spent} {account.currency}",
        t["savingsHistory"],
    ]
    chunks = []
    text = "\n".join(lines)
    for item in result.items:
        label = t["depositRecord"] if item.kind == "deposit" else t["withdrawalRecord"]
        sign = "+" if item.kind == "deposit" else "−"
        entry = f"{item.date} · {label}\n{sign}{item.amount} {account.currency}"
        if item.description:
            entry += "\n" + item.description
        # Measure UTF-16 units conservatively for Telegram, including emoji notes.
        if len((text + "\n\n" + entry).encode("utf-16-le")) // 2 > 3500:
            chunks.append(text)
            text = entry
        else:
            text += "\n\n" + entry
    if not result.items:
        text += "\n" + t["emptySavingsHistory"]
    chunks.append(text)
    for index, chunk in enumerate(chunks):
        await message.answer(chunk, reply_markup=markup(rows) if index == len(chunks) - 1 else None)


async def prompt(
    message: Message, current: ConversationDto, flow: SavingsFlow, t: dict[str, str]
) -> None:
    rows = []
    step = current.state
    if step == "savings_date":
        rows.append(
            (
                f"{t['savingsToday']} · {flow.date}",
                SavingsAction(action="today", version=current.version),
            )
        )
    if step == "savings_note":
        rows.append((t["savingsSkip"], SavingsAction(action="skip", version=current.version)))
    if step in ("savings_create", "savings_confirm"):
        rows.append((t["save"], SavingsAction(action="save", version=current.version)))
        if step == "savings_confirm":
            rows += [
                (t[key], SavingsAction(action="edit", value=key, version=current.version))
                for key in ("amount", "date", "description")
            ]
        text = (
            f"{flow.name}\n{flow.currency}"
            if step == "savings_create"
            else f"{t['depositRecord'] if flow.kind == 'deposit' else t['withdrawalRecord']} · {flow.name}\n"
            f"{flow.amount} {flow.currency}\n{flow.date}\n{flow.description}"
        )
    else:
        text = t[
            {
                "savings_name": "savingsNamePrompt",
                "savings_currency": "currency",
                "savings_amount": "savingsAmountPrompt",
                "savings_date": "date",
                "savings_note": "savingsNotePrompt",
            }[step]
        ]
    rows.append((t["cancel"], SavingsAction(action="cancel", version=current.version)))
    await message.answer(text, reply_markup=markup(rows))


async def input_value(
    message: Message, api: Api, current: ConversationDto, text: str, t: dict[str, str]
) -> None:
    flow = SavingsFlow.model_validate_json(current.field or "{}")
    if current.state == "savings_currency":
        profile = await api.get("/profile", ProfileDto)
        flow.currency = profile.currency
        current = await update_state(api, current, "savings_create", flow)
        await prompt(message, current, flow, t)
        return
    if not text:
        await prompt(message, current, flow, t)
        return
    if current.state == "savings_name":
        flow.name = text
        profile = await api.get("/profile", ProfileDto)
        flow.currency = profile.currency
        step = "savings_create"
    elif current.state == "savings_amount":
        flow.amount = text.replace(",", ".")
        step = "savings_date"
    elif current.state == "savings_date":
        flow.date = date.fromisoformat(text)
        step = "savings_note"
    elif current.state == "savings_note":
        flow.description = text
        step = "savings_confirm"
    else:
        await prompt(message, current, flow, t)
        return
    # These are UX bounds; financial validation belongs to the backend.
    if len(flow.name) > 60 or len(flow.description) > 500 or len(flow.amount) > 22:
        await message.answer(t["error"])
        return
    current = await update_state(api, current, step, flow)
    await prompt(message, current, flow, t)


@router.callback_query(SavingsAction.filter())
async def handle_callback(
    callback: CallbackQuery, callback_data: SavingsAction, api_client: httpx.AsyncClient
) -> None:
    if (
        not isinstance(callback.message, Message)
        or callback.message.chat.type != "private"
        or callback.from_user.is_bot
    ):
        await callback.answer()
        return
    await callback.answer()
    message = callback.message
    api = Api(api_client, callback.from_user.id, callback.from_user.language_code or "uk")
    profile = await api.get("/profile", ProfileDto)
    t = COPY["en" if profile.language == "en" else "uk"]
    data = callback_data
    try:
        if data.action == "list":
            await show_accounts(message, api, t, 0 if data.value == "_" else int(data.value))
            return
        if data.action in ("open", "history"):
            await show_account(
                message, api, t, UUID(data.entity), 0 if data.value == "_" else int(data.value)
            )
            return
        current = await api.get("/conversation", ConversationDto)
        if data.action in ("new", "entry"):
            flow = SavingsFlow(
                requestId=uuid5(NAMESPACE_URL, f"savings:{callback.id}"), date=profile.today
            )
            if data.action == "new":
                step = "savings_name"
            else:
                result = await api.get(
                    f"/savings/{UUID(data.entity)}/entries?page=0", SavingsEntryPage
                )
                if data.value not in ("deposit", "withdrawal"):
                    raise ApiError("conflict", 409)
                flow.accountId = result.account.id
                flow.name, flow.currency = result.account.name, result.account.currency
                flow.kind = "deposit" if data.value == "deposit" else "withdrawal"
                step = "savings_amount"
        else:
            if not current.state.startswith("savings_") or current.version != data.version:
                raise ApiError("conflict", 409)
            flow = SavingsFlow.model_validate_json(current.field or "{}")
            if data.action == "cancel":
                await update_state(api, current, "idle")
                await show_accounts(message, api, t)
                return
            if (
                data.action == "currency"
                and current.state == "savings_currency"
                and data.value in CURRENCIES
            ):
                flow.currency = profile.currency
                step = "savings_create"
            elif data.action == "today" and current.state == "savings_date":
                step = "savings_note"
            elif data.action == "skip" and current.state == "savings_note":
                flow.description = ""
                step = "savings_confirm"
            elif (
                data.action == "edit"
                and current.state == "savings_confirm"
                and data.value in ("amount", "date", "description")
            ):
                step = {
                    "amount": "savings_amount",
                    "date": "savings_date",
                    "description": "savings_note",
                }[data.value]
            elif data.action == "save" and current.state in ("savings_create", "savings_confirm"):
                if current.state == "savings_create":
                    account = await api.mutate(
                        "POST",
                        "/savings",
                        SavingsAccountDto,
                        CreateSavingsRequest(
                            requestId=flow.requestId, name=flow.name, currency=profile.currency
                        ),
                    )
                    account_id = account.id
                else:
                    if flow.accountId is None:
                        raise ApiError("conflict", 409)
                    await api.mutate(
                        "POST",
                        f"/savings/{flow.accountId}/entries",
                        SavingsEntryDto,
                        SavingsEntryRequest(
                            requestId=flow.requestId,
                            kind=flow.kind,
                            amount=flow.amount,
                            date=flow.date,
                            description=flow.description,
                        ),
                    )
                    account_id = flow.accountId
                await update_state(api, current, "idle")
                await message.answer(t["savingsSaved"])
                await message.edit_reply_markup(reply_markup=None)
                await show_account(message, api, t, account_id)
                return
            else:
                raise ApiError("conflict", 409)
        current = await update_state(api, current, step, flow)
        await prompt(message, current, flow, t)
    except ApiError as error:
        await message.answer(
            t["insufficientSavings"]
            if error.code == "insufficient_savings"
            else t["savingsLimit"]
            if error.code == "savings_limit"
            else t["stale"]
            if error.status in (404, 409)
            else t["savingsUnknown"]
            if error.status >= 500
            else t["error"]
        )
    except httpx.HTTPError:
        # The durable flow retains the exact key. Only an explicit button retries it.
        await message.answer(t["savingsUnknown"])
    except (ValueError, TypeError):
        await message.answer(t["error"])
