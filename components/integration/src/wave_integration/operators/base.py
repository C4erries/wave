from __future__ import annotations

import logging
import time
import traceback
from typing import Optional

from wavemq import WaveMQClient
from wavemq.errors import TopicNotFoundError, WaveMQConnectionError

from wave_integration.codec import decode_block

logger = logging.getLogger(__name__)


class Operator:
    def __init__(
        self,
        *,
        input_topic: str,
        output_topic: str,
        group_id: str,
        broker_addr: str = "127.0.0.1:7912",
        poll_interval: float = 0.1,
        max_messages_per_poll: int = 16,
    ) -> None:
        self.input_topic = input_topic
        self.output_topic = output_topic
        self.group_id = group_id
        self.broker_addr = broker_addr
        self.poll_interval = poll_interval
        self.max_messages_per_poll = max_messages_per_poll

    def process(self, record_in: dict) -> Optional[bytes]:
        """Override in subclass. Returns bytes to publish or None to skip."""
        raise NotImplementedError

    def run(self) -> None:
        n_processed = 0

        def _on_record(record) -> None:
            nonlocal n_processed
            try:
                decoded = decode_block(record.value)
                result = self.process(decoded)
                if result is not None:
                    client.produce_one_to_partition(
                        self.output_topic, 0, result,
                        content_type="application/octet-stream",
                    )
            except Exception:
                # at-least-once: одна битая запись не останавливает consumer group
                logger.error("error processing record:\n%s", traceback.format_exc())
                return
            n_processed += 1
            if n_processed == 1 or n_processed % 50 == 0:
                logger.info("[%s] processed=%d", self.group_id, n_processed)

        while True:
            try:
                with WaveMQClient(self.broker_addr, transport="tcp") as client:
                    client.ensure_topic(self.output_topic, partitions=1, replication_factor=1)
                    logger.info("[%s] %s -> %s", self.group_id, self.input_topic, self.output_topic)
                    client.consume_poll(
                        group=self.group_id,
                        topic=self.input_topic,
                        partition=0,
                        start_from="earliest",
                        use_committed=True,
                        max_messages=0,
                        poll_interval=self.poll_interval,
                        commit=True,
                        stop_when_caught_up=False,
                        on_record=_on_record,
                    )
            except KeyboardInterrupt:
                break
            except TopicNotFoundError:
                logger.info("[%s] topic %s not found, retry in 5s...", self.group_id, self.input_topic)
                time.sleep(5)
            except WaveMQConnectionError:
                logger.warning("[%s] connection lost, reconnect in 2s...", self.group_id)
                time.sleep(2)

        logger.info("[%s] stopped, total processed=%d", self.group_id, n_processed)
