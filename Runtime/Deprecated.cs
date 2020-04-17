using System;

namespace Unity.DataFlowGraph
{
    [Obsolete("Use ComponentNode + connections instead", true)]
    public interface INodeMemoryInputTag { }

    [Obsolete("Use ComponentNode + connections instead", true)]
    public struct NodeMemoryInput<TTag, TBufferToMove> { }

    [Obsolete("", true)]
    public class NativeAllowReinterpretationAttribute : Attribute { }

    [Obsolete("Use ComponentNode instead", true)]
    public abstract class MemoryInputSystem<TTag, TBufferToMove> { }

}
