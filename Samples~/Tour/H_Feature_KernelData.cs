using UnityEngine;

namespace Unity.DataFlowGraph.Tour
{
    public class H_Feature_KernelData : MonoBehaviour
    {
        /*
         * In this example, we'll explore what "kernel data" means, and how to use it.
         * 
         * We touched on it briefly in the last example, mentioning it was like constant parameters to your graph 
         * kernel. We can update the contents of the kernel data inside the simulation, like you can set uniforms in 
         * shaders. But they are immutable during rendering. You can think of this as the communication channel between
         * simulation and the rendering that will occur after it.
         * 
         * Since the kernel data is a "private" concept, you will generally use it to configure your node to properly
         * process its public inputs and outputs.
         */
        public class MyNode 
            /*
             * Now we're deriving from a combined simulation / rendering node definition base,
             * so that this node can participate in both simulation and the rendering.
             */
            : SimulationKernelNodeDefinition<MyNode.SimPorts, MyNode.KernelDefs>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                /// <summary>
                /// Use this to change some parameter.
                /// </summary>
                public MessageInput<MyNode, float> SomeParameter;
            }

            public struct KernelDefs : IKernelPortDefinition {}

            struct KernelData : IKernelData
            {
                /// <summary>
                /// Here's the constant parameter we'd like to use inside the rendering pass.
                /// </summary>
                public float MyPrivateParameter;
            }

            struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
            {
                public void Execute(RenderContext ctx, in KernelData data, ref KernelDefs ports)
                {
                    Debug.Log($"{nameof(data.MyPrivateParameter)} equals {data.MyPrivateParameter}");
                }
            }

            struct NodeHandler : INodeData, IMsgHandler<float>
            {
                public void HandleMessage(MessageContext ctx, in float msg)
                {
                    /*
                     * To update the kernel data from inside the simulation we have the UpdateKernelData() API.
                     */
                    ctx.UpdateKernelData(new KernelData{ MyPrivateParameter = Mathf.Sin(msg) });
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
            /*
             * Here we will provide a changing variable to the parameter.
             */
            m_Set.SendMessage(m_Node, MyNode.SimulationPorts.SomeParameter, Time.time);
            m_Set.Update();
        }

        void OnDisable()
        {
            m_Set.Destroy(m_Node);
            m_Set.Dispose();
        }
    }
}
