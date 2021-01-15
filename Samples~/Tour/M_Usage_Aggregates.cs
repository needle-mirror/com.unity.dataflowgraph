using Unity.Mathematics;
using UnityEngine;

namespace Unity.DataFlowGraph.Tour
{
    public class M_Usage_Aggregates : MonoBehaviour
    {
        /*
         * In this example, we'll explore what "aggregates" are, and how to use them.
         * 
         * Aggregates are actually just non-scalar data types in ports, or an aggregation of any combination of types,
         * like standard types like matrices / vectors, or structures with buffers and other aggregates.
         * 
         * In general, ports of aggregate types are treated the same as non-aggregate ports. There is
         * however a special syntax used for aggregates which include buffers.
         * 
         * Let's look at a concrete example of the syntax with a more complicated port layout.
         */
        public struct Aggregate
        {
            public Matrix4x4 SomeMatrix;
            public bool SomeBoolean;
            public Buffer<long> AnArray;
            public float3 AVector;
            public Buffer<long> SomeOtherArray;
        }

        public class MyNode : KernelNodeDefinition<MyNode.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                /*
                 * Again, just stuff it in a port and it will work.
                 */
                public DataInput<MyNode, Aggregate> Input;
                public DataOutput<MyNode, Aggregate> Output;
            }

            struct KernelData : IKernelData { }

            struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
            {
                public void Execute(RenderContext ctx, in KernelData data, ref KernelDefs ports)
                {
                    /*
                     * Here we resolve these ports as usual.
                     */
                    var input = ctx.Resolve(ports.Input);
                    ref var output = ref ctx.Resolve(ref ports.Output);

                    /*
                     * Everything except buffers works as expected.
                     */
                    output.AVector = input.AVector;

                    /*
                     * However, nested buffers inside an aggregate require an additional "resolve" step. The API for this is 
                     * on the buffer itself, and is called .ToNative(). Again, this just transforms the buffer to a
                     * NativeArray.
                     */
                    var myInputArray = input.AnArray.ToNative(ctx);
                    var anArray = output.AnArray.ToNative(ctx);
                    var someOtherArray = output.SomeOtherArray.ToNative(ctx);

                    Debug.Log($"Output.AnArray.Length: {anArray.Length}");
                    Debug.Log($"Output.SomeOtherArray.Length: {someOtherArray.Length}");
                }
            }
        }

        void Start()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<MyNode>();

                /*
                 * To update the size of a nested buffer, we create a local copy of the port type, and assign 
                 * SizeRequests() to the specific fields we want to update.
                 * 
                 * Only buffer fields that are explicitly assigned will be updated, anything else is ignored.
                 * We should be able to see that in the console, when running this example.
                 */
                Aggregate aggr = default;
                aggr.AnArray = Buffer<long>.SizeRequest(11);
                set.SetBufferSize(node, MyNode.KernelPorts.Output, aggr);
                set.Update();

                aggr = default;
                aggr.SomeOtherArray = Buffer<long>.SizeRequest(13);
                set.SetBufferSize(node, MyNode.KernelPorts.Output, aggr);
                set.Update();

                /*
                 * To reset a buffer, set its size to zero explicitly.
                 */
                aggr = default;
                aggr.AnArray = Buffer<long>.SizeRequest(0);
                set.SetBufferSize(node, MyNode.KernelPorts.Output, aggr);
                set.Update();

                set.Destroy(node);
            }
        }
    }
}
