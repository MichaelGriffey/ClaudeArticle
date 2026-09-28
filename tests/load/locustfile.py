"""Load for the p99 criterion in AB#1234: 2,000 valid readings per second (ADR 0006).

Each simulated gateway sends one reading per second. Readings are stamped 30 seconds in the past
by the load engine's clock, well inside the freshness window whatever the clock skew, and the value
is constant, so a repeated timestamp is an idempotent duplicate (200), never a conflict (409).
"""

import os
from datetime import datetime, timedelta, timezone

from locust import FastHttpUser, constant_throughput, task

TOKEN = os.environ["INGESTION_ACCESS_TOKEN"]


class Gateway(FastHttpUser):
    host = os.environ["INGESTION_BASE_URL"]
    wait_time = constant_throughput(1)

    @task
    def ingest(self) -> None:
        observed_at = (datetime.now(timezone.utc) - timedelta(seconds=30)).isoformat()
        self.client.post(
            "/sensors/TMP-07/readings",
            json={"value": 20.0, "observedAt": observed_at},
            headers={"Authorization": f"Bearer {TOKEN}"},
            name="POST /sensors/{sensorId}/readings",
        )
