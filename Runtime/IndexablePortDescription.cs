using System.Collections.Generic;
using System.Linq;

namespace Unity.DataFlowGraph
{
    struct IndexablePortDescription
    {
        List<PortDescription.InputPort> m_PackedInputs;
        List<PortDescription.OutputPort> m_PackedOutputs;

        public IndexablePortDescription(PortDescription desc)
        {
            m_PackedInputs = desc.Inputs.ToList();
            m_PackedOutputs = desc.Outputs.ToList();

            m_PackedInputs.Sort(
                (a, b) =>
                {
                    int c = a.Category - b.Category;
                    if (c != 0)
                        return c;

                    return a.PortID.Port.CategoryCounter - b.PortID.Port.CategoryCounter;
                }
            );

            m_PackedOutputs.Sort(
                (a, b) =>
                {
                    int c = a.Category - b.Category;
                    if (c != 0)
                        return c;

                    return a.PortID.Port.CategoryCounter - b.PortID.Port.CategoryCounter;
                }
            );

            m_DataInputOffset = (ushort)m_PackedInputs.Count(p => p.Category == PortDescription.Category.Message);
            m_DSLInputOffset = (ushort)m_PackedInputs.Count(p => p.Category == PortDescription.Category.Data);
            m_DSLInputOffset += m_DataInputOffset;

            m_DataOutputOffset = (ushort)m_PackedOutputs.Count(p => p.Category == PortDescription.Category.Message);
            m_DSLOutputOffset = (ushort)m_PackedOutputs.Count(p => p.Category == PortDescription.Category.Data);
            m_DSLOutputOffset += m_DataOutputOffset;
        }

        public PortDescription.InputPort Lookup(InputPortID id)
        {
            var encoded = id.Port;

            var index = encoded.CategoryCounter;

            index += encoded.Category == PortStorage.Category.Data ? m_DataInputOffset : (ushort)0u;
            index += encoded.Category == PortStorage.Category.DSL ? m_DSLInputOffset : (ushort)0u;

            return m_PackedInputs[index];
        }

        public PortDescription.OutputPort Lookup(OutputPortID id)
        {
            var encoded = id.Port;
            var index = encoded.CategoryCounter;

            index += encoded.Category == PortStorage.Category.Data ? m_DataOutputOffset : (ushort)0u;
            index += encoded.Category == PortStorage.Category.DSL ? m_DSLOutputOffset : (ushort)0u;

            return m_PackedOutputs[index];
        }

        ushort
            m_DataOutputOffset,
            m_DSLOutputOffset,
            m_DataInputOffset,
            m_DSLInputOffset;
    }
}
