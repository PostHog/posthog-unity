---
'com.posthog.unity': patch
---

Serialize numeric NaN and infinities as JSON null so queued analytics events retain neighboring properties and array positions and can flush safely.
