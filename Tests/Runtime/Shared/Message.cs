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
}
