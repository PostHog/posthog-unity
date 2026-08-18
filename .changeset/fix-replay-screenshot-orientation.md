---
'com.posthog.unity': patch
---

Fix upside-down session replay screenshots by correcting vertically inverted readbacks on graphics APIs whose texture UV coordinates start at the top (Metal, Vulkan, Direct3D). The capture leaves bottom-left APIs (OpenGL ES) unchanged and performs any required flip on the GPU during the downscale blit instead of per-pixel on the CPU.
