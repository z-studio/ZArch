using UnityEngine;

namespace ZArch.Unity {
    internal static class UnityRuntimeIntegration {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterBindableComparers() {
            BindableProperty<int>.Comparer = (a, b) => a == b;
            BindableProperty<float>.Comparer = Mathf.Approximately;
            BindableProperty<double>.Comparer = (a, b) => a.Equals(b);
            BindableProperty<string>.Comparer = (a, b) => a == b;
            BindableProperty<bool>.Comparer = (a, b) => a == b;
            BindableProperty<Vector2>.Comparer = (a, b) => a == b;
            BindableProperty<Vector3>.Comparer = (a, b) => a == b;
            BindableProperty<Vector4>.Comparer = (a, b) => a == b;
            BindableProperty<Color>.Comparer = (a, b) => a == b;
            BindableProperty<Color32>.Comparer = (a, b) => a.Equals(b);
            BindableProperty<Bounds>.Comparer = (a, b) => a == b;
            BindableProperty<Rect>.Comparer = (a, b) => a == b;
            BindableProperty<Quaternion>.Comparer = (a, b) => a == b;
            BindableProperty<Vector2Int>.Comparer = (a, b) => a == b;
            BindableProperty<Vector3Int>.Comparer = (a, b) => a == b;
            BindableProperty<BoundsInt>.Comparer = (a, b) => a == b;
            BindableProperty<RangeInt>.Comparer = (a, b) => a.Equals(b);
            BindableProperty<RectInt>.Comparer = (a, b) => a.Equals(b);
        }
    }
}