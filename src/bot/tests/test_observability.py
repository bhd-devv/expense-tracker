import json
import logging

from expense_tracker_bot.observability import JsonFormatter, context


def test_exception_logs_keep_stack_and_context_without_private_values() -> None:
    secret = "synthetic-private-note-and-token"
    token = context.set({"updateId": 42, "deliveryId": "synthetic-delivery"})
    try:
        try:
            raise ValueError(secret)
        except ValueError as error:
            record = logging.LogRecord(
                "test",
                logging.ERROR,
                __file__,
                1,
                "Failed with %s",
                (secret,),
                (ValueError, error, error.__traceback__),
            )
        rendered = JsonFormatter().format(record)
    finally:
        context.reset(token)
    assert secret not in rendered
    data = json.loads(rendered)
    assert data["updateId"] == 42
    assert data["failure"]["type"] == "ValueError"
    assert data["failure"]["stack"][-1]["function"] == (
        "test_exception_logs_keep_stack_and_context_without_private_values"
    )
