using UnityEngine;

namespace Unity.DataFlowGraph.Tour
{
    public class E_Usage_FlowingMessages : MonoBehaviour
    {
        /*
         * In this sample we'll expand on the previous, and instead have nodes send messages to each other, through 
         * connections and emitting messages.
         * 
         * To do this, we'll have the node emitting a modified message that it receives. Using this, we will be able
         * to see a flow of messages through a constructed graph.
         */
        public class MyNode : SimulationNodeDefinition<MyNode.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<MyNode, char> SetName;
                public MessageInput<MyNode, int> MyInput;
                /*
                 * Here is an output - it's completely similar in usage to an input, otherwise.
                 */
                public MessageOutput<MyNode, int> MyOutput;
            }

            struct MyInstanceData : INodeData, IMsgHandler<int>, IMsgHandler<char>
            {
                /*
                 * In this example, we'll store a name to make it a bit simpler to figure out what's going on.
                 */
                char Name;

                public void HandleMessage(MessageContext ctx, in char msg)
                {
                    /*
                     * Record the name being given.
                     */
                    Name = msg;
                }

                public void HandleMessage(MessageContext ctx, in int msg)
                {
                    Debug.Log($"'{Name}' received an int message of value {msg}");

                    /*
                     * To further emit a message from this node, we need to use the EmitMessage API.
                     */
                    ctx.EmitMessage(
                        /*
                         * This means any node connected to this particular port on this instance is going to receive the
                         * message that we are emitting.
                         */
                        SimulationPorts.MyOutput, 
                        /*
                         * Add something to the message to disambiguate the output.
                         */
                        msg + 1
                    );
                }
            }
        }


        void Start()
        {
            using (var set = new NodeSet())
            {
                /*
                 * So we'll create a bunch of nodes this time.
                 */
                NodeHandle<MyNode>
                    a = set.Create<MyNode>(),
                    b = set.Create<MyNode>(),
                    c = set.Create<MyNode>(),
                    d = set.Create<MyNode>(),
                    e = set.Create<MyNode>();

                /*
                 * To set the names, we'll send the nodes messages:
                 */
                set.SendMessage(a, MyNode.SimulationPorts.SetName, 'a');
                set.SendMessage(b, MyNode.SimulationPorts.SetName, 'b');
                set.SendMessage(c, MyNode.SimulationPorts.SetName, 'c');
                set.SendMessage(d, MyNode.SimulationPorts.SetName, 'd');
                set.SendMessage(e, MyNode.SimulationPorts.SetName, 'e');

                /*
                 * Then we need to connect them together in a desired layout. Let's do it like this:
                 * 
                 *   b
                 *  / \
                 * a   d - e
                 *  \ /
                 *   c
                 *   
                 * Creating a diamond with multiple input - multiple output situation.
                 * 
                 * For this, we'll use the Connect() API on the node set. In all variants of connections
                 * in the node set, argument order is {source, source port} {destination, destination port}.
                 * 
                 * Hence, if we want a message to flow from a to b, we connect a's output as a source to b's input as
                 * a destination.
                 */

                
                set.Connect(a, MyNode.SimulationPorts.MyOutput, b, MyNode.SimulationPorts.MyInput);
                set.Connect(a, MyNode.SimulationPorts.MyOutput, c, MyNode.SimulationPorts.MyInput);

                set.Connect(b, MyNode.SimulationPorts.MyOutput, d, MyNode.SimulationPorts.MyInput);
                set.Connect(c, MyNode.SimulationPorts.MyOutput, d, MyNode.SimulationPorts.MyInput);

                set.Connect(d, MyNode.SimulationPorts.MyOutput, e, MyNode.SimulationPorts.MyInput);

                /*
                 * Now trigger the barrage of messages!
                 * We should see that the flow starts at a and follows the path through:
                 * - b and d to get to e.
                 * - c and d to get to e, again.
                 * 
                 * Notice that multiple inputs at d like this may result in multiple evaluations of the same message handler,
                 * as message flow currently is implemented as a recursive traversal.
                 */
                set.SendMessage(a, MyNode.SimulationPorts.MyInput, 1);

                set.Destroy(a, b, c, d, e);
            }
        }
    }
}
