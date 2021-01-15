# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [0.19.0-preview.7] - 2021-01-15
### Changed
- Upgraded dependency com.unity.entities to 0.17.0-preview.41

## [0.19.0-preview.6] - 2021-01-13
### Changed
- Upgraded dependency com.unity.entities to 0.17.0-preview.40

## [0.19.0-preview.5] - 2020-12-15
### Changed
- Upgraded dependency com.unity.entities to 0.17.0-preview.39

## [0.19.0-preview.4] - 2020-12-08
### Changed
- Upgraded dependency com.unity.entities to 0.17.0-preview.38
- Upgraded dependency com.unity.jobs to 0.8.0-preview.23

### Deprecated
- `RenderContext.ResolvedPortArray` renamed `RenderContext.ResolvedInputPortArray`

## [0.19.0-preview.3] - 2020-11-26
### Fixed
- Fixed an ILPP issue causing rewriting of mscorlib references to System.Private.CoreLib in combination with dependent packages when run on Unity 2021.1

## [0.19.0-preview.2] - 2020-11-25
### Added
- Observability culling meaning only nodes contributing to a measurable result will actually run (off by default)
- `CausesSideEffects` attribute which can be used to decorate your `IGraphKernel<>` declarations if they cause (invisible to DFG) side effects to avoid them being culled
- `NodeSet.RendererOptimizations` flags used to control what speculative optimizations will be performed in the rendering graph
- Support for declaring `IKernelPortDefinition`s which include `PortArray<DataOutput<TDefinition,TType>>` instances
- Weak and strong variants of `NodeSetAPI.SetPortArraySize` targeting a `PortArray<DataOutput<>>`
- All variants of `NodeSetAPI.Connect/Disconnect/DisconnectAndRetain` with `PortArray<DataOutput<>>` sources
- Weak and strong variants of `NodeSetAPI.CreateGraphValueArray` and `NodeSetAPI.ReleaseGraphValueArray` for creating/destroying `GraphValueArray<T>`s, the equivalent of existing `GraphValue<T>`s but for observing `PortArray<DataOutput<>>`
- `GraphValueResolver.Resolve(GraphValueArray<T>)` and `RenderContext.Resolve(in PortArray<DataOutput<TDefinition,T>>)` for resolving read only memory of an output port array referenced by a `GraphValueArray<T>` to a `NativeSlice<T>`
- `InitContext.ForwardOutput(PortArray<DataOutput<TDefinition,TType>> origin, NodeHandle<TForwardedDefinition>, PortArray<DataOutput<TForwardedDefinition,TType>> replacement)`
- Weak and strong variants of `NodeSetAPI.SetBufferSize` targeting a specific port in a  `PortArray<DataOutput<>>` which includes `Buffer<T>`s in its element type
- Support for IDE debugging of `PortArray<DataOutput<TDefinition,TType>>` via the `PortArrayDebugView` `DebuggerTypeProxy`

### Changed
- Upgraded dependency com.unity.entities to 0.17.0-preview.37
- Upgraded dependency com.unity.jobs to 0.8.0-preview.22
- Upgraded dependency com.unity.collections to 0.15.0-preview.21

## [0.19.0-preview.1] - 2020-11-13
### Added
- Support for showing forwarded ports, private ports and ports on `ComponentNode`s in IDE debugger displays for `NodeHandle`s
- Endpoints - a new system for connecting directed node+port pairs together
- `NodeHandle<T>.Tie` for creating endpoints by tying handles and ports together
- `Connect`, `Disconnect` and `DisconnectAndRetainValue` overloads on `NodeSetAPI` taking output and input endpoints

### Changed
- `IUpdate.Update` no longer takes its `UpdateContext` argument by `in` parameter
- `IMsgHandler.HandleMessage` no longer takes its `MessageContext` argument by `in` parameter
- `IGraphKernel.Execute` now takes its `IKernelData` argument as an `in` parameter
- Upgraded dependency com.unity.entities to 0.17.0-preview.34
- Upgraded dependency com.unity.jobs to 0.8.0-preview.20
- Upgraded dependency com.unity.collections to 0.15.0-preview.19
- Upgraded dependency com.unity.burst to 1.4.1

### Removed
- `NodeDefinition<...>` base classes and `Init`, `Destroy`, `Update`, and `HandleMessage` handlers implemented on aforementioned classes are now hard obsoleted and unsupported

## [0.18.0-preview.3] - 2020-10-20
### Changed
- Upgraded dependency com.unity.entities to 0.16.0-preview.21

## [0.18.0-preview.2] - 2020-10-13
### Changed
- Upgraded dependency com.unity.entities to 0.16.0-preview.20
- Upgraded dependency com.unity.jobs to 0.7.0-preview.17
- Upgraded dependency com.unity.collections to 0.14.0-preview.16
- Upgraded dependency com.unity.burst to 1.3.7

## [0.18.0-preview.1] - 2020-09-10
### Added
- Added `InitContext.UploadRequest<T>`, `UpdateContext.UploadRequest<T>`, `MessageContext.UploadRequest<T>` and `CommonContext.UploadRequest<T>`; you can use this together with `.UpdateKernelBuffers` to upload contents of arrays from simulation to local kernel buffers inside the rendering.

### Changed
- Upgraded dependency com.unity.entities to 0.16.0-preview.18
- Upgraded dependency com.unity.jobs to 0.7.0-preview.15
- Upgraded dependency com.unity.collections to 0.14.0-preview.14
- Upgraded dependency com.unity.mathematics to 1.2.1
- Upgraded dependency com.unity.burst to 1.3.5
- Minimum supported Unity version is now 2020.1
- Renamed `InitContext.SetKernelBufferSize`, `UpdateContext.SetKernelBufferSize` and `MessageContext.SetKernelBufferSize` to `.UpdateKernelBuffers`

## [0.17.1-preview.2] - 2020-10-02
### Fixed
- Regression introduced in 0.17.1-preview.1 where `MessageInput`s with identical generic messages types but different instantiations would be treated like duplicates (DFG_IE_04 would be emitted in that case)

## [0.17.1-preview.1] - 2020-09-30
### Added
- `InitContext.SetInitialPortValue()`: Can be used for setting the initial value on a data input port
- `InitContext`, `UpdateContext`, and `MessageContext` can now be implicitly cast to the new `CommonContext` which expresses their shared API
- `IMsgHandlerGeneric` as a variant of `IMsgHandler` for situations in which it is necessary for implementations of `INodeData` to implement support for message handlers of different types which would otherwise be impossible due to C# language constraints (eg. a generic input type mixed with other input types)
- `NodeSet.SendTest` and corresponding `TestContext` to allow authoring of unit tests which verify `NodeDefinition` simulation state

### Fixed
- The `EmitMessage` variants taking a port array index will now throw an exception if the given port index is out of range
- Samples and Tour using `IUpdate` functionality 

## [0.17.0-preview.4] - 2020-09-10
### Added
- `InitContext.EmitMessage()` identical to the same function existing in `UpdateContext` and `MessageContext`

## [0.17.0-preview.3] - 2020-09-08
### Deprecated
- Adding estimated deprecation date of 2020-10-27 for the old `NodeDefinition<...>` base classes

## [0.17.0-preview.2] - 2020-09-03
### Fixed
- Regression causing incorrect reporting of DFG_UE_11 (Definition declares an input message port with no matching handler)

## [0.17.0-preview.1] - 2020-09-02
### Added
- `InitContext`, `UpdateContext`, `MessageContext` and `DestroyContext` now all have a public `NodeSetAPI Set` field which gives access to a restricted subset of functionality of the `NodeSet`
- `IUpdate`, `IDestroy`, `IInit` complimentary interfaces for implementing previous functionality from `NodeDefinition.Update`, `NodeDefinition.Destroy` and `NodeDefinition.Init`
- `.RegisterForUpdate` and `.RemoveFromUpdate` on every context for registering nodes implementing `IUpdate` for recurring update calls. Default is unregistered.
- `IUpdate`, `IDestroy`, `IInit` and `IMsgHandler<T>` can and must now be implemented directly on your `INodeData`.
- `.UpdateKernelData<T>()` on the various contexts for submitting a new value for your `IKernelData`, available in the next rendering. This supersedes `ref T GetKernelData()`
- Weak and strong variants of `HasStaticPortDescription`, `GetStaticPortDescription` and `GetPortDescription` on `NodeSet` and `NodeSetAPI` 
- `internal` and `private` port declarations on kernel / simulation port definitions are now allowed
- `NodeSet` has been split into a hierarchy of `NodeSet` deriving from `NodeSetAPI`. `.IsCreated`, `.GetGraphValueResolver`, `.Update`, `.Dispose`, `.InjectDependencyFromConsumer`, `.RenderModel` is only implemented on `NodeSet`
- Topology jobs are now asynchronous with respect to the main thread, except in case of `NodeSet.RenderExecutionModel.MaximallyParallel`
- `bool NodeSet.ValueTargetExists<T>(GraphValue<T>)` - tests whether an existing graph value's target node also still exists

### Changed
- All Samples and the Tour migrated to the new style of `NodeDefinition`s

### Deprecated
- `NodeDefinition<...>` base classes are superseded by the use of `SimulationNodeDefinition<>`, `KernelNodeDefinition<>`, and `SimulationKernelNodeDefinition<>`. Since traits are no longer passed to the base class via generic parameters, node definitions are now required to declare their `INodeData`, `ISimulationPortDefinition`, `IKernelPortDefinition`, `IKernelData`, and `IGraphKernel` structs within their class definition scope. Furthermore, `Init`, `Destroy`, `Update`, and `HandleMessage` handlers must now be implemented on the `INodeData` struct

### Fixed
- Topology jobs are now not scheduled if not needed, instead of earlying out which still has overhead
- Graph values are now only initially set up once instead of every frame
- Removed a lot of zero-sized allocations

### Internal
- Removed reliance on UnsafeUtilityEx

## [0.16.0-preview.3] - 2020-08-18
### Fixed
- Fixed regression introduced in 0.16.0-preview.1; `ComponentNode`s could transition into a passive state (read/writing default values) when unrelated topology changes were made

## [0.16.0-preview.2] - 2020-07-21
### Internal
- Removed dependency on System.Runtime.CompilerServices.Unsafe

## [0.16.0-preview.1] - 2020-07-08
### Added
- Patching the memory in the rendering graph is now incremental (ComputeValueChunkAndPatchPorts). This is a sizable performance improvement when touching few out of many graph islands
- Genealogy of topology is now computed in parallel
- Support for `PortArray<MessageOutput<>>`

### Changed
- Minimum supported Unity version is now 2019.4

## [0.15.0-preview.5] - 2020-06-17
### Changed
- Upgraded dependency com.unity.entities to 0.11.1-preview.4
- Upgraded dependency com.unity.jobs to 0.2.10-preview.12
- Upgraded dependency com.unity.collections to 0.9.0-preview.6
- Upgraded dependency com.unity.burst to 1.3.0

## [0.15.0-preview.4] - 2020-06-10
### Fixed
- Fix regression where post-processing would crash on `NodeDefinition`'s who's `INodeData`, `ISimulationPortDefinition`, `IKernelData`, `IKernelPortDefinition`, or `IGraphKernel` were defined in an assembly other than the `NodeDefinition` itself

## [0.15.0-preview.3] - 2020-06-08
### Added
- Improved topological recomputation costs when touching few out of many graph islands

### Fixed
- Correcting release of DFG with the proper/expected set of dependencies:
    com.unity.entities to 0.11.0-preview.7
    com.unity.jobs to 0.2.10-preview.11
    com.unity.collections to 0.9.0-preview.5
    com.unity.burst to 1.3.0-preview.12

## [0.15.0-preview.2] - 2020-05-29
### Fixed
- Introduced workaround for users facing issues due to nondeterministic order of ECS system creation; a `NodeSet` may be constructed with a `ComponentSystemBase` which has not yet been initialized via its `OnCreate` method, however, `ComponentNode` creation and `NodeSet.Update` should not be done before said system has been created.

## [0.15.0-preview.1] - 2020-05-26
### Added
- `DestroyContext`, see changed `NodeDefinition.OnDestroy`
- Note: Upgrade to Burst 1.3.0-preview.12 brings with it support for Debug.Log and use of generics within Bursted `IGraphKernel` implementations

### Changed
- Non-disposed `NodeSet`s no longer try to clean up anything on garbage collections.
- Forgetting to `Dispose` a `NodeSet` is now only reported if "Leak Detection" is enabled.
- `NodeSet.IsDisposed()` replaced by `NodeSet.IsCreated`
- `NodeTraits<>`, `NodeTraitsBase` and `NodeDefinition.BaseTraits` are now internal as they were nothing but user-required boilerplate in edge cases. They will now automatically be generated for you.
- `NodeDefinition.OnDestroy(NodeHandle)` changed to `NodeDefinition.OnDestroy(DestroyContext)`. The previous handle can be found at `DestroyContext.Handle`
- Upgraded dependency com.unity.entities to 0.11.0-preview.7
- Upgraded dependency com.unity.jobs to 0.2.10-preview.11
- Upgraded dependency com.unity.collections to 0.9.0-preview.5
- Upgraded dependency com.unity.burst to 1.3.0-preview.12

### Fixed
- Unique PortID assignments are now done through ILPP instead of Reflection (resolves issue #414)

## [0.14.0-preview.2] - 2020-05-04
### Added
- `NodeSet.IsDisposed()` to query Dispose state

### Changed
- `NodeSet(JobComponentSystem)` constructor changed to `NodeSet(ComponentSystemBase)` making it compatible with all ECS system types: `ComponentSystem`, `JobComponentSystem` and `SystemBase`.

### Fixed
- Default constructed PortIDs are now invalid (issue #100)

## [0.14.0-preview.1] - 2020-04-17
### Added
- Sample Tour describing how to use feedback connections

### Changed
- Upgraded dependency com.unity.entities to 0.9.1-preview.15
- Upgraded dependency com.unity.jobs to 0.2.8-preview.3
- Upgraded dependency com.unity.collections to 0.7.1-preview.3
- Upgraded dependency com.unity.burst to 1.3.0-preview.7

### Deprecated
- INodeMemoryInputTag, NodeMemoryInput, NativeAllowReinterpretationAttribute and MemoryInputSystem are now obsolete. Use ComponentNodes instead.

### Fixed
- Support for CoreCLR garbage collector

### Internal
- Added initial base framework for code generation using ILPP

## [0.13.0-preview.3] - 2020-03-27
### Fixed
- Internal name change fix to support latest ProceduralGraph package

## [0.13.0-preview.2] - 2020-03-23
### Added
- PortDescription.InputPort.IsPortArray is now available

### Fixed
- Possible crash when destroying a NodeSet containing ComponentNodes during ECS shutdown

## [0.13.0-preview.1] - 2020-03-13
### Added
- Scripting define DFG_PER_NODE_PROFILING: Enables profiler markers for individual kernel rendering invocations. This has a non-trivial performance penalty on the order of many milliseconds per 100k nodes, but, is more efficient than explicit profile markers in GraphKernel code. May also lead to more indeterministic runtime.
- Support for connecting one or many Message output ports to an otherwise unconnected Data input port of matching type (issue #13)
- Improved IDE debugger displays for NodeHandles (it is now possible to see node instance data and peers) in Simulation
- Support for allocating/resizing kernel buffer storage from simulation (resulting buffer only accessible in kernel). See SetKernelBufferSize() in Init/Update/MessageContext.

### Changed
- Upgraded dependency com.unity.entities to 0.8.0-preview.8
- Upgraded dependency com.unity.jobs to 0.2.7-preview.11
- Upgraded dependency com.unity.collections to 0.7.0-preview.2
- Upgraded dependency com.unity.burst to 1.3.0-preview.6

### Deprecated
- INodeMemoryInputTag, NodeMemoryInput, NativeAllowReinterpretationAttribute and MemoryInputSystem are now soft deprecated. Use ComponentNodes instead.

### Fixed
- Multiple Data connections to the same Data input port will now throw an exception up-front when the connection is made rather than produce a deferred error during rendering (issue #134)
- Throw an exception if a message of a given type is sent to a MessageInput of incompatible type through the weak API (issue #116)

## [0.12.0-preview.7] - 2020-02-18
### Fixed
- Disabled warnings-as-errors locally in package. Soft-obsoletion of APIs in dependencies no longer causes compilation failures.

## [0.12.0-preview.6] - 2020-01-30
### Fixed
- Trying to use a NodeHandle in a NodeSet other than the one in which it was created is now detected and throws an exception.
- Performance regression where topology would be computed outside of Burst

## [0.12.0-preview.5] - 2019-12-19
### Added
- ComponentNode as an intrinsic node allowing reading & writing from ECS component data and buffers in the rendering graph. See documentation for an in depth description.
- Strong and weak versions of ComponentNode.Input/Output() for creating data ports and port IDs referring to ECS component types.
- NodeSet.CreateComponentNode(Entity) for creating ComponentNodes. They otherwise function as normal nodes.
- New constructor for NodeSet(JobComponentSystem) which ties together a NodeSet and a JobComponentSystem to allow creation of ComponentNodes (see above)
- JobHandle NodeSet.Update(JobHandle) to be called inside a JobComponentSystem for updating a NodeSet with simple input / output dependencies

### Changed
- NodeSet.SetPortArraySize now takes an int size argument instead of ushort; maximum remains PortArray.MaxSize and will throw if exceeded.

### Fixed
- Nested Buffer<T> definitions are now properly picked up

## [0.12.0-preview.4] - 2019-11-22
### Added
- Support for feedback connections which allow information to feed back to parent nodes without introducing cycles (see ConnectionType in Connect() APIs)

### Changed
- virtual NodeDefinition.OnUpdate() now receives an UpdateContext argument (which is used to retrieve the node's handle).
- NodeDefinition.EmitMessage moved to MessageContext.EmitMessage/UpdateContext.EmitMessage.
- public APIs which took ushort port array indices now take an int port array index; maximum remains PortArray.MaxSize and will throw if exceeded.

### Fixed
- Cycles are now detected in the rendering graph, and produce a deferred error message - as long as the graph contains a cycle, the rendergraph won't execute (#17)
- Proper handling of port forwarding to a node which is subsequently destroyed during Init()
- Fixed double fault when failing to instantiate node definitions inside NodeSet.Create<T>()

## [0.12.0-preview.3] - 2019-11-15
### Fixed
- Error when a buffer was resized on a node in the same Update() as it was destroyed.
- Silenced some compiler warnings

## [0.12.0-preview.2] - 2019-11-11
### Added
- NodeDefinition.HasStaticPortDescription.
- NodeDefinition.GetStaticPortDescription().
- More profiler markers with new style API, that can assist in performance measurements.

### Changed
- Refactored internal topology tools, including generalizing data types.
- NodeDefinition.GetPortDescription() will throw if given a NodeHandle which does not match the NodeDefinition type.
- Reduced per kernel node allocations by 3x, and optimized memory layouts

### Fixed
- Unity scene for Sample Tour section P_Usage_PortForwarding references the right script.
- Could not create multiple GraphValueResolvers in the same NodeSet.Update()

## [0.12.0-preview.1] - 2019-09-30
### Added
- Strong API support for mapping ECS dynamic buffers directly to data inputs of buffers. See MemoryInputSystem<Tag, Buffer>.
- Added a mass scaling tweening example of cube positions being animated in different directions.

### Changed
- Moved enum Unity.DataFlowGraph.Usage to Unity.DataFlowGraph.PortDescription.Category
- NodeDefinition.GetPortDescription() is no longer virtual
- Renamed InputPort/OutputPort.PortUsage to Input/OutputPort.Category
- Moved enum Unity.DataFlowGraph.RenderExecutionModel to Unity.DataFlowGraph.NodeSet.RenderExecutionModel
- Added a generic parameter for NodeMemoryInput. It is now necessary to also specify the buffer type being moved (matches MemoryInputSystem<Tag, Buffer>).
- Renamed base class NodeFunctionality to NodeDefinition or SimulationNodeDefinition / KernelNodeDefinition / SimulationKernelNodeDefinition
- Removed INodeDefinition and INodeFunctionality, merged them into NodeDefinition
- NodeDefinition no longer publicly implements IDisposable
- Renamed NodeSet.GetFunctionality() -> NodeSet.GetDefinition()
- The following interface functions from INodeFunctionality have changed to be virtual protected members on NodeDefinition:
	- public BaseTraits -> protected
	- public Destroy -> protected
	- public Init -> protected
	- public OnUpdate -> protected
	- public Set -> protected
	- public Dispose -> protected
- The following interface functions/properties from INodeFunctionality/NodeDefinition are no longer accessible:
	- OnMessage<T>
	- GeneratePortDescriptions
	- AutoPorts

## [0.11.10] - 2019-11-25
### Changed
- Upgraded dependency "com.unity.entities" to 0.3.0-preview.4
- Upgraded dependency "com.unity.jobs" to 0.2.1-preview.3
- Upgraded dependency "com.unity.collections" to 0.3.0-preview.0
- Upgraded dependency "com.unity.burst" to 1.2.0-preview.9

## [0.11.9] - 2019-10-25
### Added
- Added warning in package description emphasizing that this is work-in-progress and should not be used in production.

### Fixed
- InitContext.ForwardOutput(MessageOutput<T>,...) no longer requires the destination node to implement IMsgHandler.
- Fixed occasional native crash in Unity job scheduler, when used together with ECS (#329)

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
