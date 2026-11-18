// Isolated Unity editor regression fixture. Never copied into a player build.
using System;
namespace WeChatWASM
{
    public sealed class GeneralCallbackResult { }
    public sealed class OnShowListenerResult { }
    public static class WX
    {
        public static int InitCalls;
        public static bool ThrowOnHide;
        public static Action<int> InitCallback;
        private static event Action<OnShowListenerResult> show;
        private static event Action<GeneralCallbackResult> hide;
        public static int ShowCount => show == null ? 0 : show.GetInvocationList().Length;
        public static int HideCount => hide == null ? 0 : hide.GetInvocationList().Length;
        public static void Reset() { InitCalls = 0; ThrowOnHide = false; InitCallback = null; show = null; hide = null; }
        public static void InitSDK(Action<int> callback) { InitCalls++; InitCallback = callback; }
        public static void OnShow(Action<OnShowListenerResult> callback) { show += callback; }
        public static void OffShow(Action<OnShowListenerResult> callback) { show -= callback; }
        public static void OnHide(Action<GeneralCallbackResult> callback) { if (ThrowOnHide) throw new InvalidOperationException("fixture"); hide += callback; }
        public static void OffHide(Action<GeneralCallbackResult> callback) { hide -= callback; }
        public static void Complete(int code) => InitCallback?.Invoke(code);
        public static void Show() => show?.Invoke(new OnShowListenerResult());
        public static void Hide() => hide?.Invoke(new GeneralCallbackResult());
    }
}
