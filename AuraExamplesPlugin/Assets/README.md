# AuraExamplesPlugin

A collection of progressive tutorials demonstrating how to use the low-level **Aura rendering engine** in the [Onix Client](https://onixclient.com) plugin API. Each example builds on the previous one, covering everything from drawing a basic triangle to full 3D rendering and back-buffer capture.

---

## What Is Aura?

Aura is the low-level DirectX 11 and DirectX 12 wrapper rendering backend exposed by the Onix Client plugin API. It gives plugins direct access to the GPU. Vertex buffers, index buffers, constant buffers, shaders, textures, samplers, and render states. Allowing you a lot more possibilities when not going through a higher-level abstraction. These examples show you how to use that API safely and correctly. This is still a low level graphics API, it works on DirectX 11 and 12 and models the DirectX 11 API.

---

## Tutorials

| # | File | Concepts Covered                                                                                            |
|---|------|-------------------------------------------------------------------------------------------------------------|
| 1 | `Tutorial01_HelloTriangle` | Vertex buffers, shader compilation, rasterizer/blend/depth-stencil states, drawing a colored hello triangle |
| 2 | `Tutorial02_RenderingTexture` | Texture loading, UV coordinates, samplers, alpha blending                                                   |
| 3 | `Tutorial03_Indexing` | Index buffers, reducing vertex duplication, memory efficiency                                               |
| 4 | `Tutorial04_ConstantBuffersAnd2DMatrices` | Constant buffers, orthographic projection, pixel-coordinate positioning, time-based animation               |
| 5 | `Tutorial05_TheThirdDimension` | 3D vertices, perspective projection, depth buffers, rotation and view matrices (game/custom world view)     |
| 6 | `Tutorial06_RenderingTheBackBufferAndScreenshots` | Back-buffer capture, screenshot export, texture overlay effects                                             |
| 7 | `Tutorial07_PseudoWorldRendering` | Rendering into a texture and displaying that texture in the world                                           |
| 8 | `Tutorial08_ComputeShaders` | Compute shader compilation, unordered-access textures, dispatching thread groups, sampling compute output   |

A bonus file, `AuraInItsOwnWindow.cs`, demonstrates initializing a D3D11 backend in a separate Win32 window entirely outside the game. Not necessarily useful often but could come in handy for some weird situation.

---

## Requirements

- **Onix Client** installed with plugin support enabled and runtime 8 or above.
- Something to compile the dotnet10 C# 14 source code.

---

## Building

Open `AuraExamplesPlugin.sln` in Visual Studio and build the `x64` configuration (Debug or Release).

To build from the command line:

```
dotnet build AuraExamplesPlugin/AuraExamplesPlugin.csproj -c Debug
```

---

## Switching Between Examples

Inside `AuraExamplesPlugin.cs`, find the field that holds the active example and change it to the tutorial class you want to run:

```csharp
// Swap this to any of the Tutorial0X_ classes
currentExample ??= new Tutorial01_HelloTriangle(backend);
```

Rebuild and reload the plugin in-game to see the new example.

---

## Key Concepts

**Resource management**: every GPU resource (`IAuraBuffer`, `IShaderProgram`, `ITexture`, `ISampler`) must be disposed when no longer needed. Each tutorial implements `Dispose()` to clean up. Cleaning up is important as without it, resizing will crash the game when you then go use any of these.

**Device loss**: the Onix Client fires `Rendering.AuraDeviceLost` when the D3D device is recreated (e.g., after a driver reset or resolution change). Your plugin must dispose of all resources on that event and re-creates them on the next render call. Always handle this event or you will crash.

**Constant buffer alignment**: DirectX requires constant buffers to be a multiple of 16 bytes. Pad your structs accordingly.

**Dynamic buffers**: pass `true` for the dynamic flag when creating a buffer you will update once and read once. (or more if you dont have too many draw calls reading it per frame too)

---


## Support
Don't hesitate to ask for help.
- Discord: [onixclient.com/discord](https://onixclient.com/discord)
- Plugin docs: [plugin.onixclient.com/docs/latest/guide/getting-started.html](https://plugin.onixclient.com/docs/latest/guide/getting-started.html)
