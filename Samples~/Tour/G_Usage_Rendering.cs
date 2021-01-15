using UnityEngine;

namespace Unity.DataFlowGraph.Tour
{
    public class G_Usage_Rendering : MonoBehaviour
    {
        /*
         * In this example, we'll explore what "rendering" means, and how to use it.
         * 
         * As covered in the previous example ("simulation"), execution is split into two parts in a nodeset. Inside 
         * the simulation, flow happens through messages, while in the rendering it happens through "raw" data.
         * 
         * The connection points between data inputs/outputs are done in the same way in a separate port definition
         * (IKernelPortDefinition) and processing of the data happens in small kernels (IGraphKernel<>).
         * 
         * Thus data flows through a separate graph of data connection topology, colloquially referred to as the "render graph".
         * The class of problems you solve using this system can generally be referred to as data dependency
         * processing, likely including complex DAGs with lots of buffers like audio DSP graphs or animation
         * FK / IK graphs.
         * 
         * Rendering happens after simulation, where the graph has usually been mutated. An optimal execution 
         * is then incrementally built on top of the previous rendering and the diff of changes that happened.
         *
         * Rendering runs asynchronously in jobs, although this can be configured (see NodeSet.RenderModel).
         * To extract results of the rendering, you would generally use graph values as we'll cover later. 
         * 
         * Concretely in this example, we'll cover the steps needed to have a graph kernel that will be run inside the
         * rendering pass.
         */
        class MyNode :
            /*
             * Now we're deriving from a rendering node definition base so that this node can participate in the rendering.
             */
            KernelNodeDefinition<MyNode.KernelDefs>
        {
            /*
             * Kernel port definitions are like simulation port definitions, but they are separated and literally
             * functions as the I/O blackboard while executing a graph kernel.
             * We'll have a look at that soon enough, for now our graph kernel doesn't need any I/O.
             */
            public struct KernelDefs : IKernelPortDefinition {}

            /*
             * The "Kernel Data" is constant parameters to your graph kernel, like uniforms in shaders.
             * We will look at this later.
             * 
             * Note that the presence of this IKernelData nested struct is automatically detected for you, therefore you
             * can keep it declared private.
             */
            struct KernelData : IKernelData {}

            /*
             * So finally, here's our graph kernel. You'll notice that it is specialized on the previous two structures as well.
             * The graph kernel is executed when all of the port inputs on the kernel port definition have been 
             * resolved / processed, or in other words: Whenever its input dependencies (if any) are complete.
             * This is equivalent to the topological sort of the nodes (vertices) arranged together with the given
             * connections (edges) made in the particular set.
             * 
             * For greater runtime performance, you can tag your graph kernel with the [BurstCompile] attribute, otherwise it defaults
             * to running inside Mono / IL2CPP.
             *
             * As with the IKernelData above, this IGraphKernel<> nested struct is automatically detected for you.
             */
            struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
            {
                /*
                 * The parameters to the Execute() function are, beyond what we covered so far, the rendering context.
                 * The context primarily deals with resolving your I/O ports into safe, read/write memory.
                 * 
                 * Inside this function, you would then use the parameters/data found in your kernel data
                 * to read your inputs and write something to your outputs.
                 * 
                 * You can also keep persistent state on your graph kernel itself. For example, a common use for this would be in audio for 
                 * free-running recursive style IIR DSP, where you would have state variables on your kernel (or even
                 * buffers / delay lines).
                 */
                public void Execute(RenderContext ctx, in KernelData data, ref KernelDefs ports)
                {
                    Debug.Log("Hello, world!");
                }
            }
        }

        NodeSet m_Set;
        NodeHandle<MyNode> m_Node;

        void OnEnable()
        {
            m_Set = new NodeSet();
            m_Node = m_Set.Create<MyNode>();
        }

        void Update()
        {
            m_Set.Update();
        }

        void OnDisable()
        {
            m_Set.Destroy(m_Node);
            m_Set.Dispose();
        }
    }
}
