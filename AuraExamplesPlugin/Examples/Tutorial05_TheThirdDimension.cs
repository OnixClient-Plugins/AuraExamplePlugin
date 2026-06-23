using OnixRuntime.Api;
using OnixRuntime.Api.Aura;
using OnixRuntime.Api.Maths;
using OnixRuntime.Api.Rendering;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Vanara.PInvoke;

namespace AuraExamplesPlugin.Examples {
    internal class Tutorial05_TheThirdDimension : AuraExampleBase {

        /// This is our vertex layout, we send a position and color for every vertex right now.
        [StructLayout(LayoutKind.Sequential)]
        struct MyVertex {
            public Vec3 position;
            public uint color;
            public Vec2 uv;
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

        // The code of our vertex and pixel shaders,. Of course you can get this shader code however you want.
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
                                     

                                     PSInput VSmain(float3 position : POSITION, float4 color : COLOR, float2 uv : TEXCOORD) {
                                         PSInput result;
                                         
                                         // we now multiply our position by our matrix. Note: you must multiply it as a float4 ending in 1.
                                         // now with a float3 we remove the last 0
                                         result.position = mul(mat, float4(position, 1.f));
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
        private IDepthStencilBuffer? depthStencilBuffer;
        private IAuraBuffer vertexBuffer;
        private IAuraBuffer constantBuffer;
        private IShaderProgram shader;
        private ISampler nearestSampler;
        private Stopwatch stopwatch = Stopwatch.StartNew();
        private float cubeAngle = 0.0f;
        
        // this function is there because sometimes the texture won't already be loaded.
        // of course if you load it from a file you would just call backend.LoadTexture with RawImageData and you wont haave to do anything.
        private ITexture? TryLoadTexture(IAuraBackend backend) {
            // this does not just upload the texture to the backend, it actually uses the game's instance.
            // now you could cache this but the game might just want to get rid of it eventually.
            // so if you do make sure you refresh it when joining a new world to make sure it follows the current pack.
            return Onix.Render.AuraHelpers.GetTexture(backend, TexturePath.Game("textures/blocks/stone"));
        }

        private IDepthStencilBuffer GetUpdatedDepthBuffer(IAuraBackend backend) {
            using var backBuffer = backend.GetBackBufferTarget();
            // check if the size of the back buffer changed, if so resize accordingly.
            if (depthStencilBuffer is not null && (depthStencilBuffer.Width != backBuffer.Width || depthStencilBuffer.Height != backBuffer.Height)) {
                depthStencilBuffer.Dispose();
                depthStencilBuffer = null;
            }
            // create our depth stencil buffer with depth only if it doesn't exist.
            depthStencilBuffer ??= backend.CreateDepthStencilBuffer(new DepthStencilBufferDesc(backBuffer.Width, backBuffer.Height, AuraDepthStencilFormat.D32));
            return depthStencilBuffer;
        }

        public Tutorial05_TheThirdDimension(IAuraBackend backend) {
            rasterizerState = backend.CreateRasterizerState(new RasterizerStateDesc(CullMode.Back));
            blendState = backend.CreateBlendState(new BlendStateDesc(true)); 
            depthStencilState = backend.CreateDepthStencilState(new DepthStencilStateDesc(true, false)); // enable depth testing
            _ = GetUpdatedDepthBuffer(backend); // this will pre-create our depth stencil buffer.
            vertexBuffer = backend.CreateBuffer(new AuraBufferDesc((ulong)Marshal.SizeOf<MyVertex>() * 36, BufferBindUsage.VertexBuffer));
            constantBuffer = backend.CreateBuffer(new AuraBufferDesc((ulong)Marshal.SizeOf<MyConstantBuffer>(), BufferBindUsage.ConstantBuffer, true));
            nearestSampler = backend.CreateSampler(new SamplerDesc(AuraSamplerFilterType.MinMagMipPoint, AuraSamplerAddressMode.Mirror));

            shader = backend.CreateShaderProgram();
            if (shader.Compile(_shaderCode, false) is { } shaderCompileError) {
                Logger.GetForPlugin().Error("Error compiling shader: " + shaderCompileError);
                Console.WriteLine("Error compiling shader: " + shaderCompileError);
                return;
            }
            shader.SetRecommendedInputLayout(new ShaderInputLayoutDesc([
                new () {Type = AuraFormatType.Float3, SemanticName = "POSITION"}, // update to take a float 3
                new () {Type = AuraFormatType.NUByte4, SemanticName = "COLOR"},
                new () {Type = AuraFormatType.Float2, SemanticName = "TEXCOORD"},
            ]));

            // since we upload a struct[], we don't need to set the ElementSizeInBytes
            float size = 0.5f;
            
            var vertices = new MyVertex[] {
                // Front face (Z+)
                new MyVertex { position = new Vec3(-size,  size,  size), color=0xFFFFFFFF, uv = new Vec2(0.0f, 0.0f) },
                new MyVertex { position = new Vec3( size,  size,  size), color=0xFFFFFFFF, uv = new Vec2(1.0f, 0.0f) },
                new MyVertex { position = new Vec3(-size, -size,  size), color=0xFFFFFFFF, uv = new Vec2(0.0f, 1.0f) },
                new MyVertex { position = new Vec3(-size, -size,  size), color=0xFFFFFFFF, uv = new Vec2(0.0f, 1.0f) },
                new MyVertex { position = new Vec3( size,  size,  size), color=0xFFFFFFFF, uv = new Vec2(1.0f, 0.0f) },
                new MyVertex { position = new Vec3( size, -size,  size), color=0xFFFFFFFF, uv = new Vec2(1.0f, 1.0f) },

                // Back face (Z-)
                new MyVertex { position = new Vec3( size,  size, -size), color=0xFFFFFFFF, uv = new Vec2(0.0f, 0.0f) },
                new MyVertex { position = new Vec3(-size,  size, -size), color=0xFFFFFFFF, uv = new Vec2(1.0f, 0.0f) },
                new MyVertex { position = new Vec3( size, -size, -size), color=0xFFFFFFFF, uv = new Vec2(0.0f, 1.0f) },
                new MyVertex { position = new Vec3( size, -size, -size), color=0xFFFFFFFF, uv = new Vec2(0.0f, 1.0f) },
                new MyVertex { position = new Vec3(-size,  size, -size), color=0xFFFFFFFF, uv = new Vec2(1.0f, 0.0f) },
                new MyVertex { position = new Vec3(-size, -size, -size), color=0xFFFFFFFF, uv = new Vec2(1.0f, 1.0f) },

                // Left face (X-)
                new MyVertex { position = new Vec3(-size,  size, -size), color=0xFFFFFFFF, uv = new Vec2(0.0f, 0.0f) },
                new MyVertex { position = new Vec3(-size,  size,  size), color=0xFFFFFFFF, uv = new Vec2(1.0f, 0.0f) },
                new MyVertex { position = new Vec3(-size, -size, -size), color=0xFFFFFFFF, uv = new Vec2(0.0f, 1.0f) },
                new MyVertex { position = new Vec3(-size, -size, -size), color=0xFFFFFFFF, uv = new Vec2(0.0f, 1.0f) },
                new MyVertex { position = new Vec3(-size,  size,  size), color=0xFFFFFFFF, uv = new Vec2(1.0f, 0.0f) },
                new MyVertex { position = new Vec3(-size, -size,  size), color=0xFFFFFFFF, uv = new Vec2(1.0f, 1.0f) },

                // Right face (X+)
                new MyVertex { position = new Vec3( size,  size,  size), color=0xFFFFFFFF, uv = new Vec2(0.0f, 0.0f) },
                new MyVertex { position = new Vec3( size,  size, -size), color=0xFFFFFFFF, uv = new Vec2(1.0f, 0.0f) },
                new MyVertex { position = new Vec3( size, -size,  size), color=0xFFFFFFFF, uv = new Vec2(0.0f, 1.0f) },
                new MyVertex { position = new Vec3( size, -size,  size), color=0xFFFFFFFF, uv = new Vec2(0.0f, 1.0f) },
                new MyVertex { position = new Vec3( size,  size, -size), color=0xFFFFFFFF, uv = new Vec2(1.0f, 0.0f) },
                new MyVertex { position = new Vec3( size, -size, -size), color=0xFFFFFFFF, uv = new Vec2(1.0f, 1.0f) },

                // Top face (Y+)
                new MyVertex { position = new Vec3(-size,  size, -size), color=0xFFFFFFFF, uv = new Vec2(0.0f, 0.0f) },
                new MyVertex { position = new Vec3( size,  size, -size), color=0xFFFFFFFF, uv = new Vec2(1.0f, 0.0f) },
                new MyVertex { position = new Vec3(-size,  size,  size), color=0xFFFFFFFF, uv = new Vec2(0.0f, 1.0f) },
                new MyVertex { position = new Vec3(-size,  size,  size), color=0xFFFFFFFF, uv = new Vec2(0.0f, 1.0f) },
                new MyVertex { position = new Vec3( size,  size, -size), color=0xFFFFFFFF, uv = new Vec2(1.0f, 0.0f) },
                new MyVertex { position = new Vec3( size,  size,  size), color=0xFFFFFFFF, uv = new Vec2(1.0f, 1.0f) },

                // Bottom face (Y-)
                new MyVertex { position = new Vec3(-size, -size,  size), color=0xFFFFFFFF, uv = new Vec2(0.0f, 0.0f) },
                new MyVertex { position = new Vec3( size, -size,  size), color=0xFFFFFFFF, uv = new Vec2(1.0f, 0.0f) },
                new MyVertex { position = new Vec3(-size, -size, -size), color=0xFFFFFFFF, uv = new Vec2(0.0f, 1.0f) },
                new MyVertex { position = new Vec3(-size, -size, -size), color=0xFFFFFFFF, uv = new Vec2(0.0f, 1.0f) },
                new MyVertex { position = new Vec3( size, -size,  size), color=0xFFFFFFFF, uv = new Vec2(1.0f, 0.0f) },
                new MyVertex { position = new Vec3( size, -size, -size), color=0xFFFFFFFF, uv = new Vec2(1.0f, 1.0f) },
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
            // clear & bind our depth buffer, we're using one because otherwise it will render in the order the triangles are submitted instead of respecting depth.
            var depthBuffer = GetUpdatedDepthBuffer(backend);
            depthBuffer.ClearDepth(1.0f); // Clear our depth to be as far as possible.
            backend.BindDepthStencilBuffer(depthBuffer);
            backend.BindShader(shader, true);
            
            backend.BindSampler(nearestSampler, 0);
            backend.BindTexture(texture, 0);
            float rotationSpeed = 180.0f; // degrees per second
            cubeAngle += rotationSpeed * deltaTime; // accumulate each frame
            
            // set up how the view and projection should be. setting up camera position and perspective.
            TransformationMatrix myView = TransformationMatrix.LookAt(new Vec3(0, 3, -5), new Vec3(0, 0, 0), new Vec3(0, 1, 0));
            TransformationMatrix myProj = TransformationMatrix.PerspectiveFov(80, backBuffer.Widthf, backBuffer.Heightf, 0.025f, 2500f);
            TransformationMatrix finalMatrix =
                TransformationMatrix.RotateY(cubeAngle) *
                myView *
                myProj *
                TransformationMatrix.Identity();
            
            //finalMatrix =
            //    TransformationMatrix.RotateY(cubeAngle) * // and then here goes our model matrix to transform the model, here we rotate it
            //    TransformationMatrix.TranslateWorldPosition(Onix.Render.AuraHelpers.WorldOrigin, new Vec3(0.5f, 5.5f, 0.5f)) * // place it in the world
            //    Onix.Render.AuraHelpers.WorldViewMatrix *
            //    Onix.Render.AuraHelpers.WorldProjectionMatrix * 
            //    TransformationMatrix.Identity();

            
            // Constant buffer!
            MyConstantBuffer constantBufferData = new MyConstantBuffer() {
                // convert our final matrix to an aura matrix.
                mat = new (finalMatrix),
                // and send the time over for the color pulse
                time = (float)stopwatch.Elapsed.TotalSeconds
            };
            constantBuffer.Upload(constantBufferData);
            backend.BindConstantBuffer(constantBuffer, 0);
            // send it!
            // you should see a cube rotate
            backend.Draw(vertexBuffer.ElementCount);
        }
        
        public override void Dispose() {
            // don't forget to dispose guys, I know its tempting to be lazy, but then you get issues and weird leaks.
            rasterizerState.Dispose();
            blendState.Dispose();
            depthStencilState.Dispose();
            depthStencilBuffer?.Dispose();
            vertexBuffer.Dispose();
            constantBuffer.Dispose();
            shader.Dispose();
            nearestSampler.Dispose();
        }
        
    }
}
