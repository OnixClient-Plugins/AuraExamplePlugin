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
    internal class Tutorial06_RenderingTheBackBufferAndScreenshots : AuraExampleBase {


        /// This is our vertex layout, we send a position and color for every vertex right now.
        [StructLayout(LayoutKind.Sequential)]
        struct MyVertex {
            public Vec2 position;
            public uint color; // we can keep our color for tinting!
            public Vec2 uv; // added the uv coordinates of course.
        }
        // Constant buffers are 16 bytes aligned in direct x.
        [StructLayout(LayoutKind.Sequential, Pack = 16)]
        struct MyConstantBuffer {
            // Not using TransformationMatrix here since that one contains a 3x2 matrix at the end too.
            public TransformationMatrixAura mat; // size of that is 64 so we're on that 16 bytes alignment 
            public float time;
            private float padding0;
            private float padding1;
            private float padding2;
        }

        // The code of our vertex and pixel shaders. Of course you can get this shader code however you want.
        // For the sake of this example though, we hardcode it.
        private string _shaderCode = """
                                     cbuffer cb0 : register(b0) {
                                         float4x4 mat;
                                         float time;
                                     };
                                     struct PSInput {
                                         float4 position : SV_POSITION;
                                         float4 color : COLOR;
                                         float2 uv : TEXCOORD;
                                     };
                                     Texture2D tex0 : register(t0);
                                     sampler nearestSampler : register(s0);
                                     

                                     PSInput VSmain(float2 position : POSITION, float4 color : COLOR, float2 uv : TEXCOORD) {
                                         PSInput result;
                                         
                                         result.position = mul(mat, float4(position, 0.f, 1.f));
                                         result.color = color;
                                         result.uv = uv; // pass uv to the next stage

                                         return result;
                                     }

                                     float4 PSmain(PSInput input) : SV_TARGET {
                                        float4 color = tex0.Sample(nearestSampler, input.uv) * input.color;
                                        
                                        return color;
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
        private ISampler nearestSampler;
        // we can safely keep a reference since we own that texture
        private ITexture? backBufferTexture;
        private Stopwatch stopwatch = Stopwatch.StartNew();

        private ITexture GetBackBufferCopy(IAuraBackend backend) {
            // You MUST release that reference.
            using var backBufferTarget = backend.GetBackBufferTarget();
            using var backBuffer = backBufferTarget.Texture;
            if (backBufferTexture is not null && (backBuffer.Width != backBufferTexture.Width || backBuffer.Height != backBufferTexture.Height || backBuffer.Format != backBufferTexture.Format)) {
                backBufferTexture.Dispose();
                backBufferTexture = null;
            }

            // create basically a copy but that one's CanBeRendered will be true.
            backBufferTexture ??= backend.CreateTexture(new TextureDesc(backBuffer.Width, backBuffer.Height, backBuffer.Format, 1, true));
            // we want to cache this as allocating a new texture is expensive, copying it is less bad than a new one + a copy.
            backBuffer.CopyInto(backBufferTexture);
            return backBufferTexture;
        }

        // you can set to false if you want to see a beautiful screenshot on your desktop
        private bool _savedImage = true;
        private void SaveImage(IAuraBackend backend, ITexture texture) {
            if (_savedImage) return;
            _savedImage = true;
             var image = texture.ReadbackWholeTextureAsImage();
             image?.Save(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Screenshot.png"));
        }

        public Tutorial06_RenderingTheBackBufferAndScreenshots(IAuraBackend backend) {
            // feel free to inspect the description to explore possibilities!
            rasterizerState = backend.CreateRasterizerState(new RasterizerStateDesc(CullMode.Back));
            blendState = backend.CreateBlendState(new BlendStateDesc(true));
            depthStencilState = backend.CreateDepthStencilState(new DepthStencilStateDesc(false, false));
            vertexBuffer = backend.CreateBuffer(new AuraBufferDesc((ulong)Marshal.SizeOf<MyVertex>() * 6, BufferBindUsage.VertexBuffer, false)); // dynamic is for buffers you update every draw (could get away with every few draws)
            constantBuffer = backend.CreateBuffer(new AuraBufferDesc((ulong)Marshal.SizeOf<MyConstantBuffer>(), BufferBindUsage.ConstantBuffer, true)); // create the constant buffer, as dynamic since we'll upload to it every draw.
            nearestSampler = backend.CreateSampler(new SamplerDesc(AuraSamplerFilterType.MinMagMipPoint, AuraSamplerAddressMode.Mirror));

            shader = backend.CreateShaderProgram();
            if (shader.Compile(_shaderCode, false) is { } shaderCompileError) {
                Logger.GetForPlugin().Error("Error compiling shader: " + shaderCompileError);
                Console.WriteLine("Error compiling shader: " + shaderCompileError);
                return;
            }
            shader.SetRecommendedInputLayout(new ShaderInputLayoutDesc([
                new () {Type = AuraFormatType.Float2, SemanticName = "POSITION"},
                new () {Type = AuraFormatType.NUByte4, SemanticName = "COLOR"},
                new () {Type = AuraFormatType.Float2, SemanticName = "TEXCOORD"},
            ]));

            // since we upload a struct[], we don't need to set the ElementSizeInBytes
            var vertices = new MyVertex[] {
                // Triangle 1
                new MyVertex { position = new Vec2(20,  20), color = 0xFFFFFFFF, uv = new Vec2(0.0f, 0.0f) }, // Top-left
                new MyVertex { position = new Vec2(920,  20), color = 0xFFFFFFFF, uv = new Vec2(1.0f, 0.0f) }, // Top-right
                new MyVertex { position = new Vec2(20, 220), color = 0xFFFFFFFF, uv = new Vec2(0.0f, 1.0f) }, // Bottom-left

                // Triangle 2
                new MyVertex { position = new Vec2(20, 220), color = 0xFFFFFFFF, uv = new Vec2(0.0f, 1.0f) }, // Bottom-left
                new MyVertex { position = new Vec2(920, 20), color = 0xFFFFFFFF, uv = new Vec2(1.0f, 0.0f) }, // Top-right
                new MyVertex { position = new Vec2(920, 220), color = 0xFFFFFFFF, uv = new Vec2(1.0f, 1.0f) }, // Bottom-right
            };
            vertexBuffer.Upload(vertices);
        }
        public override void Render(IAuraBackend backend, float deltaTime) {
            // never forget to dispose any reference to the back buffer target, otherwise you get a straight crash.
            using var backBuffer = backend.GetBackBufferTarget();
            var texture = GetBackBufferCopy(backend);
            SaveImage(backend, texture); // we dont need a copy here, but it avoids making a temporary variable to hold the texture so we can dispose it.
            // forgetting this and you don't see anything.
            backend.SetViewport(0.0f, 0.0f, backBuffer.Widthf, backBuffer.Heightf);
            backend.SetScissorRect(0, 0, (int)backBuffer.Width, (int)backBuffer.Height);
            backend.SetPrimitiveTopology(PrimitiveTopology.TriangleList);

            // bind our pipeline
            backend.BindRasterizerState(rasterizerState);
            backend.BindBlendState(blendState);
            backend.BindDepthStencilState(depthStencilState);
            backend.BindVertexBuffer(vertexBuffer);
            backend.BindShader(shader, true);

            backend.BindSampler(nearestSampler, 0);
            backend.BindTexture(texture, 0);

            MyConstantBuffer constantBufferData = new MyConstantBuffer() {
                mat = new(TransformationMatrix.Orthographic(0, backBuffer.Widthf, backBuffer.Heightf, 0.0f)),
                time = (float)stopwatch.Elapsed.TotalSeconds
            };
            constantBuffer.Upload(constantBufferData);
            backend.BindConstantBuffer(constantBuffer, 0);
            
            // you should see the game again at the top left.
            backend.Draw(vertexBuffer.ElementCount);
        }

        public override void Dispose() {
            // don't forget to dispose guys, I know its tempting to be lazy, but then you get issues and weird leaks.
            rasterizerState.Dispose();
            blendState.Dispose();
            depthStencilState.Dispose();
            vertexBuffer.Dispose();
            constantBuffer.Dispose();
            shader.Dispose();
            nearestSampler.Dispose();
            backBufferTexture?.Dispose();
        }
        
    }
}
