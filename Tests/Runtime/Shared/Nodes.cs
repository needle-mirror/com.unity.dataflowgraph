using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Burst;

namespace Unity.DataFlowGraph.Tests
{
    public interface TestDSL { }

    public struct Node : INodeData { }

    public struct Data : IKernelData { }

    public class DSL : DSLHandler<TestDSL>
    {
        protected override void Connect(ConnectionInfo left, ConnectionInfo right) { }
        protected override void Disconnect(ConnectionInfo left, ConnectionInfo right) { }
    }

    public class NodeWithAllTypesOfPorts
        : NodeDefinition<Node, NodeWithAllTypesOfPorts.SimPorts, Data, NodeWithAllTypesOfPorts.KernelDefs, NodeWithAllTypesOfPorts.Kernel>
            , TestDSL
            , IMsgHandler<int>
    {
        public struct SimPorts : ISimulationPortDefinition
        {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
            public MessageInput<NodeWithAllTypesOfPorts, int> MessageIn;
            public PortArray<MessageInput<NodeWithAllTypesOfPorts, int>> MessageArrayIn;
            public MessageOutput<NodeWithAllTypesOfPorts, int> MessageOut;
            public DSLInput<NodeWithAllTypesOfPorts, DSL, TestDSL> DSLIn;
            public DSLOutput<NodeWithAllTypesOfPorts, DSL, TestDSL> DSLOut;
#pragma warning restore 649
        }

        public struct KernelDefs : IKernelPortDefinition
        {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
            public DataInput<NodeWithAllTypesOfPorts, Buffer<int>> InputBuffer;
            public PortArray<DataInput<NodeWithAllTypesOfPorts, Buffer<int>>> InputArrayBuffer;
            public DataOutput<NodeWithAllTypesOfPorts, Buffer<int>> OutputBuffer;
            public DataInput<NodeWithAllTypesOfPorts, int> InputScalar;
            public PortArray<DataInput<NodeWithAllTypesOfPorts, int>> InputArrayScalar;
            public DataOutput<NodeWithAllTypesOfPorts, int> OutputScalar;
#pragma warning restore 649
        }

        [BurstCompile(CompileSynchronously = true)]
        public struct Kernel : IGraphKernel<Data, KernelDefs>
        {
            public void Execute(RenderContext context, Data data, ref KernelDefs ports) { }
        }
        public void HandleMessage(in MessageContext ctx, in int msg) { }
    }

}
