// Parsing tests hold no shared state, so classes may run concurrently.
[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]
