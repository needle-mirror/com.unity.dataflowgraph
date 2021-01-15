using UnityEngine;

namespace Unity.DataFlowGraph.Tour
{
    public class P_Usage_PortForwarding : MonoBehaviour
    {
        /*
         * In this example, we'll explore what "port forwarding" is, and how to use it.
         * 
         * Port forwarding is the node set's version of subgraphs. Since a node set otherwise always represents a 
         * flattened graph, there has to be a solution for embedding sub graphs.
         * 
         * The solution here is to allow nodes to act as "shells" around other nodes, that, to the outside world,
         * appear as one node - one unit of functionality. Modelling subgraphs as isomorphic nodes is important not only
         * for performance, but also for encapsulation: A user of a node shouldn't have to care whether it is one node
         * or many, as long as it upholds its end of the contract: The data protocol (I/O port description).
         * 
         * This is a core philosophy but also the basis of composability and reusability: If someone has already 
         * implemented some basic functionality you need in a more complex node, why not reuse it? This authoring
         * principle holds both for working in code with the node set API, as well as in compositable UI editors.
         * 
         * It works like this: The parent node (commonly nicknamed the "uber node") declares the inputs and outputs 
         * which will be visible publicly. The initialization of that node uses the provided initialization context to 
         * forward any of its ports to child node ports. 
         * 
         * Port forwarding is supported for both inputs and outputs, and applies recursively and transitively. The runtime 
         * resolves the forwarding once during node creation, after that, any connections made to a forwarded port will
         * result in a direct connection between the source and destination skipping over the parent node.
         * 
         * Let's use the weak system now for a couple of simple tasks we already covered, like connections.
         */

        public class ChildNode : SimulationNodeDefinition<ChildNode.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<ChildNode, float> Input;
                public MessageOutput<ChildNode, float> Output;
            }

            struct NodeHandler : INodeData, IMsgHandler<float>
            {
                public void HandleMessage(MessageContext ctx, in float msg)
                {
                    Debug.Log($"Child: Got a message {msg}");
                    ctx.EmitMessage(SimulationPorts.Output, msg * 2);
                }
            }
        }

        /*
         * Here we will create a parent "shell" over an internal node.
         * This parent node also takes responsibility for creating and maintain ownership over the child node.
         */
        public class ParentNode : SimulationNodeDefinition<ParentNode.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<ParentNode, float> SecretlyForwardedInput;
                public MessageInput<ParentNode, float> NormalInput;
                public MessageOutput<ParentNode, float> SecretlyForwardedOutput;
            }

            struct InstanceData : INodeData, IInit, IDestroy, IMsgHandler<float>
            {
                /*
                 * Here's the first thing to notice. The parent stores a handle to the child.
                 */
                public NodeHandle<ChildNode> Child;

                public void HandleMessage(MessageContext ctx, in float msg)
                {
                    Debug.Log($"Parent: Got a message {msg}");
                }

                public void Init(InitContext ctx)
                {
                    /*
                     * During initialization for this node we create a child node and remember it.
                     */
                    Child = ctx.Set.Create<ChildNode>();

                    /*
                     * Here's the interesting part where we essentially tell the node set that our ports should map directly
                     * to the child node's ports. This is essentially how we can emplace a subgraph partially inside another
                     * node. This is sometimes also referred to as "internal edges".
                     * 
                     * When someone sends a message to the SecretlyForwardedInput, it will go directly to the child's
                     * Input message port.
                     * Similarly, if someone is connected to SecretlyForwardedOutput, they're actually connected directly
                     * to the child output, and will receive above mentioned message.
                     * 
                     * You can of course forward data ports, and port arrays as well. In the latter case, the entire port 
                     * array will be forwarded.
                     */
                    ctx.ForwardInput(SimulationPorts.SecretlyForwardedInput, Child, ChildNode.SimulationPorts.Input);
                    ctx.ForwardOutput(SimulationPorts.SecretlyForwardedOutput, Child, ChildNode.SimulationPorts.Output);
                }

                public void Destroy(DestroyContext ctx)
                {
                    /*
                     * And remember, since we created the child node, we need to clean up after ourselves!
                     */
                    ctx.Set.Destroy(Child);
                }
            }
        }

 

        void Start()
        {
            using (var set = new NodeSet())
            {
                var parentOne = set.Create<ParentNode>();
                var parentTwo = set.Create<ParentNode>();

                /*
                 * Connecting the source's forwarded ports to the destination's non-forwarded ports results, 
                 * as described above, in a connection between the source's child node's output to the destination's 
                 * actual own input.
                 */
                set.Connect(
                    parentOne, 
                    ParentNode.SimulationPorts.SecretlyForwardedOutput, 
                    parentTwo, 
                    ParentNode.SimulationPorts.NormalInput
                );

                /*
                 * Thus sending a message to parentOne, should result in log output from a child node, then a from 
                 * parent node. 
                 */
                set.SendMessage(parentOne, ParentNode.SimulationPorts.SecretlyForwardedInput, 5f);

                /*
                 * In general, forwarding takes effect on any API that deals with (node handle, port id) pairs, 
                 * including but not limited to:
                 *  - Connections
                 *  - SetData
                 *  - SendMessage
                 *  - SetBufferSize
                 *  - GraphValues
                 */
                
                set.Destroy(parentOne, parentTwo);
            }
        }
    }
}
