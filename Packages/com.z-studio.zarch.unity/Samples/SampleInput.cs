using UnityEngine;
using UnityEngine.EventSystems;

namespace ZArch.Unity.Samples {
    // 样例只序列化本组件，避免把项目特有的 Input System 引用带入其他项目。
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
            Debug.LogError("ZArch sample: install Input System or enable the legacy Input Manager in Player Settings.",
                this);
#endif
        }
    }
}