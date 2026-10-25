"""Persistent reservations only: no keys, prompts, reports or participant data."""

import sqlite3
from datetime import datetime, timezone
from decimal import Decimal

from app.config import Settings


class AllowanceLedger:
    def __init__(self, settings: Settings):
        self.settings = settings
        settings.gemini_ledger_path.parent.mkdir(parents=True, exist_ok=True)
        with self.connect() as db:
            db.execute("CREATE TABLE IF NOT EXISTS reservations (day TEXT, usd TEXT)")

    def connect(self):
        return sqlite3.connect(self.settings.gemini_ledger_path, timeout=5)

    def check(self, cost: Decimal, *, reserve: bool = False) -> str | None:
        with self.connect() as db:
            # Serialise both the check and reservation across processes/restarts.
            db.execute("BEGIN IMMEDIATE")
            rows = db.execute("SELECT day, usd FROM reservations").fetchall()
            day = datetime.now(timezone.utc).date().isoformat()
            total = self.settings.gemini_max_total_attempts
            if total is not None and len(rows) >= total:
                return "attempt_limit"
            if sum(d == day for d, _ in rows) >= self.settings.gemini_max_attempts_per_day:
                return "attempt_limit"
            reserved = sum((Decimal(usd) for _, usd in rows), Decimal(0))
            if reserved + cost > self.settings.gemini_budget_usd:
                return "budget_limit"
            if reserve:
                db.execute("INSERT INTO reservations VALUES (?, ?)", (day, str(cost)))
        return None
