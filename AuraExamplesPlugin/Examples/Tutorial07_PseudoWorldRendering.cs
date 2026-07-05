using OnixRuntime.Api;
using OnixRuntime.Api.Aura;
using OnixRuntime.Api.Maths;
using OnixRuntime.Api.Rendering;
using OnixRuntime.Api.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Vanara.PInvoke;

namespace AuraExamplesPlugin.Examples {
    
    internal class Tutorial07_PseudoWorldRendering : AuraExampleBase {

        /// This is our vertex layout, we send a position and color for every vertex right now.
        [StructLayout(LayoutKind.Sequential)]
        struct MyVertex {
            public Vec2 position;
            public Vec2 uv;
        }
        // Constant buffers are 16 bytes aligned in direct x.
        [StructLayout(LayoutKind.Sequential, Pack = 16)]
        struct MyConstantBuffer {
            public Vec4 timeResolution; // for convenience and because of the padding
        }

        // then here we can do some cool effect, I had chat gpt generate a cool looking one for you guys
        // dont worry if you dont understand the code.
        private string _shaderCode = """
                                        cbuffer cb0 : register(b0) {
                                            float4 timeResolution; // x=time, y=aspect
                                        };
                                        
                                        struct PSInput {
                                            float4 position : SV_POSITION;
                                            float2 uv : TEXCOORD;
                                        };
                                        
                                        PSInput VSmain(float2 position : POSITION, float2 uv : TEXCOORD) {
                                            PSInput result;
                                        
                                            result.position = float4(position, 0.f, 1.f);
                                        
                                            // centered + aspect corrected
                                            result.uv = float2(
                                                (uv.x - 0.5f) * timeResolution.y,
                                                uv.y - 0.5f
                                            );
                                        
                                            return result;
                                        }
                                        
                                        float hash(float2 p) {
                                            return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453123);
                                        }
                                        
                                        float noise(float2 p) {
                                            float2 i = floor(p);
                                            float2 f = frac(p);
                                        
                                            float a = hash(i);
                                            float b = hash(i + float2(1,0));
                                            float c = hash(i + float2(0,1));
                                            float d = hash(i + float2(1,1));
                                        
                                            float2 u = f * f * (3.0 - 2.0 * f);
                                        
                                            return lerp(a, b, u.x)
                                                 + (c - a) * u.y * (1.0 - u.x)
                                                 + (d - b) * u.x * u.y;
                                        }
                                        
                                        float fbm(float2 p) {
                                            float v = 0.0;
                                            float a = 0.5;
                                        
                                            for(int i = 0; i < 5; i++) {
                                                v += noise(p) * a;
                                                p *= 2.0;
                                                a *= 0.5;
                                            }
                                        
                                            return v;
                                        }
                                        
                                        float4 PSmain(PSInput input) : SV_TARGET {
                                            float time = timeResolution.x;
                                            float2 uv = input.uv;
                                        
                                            float2 p = uv;
                                        
                                            float r = length(p);
                                            float a = atan2(p.y, p.x);
                                        
                                            float wave = sin(r * 12.0 - time * 4.0 + sin(a * 6.0)) * 0.15;
                                        
                                            if (r > 0.0001)
                                                p += normalize(p) * wave;
                                        
                                            float swirl = sin(r * 8.0 - time * 2.0) * 0.5;
                                        
                                            float cs = cos(swirl);
                                            float sn = sin(swirl);
                                        
                                            p = float2(
                                                p.x * cs - p.y * sn,
                                                p.x * sn + p.y * cs
                                            );
                                        
                                            float f = fbm(p * 3.0 + time * 0.2);
                                        
                                            float lines = sin((a + f * 2.0) * 20.0 - time * 5.0);
                                            lines = abs(lines);
                                            lines = pow(lines, 8.0);
                                        
                                            float glow = 0.02 / max(r, 0.001);
                                            glow += lines * 0.5;
                                        
                                            float3 col;
                                        
                                            col.r = sin(f * 4.0 + time * 1.1) * 0.5 + 0.5;
                                            col.g = sin(f * 4.0 + time * 1.6 + 2.0) * 0.5 + 0.5;
                                            col.b = sin(f * 4.0 + time * 2.1 + 4.0) * 0.5 + 0.5;
                                        
                                            col *= glow * 2.5;
                                        
                                            col *= smoothstep(1.4, 0.2, r);
                                        
                                            return float4(col, 1.0);
                                        }
                                        """;

        // various states and device objects to do rendering with.
        // You can explore the options from each's descriptions.
        private IRasterizerState rasterizerState;
        private IBlendState blendState;
        private IDepthStencilState depthStencilState;
        private IAuraBuffer vertexBuffer;
        private IAuraBuffer constantBuffer;
        private IShaderProgram shader;
        private IRenderTarget fakeTextureTarget; // we need a whole render target of our own since we cant render directly to the texture.
        private Stopwatch stopwatch = Stopwatch.StartNew();
        private TexturePath fakeTexturePath = TexturePath.Assets("textures/fake/cool_shader");
        private Vec2I fakeTextureSize = new Vec2I(1280, 720);
        private bool hasUploadedTexture = false;
        private Vec3 renderPosition = new();
        
        // this function is there because sometimes the texture won't already be loaded.
        // of course if you load it from a file you would just call backend.LoadTexture with RawImageData and you wont have to do anything.
        private ITexture? TryLoadTexture(IAuraBackend backend) {
            // so this will be our texture we upload from the game.
            return Onix.Render.AuraHelpers.GetTexture(backend, fakeTexturePath);
        }

        // and this function uploads an empty texture to the game's rendering backend.
        public void UploadFakeTexture(RendererWorld gfx) {
            if (!hasUploadedTexture || gfx.GetTextureStatus(fakeTexturePath) is RendererTextureStatus.Missing or RendererTextureStatus.Unloaded) {
                hasUploadedTexture = true;
                gfx.UploadTexture(fakeTexturePath, RawImageData.Create(fakeTextureSize.X, fakeTextureSize.Y));
            }
        }


        public Tutorial07_PseudoWorldRendering(IAuraBackend backend) {
            rasterizerState = backend.CreateRasterizerState(new RasterizerStateDesc(CullMode.Back));
            blendState = backend.CreateBlendState(new BlendStateDesc(true)); 
            depthStencilState = backend.CreateDepthStencilState(new DepthStencilStateDesc(false, false));
            vertexBuffer = backend.CreateBuffer(new AuraBufferDesc((ulong)Marshal.SizeOf<MyVertex>() * 6, BufferBindUsage.VertexBuffer, false)); // dynamic is for buffers you update every draw (could get away with every few draws)
            constantBuffer = backend.CreateBuffer(new AuraBufferDesc((ulong)Marshal.SizeOf<MyConstantBuffer>(), BufferBindUsage.ConstantBuffer, true));
            //create our own render target matching the texture's size for convenience.
            fakeTextureTarget = backend.CreateRenderTarget(new RenderTargetDesc((uint)fakeTextureSize.X, (uint)fakeTextureSize.Y, AuraColorFormat.RGBA8));

            shader = backend.CreateShaderProgram();
            if (shader.Compile(_shaderCode, false) is { } shaderCompileError) {
                Logger.GetForPlugin().Error("Error compiling shader: " + shaderCompileError);
                Console.WriteLine("Error compiling shader: " + shaderCompileError);
                return;
            }
            shader.SetRecommendedInputLayout(new ShaderInputLayoutDesc([
                new () {Type = AuraFormatType.Float2, SemanticName = "POSITION"},
                new () {Type = AuraFormatType.Float2, SemanticName = "TEXCOORD"},
            ]));

            // since we upload a struct[], we don't need to set the ElementSizeInBytes
            // this is a full screen quad
            MyVertex[] vertices =
            [
                new() { position = new Vec2(-1, -1), uv = new Vec2(0, 1) }, // bottom-left
                new() { position = new Vec2(-1,  1), uv = new Vec2(0, 0) }, // top-left
                new() { position = new Vec2( 1,  1), uv = new Vec2(1, 0) }, // top-right

                new() { position = new Vec2(-1, -1), uv = new Vec2(0, 1) }, // bottom-left
                new() { position = new Vec2( 1,  1), uv = new Vec2(1, 0) }, // top-right
                new() { position = new Vec2( 1, -1), uv = new Vec2(1, 1) }, // bottom-right
            ];
            vertexBuffer.Upload(vertices);
        }

        public override void Render(IAuraBackend backend, float deltaTime) {
            if (!hasUploadedTexture) return;
            using var texture = TryLoadTexture(backend); // dont forget it needs to be disposed when you're done.
            if (texture == null)
                return; // pack it up guys, draw's not for this frame.
            backend.SetPrimitiveTopology(PrimitiveTopology.TriangleList);

            // bind our pipeline
            backend.BindRasterizerState(rasterizerState);
            backend.BindBlendState(blendState);
            backend.BindDepthStencilState(depthStencilState);
            backend.BindVertexBuffer(vertexBuffer);
            backend.BindShader(shader, true);
            
            // now we bind our render target to draw to it
            backend.BindRenderTarget(fakeTextureTarget);
            backend.SetViewport(0.0f, 0.0f, fakeTextureTarget.Widthf, fakeTextureTarget.Heightf);
            backend.SetScissorRect(0, 0, (int)fakeTextureTarget.Width, (int)fakeTextureTarget.Height);

            // Constant buffer data!
            MyConstantBuffer constantBufferData = new MyConstantBuffer() {
                timeResolution = new Vec4((float)stopwatch.Elapsed.TotalSeconds, fakeTextureTarget.Widthf / fakeTextureTarget.Heightf, 0, 0)
            };
            constantBuffer.Upload(constantBufferData);
            backend.BindConstantBuffer(constantBuffer, 0);
            
            // send it! we now rendered onto our render target
            backend.Draw(vertexBuffer.ElementCount);

            // Then copy the data from our fresh texture to the target texture.
            using var targetTexture = fakeTextureTarget.Texture;
            targetTexture.CopyInto(texture);
        }

        public override void OnWorldRender(RendererWorld gfx, float deltaTime) {
            if (!hasUploadedTexture) // set a static location in the world to render it, you decide where you put it, but im putting it here.
                renderPosition = Onix.Render.Origin - new Vec3(0, 2, 0);
            
            // upload our texture into the game's renderer.
            UploadFakeTexture(gfx);

            using (var session = gfx.NewMeshBuilderSession(MeshBuilderPrimitiveType.Quad, ColorF.White, fakeTexturePath)) {
                float aspect = (float)fakeTextureSize.X / (float)fakeTextureSize.Y;
                float sizeX = 0.7f * aspect; // make it respect the aspect ratio
                float sizeY = 0.7f;
                session.Builder.Uv(0, 0);
                session.Builder.Vertex(renderPosition.X - sizeX, renderPosition.Y, renderPosition.Z - sizeY);
                session.Builder.Uv(0, 1);
                session.Builder.Vertex(renderPosition.X - sizeX, renderPosition.Y, renderPosition.Z + sizeY);
                session.Builder.Uv(1, 1);
                session.Builder.Vertex(renderPosition.X + sizeX, renderPosition.Y, renderPosition.Z + sizeY);
                session.Builder.Uv(1, 0);
                session.Builder.Vertex(renderPosition.X + sizeX, renderPosition.Y, renderPosition.Z - sizeY);
            }
        }

        public override void Dispose() {
            // don't forget to dispose guys, I know its tempting to be lazy, but then you get issues and weird leaks.
            rasterizerState.Dispose();
            blendState.Dispose();
            depthStencilState.Dispose();
            vertexBuffer.Dispose();
            constantBuffer.Dispose();
            shader.Dispose();
            fakeTextureTarget.Dispose();
        }
        
    }
}
