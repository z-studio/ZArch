using System.Collections.Generic;

namespace ZArch {
    public interface IUnregister {
        void Unregister();
    }

    public interface IUnregisterList {
        List<IUnregister> UnregisterList { get; }
    }

}
