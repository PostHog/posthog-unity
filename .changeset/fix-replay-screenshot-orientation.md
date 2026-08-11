---
'com.posthog.unity': patch
---

Fix upside-down session replay screenshots on graphics APIs whose texture origin is top-left (Metal, Vulkan, Direct3D). The capture now flips a frame only when the platform origin is bottom-left (OpenGL ES), and does the flip on the GPU during the downscale blit instead of per-pixel on the CPU.
