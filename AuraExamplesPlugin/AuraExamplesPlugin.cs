using AuraExamplesPlugin.Examples;
using OnixRuntime.Api;
using OnixRuntime.Api.Aura;
using OnixRuntime.Plugin;
using OnixRuntime.Api.Rendering;
using OnixRuntime.Api.Inputs;

namespace AuraExamplesPlugin {
    public class AuraExamplesPlugin : OnixPluginBase {
        public static AuraExamplesPlugin Instance { get; private set; } = null!;
        public static AuraExamplesPluginConfig Config { get; private set; } = null!;

        public AuraExamplesPlugin(OnixPluginInitInfo initInfo) : base(initInfo) {
            Instance = this;
            // If you can clean up what the plugin leaves behind manually, please do not unload the plugin when disabling.
            base.DisablingShouldUnloadPlugin = false;
#if DEBUG
           // base.WaitForDebuggerToBeAttached();
#endif
        }

        protected override void OnLoaded() {
            Console.WriteLine($"Plugin {CurrentPluginManifest.Name} loaded!");
            Config = new AuraExamplesPluginConfig(PluginDisplayModule, true);
            Onix.Events.Common.Tick += OnTick;
            Onix.Events.Common.HudRenderGame += OnHudRenderGame;
            Onix.Events.Common.WorldRender += OnWorldRender;
            Onix.Events.Common.HudInput += OnHudInput;
            Onix.Events.Rendering.LowLevelAuraRender += RenderingOnLowLevelAuraRender;
            Onix.Events.Rendering.AuraDeviceLost += RenderingOnAuraDeviceLost;
        }


        private AuraExampleBase? currentExample = null;
        private void RenderingOnLowLevelAuraRender(IAuraBackend backend, float deltaTime, string screenName, bool isHudHidden, bool isClientUi) {
            // switch to any example here!!
            currentExample ??= new Tutorial01_HelloTriangle(backend);
            currentExample.Render(backend, deltaTime);
        }
        // This is VERY important. You'll likely crash at the first resize otherwise.
        private void RenderingOnAuraDeviceLost(IAuraBackend oldBackend) {
            OnDisabled();
        }

        private CancellationTokenSource auraWindowCancellation = new();
        protected override void OnEnabled() {
            //AuraInItsOwnWindow.StartWindow(auraWindowCancellation.Token);
        }

        protected override void OnDisabled() {
            currentExample?.Dispose();
            currentExample = null;
            auraWindowCancellation.Cancel();
        }

        protected override void OnUnloaded() {
            // Ensure every task or thread is stopped when this function returns.
            // You can give them base.PluginEjectionCancellationToken which will be cancelled when this function returns. 
            Console.WriteLine($"Plugin {CurrentPluginManifest.Name} unloaded!");
            OnDisabled();
            Onix.Events.Common.Tick -= OnTick;
            Onix.Events.Common.HudRenderGame -= OnHudRenderGame;
            Onix.Events.Common.WorldRender -= OnWorldRender;
            Onix.Events.Common.HudInput -= OnHudInput;
            Onix.Events.Rendering.LowLevelAuraRender -= RenderingOnLowLevelAuraRender;
            Onix.Events.Rendering.AuraDeviceLost -= RenderingOnAuraDeviceLost;
        }

        private void OnTick() {
        }

        private void OnHudRenderGame(RendererGame gfx, float delta) {
        }

        private void OnWorldRender(RendererWorld gfx, float delta) {
            currentExample?.OnWorldRender(gfx, delta);
        }

        private bool OnHudInput(InputKey key, bool isDown) {
            return false;
        }
    }
}
