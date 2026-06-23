using OnixRuntime.Api;
using OnixRuntime.Api.Aura;
using OnixRuntime.Api.Maths;
using OnixRuntime.Api.Rendering;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AuraExamplesPlugin.Examples {
    // Passing in some data for all shader invocations!
    // and using 2d matrices to finally render to more familiar coordinates, outputting something that isn't all squished depending on window size.
    internal class Tutorial04_ConstantBuffersAnd2DMatrices : AuraExampleBase {

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
                                     // our new constant buffer containing our matrix!
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
                                         
                                         // we now multiply our position by our matrix. Note: you must multiply it as a float4 ending in 1.
                                         result.position = mul(mat, float4(position, 0.f, 1.f));
                                         result.color = color;
                                         result.uv = uv; // pass uv to the next stage

                                         return result;
                                     }

                                     float4 PSmain(PSInput input) : SV_TARGET {
                                        float4 color = tex0.Sample(nearestSampler, input.uv) * input.color;
                                        // make the green and blue channel go up and down based on a sin wave of the time to make it pulsate red.
                                        color.gb *= min(sin(time*5.f) * 0.5f + 0.75f, 1.f);
                                        
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
        private Stopwatch stopwatch = Stopwatch.StartNew();
        
        // this function is there because sometimes the texture won't already be loaded.
        // of course if you load it from a file you would just call backend.LoadTexture with RawImageData and you wont haave to do anything.
        private ITexture? TryLoadTexture(IAuraBackend backend) {
            // this does not just upload the texture to the backend, it actually uses the game's instance.
            // now you could cache this but the game might just want to get rid of it eventually.
            // so if you do make sure you refresh it when joining a new world to make sure it follows the current pack.
            return Onix.Render.AuraHelpers.GetTexture(backend, TexturePath.Game("textures/ui/title"));
        }

        public Tutorial04_ConstantBuffersAnd2DMatrices(IAuraBackend backend) {
            // feel free to inspect the description to explore possibilities!
            rasterizerState = backend.CreateRasterizerState(new RasterizerStateDesc(CullMode.Back));
            // we set this to true, this applies "normal" blending by default, but you can customize how things blend further if you want.
            // this lets our texture's background be transparent. We also could've implemented alpha testing in our shader.
            blendState = backend.CreateBlendState(new BlendStateDesc(true)); 
            depthStencilState = backend.CreateDepthStencilState(new DepthStencilStateDesc(false, false));
            vertexBuffer = backend.CreateBuffer(new AuraBufferDesc((ulong)Marshal.SizeOf<MyVertex>() * 6, BufferBindUsage.VertexBuffer));
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
            
            backend.BindSampler(nearestSampler, 0);
            backend.BindTexture(texture, 0);
            
            // Constant buffer!
            MyConstantBuffer constantBufferData = new MyConstantBuffer() {
                // An orthographic matrix does not distort with depth, making it ideal for UI or purely 2d games
                // positions specified here will be our world view.
                // right now our world matches the screen's pixel bounds meaning we can put our vertex coordinates in pixel coordinates instead.
                mat = new (TransformationMatrix.Orthographic(0, backBuffer.Widthf, backBuffer.Heightf, 0.0f)),
                // of course give it the current time too which is useful in many effects.
                // of course doing this right you'd probably want to only update this once a frame, if you have more data that wouldn't change in the entire frame and include the time there
                time = (float)stopwatch.Elapsed.TotalSeconds
            };
            // upload our data for this draw!
            constantBuffer.Upload(constantBufferData);
            // and of course bind it to slot 0 or whatever the shader expects.
            backend.BindConstantBuffer(constantBuffer, 0);
            // send it!
            // you'll be able to see that regardless of window size, our minecraft stays at the pixel coordinates we've defined in our world.
            // and it pulses red every now and then based on the time we upload.
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
        }
        
    }
}
