using System;
namespace Unity.DataFlowGraph
{
    struct PureVirtualFunction : IGraphNodeExecutor
    {
        public void Execute()
        {
            throw new InvalidOperationException("Pure virtual function called. This is an internal bug.");
        }

        public static IntPtr GetReflectionData()
        {
            return IGraphNodeExecutorExtensions.JobStruct<PureVirtualFunction>.JobReflectionData;
        }
    }
}
