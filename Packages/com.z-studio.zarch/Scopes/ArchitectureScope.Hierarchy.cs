using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZArch {
    public sealed partial class ArchitectureScope {
        public ArchitectureScope CreateChild(string name, Action<ArchitectureScope> setup, object tag = null) =>
            Architecture.CreateChildScope(this, name, setup, tag);

        public Task<ArchitectureScope> CreateChildAsync(
            string name,
            Func<ArchitectureScope, Task> setup,
            object tag = null
        ) =>
            Architecture.CreateChildScopeAsync(this, name, setup, tag);

        public Task<ArchitectureScope> CreateChildAsync(
            string name,
            Func<ArchitectureScope, CancellationToken, Task> setup,
            object tag = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default
        ) =>
            Architecture.CreateChildScopeAsync(this, name, setup, tag, timeout, cancellationToken);
    }
}
