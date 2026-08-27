using System;

namespace ZArch {
    public static class ScopeOperationExtension {
        public static void SendCommand<T>(this ArchitectureScope scope, T command) where T : ICommand {
            if (scope == null) {
                throw new ArgumentNullException(nameof(scope));
            }

            if (command == null) {
                throw new ArgumentNullException(nameof(command));
            }

            command.SetScope(scope);
            command.Execute();
        }

        public static TResult SendCommand<TResult>(
            this ArchitectureScope scope,
            ICommand<TResult> command
        ) {
            if (scope == null) {
                throw new ArgumentNullException(nameof(scope));
            }

            if (command == null) {
                throw new ArgumentNullException(nameof(command));
            }

            command.SetScope(scope);
            return command.Execute();
        }

        public static TResult SendQuery<TResult>(this ArchitectureScope scope, IQuery<TResult> query) {
            if (scope == null) {
                throw new ArgumentNullException(nameof(scope));
            }

            if (query == null) {
                throw new ArgumentNullException(nameof(query));
            }

            query.SetScope(scope);
            return query.Execute();
        }
    }
}
