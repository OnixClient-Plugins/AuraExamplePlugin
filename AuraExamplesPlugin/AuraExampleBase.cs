using OnixRuntime.Api.Aura;
using OnixRuntime.Api.NBT;
using System;
using System.Collections.Generic;
using System.Text;

namespace AuraExamplesPlugin {
    public abstract class AuraExampleBase : IDisposable {
        
        public abstract void Render(IAuraBackend backend, float deltaTime);
        public abstract void Dispose();
    }
}
