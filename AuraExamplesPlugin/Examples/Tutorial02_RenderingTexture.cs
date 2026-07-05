using OnixRuntime.Api;
using OnixRuntime.Api.Aura;
using OnixRuntime.Api.Maths;
using OnixRuntime.Api.Rendering;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace AuraExamplesPlugin.Examples {


    internal class Tutorial02_RenderingTexture : AuraExampleBase {

        /// This is our vertex layout, we send a position and color for every vertex right now.
        [StructLayout(LayoutKind.Sequential)]
        struct MyVertex {
            public Vec2 position;
            public uint color; // we can keep our color for tinting!
            public Vec2 uv; // added the uv coordinates of course.
        }

        // The code of our vertex and pixel shaders, basically just passing it along right now. Of course you can get this shader code however you want.
        // For the sake of this example though, we hardcode it.
        private string _shaderCode = """
                                     struct PSInput {
                                         float4 position : SV_POSITION;
                                         float4 color : COLOR;
                                         float2 uv : TEXCOORD;
                                     };
                                     Texture2D tex0 : register(t0);
                                     sampler nearestSampler : register(s0);
                                     

                                     PSInput VSmain(float2 position : POSITION, float4 color : COLOR, float2 uv : TEXCOORD) { // add uv to your vertex shader so we can pass it along.
                                         PSInput result;
                                         
                                         result.position = float4(position, 0.f, 1.f);
                                         result.color = color;
                                         result.uv = uv; // pass uv to the next stage

                                         return result;
                                     }

                                     float4 PSmain(PSInput input) : SV_TARGET {
                                        // sample texture 0 with the nearest sampler at input.uv and multiplying with input.color for tinting opportunities.
                                        // input.uv is basically normalized pixel coordinates for the current pixel 
                                        float4 color = tex0.Sample(nearestSampler, input.uv) * input.color;
                                        
                                        //example of alpha testing if you want to mess around.
                                        if (color.a < 0.95) // or whatever threshold
                                            discard;
                                        return color;
                                     }
                                     """;

        // various states and device objects to do rendering with.
        // You can explore the options from each's descriptions.
        private IRasterizerState rasterizerState;
        private IBlendState blendState;
        private IDepthStencilState depthStencilState;
        private IAuraBuffer vertexBuffer;
        private IShaderProgram shader;
        private ISampler nearestSampler;

        // this function is there because sometimes the texture won't already be loaded.
        // of course if you load it from a file you would just call backend.LoadTexture with RawImageData and you wont haave to do anything.
        private ITexture? TryLoadTexture(IAuraBackend backend) {
            // this does not just upload the texture to the backend, it actually uses the game's instance.
            // now you could cache this but the game might just want to get rid of it eventually.
            // so if you do make sure you refresh it when joining a new world to make sure it follows the current pack.
            return Onix.Render.AuraHelpers.GetTexture(backend, TexturePath.Game("textures/ui/title"));
        }

        public Tutorial02_RenderingTexture(IAuraBackend backend) {
            // feel free to inspect the description to explore possibilities!
            rasterizerState = backend.CreateRasterizerState(new RasterizerStateDesc(CullMode.Back));
            // we set this to true, this applies "normal" blending by default, but you can customize how things blend further if you want.
            // this lets our texture's background be transparent. We also could've implemented alpha testing in our shader.
            blendState = backend.CreateBlendState(new BlendStateDesc(true));
            depthStencilState = backend.CreateDepthStencilState(new DepthStencilStateDesc(false, false));
            vertexBuffer = backend.CreateBuffer(new AuraBufferDesc((ulong)Marshal.SizeOf<MyVertex>() * 6, BufferBindUsage.VertexBuffer, false)); // dynamic is for buffers you update every draw (could get away with every few draws) // now we have 6!
            // now we'll need a sampler to essentially scale our texture and figure out what pixels go where when resizing and also what happens when indexing out of bounds.
            nearestSampler = backend.CreateSampler(new SamplerDesc(AuraSamplerFilterType.MinMagMipPoint /* nearest neighbor */, AuraSamplerAddressMode.Mirror));

            shader = backend.CreateShaderProgram();
            if (shader.Compile(_shaderCode, false) is { } shaderCompileError) {
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
                new MyVertex { position = new Vec2(-0.5f,  0.5f), color = 0xFFFFFFFF, uv = new Vec2(0.0f, 0.0f) }, // Top-left
                new MyVertex { position = new Vec2( 0.5f,  0.5f), color = 0xFFFFFFFF, uv = new Vec2(1.0f, 0.0f) }, // Top-right
                new MyVertex { position = new Vec2(-0.5f, -0.5f), color = 0xFFFFFFFF, uv = new Vec2(0.0f, 1.0f) }, // Bottom-left

                // Triangle 2
                new MyVertex { position = new Vec2(-0.5f, -0.5f), color = 0xFFFFFFFF, uv = new Vec2(0.0f, 1.0f) }, // Bottom-left
                new MyVertex { position = new Vec2( 0.5f,  0.5f), color = 0xFFFFFFFF, uv = new Vec2(1.0f, 0.0f) }, // Top-right
                new MyVertex { position = new Vec2( 0.5f, -0.5f), color = 0xFFFFFFFF, uv = new Vec2(1.0f, 1.0f) }, // Bottom-right
            };
            vertexBuffer.Upload(vertices);
        }
        public override void Render(IAuraBackend backend, float deltaTime) {
            // never forget to dispose any reference to the back buffer target, otherwise you get a straight crash.
            using var backBuffer = backend.GetBackBufferTarget();
            using var texture = TryLoadTexture(backend); // dont forget it needs to be disposed when you're done.
            if (texture == null)
                return; // pack it up guys, draw's not for this frame.
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

            // bind our sampler and texture to slot 0
            backend.BindSampler(nearestSampler, 0);
            backend.BindTexture(texture, 0);

            // send it!
            backend.Draw(vertexBuffer.ElementCount);
        }

        public override void Dispose() {
            // don't forget to dispose guys, I know its tempting to be lazy but then you get issues and weird leaks.
            rasterizerState.Dispose();
            blendState.Dispose();
            depthStencilState.Dispose();
            vertexBuffer.Dispose();
            shader.Dispose();
            nearestSampler.Dispose();
        }

    }
}
