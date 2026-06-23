using OnixRuntime.Api;
using OnixRuntime.Api.Aura;
using OnixRuntime.Api.Maths;
using OnixRuntime.Api.Rendering;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace AuraExamplesPlugin.Examples {

    internal class Tutorial03_Indexing : AuraExampleBase {

        /// This is our vertex layout, we send a position and color for every vertex right now.
        [StructLayout(LayoutKind.Sequential)]
        struct MyVertex {
            public Vec2 position;
            public uint color;
            public Vec2 uv;
        }

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
                                        return color;
                                     }
                                     """;

        // various states and device objects to do rendering with.
        // You can explore the options from each's descriptions.
        private IRasterizerState rasterizerState;
        private IBlendState blendState;
        private IDepthStencilState depthStencilState;
        private IAuraBuffer vertexBuffer;
        private IAuraBuffer indexBuffer;
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

        public Tutorial03_Indexing(IAuraBackend backend) {
            // feel free to inspect the description to explore possibilities!
            rasterizerState = backend.CreateRasterizerState(new RasterizerStateDesc(CullMode.Back));
            blendState = backend.CreateBlendState(new BlendStateDesc(true));
            depthStencilState = backend.CreateDepthStencilState(new DepthStencilStateDesc(false, false));
            vertexBuffer = backend.CreateBuffer(new AuraBufferDesc((ulong)Marshal.SizeOf<MyVertex>() * 4, BufferBindUsage.VertexBuffer)); // now we have 4 unique vertices
            indexBuffer = backend.CreateBuffer(new AuraBufferDesc((ulong)Marshal.SizeOf<short>() * 6, BufferBindUsage.IndexBuffer)); // we have 6 vertices to reference
            nearestSampler = backend.CreateSampler(new SamplerDesc(AuraSamplerFilterType.MinMagMipPoint, AuraSamplerAddressMode.Mirror));

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
            // Basically with indexing, you specify only the unique vertices.
            var vertices = new MyVertex[] {
                new MyVertex { position = new Vec2(-0.5f,  0.5f), color = 0xFFFFFFFF, uv = new Vec2(0.0f, 0.0f) }, // Top-left
                new MyVertex { position = new Vec2( 0.5f,  0.5f), color = 0xFFFFFFFF, uv = new Vec2(1.0f, 0.0f) }, // Top-right
                new MyVertex { position = new Vec2(-0.5f, -0.5f), color = 0xFFFFFFFF, uv = new Vec2(0.0f, 1.0f) }, // Bottom-left
                new MyVertex { position = new Vec2( 0.5f, -0.5f), color = 0xFFFFFFFF, uv = new Vec2(1.0f, 1.0f) }, // Bottom-right
            };
            vertexBuffer.Upload(vertices);
            // and the index buffer is a short, it'll set the ElementSizeInBytes to 2 for us.
            // And then your mesh will be formed with the index buffer.
            // The index buffer basically is the index from the unique vertex array.
            // this ends up being the equivalent of the previous example
            var indices = new ushort[] {
                0, 1, 2, // First triangle  (top-left,  top-right, bottom-left)
                2, 1, 3, // Second triangle (bottom-left, top-right, bottom-right)
            };
            indexBuffer.Upload(indices);
            // now why go through all that?
            // because we only upload 4 unique vertices to the gpu. so we take less vram and pcie bandwidth.
            // with larger meshes it would of course be more consequential.
            // Our vertex is 20 bytes, * 4 is 80 bytes. then 6 indices of 2 bytes each which is 12 bytes. Total: 92
            // Previously, vertex is still 20 but * 6 = 120 then 6 indices, 2 bytes each is 12 bytes so   Total: 132
            // of course with smaller meshes, dealing with the extra buffer could be slower, especially if updated every frame.
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
            backend.BindIndexBuffer(indexBuffer); // bind our index buffer.
            backend.BindShader(shader, true);

            // bind our sampler and texture to slot 0
            backend.BindSampler(nearestSampler, 0);
            backend.BindTexture(texture, 0);

            // send it!
            backend.DrawIndexed(indexBuffer.ElementCount); // we say we're drawing indexed from an index buffer and specify how many indices we got.
        }

        public override void Dispose() {
            // don't forget to dispose guys, I know its tempting to be lazy but then you get issues and weird leaks.
            rasterizerState.Dispose();
            blendState.Dispose();
            depthStencilState.Dispose();
            vertexBuffer.Dispose();
            indexBuffer.Dispose();
            shader.Dispose();
            nearestSampler.Dispose();
        }

    }
}
