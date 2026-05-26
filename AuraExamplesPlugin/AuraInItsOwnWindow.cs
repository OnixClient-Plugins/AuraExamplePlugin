using OnixRuntime.Api.Aura;
using OnixRuntime.Api.Maths;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace AuraExamplesPlugin {
    // basically the hello triangle but in its own window.
    internal class AuraInItsOwnWindow {
        private static CancellationToken _cancellationToken = default;

        [StructLayout(LayoutKind.Sequential)]
        struct MyVertex {
            public Vec2 position;
            public uint color;
        }

        private static string _shaderCode = """
                                     struct PSInput {
                                         float4 position : SV_POSITION;
                                         float4 color : COLOR;
                                     };

                                     PSInput VSmain(float2 position : POSITION, float4 color : COLOR) {
                                         PSInput result;
                                         
                                         result.position = float4(position, 0.f, 1.f);
                                         result.color = color;

                                         return result;
                                     }

                                     float4 PSmain(PSInput input) : SV_TARGET {
                                         float4 color = input.color;
                                         return color;
                                     }
                                     """;

        
        public static void HandleWindow() {
            try {
                // you could also do the same on the game's backend if it's more convenient for you (the only real difference would be getting game textures/dx objects though)
                // You'd also need to make sure you're synched properly when rendering.
                using var backendCreator = IAuraBackendCreator.CreateD3d11();
                backendCreator.EnableDebugMode(); // Only with d3d11, with direct X 12 you need a special d3d12.dll to enable it before the game creates a device.
                using var backend = backendCreator.CreateBackend();

                using var rasterizerState = backend.CreateRasterizerState(new(CullMode.Back));
                using var blendState = backend.CreateBlendState(new BlendStateDesc(false));
                using var depthStencilState = backend.CreateDepthStencilState(new DepthStencilStateDesc(false, false));
                using var vertexBuffer = backend.CreateBuffer(new AuraBufferDesc((ulong)Marshal.SizeOf<MyVertex>() * 3, BufferBindUsage.VertexBuffer));

                var shader = backend.CreateShaderProgram();
                if (shader.Compile(_shaderCode, false) is { } shaderCompileError) {
                    Console.WriteLine("Error compiling shader: " + shaderCompileError);
                    return;
                }
                shader.SetRecommendedInputLayout(new ShaderInputLayoutDesc([
                    new () {Type = AuraFormatType.Float2, SemanticName = "POSITION"},
                    new () {Type = AuraFormatType.NUByte4, SemanticName = "COLOR"},
                ]));

                var vertices = new MyVertex[] {
                    new MyVertex { position = new Vec2(-0.5f, -0.5f), color = 0xFF0000FF }, // Red
                    new MyVertex { position = new Vec2(0.0f, 0.5f),   color = 0xFF00FF00 }, // Green
                    new MyVertex { position = new Vec2(0.5f, -0.5f),  color = 0xFFFF0000 }, // Blue
                };

                int renderTargetWidth = 1280;
                int renderTargetHeight = 720;
                using var window = IAuraWindow.CreateWin32Window(renderTargetWidth, renderTargetHeight, "My Aura Window!");
                window.Center();


                vertexBuffer.Upload(vertices);
                backend.SetViewport(0.0f, 0.0f, (float)renderTargetWidth, (float)renderTargetHeight);
                backend.SetPrimitiveTopology(PrimitiveTopology.TriangleList);

                backend.BindRasterizerState(rasterizerState);
                backend.BindBlendState(blendState);
                backend.BindDepthStencilState(depthStencilState);
                backend.BindVertexBuffer(vertexBuffer);
                backend.BindShader(shader, true);


                // window.update can give you an exit code if it's been posted a quit message, for our purposes we don't care too much although our window's probably gone if it happens
                while (window.Update() is null && !window.HasBeenDestroyed() && !_cancellationToken.IsCancellationRequested) {
                    backend.BeginFrame();

                    backend.SetWindowRenderTarget(window);
                    using var backBufferTarget = backend.GetBackBufferTarget();
                    backBufferTarget.Clear(ColorF.Gray);


                    backend.Draw(3);
                    backend.Present(window, true);

                    backend.EndFrame();
                }
                if (!window.HasBeenDestroyed()) window.Destroy();
                backend.Shutdown();
            } catch (Exception e) {
                // catch anything, we don't want the process to crash if we get an exception.
                Console.WriteLine(e);
            }
        }

        public static void StartWindow(CancellationToken cancellationToken) {
            _cancellationToken = cancellationToken;
            new Thread(HandleWindow).Start();
        }
    }
}
