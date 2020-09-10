using UnityEngine;

namespace Unity.DataFlowGraph.Tour
{
    public class Q_Usage_FeedbackConnections : MonoBehaviour
    {
        /*
         * In this example, we'll explore what feedback connections are, and how to use them.
         * 
         * Feedback connections can be used to introduce cycles in your graph, with an update delay. That means an output from a node, 
         * can be connected back to a node input upstream, but the value read upstream will be 1 update old (the result of the 
         * previous call to update). 
         * 
         * Since the value coming in is one update old, the first time you read the input, you will receive a default value. 
         * As of now, there is no explicit way to write in a feedback output value, so any initalization should be 
         * done via simulation messaging.
         * 
         * It is also forbidden to feed back to your own node. Feedback can only be connected to a different node, upstream
         * (if it was downstream, it would be the same as a normal connection).
         * 
         */
        public class AddNode : KernelNodeDefinition<AddNode.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<AddNode, uint>   InputA;
                public DataInput<AddNode, uint>   InputB;                
                public DataOutput<AddNode, uint>  Result;
            }

            struct KernelData : IKernelData { }

            struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
            {
                public void Execute(RenderContext ctx, KernelData data, ref KernelDefs ports)
                {
                    var a = ctx.Resolve(ports.InputA);
                    var b = ctx.Resolve(ports.InputB);
                    ref var result = ref ctx.Resolve(ref ports.Result);
                    result = a + b;
                    Debug.Log(result);

                    // Notice that in the first iteration, feedback connections will result in a default value of zero as the downstream nodes
                    // have not yet been evaluated for the first time.  Therefore, to properly start off the Fibonacci, we have a special case to
                    // return the first '1' in the series.
                    if (a == 0 && b == 0)
                        result = 1;
                }
            }
        }

        public class DelayLineNode : KernelNodeDefinition<DelayLineNode.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<DelayLineNode, uint>   InputN;
                public DataOutput<DelayLineNode, uint>  OutputN1;
                public DataOutput<DelayLineNode, uint>  OutputN2;
            }

            struct KernelData : IKernelData { }

            struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
            {
                uint m_PreviousValue;
                public void Execute(RenderContext ctx, KernelData data, ref KernelDefs ports)
                {
                    ref var n1 = ref ctx.Resolve(ref ports.OutputN1);
                    ref var n2 = ref ctx.Resolve(ref ports.OutputN2);
                    n2 = m_PreviousValue;
                    m_PreviousValue = ctx.Resolve(ports.InputN);
                    n1 = m_PreviousValue;
                }
            }
        }

        NodeSet                     m_Set;
        NodeHandle<AddNode>         m_AddNode;
        NodeHandle<DelayLineNode>   m_DelayLineNode;

        void OnEnable()
        {
            m_Set = new NodeSet();
            m_AddNode = m_Set.Create<AddNode>();
            m_DelayLineNode = m_Set.Create<DelayLineNode>();

            // The DelayLine node takes one input value, which is stored internally, and outputs that same value and the value from
            // the last frame (input n, and output n & n-1).
            // The AddNode is adding the two values together and feeding the result to the DelayLine node input trough a feedback connection.
            m_Set.Connect(m_DelayLineNode, DelayLineNode.KernelPorts.OutputN1, m_AddNode,AddNode.KernelPorts.InputA);
            m_Set.Connect(m_DelayLineNode, DelayLineNode.KernelPorts.OutputN2, m_AddNode, AddNode.KernelPorts.InputB);
            m_Set.Connect(m_AddNode, AddNode.KernelPorts.Result, m_DelayLineNode, DelayLineNode.KernelPorts.InputN, NodeSet.ConnectionType.Feedback);
        }

        void Update()
        {
            m_Set.Update();
        }

        void OnDisable()
        {
            m_Set.Destroy(m_DelayLineNode);
            m_Set.Destroy(m_AddNode);
            m_Set.Dispose();
        }
    }
}
