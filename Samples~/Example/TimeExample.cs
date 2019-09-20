using System;
using TimeStandards;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Unity.DataFlowGraph;

using StreamType = Unity.Mathematics.float2;

namespace TimeStandards
{
    struct SeekMessage
    {
        public float Time;
    }

    struct SpeedMessage
    {
        public float Scale;
    }

    struct PlayStateMessage
    {
        public bool ShouldPlay;
    }

    struct WeightMessage
    {
        public float Gradient;
    }

    struct TimeOffsetMessage
    {
        public float Origin;
    }

    interface ISeekable : ITaskPortMsgHandler<ISeekable, SeekMessage> { }
    interface ISpeed : ITaskPortMsgHandler<ISpeed, SpeedMessage> { }
    interface IPlayable : ITaskPortMsgHandler<IPlayable, PlayStateMessage> { }
    interface IWeightable : ITaskPortMsgHandler<IWeightable, WeightMessage> { }
    interface IOffsettable : ITaskPortMsgHandler<IOffsettable, TimeOffsetMessage> { }
}

namespace TimeExample
{
    class Generator 
        : NodeDefinition<Generator.Data, Generator.SimPorts, Generator.KernelData, Generator.KernelDefs, Generator.Kernel>
        , ISeekable
        , ISpeed
        , IPlayable
        , IMsgHandler<StreamType>
    {
        public struct SimPorts : ISimulationPortDefinition
        {
            #pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
            public MessageInput<Generator, SeekMessage> SeekPort;
            public MessageInput<Generator, SpeedMessage> SpeedPort;
            public MessageInput<Generator, PlayStateMessage> PlayPort;
            public MessageInput<Generator, StreamType> OutputMask;
            #pragma warning restore 649
        }

        InputPortID ITaskPort<ISeekable>.GetPort(NodeHandle handle) => (InputPortID)SimulationPorts.SeekPort;
        InputPortID ITaskPort<ISpeed>.GetPort(NodeHandle handle) => (InputPortID)SimulationPorts.SpeedPort;
        InputPortID ITaskPort<IPlayable>.GetPort(NodeHandle handle) => (InputPortID)SimulationPorts.PlayPort;

        public struct KernelDefs : IKernelPortDefinition
        {
            #pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
            public DataOutput<Generator, StreamType> Output;
            #pragma warning restore 649
        }

        public struct Data : INodeData
        {
            public float Time;
            public float Speed;
            public int IsPlaying;
        }

        public struct KernelData : IKernelData
        {
            public float Time;
            public StreamType Mask;
        }

        public struct Kernel : IGraphKernel<KernelData, KernelDefs>
        {
            public void Execute(RenderContext ctx, KernelData data, ref KernelDefs ports)
            {
                math.sincos(data.Time * 2 * (float)Math.PI, out float x, out float y);
                ctx.Resolve(ref ports.Output) = data.Mask * new StreamType(x, y);
            }
        }

        public void HandleMessage(in MessageContext ctx, in SeekMessage msg) => GetNodeData(ctx.Handle).Time = msg.Time;
        public void HandleMessage(in MessageContext ctx, in SpeedMessage msg) => GetNodeData(ctx.Handle).Speed = msg.Scale;
        public void HandleMessage(in MessageContext ctx, in PlayStateMessage msg) => GetNodeData(ctx.Handle).IsPlaying = msg.ShouldPlay ? 1 : 0;
        public void HandleMessage(in MessageContext ctx, in StreamType msg) => GetKernelData(ctx.Handle).Mask = msg;

        public override void OnUpdate(NodeHandle handle)
        {
            ref var data = ref GetNodeData(handle);

            if (data.IsPlaying > 0)
            {
                data.Time += Time.deltaTime * data.Speed;
            }

            GetKernelData(handle).Time = data.Time;
        }
    }

    class Mixer
        : NodeDefinition<Mixer.Data, Mixer.SimPorts, Mixer.KernelData, Mixer.KernelDefs, Mixer.Kernel>
        , IWeightable
    {
        public struct SimPorts : ISimulationPortDefinition
        {
            #pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
            public MessageInput<Mixer, WeightMessage> WeightPort;
            #pragma warning restore 649
        }
        InputPortID ITaskPort<IWeightable>.GetPort(NodeHandle handle) => (InputPortID)SimulationPorts.WeightPort;

        public struct Data : INodeData {}

        public struct KernelDefs : IKernelPortDefinition
        {
            #pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
            public DataInput<Mixer, StreamType> Left;
            public DataInput<Mixer, StreamType> Right;
            public DataOutput<Mixer, StreamType> Output;
            #pragma warning restore 649
        }

        public struct KernelData : IKernelData
        {
            public float Gradient;
        }

        public struct Kernel : IGraphKernel<KernelData, KernelDefs>
        {
            public void Execute(RenderContext ctx, KernelData data, ref KernelDefs ports)
            {
                ctx.Resolve(ref ports.Output) = math.lerp(ctx.Resolve(ports.Left), ctx.Resolve(ports.Right), data.Gradient);
            }
        }

        public void HandleMessage(in MessageContext ctx, in WeightMessage msg) => GetKernelData(ctx.Handle).Gradient = msg.Gradient;
    }

    class ClipContainer 
        : NodeDefinition<ClipContainer.Data, ClipContainer.SimPorts>
        , ISeekable
        , ISpeed
        , IPlayable
        , IOffsettable
    {
        public struct SimPorts : ISimulationPortDefinition
        {
            #pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
            public MessageInput<ClipContainer, SeekMessage> SeekPort;
            public MessageInput<ClipContainer, SpeedMessage> SpeedPort;
            public MessageInput<ClipContainer, PlayStateMessage> PlayPort;
            public MessageInput<ClipContainer, TimeOffsetMessage> TimeOffset;

            public MessageOutput<ClipContainer, SeekMessage> SeekOut;
            public MessageOutput<ClipContainer, SpeedMessage> SpeedOut;
            public MessageOutput<ClipContainer, PlayStateMessage> PlayOut;
            #pragma warning restore 649
        }

        InputPortID ITaskPort<ISeekable>.GetPort(NodeHandle handle) => (InputPortID)SimulationPorts.SeekPort;
        InputPortID ITaskPort<ISpeed>.GetPort(NodeHandle handle) => (InputPortID)SimulationPorts.SpeedPort;
        InputPortID ITaskPort<IPlayable>.GetPort(NodeHandle handle) => (InputPortID)SimulationPorts.PlayPort;
        InputPortID ITaskPort<IOffsettable>.GetPort(NodeHandle handle) => (InputPortID)SimulationPorts.TimeOffset;

        public struct Data : INodeData
        {
            public float Time;
            public float Speed;
            public float Origin;
            public int IsPlaying;
            public int WasPlaying;
        }

        public void HandleMessage(in MessageContext ctx, in SeekMessage msg)
        {
            ref var data = ref GetNodeData(ctx.Handle);
            data.Time = msg.Time;
            EmitMessage(ctx.Handle, SimulationPorts.SeekOut, new SeekMessage {Time = data.Time - data.Origin});
        }

        public void HandleMessage(in MessageContext ctx, in SpeedMessage msg)
        {
            GetNodeData(ctx.Handle).Speed = msg.Scale;
            EmitMessage(ctx.Handle, SimulationPorts.SpeedOut, msg);
        }

        public void HandleMessage(in MessageContext ctx, in PlayStateMessage msg) => GetNodeData(ctx.Handle).IsPlaying = msg.ShouldPlay ? 1 : 0;
        public void HandleMessage(in MessageContext ctx, in TimeOffsetMessage msg) => GetNodeData(ctx.Handle).Origin = msg.Origin;

        public override void OnUpdate(NodeHandle handle)
        {
            ref var data = ref GetNodeData(handle);

            if (data.IsPlaying > 0)
            {
                data.Time += Time.deltaTime * data.Speed;
                if (data.Time >= data.Origin && data.WasPlaying == 0)
                {
                    EmitMessage(handle, SimulationPorts.PlayOut, new PlayStateMessage { ShouldPlay = true});
                    data.WasPlaying = 1;
                }
                else if (data.WasPlaying == 1)
                {
                    EmitMessage(handle, SimulationPorts.PlayOut, new PlayStateMessage { ShouldPlay = false });
                    data.WasPlaying = 0;
                }
            } 
            else if (data.WasPlaying == 1)
            {
                EmitMessage(handle, SimulationPorts.PlayOut, new PlayStateMessage { ShouldPlay = false });
                data.WasPlaying = 0;
            }
        }
    }

    class TimelineAnchor 
        : NodeDefinition<TimelineAnchor.Data, TimelineAnchor.SimPorts>
        , ISeekable
        , ISpeed
        , IPlayable
    {
        public struct SimPorts : ISimulationPortDefinition
        {
            #pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
            public MessageInput<TimelineAnchor, SeekMessage> SeekPort;
            public MessageInput<TimelineAnchor, SpeedMessage> SpeedPort;
            public MessageInput<TimelineAnchor, PlayStateMessage> PlayPort;

            public MessageOutput<TimelineAnchor, SeekMessage> SeekOut;
            public MessageOutput<TimelineAnchor, SpeedMessage> SpeedOut;
            public MessageOutput<TimelineAnchor, PlayStateMessage> PlayOut;
            #pragma warning restore 649
        }

        public struct Data : INodeData { }

        InputPortID ITaskPort<ISeekable>.GetPort(NodeHandle handle) => (InputPortID)SimulationPorts.SeekPort;
        InputPortID ITaskPort<ISpeed>.GetPort(NodeHandle handle) => (InputPortID)SimulationPorts.SpeedPort;
        InputPortID ITaskPort<IPlayable>.GetPort(NodeHandle handle) => (InputPortID)SimulationPorts.PlayPort;

        public void HandleMessage(in MessageContext ctx, in SeekMessage msg) => EmitMessage(ctx.Handle, SimulationPorts.SeekOut, msg);
        public void HandleMessage(in MessageContext ctx, in SpeedMessage msg) => EmitMessage(ctx.Handle, SimulationPorts.SpeedOut, msg);
        public void HandleMessage(in MessageContext ctx, in PlayStateMessage msg) => EmitMessage(ctx.Handle, SimulationPorts.PlayOut, msg);
    }


    public class TimeExample : MonoBehaviour
    {
        [Range(-1, 1)]
        public float X;

        [Range(-1, 1)]
        public float Y;

        [Range(0, 2)]
        public float Speed = 0.5f;

        [Range(0, 1)]
        public float MixerBlend = 0.25f;

        [Range(0, 10)]
        public float RestartSeekPoint = 2.0f;

        public bool Playing = true;

        NodeSet m_Set;

        NodeHandle<TimelineAnchor> m_Timeline;
        NodeHandle<Mixer> m_LastMixer;

        NativeList<NodeHandle> m_Nodes;

        GraphValue<StreamType> m_GraphOutput;

        float m_Speed, m_MixerBlend;
        bool m_Playing;

        void AddClip(NodeHandle<TimelineAnchor> timeline, float timeOffset, StreamType mask)
        {
            var leafSource = m_Set.Create<Generator>();
            var clip = m_Set.Create<ClipContainer>();
            var mixer = m_Set.Create<Mixer>();

            var leafAdapter = m_Set.Adapt(leafSource);
            var clipAdapter = m_Set.Adapt(clip);

            // Connect leaf into a clip, that handles time translation
            m_Set.Connect(clip, ClipContainer.SimulationPorts.PlayOut, leafAdapter.To<IPlayable>());
            m_Set.Connect(clip, ClipContainer.SimulationPorts.SpeedOut, leafAdapter.To<ISpeed>());
            m_Set.Connect(clip, ClipContainer.SimulationPorts.SeekOut, leafAdapter.To<ISeekable>());

            // connect clip to timeline anchor
            m_Set.Connect(timeline, TimelineAnchor.SimulationPorts.PlayOut, clipAdapter.To<IPlayable>());
            m_Set.Connect(timeline, TimelineAnchor.SimulationPorts.SpeedOut, clipAdapter.To<ISpeed>());
            m_Set.Connect(timeline, TimelineAnchor.SimulationPorts.SeekOut, clipAdapter.To<ISeekable>());
            
            // set up params on clip and generator
            m_Set.SendMessage(m_Set.Adapt(clip).To<IOffsettable>(), new TimeOffsetMessage { Origin = timeOffset });
            m_Set.SendMessage(leafSource, Generator.SimulationPorts.OutputMask, mask);

            // push back an animation tree, and connect a new mixer to the top
            m_Set.Connect(leafSource, Generator.KernelPorts.Output, mixer, Mixer.KernelPorts.Left);
            m_Set.Connect(m_LastMixer, Mixer.KernelPorts.Output, mixer, Mixer.KernelPorts.Right);

            m_Nodes.Add(mixer);
            m_Nodes.Add(clip);
            m_Nodes.Add(leafSource);

            m_Set.ReleaseGraphValue(m_GraphOutput);
            m_GraphOutput = m_Set.CreateGraphValue(mixer, Mixer.KernelPorts.Output);

            m_LastMixer = mixer;
        }

        void OnEnable()
        {
            m_Set = new NodeSet();
            m_Nodes = new NativeList<NodeHandle>(Allocator.Persistent);
            m_Timeline = m_Set.Create<TimelineAnchor>();
            m_LastMixer = m_Set.Create<Mixer>();
            m_Nodes.Add(m_LastMixer);
            m_Nodes.Add(m_Timeline);
            m_GraphOutput = m_Set.CreateGraphValue(m_LastMixer, Mixer.KernelPorts.Output);


            AddClip(m_Timeline, 1, new float2(1, 0));
            AddClip(m_Timeline, 2, new float2(0, 1));
        }

        void OnDisable()
        {
            for(int i = 0; i < m_Nodes.Length; ++i)
                m_Set.Destroy(m_Nodes[i]);

            m_Set.ReleaseGraphValue(m_GraphOutput);
            m_Nodes.Dispose();
            m_Set.Dispose();
        }

        void Update()
        {
            // Controlling the whole timeline, and every clip playing in it:

            if (m_Speed != Speed)
            {
                m_Speed = Speed;
                m_Set.SendMessage(m_Timeline, TimelineAnchor.SimulationPorts.SpeedPort, new SpeedMessage { Scale = m_Speed });
            }

            if (m_MixerBlend != MixerBlend)
            {
                m_MixerBlend = MixerBlend;
                m_Set.SendMessage(m_LastMixer, Mixer.SimulationPorts.WeightPort, new WeightMessage{ Gradient = m_MixerBlend});
            }

            if (m_Playing != Playing)
            {
                m_Playing = Playing;
                if (m_Playing)
                {
                    m_Set.SendMessage(m_Timeline, TimelineAnchor.SimulationPorts.SeekPort, new SeekMessage { Time = RestartSeekPoint });
                }
                m_Set.SendMessage(m_Timeline, TimelineAnchor.SimulationPorts.PlayPort, new PlayStateMessage { ShouldPlay = m_Playing });
            }

            // advancing time:
            m_Set.Update();

            // getting output out of the graph:
            var current = m_Set.GetValueBlocking(m_GraphOutput);
            X = current.x;
            Y = current.y;
        }
    }
}
