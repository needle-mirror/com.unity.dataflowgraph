namespace Unity.DataFlowGraph.Tests
{
    public struct Message
    {
        public int Contents;

        public Message(int contentsToSend)
        {
            Contents = contentsToSend;
        }

        public static implicit operator Message(int v)
        {
            return new Message { Contents = v };
        }
    }

    public struct GenericMessage<T>
        where T : struct
    {
        public T Contents;

        public GenericMessage(in T contentsToSend)
        {
            Contents = contentsToSend;
        }

        public static implicit operator GenericMessage<T>(T v)
        {
            return new GenericMessage<T> { Contents = v };
        }
    }
}
