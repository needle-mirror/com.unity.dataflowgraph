using UnityEngine;

namespace Unity.DataFlowGraph.Tour
{
    public class J_Feature_SetData : MonoBehaviour
    {
        /*
         * In this example, we'll explore what "set data" is, and how we can use it.
         * 
         * Expanding on the previous example, you'll notice that the binary mixing tree starts out 0 values at the 
         * first nodes being executed. This is because unconnected inputs in the data flow graph always have a default
         * value (being 0 for floats).
         * 
         * There's an API for updating the value of an unconnected input called SetData() on the NodeSet.
         * Concretely, we'll use it for setting initial values on the leaves of the graph. 
         */
        public class MyNode : KernelNodeDefinition<MyNode.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<MyNode, float> InputA, InputB;
                public DataOutput<MyNode, float> Output;
            }

            struct KernelData : IKernelData { }

            struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
            {
                public void Execute(RenderContext ctx, in KernelData data, ref KernelDefs ports)
                {
                    var inputA = ctx.Resolve(ports.InputA);
                    var inputB = ctx.Resolve(ports.InputB);

                    ref var output = ref ctx.Resolve(ref ports.Output);

                    /*
                     * Notice we removed the offset here.
                     */
                    output = inputA + inputB;
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

                /*
                 * Updating the data on the leaf inputs allows should change the output in the log.
                 * Previously the graph kernel would add an offset, otherwise everything in the data flow
                 * would be zero.
                 * We'll look at verifying the correct values in the next installment.
                 */
                float inputValue = 1.0f;
                foreach(var leaf in new []{a, b, c, d})
                {
                    set.SetData(leaf, MyNode.KernelPorts.InputA, inputValue *= 2.0f);
                    set.SetData(leaf, MyNode.KernelPorts.InputB, inputValue *= 2.0f);
                }

                set.Update();

                set.Destroy(a, b, c, d, e, f, g);
            }

        }
    }

    
}
