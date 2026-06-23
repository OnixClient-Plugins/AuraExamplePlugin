using OnixRuntime.Api.Aura;
using OnixRuntime.Api.Maths;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace AuraExamplesPlugin.Examples {
    internal class Tutorial01_HelloTriangle : AuraExampleBase {

        /// This is our vertex layout, we send a position and color for every vertex right now.
        [StructLayout(LayoutKind.Sequential)]
        struct MyVertex {
            public Vec2 position;
            public uint color;
        }

        // The code of our vertex and pixel shaders, basically just passing it along right now. Of course you can get this shader code however you want.
        // For the sake of this example though, we hardcode it.
        private string _shaderCode = """
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

        // various states and device objects to do rendering with.
        // You can explore the options from each's descriptions.
        private IRasterizerState rasterizerState;
        private IBlendState blendState;
        private IDepthStencilState depthStencilState;
        private IAuraBuffer vertexBuffer;
        private IShaderProgram shader;

        public Tutorial01_HelloTriangle(IAuraBackend backend) {
            // feel free to inspect the description to explore possibilities!
            rasterizerState = backend.CreateRasterizerState(new RasterizerStateDesc(CullMode.Back));
            blendState = backend.CreateBlendState(new BlendStateDesc(false));
            depthStencilState = backend.CreateDepthStencilState(new DepthStencilStateDesc(false, false));
            vertexBuffer = backend.CreateBuffer(new AuraBufferDesc((ulong)Marshal.SizeOf<MyVertex>() * 3, BufferBindUsage.VertexBuffer));

            shader = backend.CreateShaderProgram();
            if (shader.Compile(_shaderCode, false) is { } shaderCompileError) {
                Console.WriteLine("Error compiling shader: " + shaderCompileError);
                return;
            }
            shader.SetRecommendedInputLayout(new ShaderInputLayoutDesc([
                new () {Type = AuraFormatType.Float2, SemanticName = "POSITION"},
                new () {Type = AuraFormatType.NUByte4, SemanticName = "COLOR"},
            ]));

            // since we upload a struct[], we don't need to set the ElementSizeInBytes
            var vertices = new MyVertex[] {
                new MyVertex { position = new Vec2(-0.5f, -0.5f), color = 0xFF0000FF }, // Red
                new MyVertex { position = new Vec2(0.0f, 0.5f),   color = 0xFF00FF00 }, // Green
                new MyVertex { position = new Vec2(0.5f, -0.5f),  color = 0xFFFF0000 }, // Blue
            };
            vertexBuffer.Upload(vertices);
        }
        public override void Render(IAuraBackend backend, float deltaTime) {
            // never forget to dispose any reference to the back buffer target, otherwise you get a straight crash.
            using var backBuffer = backend.GetBackBufferTarget();
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
        }
        
    }
}
