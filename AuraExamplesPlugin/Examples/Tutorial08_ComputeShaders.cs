using OnixRuntime.Api;
using OnixRuntime.Api.Aura;
using OnixRuntime.Api.Maths;
using OnixRuntime.Api.Rendering;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AuraExamplesPlugin.Examples {
    internal class Tutorial08_ComputeShaders : AuraExampleBase {

        /// This is the vertex layout for the quad that displays our compute output.
        [StructLayout(LayoutKind.Sequential)]
        struct MyVertex {
            public Vec2 position; // where this corner of the quad goes on screen, from -1 to +1
            public Vec2 uv; // which part of the texture this corner should sample
        }

        // Constant buffers are 16 bytes aligned in direct x.
        [StructLayout(LayoutKind.Sequential, Pack = 16)]
        struct MyConstantBuffer {
            public Vec4 timeResolution; // x=time, y=width, z=height, w=unused
        }

        private string _computeShaderCode = """
                                            cbuffer cb0 : register(b0) {
                                                float4 timeResolution;
                                            };

                                            // This is a writable texture. Pixel shaders usually sample textures with Texture2D,
                                            // but compute shaders can write into resources marked RWTexture2D.
                                            // register(u0) means "unordered access slot 0", which we bind from C# below.
                                            RWTexture2D<float4> outputTexture : register(u0);

                                            float hash(float2 p) {
                                                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453123);
                                            }

                                            // Every compute dispatch is split into groups of threads.
                                            // This says each group is 8 pixels wide, 8 pixels tall, and 1 deep.
                                            [numthreads(8, 8, 1)]
                                            void CSmain(uint3 threadId : SV_DispatchThreadID) {
                                                uint width = (uint)timeResolution.y;
                                                uint height = (uint)timeResolution.z;
                                                // Our dispatch rounds up to cover the whole texture. That can create
                                                // extra threads past the edge, so those threads must do nothing.
                                                if (threadId.x >= width || threadId.y >= height)
                                                    return;

                                                float time = timeResolution.x;
                                                float2 resolution = float2(width, height);
                                                // Convert the current pixel coordinate into a 0..1 UV coordinate.
                                                float2 uv = ((float2)threadId.xy + 0.5f) / resolution;
                                                // Center the coordinate system and keep circles round on non-square textures.
                                                float2 p = (uv - 0.5f) * float2(resolution.x / resolution.y, 1.0f);

                                                float rings = sin(length(p) * 38.0f - time * 5.0f);
                                                float sweep = sin((p.x + p.y) * 18.0f + time * 2.8f);
                                                float sparkle = step(0.985f, hash(floor(uv * 96.0f) + floor(time * 18.0f)));
                                                float pulse = 0.5f + 0.5f * sin(time + length(p) * 12.0f);

                                                float3 deep = float3(0.03f, 0.06f, 0.08f);
                                                float3 cyan = float3(0.10f, 0.85f, 0.95f);
                                                float3 coral = float3(1.00f, 0.32f, 0.18f);
                                                float3 gold = float3(1.00f, 0.78f, 0.24f);

                                                float3 color = deep;
                                                color += cyan * smoothstep(0.65f, 1.0f, rings * 0.5f + 0.5f) * pulse;
                                                color += coral * smoothstep(0.6f, 1.0f, sweep * 0.5f + 0.5f) * 0.55f;
                                                color += gold * sparkle;
                                                color *= smoothstep(0.95f, 0.15f, length(p));

                                                // Unlike a pixel shader, compute does not return a color.
                                                // We manually write the color into the pixel we are responsible for.
                                                outputTexture[threadId.xy] = float4(color, 1.0f);
                                            }
                                            """;

        private string _renderShaderCode = """
                                           struct PSInput {
                                               float4 position : SV_POSITION;
                                               float2 uv : TEXCOORD;
                                           };

                                           Texture2D tex0 : register(t0);
                                           sampler nearestSampler : register(s0);

                                           PSInput VSmain(float2 position : POSITION, float2 uv : TEXCOORD) {
                                               PSInput result;
                                               result.position = float4(position, 0.f, 1.f);
                                               result.uv = uv;
                                               return result;
                                           }

                                           float4 PSmain(PSInput input) : SV_TARGET {
                                               return tex0.Sample(nearestSampler, input.uv);
                                           }
                                           """;

        // These are the normal rendering objects we use after compute finishes.
        // The compute shader fills a texture, then the render shader draws that texture to a quad.
        private IRasterizerState rasterizerState;
        private IBlendState blendState;
        private IDepthStencilState depthStencilState;
        private IAuraBuffer vertexBuffer;
        private IAuraBuffer constantBuffer;
        private IShaderProgram computeShader;
        private IShaderProgram renderShader;
        private ISampler nearestSampler;
        private ITexture outputTexture;
        private Stopwatch stopwatch = Stopwatch.StartNew();
        // The compute shader writes to a 512x512 offscreen texture. The final quad can scale it to any screen size.
        private const uint TextureWidth = 512;
        private const uint TextureHeight = 512;
        // This must match the [numthreads(8, 8, 1)] value in the compute shader.
        private const uint ThreadsPerGroup = 8;

        public Tutorial08_ComputeShaders(IAuraBackend backend) {
            // These are the same render states used in earlier examples.
            // Compute itself does not use rasterizer/blend/depth state, but the fullscreen quad does.
            rasterizerState = backend.CreateRasterizerState(new RasterizerStateDesc(CullMode.Back));
            blendState = backend.CreateBlendState(new BlendStateDesc(true));
            depthStencilState = backend.CreateDepthStencilState(new DepthStencilStateDesc(false, false));

            // The vertex buffer is just a quad on screen. The compute shader creates the image;
            // these vertices only give us somewhere to show that image.
            vertexBuffer = backend.CreateBuffer(new AuraBufferDesc((ulong)Marshal.SizeOf<MyVertex>() * 6, BufferBindUsage.VertexBuffer, false)); // dynamic is for buffers you update every draw (could get away with every few draws)

            // This buffer is updated every frame with time and texture size.
            // We bind the exact same constant buffer when running the compute shader.
            constantBuffer = backend.CreateBuffer(new AuraBufferDesc((ulong)Marshal.SizeOf<MyConstantBuffer>(), BufferBindUsage.ConstantBuffer, true));
            nearestSampler = backend.CreateSampler(new SamplerDesc(AuraSamplerFilterType.MinMagMipPoint, AuraSamplerAddressMode.Clamp));

            // This texture is the whole point: compute writes to it as a UAV, then the render shader samples it as a regular texture.
            // UAV means unordered access view. That is the DirectX name for a resource shaders can write to in arbitrary order.
            TextureDesc outputTextureDesc = new TextureDesc(TextureWidth, TextureHeight, AuraColorFormat.RGBA8, 1, false, false, false) {
                AllowUnorderedAccess = true
            };
            outputTexture = backend.CreateTexture(outputTextureDesc);

            // The second argument is true because this shader has CSmain instead of VSmain/PSmain.
            computeShader = backend.CreateShaderProgram();
            // This second shader is completely ordinary. It samples the texture the compute shader wrote.
            renderShader = backend.CreateShaderProgram();
            if (computeShader.Compile(_computeShaderCode, true) is { } computeCompileError) {
                Logger.GetForPlugin().Error("Error compiling compute shader: " + computeCompileError);
                Console.WriteLine("Error compiling compute shader: " + computeCompileError);
                return;
            }

            if (renderShader.Compile(_renderShaderCode, false) is { } renderCompileError) {
                Logger.GetForPlugin().Error("Error compiling render shader: " + renderCompileError);
                Console.WriteLine("Error compiling render shader: " + renderCompileError);
                return;
            }
            renderShader.SetRecommendedInputLayout(new ShaderInputLayoutDesc([
                new () {Type = AuraFormatType.Float2, SemanticName = "POSITION"},
                new () {Type = AuraFormatType.Float2, SemanticName = "TEXCOORD"},
            ]));

            // Two triangles make one square. The position is where the quad appears on screen,
            // and uv decides which part of the compute texture each corner receives.
            MyVertex[] vertices =
            [
                new() { position = new Vec2(-0.75f, -0.75f), uv = new Vec2(0, 1) },
                new() { position = new Vec2(-0.75f,  0.75f), uv = new Vec2(0, 0) },
                new() { position = new Vec2( 0.75f,  0.75f), uv = new Vec2(1, 0) },

                new() { position = new Vec2(-0.75f, -0.75f), uv = new Vec2(0, 1) },
                new() { position = new Vec2( 0.75f,  0.75f), uv = new Vec2(1, 0) },
                new() { position = new Vec2( 0.75f, -0.75f), uv = new Vec2(1, 1) },
            ];
            vertexBuffer.Upload(vertices);
        }

        public override void Render(IAuraBackend backend, float deltaTime) {
            using var backBuffer = backend.GetBackBufferTarget();

            // First, update the data the compute shader needs this frame.
            // timeResolution.x animates the effect; y/z tell the shader how big the output texture is.
            MyConstantBuffer constantBufferData = new MyConstantBuffer() {
                timeResolution = new Vec4((float)stopwatch.Elapsed.TotalSeconds, TextureWidth, TextureHeight, 0)
            };
            constantBuffer.Upload(constantBufferData);

            // Step 1: run the compute shader.
            // We bind the compute shader, the constants, and the writable texture.
            backend.BindShader(computeShader, false);
            backend.BindConstantBuffer(constantBuffer, 0);
            backend.BindUnorderedAccessTexture(outputTexture, 0);

            // Dispatch does not count pixels directly. It counts groups of threads.
            // Since the shader uses 8x8 threads per group, a 512x512 texture needs 64x64 groups.
            // The round-up math keeps this correct even if the texture size is not divisible by 8.
            backend.Dispatch(
                (TextureWidth + ThreadsPerGroup - 1) / ThreadsPerGroup,
                (TextureHeight + ThreadsPerGroup - 1) / ThreadsPerGroup,
                1);

            // Clear the UAV binding before sampling that same texture in the render shader.
            // DirectX does not like a texture being bound for writing and reading at the same time.
            backend.BindUnorderedAccessTexture(null, 0);

            // Step 2: draw the texture the compute shader just filled.
            // From this point on, this looks like the earlier texture tutorial.
            backend.SetViewport(0.0f, 0.0f, backBuffer.Widthf, backBuffer.Heightf);
            backend.SetScissorRect(0, 0, (int)backBuffer.Width, (int)backBuffer.Height);
            backend.SetPrimitiveTopology(PrimitiveTopology.TriangleList);
            backend.BindRasterizerState(rasterizerState);
            backend.BindBlendState(blendState);
            backend.BindDepthStencilState(depthStencilState);
            backend.BindVertexBuffer(vertexBuffer);
            backend.BindShader(renderShader, true);
            backend.BindSampler(nearestSampler, 0);
            backend.BindTexture(outputTexture, 0);

            // Draw the quad. The pixel shader samples outputTexture for every pixel of the quad.
            backend.Draw(vertexBuffer.ElementCount);
        }

        public override void Dispose() {
            rasterizerState.Dispose();
            blendState.Dispose();
            depthStencilState.Dispose();
            vertexBuffer.Dispose();
            constantBuffer.Dispose();
            computeShader.Dispose();
            renderShader.Dispose();
            nearestSampler.Dispose();
            outputTexture.Dispose();
        }
    }
}
