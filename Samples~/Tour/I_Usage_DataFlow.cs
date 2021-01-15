using UnityEngine;

namespace Unity.DataFlowGraph.Tour
{
    public class I_Usage_DataFlow : MonoBehaviour
    {
        /*
         * In this example, we'll explore what "data flow" is, and how we can use it.
         * 
         * Like shown in message flow example, data can flow as well. This is done in a similar fashion:
         * 1) You have a node with data I/O ports
         * 2) It implements a graph kernel reading some ports and writing to some ports
         * 3) You connect a bunch of these nodes together.
         * 
         * Concretely, we'll connect some nodes containing data ports together, in a very similar way as to how we did
         * it in the messaging example.
         */
        public class MyNode : KernelNodeDefinition<MyNode.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                /*
                 * Here we'll set up our data contract like for simulation, shown earlier.
                 * In this case, we declare two inputs, and one output.
                 */
                public DataInput<MyNode, float> InputA, InputB;
                public DataOutput<MyNode, float> Output;
            }

            struct KernelData : IKernelData { }

            struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
            {
                public void Execute(RenderContext ctx, in KernelData data, ref KernelDefs ports)
                {
                    /*
                     * Here we can see the syntax for using the rendering context to resolve data I/O ports into usable
                     * memory.
                     */
                    var inputA = ctx.Resolve(ports.InputA);
                    var inputB = ctx.Resolve(ports.InputB);

                    /*
                     * Notice the added "ref"s here. A reference to the type is returned for any outputs, so you can 
                     * write results back to it, like shown below.
                     */
                    ref var output = ref ctx.Resolve(ref ports.Output);

                    /*
                     * Mix the two inputs together, and write them to our output port.
                     * Add an offset as well.
                     */
                    output = inputA + inputB + 10;

                    Debug.Log($"My output was {output}");
                }
            }
        }

        void Start()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<MyNode>
                    a = set.Create<MyNode>(),
                    b = set.Create<MyNode>(),
                    c = set.Create<MyNode>(),
                    d = set.Create<MyNode>(),
                    e = set.Create<MyNode>(),
                    f = set.Create<MyNode>(),
                    g = set.Create<MyNode>();

                /*
                 * Form a binary mixing tree.
                 * 
                 * a
                 *  \
                 *   e
                 *  / \
                 * b   \
                 *      g
                 * c   /
                 *  \ /
                 *   f
                 *  /
                 * d
                 * 
                 */

                set.Connect(a, MyNode.KernelPorts.Output, e, MyNode.KernelPorts.InputA);
                set.Connect(b, MyNode.KernelPorts.Output, e, MyNode.KernelPorts.InputB);

                set.Connect(c, MyNode.KernelPorts.Output, f, MyNode.KernelPorts.InputA);
                set.Connect(d, MyNode.KernelPorts.Output, f, MyNode.KernelPorts.InputB);

                set.Connect(e, MyNode.KernelPorts.Output, g, MyNode.KernelPorts.InputA);
                set.Connect(f, MyNode.KernelPorts.Output, g, MyNode.KernelPorts.InputB);

                set.Update();

                set.Destroy(a, b, c, d, e, f, g);

            }

        }

    }
}