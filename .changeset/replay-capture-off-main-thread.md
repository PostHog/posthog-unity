---
'com.posthog.unity': patch
---

Reduce session replay frame hitches by encoding screenshots to JPEG and base64 on a worker thread, and by serializing and gzipping replay batches on a worker thread instead of the main thread.
