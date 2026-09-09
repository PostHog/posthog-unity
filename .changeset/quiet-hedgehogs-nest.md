---
'com.posthog.unity': patch
---

Omit null-valued custom object properties recursively when serializing events for delivery or disk storage, while preserving null array positions and existing generic serialization behavior.
