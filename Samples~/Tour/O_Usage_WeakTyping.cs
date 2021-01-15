using System.Linq;
using UnityEngine;

namespace Unity.DataFlowGraph.Tour
{
    public class O_Usage_WeakTyping : MonoBehaviour
    {
        /*
         * In this example, we'll explore what "weak typing" is, and how to use it.
         * 
         * Up until now, we've been using the node set's strongly typed API, where all node and port types are exactly
         * specified at compile time (e.g. "MyNode.KernelPorts.MyPort"). There are often situations where you don't know
         * the exact node types you are dealing with, or what sort of ports they have - only that they should be able to connect
         * together. 
         * 
         * A common use case here is generic controller code, like a timeline. A timeline can be extended with new 
         * nodes and tracks, but that doesn't mean the runtime scheduler and controller need explicit compile time knowledge of the 
         * node types it will be dealing with - only that they support some expected data protocol.
         * 
         * To that end, the node set includes a runtime type information system so that:
         * 1) A user can introspect a node's capabilities
         * 2) At runtime, the node set can verify that weakly typed operations are safe
         * 
         * In particular, every node definition carries a port description. This is a list of every input port 
         * and output port declared by the node definition, including what declared type it is and its category:
         * data / message / DSL etc.
         * 
         * A node's port identifiers are declared in its node definition (e.g. DataInput<MyNode, float> or MessageOutput<MyNode, long>). The 
         * weakly typed analog for these are InputPortID and OutputPortID, which is what you'll find in the port 
         * description. You will also find a weakly typed analog for every API in the node set taking
         * strongly typed node and port arguments.
         * 
         * The strongly typed system not only provides better performance (by reducing runtime checks) but also leads
         * to clearer and more readable code: The named symbols being connected together are given explicitly
         * rather than some meaningless indices on generic node handles. Furthermore, invalid connections and such
         * will not even compile.
         * 
         * In general, compositing UI tools rely heavily on the weak system, while handwritten DataFlowGraph code will
         * usually rely more on the strong system. It's important to note that the extra runtime checks which occur when
         * using the weak system only incur a performance penalty at the API callsite. e.g. Extra runtime checks
         * will be performed when initially establishing a connection using the weak API, but performance beyond
         * that would be identical to a connection established using the strong API.
         * 
         * Let's use the weak system now for a couple of simple tasks we already covered.
         */
        public class MyNode : SimulationNodeDefinition<MyNode.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<MyNode, float> Input;
                public MessageOutput<MyNode, float> Output;
            }

            struct NodeHandlers : INodeData, IMsgHandler<float>
            {
                public void HandleMessage(MessageContext ctx, in float msg)
                {
                    Debug.Log($"Got a message {msg}");
                    ctx.EmitMessage(SimulationPorts.Output, msg + 2);
                }
            }
        }

        void Start()
        {
            using (var set = new NodeSet())
            {
                /*
                 * Note that we forcibly decay the normally typed node handle to an untyped version here.
                 */
                NodeHandle
                    a = set.Create<MyNode>(),
                    b = set.Create<MyNode>();

                /*
                 * In this case, we know both nodes are of the same type. This can also be tested, using Is<>, As<>
                 * or definition comparison - GetDefinition().
                 */
                var definition = set.GetDefinition<MyNode>();

                /*
                 * The port description for a node can be acquired through the definition's GetPortDescription()
                 * function, given some node.
                 */
                PortDescription 
                    portsForA = definition.GetPortDescription(a),
                    portsForB = definition.GetPortDescription(b);
                
                /*
                 * Let's pretty-print the I/O port configuration on node A (same procedure for node B).
                 */
                Debug.Log("Ports on node A: ");

                foreach (var input in portsForA.Inputs)
                    Debug.Log($"\t{input.Name} {input.Category}Input<{input.Type}>");

                foreach (var output in portsForA.Outputs)
                    Debug.Log($"\t{output.Name} {output.Category}Output<{output.Type}>");

                /*
                 * Now let's connect message ports together.
                 * First we find the correct ports to connect.
                 */
                OutputPortID messageOutputOnA = portsForA
                    .Outputs
                    .Where(p => p.Category == PortDescription.Category.Message)
                    .First();

                InputPortID messageInputOnB = portsForB
                    .Inputs
                    .Where(p => p.Category == PortDescription.Category.Message)
                    .First();

                /*
                 * And then it's simply a matter of connecting them up.
                 * Note that this API will allow you to attempt to make potentially invalid connections, which is why
                 * runtime checks are performed to ensure validity. An exception will be thrown if the connection is not
                 * possible, usually due to mismatched types on either side.
                 */
                set.Connect(a, messageOutputOnA, b, messageInputOnB);

                /*
                 * We can also send a typed message directly to an unknown node,
                 * using an acquired runtime port id.
                 * 
                 * Note that if the node does not support the message you send to it, an exception will be thrown.
                 */
                var messageInputOnA = portsForA
                    .Inputs
                    .Where(p => p.Category == PortDescription.Category.Message)
                    .First();

                set.SendMessage(a, messageInputOnA, Mathf.PI);

                /*
                 * You can also convert a strong port declaration to a runtime port id, through an explicit case operator:
                 */
                set.Disconnect(a, (OutputPortID)MyNode.SimulationPorts.Output, b, messageInputOnB);

                set.Destroy(a, b);
            }
        }
    }
}
