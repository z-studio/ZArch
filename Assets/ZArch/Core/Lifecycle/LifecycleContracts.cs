using System.Threading;
using System.Threading.Tasks;

namespace ZArch {
    public interface IInitializable {
        void Initialize();
    }

    public interface IAsyncInitializable {
        Task InitializeAsync(CancellationToken cancellationToken);
    }

    public interface IDeinitializable {
        void Deinitialize();
    }
}
