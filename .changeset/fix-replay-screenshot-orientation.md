---
'com.posthog.unity': patch
---

Fix upside-down session replay screenshots on graphics APIs whose texture UV coordinates start at the bottom, such as OpenGL ES, by avoiding an unnecessary vertical flip. Readbacks on top-origin APIs such as Metal, Vulkan, and Direct3D remain corrected, with the required flip now performed on the GPU during the downscale blit instead of per-pixel on the CPU.
