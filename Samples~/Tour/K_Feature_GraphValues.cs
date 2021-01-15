using Unity.Burst;
using UnityEngine;

namespace Unity.DataFlowGraph.Tour
{
    public class K_Feature_GraphValues : MonoBehaviour
    {
        /*
         * In this example, we'll explore what "graph values" are, and how to use them.
         * 
         * For data flow processing to be useful, usually you would want to know the "output" or "result" of the 
         * rendering.
         * 
         * To solve this problem, data flow graphs have GraphValue<T> which can be used to read back the value of an
         * output data port at any point in the graph. They also serve to tell the node set that "someone is interested
         * in the state of the graph at this point" which is good optimization / culling information.
         * 
         * Graph values need to be created ahead of time pointing at a {node, output port} pair. After this, you can use a graph 
         * value to read back the state in a blocking fashion, or better, you can schedule a dependent job using a 
         * GraphValueResolver.
         * 
         * Concretely, we'll look at reading back a computed value from a rendering each frame in this example.
         * To do this, we'll reuse the binary mixer tree starred in I_Usage_DataFlow example.
         */
        public class MyNode : KernelNodeDefinition<MyNode.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<MyNode, float> InputA, InputB;
                public DataOutput<MyNode, float> Output;
            }

            struct KernelData : IKernelData { }

            /*
             * Since we'll communicate entirely through data, we can actually run entirely in Burst, so let's do that.
             */
            [BurstCompile]
            struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
            {
                public void Execute(RenderContext ctx, in KernelData data, ref KernelDefs ports)
                {
                    var inputA = ctx.Resolve(ports.InputA);
                    var inputB = ctx.Resolve(ports.InputB);

                    ref var output = ref ctx.Resolve(ref ports.Output);

                    output = inputA + inputB;
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

                set.Connect(a, MyNode.KernelPorts.Output, e, MyNode.KernelPorts.InputA);
                set.Connect(b, MyNode.KernelPorts.Output, e, MyNode.KernelPorts.InputB);

                set.Connect(c, MyNode.KernelPorts.Output, f, MyNode.KernelPorts.InputA);
                set.Connect(d, MyNode.KernelPorts.Output, f, MyNode.KernelPorts.InputB);

                set.Connect(e, MyNode.KernelPorts.Output, g, MyNode.KernelPorts.InputA);
                set.Connect(f, MyNode.KernelPorts.Output, g, MyNode.KernelPorts.InputB);

                foreach (var leaf in new[] { a, b, c, d })
                {
                    set.SetData(leaf, MyNode.KernelPorts.InputA, 10);
                    set.SetData(leaf, MyNode.KernelPorts.InputB, 10);
                }

                /*
                 * Recall the graph layout again:
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
                 * If the leaves start with values of 10, the analytical output of any N-depth binary tree of this form
                 * is 10 * 2^N: We'll check at points e & f (N == 2) and g (N == 3).
                 * 
                 * To read values at these points, we create graph values.
                 * Graph values need to be created one "update" before you need them.
                 */

                var valueAtE = set.CreateGraphValue(e, MyNode.KernelPorts.Output);
                var valueAtF = set.CreateGraphValue(f, MyNode.KernelPorts.Output);
                var valueAtG = set.CreateGraphValue(g, MyNode.KernelPorts.Output);

                set.Update();

                /* 
                * So e & f should have a value of 10 * 2^2 == 40, and g 10 * 2^3 == 80.
                * 
                * To read the value back after an update, you can use the GetValueBlocking() API which will ensure any
                * related rendering is done before returning. 
                * 
                * Note that it would generally be a better idea to access this information without blocking through the use of a job (see GraphValueResolver documentation).
                */
                Debug.Log($"Values at e & f should be 40, " +
                    $"e is actually: {set.GetValueBlocking(valueAtE)}, " +
                    $"f is actually: {set.GetValueBlocking(valueAtF)}"
                );

                Debug.Log($"Value at g should be 80, it is actually: {set.GetValueBlocking(valueAtG)}");

                /*
                 * Graph values needs to be cleaned up as well.
                 */
                set.ReleaseGraphValue(valueAtE); set.ReleaseGraphValue(valueAtF); set.ReleaseGraphValue(valueAtG);

                set.Destroy(a, b, c, d, e, f, g);
            }

        }

    }
}
