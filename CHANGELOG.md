# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [0.11.8] - 2019-10-18
### Fixed
- Internally updating graph inputs on topology changes now runs in parallel

## [0.11.7] - 2019-10-08
### Changed
- Removed Test classes from documentation

## [0.11.6] - 2019-09-20
### Added
- Added a documented walkthrough of API and concepts of DataFlowGraph in a segmented, guided tour in code. You can find it inside /Samples/, or install it through the package manager. 
- Filled out most of the API documentation.

## [0.11.5] - 2019-09-12
### Changed
- Updated LICENSE.md file

## [0.11.4] - 2019-09-09
### Added
- Support for disabling Bursted Kernels in Editor via the Jobs->Burst->Enable menu toggle

### Fixed
- Silenced log errors about Bursted Kernels falling back to Managed when in a non-Bursted Standalone build

## [0.11.3] - 2019-09-08
### Fixed
- Preliminary support for IL2CPP (only in Burst enabled builds and only if ALL Kernels are Burst compiled)

## [0.11.2] - 2019-09-04
### Fixed
- Non-generic graph kernels tagged [BurstCompile] will now be bursted in standalone mono builds
- Non-generic graph kernels tagged [BurstCompile] will now appear in the Burst Inspector

## [0.11.1] - 2019-09-03
### Fixed
- Moving internal container helpers to a more appropriate namespace.

## [0.11.0] - 2019-08-30
### Changed
- the package name is now com.unity.dataflowgraph
- the top level namespace is now Unity.DataFlowGraph

## [0.10.0] - 2019-08-29
### Added
- PortArray: Allows any NodeDefinition to include arrays of MessageInput or DataInput.
- PortDescription.InputPort.IsPortArray has beed added to allow identifying which ports are arrays vs normal
- NodeSet.SetPortArraySize() is used to set the size of the array for a given PortArray<>
- MessageContext (for IMsgHandler.HandleMessage) has property ArrayIndex for use when receiving messages on a PortArray<MessageInput<>>
- NodeSet.SendMessage now has a variant taking an array index to support PortArray<MessageInput<>>
- NodeSet.SetData now has a variant taking an array index to support PortArray<DataInput<>>
- NodeSet.Connect/Disconnect/DisconnectAndRetain now have variants taking an array index to support PortArray<>
- InitContext.ForwardInput now has a variant taking an array index to support PortArray<>
- RenderContext.Resolve has a variant which allows resolving PortArray<DataInput>
- NodeMemoryInput (for ECS Buffer transfers) now has a variant taking an array index to support PortArray<>

## [0.9.4] - 2019-08-20
### Added
- Support for mapping ECS dynamic buffers directly to data inputs of buffers. Use the newly provided MemoryInputSystem<Tag, Buffer> system together with some component datas to set up an automatic memory pipeline.

## [0.9.3] - 2019-08-19
### Fixed
- Relaxed type restrictions on KernelData + KernelPorts + GraphKernel + NodeData to be "unmanaged" instead of "blittable". This includes support for storing booleans

## [0.9.2] - 2019-08-13
### Fixed
- Fixed reuse of prior port-forwarding table when making connections in a node's Init()

## [0.9.1] - 2019-08-11
### Fixed
- Fixed resetting of port forwarding tables when node entries are re-used
- Fixed debug display of DataInput ports outisde of the RenderGraph

## [0.9.0] - 2019-08-07
### Added
- Added TimeExample sample to show a possible choice for how to implement time interfaces between nodes.

### Changed
- IMsgHandler.HandleMessage now takes an "in MessageContext" instead of an NodeHandle/InputPortID pair. The pair is now available as properties Handle/Port from MessageContext.
- IMsgHandler.HandleMessage now takes an "in TMsg msg" instead of a "ref TMsg msg"
- NodeSet.SendMessage now takes an "in TMsg msg" instead of a "ref TMsg msg" or simple "TMsg msg"
- NodeSet.EmitMessage now takes an "in TMsg msg" instead of a "ref TMsg msg"

## [0.8.1] - 2019-08-06
### Added
- Added debugging displays of common objects in the DataFlowGraph framework (handles, data I/O, buffers)
- Added weakly-typed overload of creating graph values: GraphValue<T> CreateGraphValue<T>(NodeHandle handle, OutputPortID port);

## [0.8.0] - 2019-07-10
### Added
- GraphValueResolver: A concurrent container able to resolve read only memory of any output port a GraphValue points to, including aggregates and buffers. Please see API documentation.
- NodeSet.GetGraphValueResolver() and NodeSet.InjectDependencyFromConsumer(): API for interacting with GraphValueResolvers

### Changed
- NodeHandle.Null is now deprecated, use NodeSet.Exists instead for proper invalidation checks, or, default value if appropriate
- NodeSet.ValueExists<T>(GraphValue<T>) will no longer throw an exception if it is orphaned (target node destroyed), as the value still needs to be cleaned up separately.

### Removed
- Removed NodeSet.GetLastValue<T>(GraphValue<T>). It's not performant by default and is easy to implement otherwise jobified or not with new features.

### Fixed
- Orphaned graph values no longer results in errors inside the render graph

## [0.7.2] - 2019-06-21
### Changed
- Bumped com.unity.burst dependency to 1.1.0-preview.2 for compatibility with Unity's latest trunk

## [0.7.0] - 2019-05-24
### Added
- A system for port forwarding, an entire graph can now act as a single node in the graph.
- Initialization contexts for configuring node instances on creation

### Changed
- INodeFunctionality.Init(NodeHandle handle) => INodeFunctionality.Init(InitContext ctx). The handle previously given direct can now be obtained through InitContext.Handle.

### Removed
- Removed NodeSet.DisconnectAll(NodeHandle handle). It was never intended to be public, and breaks composability (you can break someone else's connection without knowing them or them being aware of it)

## [0.6.1] - 2019-05-23
### Added
- Support for DataInput/Output ports of a struct type which includes multiple Buffer<T> instances

### Changed
- PortDescription.Input/OutputPort.IsBuffer replaced with .HasBuffers

### Fixed
- Buffer<T>.SizeRequest(int) refuses negative size arguments

## [0.6.0] - 2019-05-22
### Changed
- Bumping Burst dependency to 1.0.4
- Fixed package to conform to validation rules
- Standardizing Data port Buffer types to match other Data port types (eg. DataInput/OutputBuffer<T> becomes DataInput/Output<Buffer<T>>)
- Data port Buffer types are now resolved as NativeArrays when used in Kernels
- Data Ports can no longer be resolved outside of a Kernel (now requires use of new RenderContext only available in Kernel execution)
- Burst compilation failures for Kernels now result in an console Error message (instead of Warning) in Editor

### Fixed
- can no longer specifcy negative sizes for Data port Buffers.

## [0.5.4] - 2019-04-26
### Added
- Runtime detection of incorrectly defined ports where the given node definition class does not correspond to the enclosing class.
- Missing weak API port conversions (eg. you can now explicit cast a MessageOutput<...> to OutputPortID)

### Fixed
- Silenced all compiler warnings for Runtime and Tests

## [0.5.3] - 2019-04-11
### Added
- NodeSet.SetData() which allows setting data directly from the simulation side (eg. from a node's HandleMessage or OnUpdate) on a node's DataInput ports (if and only if it is disconnected; the only case that makes sense)
- NodeSet.DisconnectAndRetainValue() for Data connections; once disconnected, said input will continue to hold the last value it had at the moment the disconnection occurred.

### Fixed
- Fixed bug in NodeSet.Connect() so we now disallow connecting a DataOutput to a DataInputBuffer (was not detected as long as the type matched). Same thing for DataOutputBuffer<T> to DataInput<T>.
- Detection of negative indices used to dereference DataOutputBuffers (throws IndexOutOfRangeException)
- Fixed unstable playmode test RuntimeTests.NodeDefinition_DeclaredManaged_CanRetainAndRelease_ManagedObjects
