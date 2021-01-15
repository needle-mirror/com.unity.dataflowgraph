using System.Linq;
using Unity.Burst;
using UnityEngine;

namespace Unity.DataFlowGraph.Tour
{
    public class L_Feature_Buffers : MonoBehaviour
    {
        /*
         * In this example, we'll explore what "buffers" are, and how to use them.
         * 
         * Buffers are simply a way of working with flowing data arrays inside the rendering graph. They behave exactly
         * like normal scalar ports: Each node "owns" the output types and can write to them, while inputs are read
         * only.
         * 
         * So concretely, here we will connect two nodes together using a buffer and see what APIs are needed along the
         * way.
         */
        public class MyWriter : KernelNodeDefinition<MyWriter.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                /*
                 * Like any other I/O type, you just put it as the second type argument to a port.
                 */
                public DataOutput<MyWriter, Buffer<float>> OutputBuffer;
            }

            struct KernelData : IKernelData { }

            [BurstCompile]
            struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
            {
                public void Execute(RenderContext ctx, in KernelData data, ref KernelDefs ports)
                {
                    /*
                     * As usual, we just resolve an output.
                     * You'll notice however it returns a NativeArray<float>.
                     * So buffers are a sort of intermediate structure that needs to be resolved into a native array
                     * to work with the memory.
                     */
                    var array = ctx.Resolve(ref ports.OutputBuffer);

                    /*
                     * Let's just put some numbers in here.
                     */
                    for (int i = 0; i < array.Length; ++i)
                        array[i] = i;
                }
            }
        }

        public class MyReader : KernelNodeDefinition<MyReader.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<MyReader, Buffer<float>> InputBuffer;
            }

            struct KernelData : IKernelData { }

            struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
            {
                public void Execute(RenderContext ctx, in KernelData data, ref KernelDefs ports)
                {
                    /*
                     * Similarly, here we just resolve an input instead without a ref argument.
                     * Note the array is read-only.
                     */
                    var array = ctx.Resolve(ports.InputBuffer);

                    Debug.Log($"The sum of all numbers from 0 ... {array.Length} is {array.Sum()}");
                }
            }
        }

        void Start()
        {
            using (var set = new NodeSet())
            {
                var writer = set.Create<MyWriter>();
                var reader = set.Create<MyReader>();

                set.Connect(writer, MyWriter.KernelPorts.OutputBuffer, reader, MyReader.KernelPorts.InputBuffer);

                /*
                 * You'll notice we haven't declared the size of the array anywhere yet.
                 * This is done in the simulation (or externally), by transferring a size request in the SetBufferSize() API
                 * on the node set.
                 */
                set.SetBufferSize(writer, MyWriter.KernelPorts.OutputBuffer, Buffer<float>.SizeRequest(50));

                set.Update();

                /*
                 * Buffer memory is otherwise managed by the node set, so we don't have to worry about cleaning that up.
                 */
                set.Destroy(writer, reader);
            }
        }
    }
}
