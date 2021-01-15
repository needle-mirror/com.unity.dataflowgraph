using UnityEngine;

namespace Unity.DataFlowGraph.Tour
{
    public class N_Feature_PortArrays : MonoBehaviour
    {
        /*
         * In this example, we'll explore what "port arrays" are, and how to use them.
         * 
         * Port arrays allows you to create a dynamic amount of inputs to one "port" ID.
         * This is typically used for data mixers (animation / audio) that can take any amount of inputs, and produce
         * a fixed amount of outputs.
         * Insertion order is stable as well, useful for depth sorting in compositors or similar.
         * 
         * Specifically, you can set the size of a port array and make a connection on a specific index. It works both
         * in simulation and rendering, and there's no restriction on contents of the port array - e.g. it can be 
         * complex aggregates, like shown in the previous example. All APIs that include an input NodeHandle and InputPortID pair have an overload with
         * an additional index argument.
         * 
         * Let's create a node with a port array and explore the syntax and APIs needed.
         */
        public class MyNode : SimulationKernelNodeDefinition<MyNode.SimPorts, MyNode.KernelDefs>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public PortArray<MessageInput<MyNode, Vector3>> Input;
            }

            public struct KernelDefs : IKernelPortDefinition
            {
                public PortArray<DataInput<MyNode, Vector3>> Input;
            }

            struct KernelData : IKernelData { }

            struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
            {
                public void Execute(RenderContext ctx, in KernelData data, ref KernelDefs ports)
                {
                    /*
                     * To iterate over the inputs contained in a port array, it must be resolved through the context.
                     * This returns a proxy object (ResolvedPortArray), which can be index to get the contents of a given
                     * port "index".
                     */
                    var portArray = ctx.Resolve(ports.Input);

                    for(int i = 0; i < portArray.Length; ++i)
                    {
                        var vector = portArray[i];
                        Debug.Log($"The value of vector on array index {i} is: {vector}");
                    }
                }
            }

            struct NodeHandlers : INodeData, IMsgHandler<Vector3>
            {
                public void HandleMessage(MessageContext ctx, in Vector3 msg)
                {
                    if (ctx.Port == SimulationPorts.Input)
                    {
                        Debug.Log($"Got a Vector3 on input port array, on index {ctx.ArrayIndex} with value {msg}");
                    }
                }
            }
        }

        void Start()
        {
            using (var set = new NodeSet())
            {
                /*
                 * First we'll create some distinct values for each index.
                 */
                var vectors = new Vector3[5];
                for (int i = 0; i < 5; ++i)
                    vectors[i] = Quaternion.Euler(i, 0, 0) * Vector3.up * i;

                var node = set.Create<MyNode>();
                
                /*
                 * To set the size of a port array, simply use the SetPortArraySize()
                 * API.
                 */
                set.SetPortArraySize(node, MyNode.KernelPorts.Input, (ushort)vectors.Length);
                set.SetPortArraySize(node, MyNode.SimulationPorts.Input, (ushort)vectors.Length);

                /*
                 * Now you can make connections to specific indices on those port arrays as mentioned.
                 * In this example, we'll just send a message directly, and set some data on the kernel ports to
                 * illustrate the principle.
                 */
                for (ushort portArrayIndex = 0; portArrayIndex < 5; ++portArrayIndex)
                    set.SetData(node, MyNode.KernelPorts.Input, portArrayIndex, vectors[portArrayIndex]);

                for (ushort portArrayIndex = 0; portArrayIndex < 5; ++portArrayIndex)
                    set.SendMessage(node, MyNode.SimulationPorts.Input, portArrayIndex, vectors[portArrayIndex]);

                set.Update();

                set.Destroy(node);
            }
        }
    }
}
