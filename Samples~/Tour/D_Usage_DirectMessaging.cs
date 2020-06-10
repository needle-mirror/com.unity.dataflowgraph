using System;
using UnityEngine;

namespace Unity.DataFlowGraph.Tour
{
    public class D_Usage_DirectMessaging : MonoBehaviour
    {
        /*
         * In this sample we'll explore sending a message to a node.
         * A message is any kind of data that will be delivered to a node and handled by it's IMessageHandler<T>.OnMessage()
         * function.
         * 
         * Direct messaging can be used to inject events or transfer data into the simulation of a node set.
         * 
         * To do this, we need two things:
         * 1) Our node needs to implement a typed message port 
         * 2) Our node needs to implement a message handler for that type
         */
        public class MyNode 
            /*
             * Notice that we derive from an extended node definition here, since we need a "simulation port definition" 
             * as well. More on that below.
             */
            : NodeDefinition<MyNode.MyInstanceData, MyNode.SimPorts>
            /*
             * For any kind of message that our node can receive, we need to implement the message handler interface
             * for that type.
             */
            , IMsgHandler<double>
        {
            /*
             * The simulation port definition is a struct containing declarations of all of the simulation-type ports
             * that your node supports. This is a part of your node's contract to the outside world.
             * It can also be viewed as the "event"-based blackboard for your node.
             */
            public struct SimPorts : ISimulationPortDefinition
            {
                /*
                 * A simulation port definition will contain a range of XYZInput<> and XYZOutput<>.
                 * Here we declared two message input of type float.
                 * The first generic argument is the enclosing node. This helps in assisting producing compiler errors
                 * if you connect something together in the wrong way, or declare message inputs that your node doesn't
                 * actually support.
                 */
                public MessageInput<MyNode, double> MyFirstInput;
                public MessageInput<MyNode, double> MySecondInput;
            }

            public struct MyInstanceData : INodeData { }

            /*
             * Here is our implementation of the message handler for float types. The actual message comes in as a 
             * readonly reference (the last argument). The context provides additional information, like which
             * port it arrived on which is useful if you have multiple port declarations of the same type.
             */
            public void HandleMessage(in MessageContext ctx, in double msg)
            {
                if(ctx.Port == SimulationPorts.MyFirstInput)
                {
                    Debug.Log($"{nameof(MyNode)} recieved a float message of value on the first input: {msg}");
                }
                else if(ctx.Port == SimulationPorts.MySecondInput)
                {
                    Debug.Log($"{nameof(MyNode)} recieved a float message of value on the second input: {msg}");
                }
            }
        }


        void Start()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<MyNode>();

                /*
                 * To send a message directly to a node, we simply use the SendMessage() API on the NodeSet. There's a
                 * range of overloads for different situations, but in this case we statically know the exact node type
                 * and node port that we intend to deliver a message to.
                 * Therefore, we can literally type out the path to the exact message port.
                 */
                set.SendMessage(node, MyNode.SimulationPorts.MySecondInput, Math.PI);
                set.SendMessage(node, MyNode.SimulationPorts.MyFirstInput, Math.E);

                set.Destroy(node);
            }
        }
    }
}
