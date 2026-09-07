---
'com.posthog.unity': patch
---

Preserve analytics and session replay events across transient ingestion failures, safely handle oversized payloads, and prevent in-flight queue replacements from being acknowledged as sent. Drop analytics events that fail serialization without throwing or evicting existing queued events.
