using System;
using System.Diagnostics;

namespace Unity.DataFlowGraph
{
    partial class RenderGraph : IDisposable
    {
        class KernelNodeDebugView
        {
            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public object DebugInfo;

            struct FullDebugInfo
            {
                [DebuggerBrowsable(DebuggerBrowsableState.Never)]
                public KernelLayout.Pointers Instance;
                public NodeDefinition Definition;
                public LowLevelNodeTraits Traits;
                public IGraphKernel Kernel => Definition?.BaseTraits.DebugGetKernel(Instance);
                public IKernelData Data => Definition?.BaseTraits.DebugGetKernelData(Instance);
                public IKernelPortDefinition Ports => Definition?.BaseTraits.DebugGetKernelPorts(Instance);
            }

            public static string DebugDisplay(KernelNode node) =>
                $"{node.Handle.ToPublicHandle().DebugDisplay()}, Node: {GetNodeDefinition(node)?.GetType().Name ?? "<INVALID>"}";

            public KernelNodeDebugView(KernelNode node)
            {
                DebugInfo = GetDebugInfo(node);
            }

            static object GetDebugInfo(KernelNode node)
            {
                var def = GetNodeDefinition(node);

                if (def != null)
                {
                    return new FullDebugInfo
                    {
                        Instance = node.Instance,
                        Definition = def,
                        Traits = node.TraitsHandle.Resolve()
                    };
                }
                else
                {
                    return node.Handle;
                }
            }

            static NodeDefinition GetNodeDefinition(KernelNode node)
            {
                var set = DataFlowGraph.DebugInfo.DebugGetNodeSet(node.Handle.Versioned.ContainerID);
                return set == null || !set.DataGraph.StillExists(node.Handle) ? null : set.GetDefinitionInternal(node.Handle);
            }
        }
    }
}
