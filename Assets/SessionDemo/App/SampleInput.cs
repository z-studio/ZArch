using UnityEngine;
using UnityEngine.EventSystems;

namespace Demo.App {
    public sealed class SampleInput : MonoBehaviour {
        private void Start() {
            if (EventSystem.current) {
                return;
            }

            gameObject.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            var input = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");

            if (input != null) {
                gameObject.AddComponent(input);
                return;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            gameObject.AddComponent<StandaloneInputModule>();
#else
            Debug.LogError("Enable Input System or the legacy Input Manager to interact with the demo.", this);
#endif
        }
    }
}