using System;

namespace Unity.DataFlowGraph
{
    struct PureVirtualFunction : IGraphNodeExecutor
    {
        public void Execute()
        {
            throw new InternalException("Pure virtual function called.");
        }

        public static IntPtr GetReflectionData()
        {
            return IGraphNodeExecutorExtensions.JobStruct<PureVirtualFunction>.JobReflectionData;
        }
    }
}
