import io
import logging
from datetime import date, timedelta
from urllib.parse import urlencode
from uuid import NAMESPACE_URL, UUID, uuid5

import httpx
from aiogram import Bot, F, Router
from aiogram.filters import Command
from aiogram.filters.callback_data import CallbackData
from aiogram.types import (
    CallbackQuery,
    InlineKeyboardButton,
    InlineKeyboardMarkup,
    KeyboardButton,
    Message,
    ReplyKeyboardMarkup,
    Update,
    WebAppInfo,
)
from aiogram.utils.keyboard import InlineKeyboardBuilder

from . import savings
from .api import Api, ApiError
from .copy import COPY, CURRENCIES, category
from .models import (
    CatalogDto,
    CatalogItemDto,
    CategoryIconLibraryDto,
    ConversationDto,
    DraftDto,
    ExpenseDto,
    ExpensePage,
    ProfileDto,
    ReportDto,
    SeedResultDto,
)
from .observability import log_failure

router = Router()
router.include_router(savings.router)
logger = logging.getLogger(__name__)


class Action(CallbackData, prefix="l"):
    action: str
    entity: str = "_"
    field: str = "_"
    version: int = 0


def button(
    t: dict[str, str],
    label: str,
    action: str,
    entity: str = "_",
    field: str = "_",
    version: int = 0,
) -> tuple[str, Action]:
    return t.get(label, label), Action(action=action, entity=entity, field=field, version=version)


def keyboard(
    rows: list[tuple[str, Action]], width: int | tuple[int, ...] = 2
) -> InlineKeyboardMarkup:
    builder = InlineKeyboardBuilder()
    for label, data in rows:
        builder.button(text=label, callback_data=data.pack())
    builder.adjust(*(width if isinstance(width, tuple) else (width,)))
    return builder.as_markup()


def menu(t: dict[str, str]) -> ReplyKeyboardMarkup:
    return ReplyKeyboardMarkup(
        keyboard=[
            [KeyboardButton(text=t["add"]), KeyboardButton(text=t["expenses"])],
            [KeyboardButton(text=t["reports"]), KeyboardButton(text=t["categories"])],
            [KeyboardButton(text=t["savings"])],
            [KeyboardButton(text=t["settings"]), KeyboardButton(text=t["app"])],
        ],
        resize_keyboard=True,
        is_persistent=True,
    )


def app_keyboard(t: dict[str, str], url: str) -> InlineKeyboardMarkup:
    return InlineKeyboardMarkup(
        inline_keyboard=[[InlineKeyboardButton(text=t["app"], web_app=WebAppInfo(url=url))]]
    )


async def state(
    api: Api, name: str, entity: str | None = None, field: str | None = None
) -> ConversationDto:
    current = await api.get("/conversation", ConversationDto)
    return await api.mutate(
        "PUT",
        "/conversation",
        ConversationDto,
        {"state": name, "entityId": entity, "field": field, "version": current.version},
    )


async def profile_context(api: Api) -> tuple[ProfileDto, dict[str, str]]:
    profile = await api.get("/profile", ProfileDto)
    return profile, COPY["en" if profile.language == "en" else "uk"]


def compact(value: UUID) -> str:
    return value.hex


def entity_path(kind: str, entity: str) -> str:
    return f"/{'drafts' if kind == 'd' else 'expenses'}/{UUID(entity)}"


async def get_record(api: Api, kind: str, entity: str) -> DraftDto | ExpenseDto:
    if kind == "d":
        return await api.get(entity_path(kind, entity), DraftDto)
    return await api.get(entity_path(kind, entity), ExpenseDto)


async def show_record(
    message: Message, api: Api, kind: str, entity: str, t: dict[str, str]
) -> None:
    catalog = await api.get("/catalog", CatalogDto)
    record = await get_record(api, kind, entity)
    if isinstance(record, DraftDto) and record.status in ("cancelled", "processing"):
        await message.answer(t["stale"])
        return
    if isinstance(record, DraftDto) and record.status == "saved":
        await show_record(message, api, "e", str(record.expenseId), t)
        return
    cat = next((category(c, t) for c in catalog.categories if c.id == record.categoryId), "—")
    lines = [
        t["draft"] if kind == "d" else t["expenses"],
        f"{record.amount or '—'} {record.currency or ''}",
        record.description or "—",
        "—" if record.date == date.min else str(record.date),
        cat,
    ]
    if isinstance(record, DraftDto) and record.errorCode:
        lines.append(
            t["clarifyDate"] if record.errorCode == "date_needs_clarification" else t["manual"]
        )
    if isinstance(record, DraftDto) and record.transcript:
        lines.append(record.transcript)
    fields: tuple[str, ...] = ("amount", "description", "date", "categoryId")
    if isinstance(record, DraftDto) and record.errorCode == "date_needs_clarification":
        fields = ("date",)
    rows = [
        button(t, field, "field", entity, f"{kind}.{field}", int(record.version))
        for field in fields
    ]
    if isinstance(record, DraftDto) and record.errorCode != "date_needs_clarification":
        rows.append(
            button(
                t,
                "save",
                "save",
                entity,
                kind,
                int(record.version),
            )
        )
    rows += [
        button(
            t,
            "cancel" if kind == "d" else "back",
            "cancel" if kind == "d" else "browse",
            entity,
            kind,
            int(record.version),
        ),
    ]
    await message.answer("\n".join(lines)[:3900], reply_markup=keyboard(rows))


async def browse(
    message: Message, api: Api, t: dict[str, str], mode: str = "expenses", page: int = 0
) -> None:
    conversation = await api.get("/conversation", ConversationDto)
    query: dict[str, str | int] = {"page": page}
    if conversation.state in ("expenses", "reports"):
        if conversation.field:
            start, end = conversation.field.split(",")
            query.update({"from": start, "to": end})
        if conversation.entityId:
            query["categoryId"] = conversation.entityId
    rows = [
        button(t, "month", "period", mode, "month"),
        button(t, "week", "period", mode, "week"),
        button(t, "all", "period", mode, "all"),
        button(t, "custom", "period", mode, "custom"),
        button(t, "categoryId", "filter", mode, "categoryId"),
    ]
    if mode == "reports":
        report = await api.get("/reports?" + urlencode(query), ReportDto)
        catalog = await api.get("/catalog", CatalogDto)
        lines = [t["reports"]]
        for total in report.totals:
            lines.append(f"\n{total.amount} {total.currency} · {total.count}")
        for row in report.categories[page * 20 : (page + 1) * 20]:
            name = next((category(c, t) for c in catalog.categories if c.id == row.categoryId), "—")
            lines.append(f"{name}: {row.amount} {row.currency}")
        if page:
            rows.append(button(t, "previous", "page", str(page - 1), "reports"))
        if len(report.categories) > (page + 1) * 20:
            rows.append(button(t, "next", "page", str(page + 1), "reports"))
        await message.answer(
            "\n".join(lines)[:3900] if report.totals else t["empty"], reply_markup=keyboard(rows)
        )
    else:
        result = await api.get("/expenses?" + urlencode(query), ExpensePage)
        records: list[tuple[str, Action]] = []
        for item in result.items:
            records.append(
                button(
                    t,
                    f"{item.date} · {item.description[:25]} · {item.amount} {item.currency}",
                    "open",
                    compact(item.id),
                    "e",
                )
            )
            records.append(
                button(t, "🗑 " + t["delete"], "delete", compact(item.id), "e", item.version)
            )
        if page:
            records.append(button(t, "previous", "page", str(page - 1)))
        if result.hasMore:
            records.append(button(t, "next", "page", str(page + 1)))
        await message.answer(
            t["expenses"] if result.items else t["empty"],
            reply_markup=keyboard(records + rows, (2,) * len(result.items) + (1,)),
        )


async def catalogs(message: Message, api: Api, t: dict[str, str], page: int = 0) -> None:
    catalog = await api.get("/catalog", CatalogDto)
    if page < 0:
        raise ApiError("conflict", 409)
    page = min(page, max(0, (len(catalog.categories) - 1) // 20))
    items = catalog.categories[page * 20 : (page + 1) * 20]
    rows: list[tuple[str, Action]] = []
    for item in items:
        rows.append(
            button(
                t,
                ("○ " if item.archived else "") + category(item, t)[:35],
                "catalog",
                compact(item.id),
                "categories",
            )
        )
        rows.append(
            button(t, "🗑 " + t["delete"], "catdelete", compact(item.id), "categories", item.version)
        )
    rows.append(button(t, "create", "new", "categories"))
    if page:
        rows.append(button(t, "previous", "catlist", str(page - 1)))
    if len(catalog.categories) > (page + 1) * 20:
        rows.append(button(t, "next", "catlist", str(page + 1)))
    lines = [t["categories"]] + [
        category(item, t) + "\n" + category_limit_text(item, t) for item in items if item.limit
    ]
    await message.answer(
        "\n\n".join(lines)[:3900], reply_markup=keyboard(rows, (2,) * len(items) + (1,))
    )


def category_limit_text(item: CatalogItemDto, t: dict[str, str]) -> str:
    limit = item.limit
    if limit is None:
        return t["noCategoryLimit"]
    filled = min(10, int(limit.usedPercent / 10))
    bar = "█" * filled + "░" * (10 - filled)
    percent = f"{limit.usedPercent:.1f}".rstrip("0").rstrip(".")
    if t is COPY["uk"]:
        percent = percent.replace(".", ",")
    lines = [
        f"{t['monthlyLimit']} · {limit.from_:%Y-%m}",
        f"{bar} {percent}%",
        f"{limit.spent} / {limit.amount} {limit.currency}",
    ]
    if limit.exceeded:
        lines.append(t["limitExceeded"])
    return "\n".join(lines)


async def choose_limit_currency(
    message: Message, api: Api, t: dict[str, str], conversation: ConversationDto
) -> None:
    item = await find_item(api, conversation.entityId or "")
    if str(item.version) != conversation.field:
        raise ApiError("conflict", 409)
    await request_limit_amount(message, api, t, item)


async def request_limit_amount(
    message: Message, api: Api, t: dict[str, str], item: CatalogItemDto
) -> None:
    profile, _ = await profile_context(api)
    current = await state(api, "category_limit", str(item.id), f"{item.version}.{profile.currency}")
    rows = []
    if item.limit:
        rows.append(
            button(
                t, "removeCategoryLimit", "limitclear", compact(item.id), "categories", item.version
            )
        )
    rows.append(button(t, "cancel", "limitcancel", version=current.version))
    await message.answer(
        category(item, t)
        + "\n"
        + category_limit_text(item, t)
        + "\n\n"
        + t["limitAmount"]
        + f" · {profile.currency}",
        reply_markup=keyboard(rows),
    )


async def category_icons(
    message: Message,
    api: Api,
    t: dict[str, str],
    conversation: ConversationDto,
    group_id: str = "_",
) -> None:
    library = await api.get("/category-icons", CategoryIconLibraryDto)
    english = t is COPY["en"]
    item = await find_item(api, conversation.entityId or "")
    if str(item.version) != conversation.field:
        raise ApiError("conflict", 409)
    group = next((item for item in library.groups if item.id == group_id), None)
    if group_id != "_" and group is None:
        raise ApiError("conflict", 409)
    if group is None:
        rows = [
            button(
                t,
                item.en if english else item.uk,
                "icongroup",
                item.id,
                version=conversation.version,
            )
            for item in library.groups
        ]
    else:
        rows = [
            button(
                t,
                ("✓ " if icon.id == item.icon else "")
                + icon.symbol
                + " "
                + (icon.en if english else icon.uk),
                "iconset",
                icon.id,
                version=conversation.version,
            )
            for icon in group.icons
        ]
        rows.append(button(t, "iconGroups", "icongroup", version=conversation.version))
    rows.append(button(t, "back", "iconback", version=conversation.version))
    current = next(
        (icon for family in library.groups for icon in family.icons if icon.id == item.icon), None
    )
    title = (current.symbol + " " if current else "") + category(item, t)
    await message.answer(
        title + "\n" + (t["chooseIcon"] if group is None else group.en if english else group.uk),
        reply_markup=keyboard(rows, 2),
    )


async def settings(message: Message, api: Api, profile: ProfileDto, t: dict[str, str]) -> None:
    values = {
        "language": "English" if profile.language == "en" else "Українська",
        "theme": t[profile.theme],
        "timeZone": profile.timeZone,
        "currency": profile.currency,
        "aiConsent": t["enable"] if profile.aiConsent else t["disable"],
        "inputMode": t["manualMode"] if profile.inputMode == "manual" else t["aiMode"],
    }
    rows = [
        button(
            t,
            f"{t[field]} · {values[field]}",
            "setting",
            field,
            version=int(profile.version),
        )
        for field in ("inputMode", "language", "theme", "timeZone", "currency", "aiConsent")
    ]
    rows.append(button(t, "seed", "seed"))
    await message.answer(t["settings"], reply_markup=keyboard(rows, 1))


async def find_item(api: Api, entity: str) -> CatalogItemDto:
    catalog = await api.get("/catalog", CatalogDto)
    items = catalog.categories
    return next((item for item in items if item.id == UUID(entity)), None) or _missing()


def _missing() -> CatalogItemDto:
    raise ApiError("not_found", 404)


async def input_value(
    message: Message,
    api: Api,
    conversation: ConversationDto,
    text: str,
    t: dict[str, str],
    app_url: str,
) -> None:
    field = conversation.field or ""
    entity = conversation.entityId or ""
    if conversation.state == "record":
        kind, name, expected = field.split(".")
        if name not in ("amount", "description", "date", "categoryId"):
            raise ApiError("conflict", 409)
        path = entity_path(kind, entity)
        record = await get_record(api, kind, entity)
        if int(record.version) != int(expected):
            raise ApiError("conflict", 409)
        body = record.model_dump(
            mode="json",
            include={"amount", "description", "date", "currency", "categoryId", "version"},
        )
        body[name] = text.replace(",", ".") if name == "amount" else text
        await api.mutate("PUT", path, DraftDto if kind == "d" else ExpenseDto, body)
        await state(api, "idle")
        await show_record(message, api, kind, entity, t)
    elif conversation.state == "new":
        if entity != "categories":
            raise ApiError("conflict", 409)
        created = await api.mutate(
            "POST",
            f"/{entity}",
            CatalogItemDto,
            {
                "name": text,
                "archived": False,
                "version": 0,
            },
        )
        current = await state(api, "category_icon", str(created.id), str(created.version))
        await category_icons(message, api, t, current)
    elif conversation.state == "rename":
        kind, expected = field.split(".")
        if kind != "categories":
            raise ApiError("conflict", 409)
        item = await find_item(api, entity)
        if int(item.version) != int(expected):
            raise ApiError("conflict", 409)
        await api.mutate(
            "PUT",
            f"/{kind}/{item.id}",
            CatalogItemDto,
            {**item.model_dump(mode="json"), "name": text},
        )
        await state(api, "idle")
        await catalogs(message, api, t)
    elif conversation.state == "category_limit":
        expected, currency = field.split(".")
        item = await find_item(api, entity)
        if str(item.version) != expected:
            raise ApiError("conflict", 409)
        await api.mutate(
            "PUT",
            f"/categories/{item.id}/limit",
            CatalogItemDto,
            {
                "amount": text.replace(",", "."),
                "currency": currency,
                "version": item.version,
            },
        )
        await state(api, "idle")
        await catalogs(message, api, t)
    elif conversation.state == "preference":
        profile, _ = await profile_context(api)
        name, expected = field.split(".")
        if name not in ("language", "theme", "timeZone", "currency", "aiConsent", "inputMode"):
            raise ApiError("conflict", 409)
        if int(profile.version) != int(expected):
            raise ApiError("conflict", 409)
        body = profile.model_dump(mode="json")
        body[name] = text
        updated = await api.mutate("PUT", "/profile", ProfileDto, body)
        await state(api, "idle")
        await message.answer(
            COPY[updated.language]["welcomeManual" if updated.inputMode == "manual" else "welcome"],
            reply_markup=menu(COPY[updated.language]),
        )
        await settings(message, api, updated, COPY[updated.language])
    elif conversation.state == "period":
        start, end = text.split()
        date.fromisoformat(start)
        date.fromisoformat(end)
        mode, filters = entity.split(":", 1)
        await state(api, mode, filters or None, f"{start},{end}")
        await browse(message, api, t, mode)


async def seed_demo(message: Message, api: Api, t: dict[str, str]) -> None:
    try:
        result = await api.mutate("POST", "/seed", SeedResultDto, {})
    except ApiError as error:
        log_failure(logger, "Demo seeding rejected", error, status=error.status)
        key = "unavailable"
        if error.code == "catalog_limit":
            key = "seedLimit"
        elif error.status >= 500:
            key = "seedUnknown"
        await message.answer(t[key])
        return
    except (httpx.HTTPError, ValueError) as error:
        log_failure(logger, "Demo seeding failed", error)
        await message.answer(t["seedUnknown"])
        return
    summary = (
        t["seedAdded"].format(count=result.createdCount) if result.createdCount else t["seedExists"]
    )
    await message.answer(summary + "\n" + t["seedPeriod"], reply_markup=menu(t))


@router.message(F.chat.type == "private", Command("seed"))
async def handle_seed(message: Message, api_client: httpx.AsyncClient) -> None:
    if not message.from_user or message.from_user.is_bot:
        return
    api = Api(api_client, message.from_user.id, message.from_user.language_code or "uk")
    _, t = await profile_context(api)
    await seed_demo(message, api, t)


@router.message(F.chat.type == "private")
async def handle_message(
    message: Message, api_client: httpx.AsyncClient, app_url: str, event_update: Update, bot: Bot
) -> None:
    if not message.from_user or message.from_user.is_bot:
        return
    api = Api(api_client, message.from_user.id, message.from_user.language_code or "uk")
    profile, t = await profile_context(api)
    request_id = str(uuid5(NAMESPACE_URL, f"telegram:{event_update.update_id}"))
    try:
        text = message.text or ""
        # Refresh reply keyboards left on devices by releases with the removed catalogue.
        if text in ("💳 Рахунки", "💳 Accounts"):
            await state(api, "idle")
            await message.answer(t["stale"], reply_markup=menu(t))
            return
        selected = next(
            (
                key
                for copy in COPY.values()
                for key in (
                    "add",
                    "expenses",
                    "reports",
                    "categories",
                    "savings",
                    "settings",
                    "app",
                )
                if copy[key] == text
            ),
            None,
        )
        if text.startswith("/") or selected:
            logger.info("Menu action", extra={"details": {"action": selected or "start"}})
            # Menu navigation preserves draft records. Old buttons still have owner/version checks.
            await state(api, selected if selected in ("expenses", "reports") else "idle")
            if selected in ("expenses", "reports"):
                await browse(message, api, t, selected)
            elif selected == "savings":
                await savings.show_accounts(message, api, t)
            elif selected == "categories":
                await catalogs(message, api, t)
            elif selected == "settings":
                await settings(message, api, profile, t)
            elif selected == "app":
                await message.answer(t["openAppPrompt"], reply_markup=app_keyboard(t, app_url))
            elif selected == "add" and profile.inputMode == "manual":
                draft = await api.mutate(
                    "POST",
                    "/drafts",
                    DraftDto,
                    {"requestId": request_id, "text": "", "mode": "manual"},
                )
                await message.answer(t["manualPrompt"], reply_markup=menu(t))
                await show_record(message, api, "d", str(draft.id), t)
            else:
                await message.answer(
                    t["prompt"]
                    if selected == "add"
                    else t["welcomeManual" if profile.inputMode == "manual" else "welcome"],
                    reply_markup=menu(t),
                )
                if text.split(maxsplit=1)[0].split("@", 1)[0] == "/start":
                    await message.answer(t["openAppPrompt"], reply_markup=app_keyboard(t, app_url))
            return
        conversation = await api.get("/conversation", ConversationDto)
        if conversation.state.startswith("savings_"):
            await savings.input_value(message, api, conversation, text, t)
            return
        if conversation.state == "category_icon":
            await category_icons(message, api, t, conversation)
            return
        if conversation.state == "category_limit_currency":
            await choose_limit_currency(message, api, t, conversation)
            return
        if conversation.state in (
            "record",
            "new",
            "rename",
            "preference",
            "period",
            "category_limit",
        ):
            logger.info("Dialog input received", extra={"details": {"state": conversation.state}})
            if text:
                await input_value(message, api, conversation, text, t, app_url)
            else:
                await message.answer(t["input"])
            return
        if profile.inputMode == "manual":
            if message.voice or not text:
                await message.answer(
                    t["manualVoice"] if message.voice else t["manualPrompt"], reply_markup=menu(t)
                )
                return
            draft = await api.mutate(
                "POST",
                "/drafts",
                DraftDto,
                {"requestId": request_id, "text": text, "mode": "manual"},
            )
            await state(api, "idle")
            await message.answer(t["manualPrompt"])
            await show_record(message, api, "d", str(draft.id), t)
            return
        if message.voice:
            if not profile.aiConsent:
                await message.answer(t["consentNeeded"])
                return
            if message.voice.duration > 120 or (message.voice.file_size or 0) > 10 * 1024 * 1024:
                await message.answer(t["limit"])
                return
            await message.answer(t["processing"])
            buffer = io.BytesIO()
            await bot.download(message.voice, destination=buffer, timeout=30)
            logger.info("Voice downloaded", extra={"details": {"bytes": buffer.getbuffer().nbytes}})
            draft = DraftDto.model_validate((await api.audio(request_id, buffer.getvalue())).json())
        elif text:
            await message.answer(t["processing"])
            draft = await api.mutate(
                "POST",
                "/drafts",
                DraftDto,
                {"requestId": request_id, "text": text},
            )
        else:
            await message.answer(t["prompt"], reply_markup=menu(t))
            return
        await state(api, "idle")
        await show_record(message, api, "d", str(draft.id), t)
    except ApiError as error:
        log_failure(logger, "Message action rejected", error, status=error.status)
        await message.answer(
            t["stale"]
            if error.status in (404, 409)
            else t["error"]
            if error.status < 500
            else t["unavailable"]
        )
    except (ValueError, TypeError) as error:
        log_failure(logger, "Message input rejected", error)
        await message.answer(t["error"])


@router.callback_query(Action.filter())
async def handle_callback(
    callback: CallbackQuery, callback_data: Action, api_client: httpx.AsyncClient, app_url: str
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
    profile, t = await profile_context(api)
    data = callback_data
    action = (
        data.action
        if data.action
        in {
            "seed",
            "open",
            "save",
            "delete",
            "remove",
            "cancel",
            "field",
            "pick",
            "reset",
            "browse",
            "page",
            "period",
            "filter",
            "select",
            "new",
            "catalog",
            "rename",
            "catdelete",
            "catremove",
            "catlist",
            "catlimit",
            "limitcurrency",
            "limitclear",
            "limitcancel",
            "setting",
            "preference",
            "icon",
            "icongroup",
            "iconset",
            "iconback",
        }
        else "unknown"
    )
    logger.info("Callback action", extra={"details": {"action": action}})
    try:
        if data.action == "seed":
            await seed_demo(message, api, t)
        elif data.action in ("open", "save", "delete", "remove", "cancel", "field"):
            kind = data.field.split(".")[0]
            record = await get_record(api, kind, data.entity)
            if data.action not in ("open",) and int(record.version) != data.version:
                raise ApiError("conflict", 409)
            if data.action == "open":
                await show_record(message, api, kind, data.entity, t)
            elif data.action == "save":
                await api.mutate(
                    "POST",
                    entity_path(kind, data.entity) + "/confirm",
                    ExpenseDto,
                    record.model_dump(
                        mode="json",
                        include={
                            "amount",
                            "description",
                            "date",
                            "currency",
                            "categoryId",
                            "version",
                        },
                    ),
                )
                await state(api, "idle")
                await message.edit_reply_markup(reply_markup=None)
            elif data.action == "cancel":
                await api.mutate(
                    "POST",
                    entity_path(kind, data.entity) + "/cancel",
                    DraftDto,
                    {"version": record.version},
                )
                await state(api, "idle")
                await message.edit_reply_markup(reply_markup=None)
                await message.answer(
                    t["welcomeManual" if profile.inputMode == "manual" else "welcome"],
                    reply_markup=menu(t),
                )
            elif data.action == "delete":
                await message.answer(
                    f"{t['delete']}? {record.description} · {record.amount} {record.currency}",
                    reply_markup=keyboard(
                        [
                            button(t, "confirm", "remove", data.entity, "e", data.version),
                            button(t, "cancel", "browse"),
                        ]
                    ),
                )
            elif data.action == "remove":
                await api.call(
                    "DELETE", entity_path(kind, data.entity) + f"?version={data.version}"
                )
                await message.edit_reply_markup(reply_markup=None)
                await message.answer(t["deleted"])
                await browse(message, api, t)
            elif data.action == "field":
                name = data.field.split(".")[1]
                if name not in ("amount", "description", "date", "categoryId"):
                    raise ApiError("conflict", 409)
                current = await state(api, "record", data.entity, f"{kind}.{name}.{data.version}")
                if name == "categoryId":
                    catalog = await api.get("/catalog", CatalogDto)
                    items = catalog.categories
                    # Selection ID is in callback; version stays in the durable conversation.
                    await message.answer(
                        t["choose"],
                        reply_markup=keyboard(
                            [
                                button(
                                    t,
                                    category(item, t)[:40],
                                    "select",
                                    compact(item.id),
                                    "record",
                                    int(current.version),
                                )
                                for item in items
                                if not item.archived
                            ],
                            1,
                        ),
                    )
                else:
                    await message.answer(
                        f"{t[name]}: {t['input']}",
                        reply_markup=keyboard([button(t, "cancel", "reset")]),
                    )
        elif data.action == "reset":
            await state(api, "idle")
            await message.answer(
                t["welcomeManual" if profile.inputMode == "manual" else "welcome"],
                reply_markup=menu(t),
            )
        elif data.action in ("browse", "page"):
            await browse(
                message,
                api,
                t,
                "reports" if data.field == "reports" else "expenses",
                page=int(data.entity) if data.action == "page" else 0,
            )
        elif data.action == "period":
            previous = await api.get("/conversation", ConversationDto)
            filters = previous.entityId if previous.state == data.entity else None
            if data.field == "custom":
                await state(api, "period", f"{data.entity}:{filters or ''}")
                await message.answer(
                    t["periodPrompt"], reply_markup=keyboard([button(t, "cancel", "reset")])
                )
            else:
                start = (
                    profile.today.replace(day=1)
                    if data.field == "month"
                    else profile.today - timedelta(days=6)
                )
                await state(
                    api,
                    data.entity,
                    entity=filters if data.field != "all" else None,
                    field=None if data.field == "all" else f"{start},{profile.today}",
                )
                await browse(message, api, t, data.entity)
        elif data.action == "filter":
            if data.field != "categoryId":
                raise ApiError("conflict", 409)
            catalog = await api.get("/catalog", CatalogDto)
            items = catalog.categories
            previous = await api.get("/conversation", ConversationDto)
            filters = previous.entityId if previous.state == data.entity else None
            period = previous.field if previous.state == data.entity else None
            code = "r" if data.entity == "reports" else "e"
            current = await state(api, "filter", filters, f"{code}.{period or ''}")
            await message.answer(
                t["choose"],
                reply_markup=keyboard(
                    [
                        button(
                            t,
                            category(item, t)[:40],
                            "select",
                            compact(item.id),
                            "filter",
                            int(current.version),
                        )
                        for item in items
                    ]
                    + [button(t, "all", "period", data.entity, "all")],
                    1,
                ),
            )
        elif data.action == "select":
            conversation = await api.get("/conversation", ConversationDto)
            if int(conversation.version) != data.version or conversation.state != data.field:
                raise ApiError("conflict", 409)
            if data.field == "record":
                if (conversation.field or "").split(".")[1] != "categoryId":
                    raise ApiError("conflict", 409)
                await input_value(
                    message,
                    api,
                    conversation,
                    str(UUID(data.entity)),
                    t,
                    app_url,
                )
            elif data.field == "filter":
                code, period = (conversation.field or "").split(".", 1)
                mode = "reports" if code[0] == "r" else "expenses"
                await state(api, mode, str(UUID(data.entity)), period or None)
                await browse(message, api, t, mode)
        elif data.action == "new":
            if data.entity != "categories":
                raise ApiError("conflict", 409)
            await state(api, "new", "categories")
            await message.answer(
                t["name"] + ": " + t["input"],
                reply_markup=keyboard([button(t, "cancel", "reset")]),
            )
        elif data.action in ("icongroup", "iconset", "iconback"):
            conversation = await api.get("/conversation", ConversationDto)
            if conversation.state != "category_icon" or conversation.version != data.version:
                raise ApiError("conflict", 409)
            if data.action == "icongroup":
                await category_icons(message, api, t, conversation, data.entity)
            elif data.action == "iconback":
                await state(api, "idle")
                await catalogs(message, api, t)
            else:
                item = await find_item(api, conversation.entityId or "")
                if str(item.version) != conversation.field:
                    raise ApiError("conflict", 409)
                await api.mutate(
                    "PUT",
                    f"/categories/{item.id}",
                    CatalogItemDto,
                    {**item.model_dump(mode="json"), "icon": data.entity},
                )
                await state(api, "idle")
                await message.edit_reply_markup(reply_markup=None)
                await catalogs(message, api, t)
        elif data.action == "catlist":
            await catalogs(message, api, t, int(data.entity) if data.entity != "_" else 0)
        elif data.action in ("catdelete", "catremove"):
            if data.field != "categories":
                raise ApiError("conflict", 409)
            item = await find_item(api, data.entity)
            if item.version != data.version:
                raise ApiError("conflict", 409)
            if data.action == "catdelete":
                await message.answer(
                    t["confirmDeleteCategory"] + "\n" + category(item, t),
                    reply_markup=keyboard(
                        [
                            button(
                                t, "confirm", "catremove", data.entity, "categories", item.version
                            ),
                            button(t, "cancel", "catlist"),
                        ]
                    ),
                )
            else:
                await api.call("DELETE", f"/categories/{item.id}?version={data.version}")
                await state(api, "idle")
                await message.edit_reply_markup(reply_markup=None)
                await message.answer(t["categoryDeleted"])
                await catalogs(message, api, t)
        elif data.action in ("limitcurrency", "limitcancel"):
            conversation = await api.get("/conversation", ConversationDto)
            if conversation.version != data.version or conversation.state not in (
                "category_limit_currency",
                "category_limit",
            ):
                raise ApiError("conflict", 409)
            if data.action == "limitcancel":
                await state(api, "idle")
                await catalogs(message, api, t)
            else:
                if conversation.state != "category_limit_currency" or data.entity not in CURRENCIES:
                    raise ApiError("conflict", 409)
                item = await find_item(api, conversation.entityId or "")
                if str(item.version) != conversation.field:
                    raise ApiError("conflict", 409)
                await request_limit_amount(message, api, t, item)
        elif data.action in ("catalog", "rename", "icon", "catlimit", "limitclear"):
            if data.field != "categories":
                raise ApiError("conflict", 409)
            item = await find_item(api, data.entity)
            if data.action != "catalog" and int(item.version) != data.version:
                raise ApiError("conflict", 409)
            if data.action == "catalog":
                rows = [
                    button(t, "chooseIcon", "icon", data.entity, data.field, int(item.version)),
                    button(t, "edit", "rename", data.entity, data.field, int(item.version)),
                    button(t, "monthlyLimit", "catlimit", data.entity, data.field, item.version),
                    button(t, "delete", "catdelete", data.entity, "categories", item.version),
                ]
                await message.answer(
                    category(item, t) + "\n" + category_limit_text(item, t),
                    reply_markup=keyboard(rows),
                )
            elif data.action == "catlimit":
                await request_limit_amount(message, api, t, item)
            elif data.action == "limitclear":
                await api.mutate(
                    "PUT",
                    f"/categories/{item.id}/limit",
                    CatalogItemDto,
                    {
                        "amount": None,
                        "currency": None,
                        "version": item.version,
                    },
                )
                await state(api, "idle")
                await message.edit_reply_markup(reply_markup=None)
                await catalogs(message, api, t)
            elif data.action == "icon":
                current = await state(api, "category_icon", str(item.id), str(item.version))
                await category_icons(message, api, t, current)
            elif data.action == "rename":
                await state(api, "rename", data.entity, f"{data.field}.{data.version}")
                await message.answer(
                    t["name"] + ": " + t["input"],
                    reply_markup=keyboard([button(t, "cancel", "reset")]),
                )
        elif data.action == "setting":
            if int(profile.version) != data.version:
                raise ApiError("conflict", 409)
            field = data.entity
            if field not in ("language", "theme", "timeZone", "currency", "aiConsent", "inputMode"):
                raise ApiError("conflict", 409)
            current = await state(api, "preference", field=f"{field}.{data.version}")
            options = (
                ("uk", "en")
                if field == "language"
                else ("auto", "light", "dark")
                if field == "theme"
                else CURRENCIES
                if field == "currency"
                else ("enable", "disable")
                if field == "aiConsent"
                else ("manual", "ai")
                if field == "inputMode"
                else ()
            )
            rows = [
                button(
                    t,
                    "manualMode" if option == "manual" else "aiMode" if option == "ai" else option,
                    "preference",
                    option,
                    field,
                    int(current.version),
                )
                for option in options
            ]
            rows.append(button(t, "cancel", "reset"))
            await message.answer(
                t["consent"] if field == "aiConsent" else t.get(field, t["input"]),
                reply_markup=keyboard(rows),
            )
        elif data.action == "preference":
            conversation = await api.get("/conversation", ConversationDto)
            if conversation.state != "preference" or int(conversation.version) != data.version:
                raise ApiError("conflict", 409)
            name, expected = (conversation.field or "").split(".")
            if int(profile.version) != int(expected) or name != data.field:
                raise ApiError("conflict", 409)
            if name not in ("language", "theme", "timeZone", "currency", "aiConsent", "inputMode"):
                raise ApiError("conflict", 409)
            body = profile.model_dump(mode="json")
            body[name] = data.entity == "enable" if name == "aiConsent" else data.entity
            profile = await api.mutate("PUT", "/profile", ProfileDto, body)
            await state(api, "idle")
            t = COPY[profile.language]
            await message.answer(
                t["welcomeManual" if profile.inputMode == "manual" else "welcome"],
                reply_markup=menu(t),
            )
            await settings(message, api, profile, t)
        else:
            raise ApiError("conflict", 409)
    except ApiError as error:
        log_failure(logger, "Callback action rejected", error, status=error.status)
        await message.answer(
            t["categoryInUse"]
            if error.code == "category_in_use"
            else t["stale"]
            if error.status in (404, 409)
            else t["error"]
            if error.status < 500
            else t["unavailable"]
        )
    except (ValueError, TypeError) as error:
        log_failure(logger, "Callback input rejected", error)
        await message.answer(t["error"])
