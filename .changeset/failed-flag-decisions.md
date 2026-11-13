---
"com.posthog.unity": patch
---

Treat failed feature flag evaluations as missing values, honor caller defaults, and report evaluation failures without exposing placeholder values or payloads. Preserve successful cached flags on response-wide evaluation errors while replacing them on complete nonerrored refreshes.
