
# About DataFlowGraph


DataFlowGraph is a framework which provides a toolbox of runtime APIs for authoring and composing nodes that form a processing graph in DOTS. 

DataFlowGraph can be broken up into various modules:
- Topology database APIs
- Traversal cache APIs
- Graph scheduling and execution
- Data flow management
- Compositable node & connection system
- Messaging / events
- Strongly typed port I/O system

# Installing DataFlowGraph Package

To install this package, follow the instructions in the [Package Manager documentation](https://docs.unity3d.com/Packages/com.unity.package-manager-ui@latest/index.html). 


<a name="UsingPackageName"></a>
# Using DataFlowGraph


# Technical details
## Requirements

This version of DataFlowGraph package is compatible with the following versions of the Unity Editor:

* 2019.4 and later


## Known limitations

DataFlowGraph version 0.19 includes the following known limitations:

* The DataFlowGraph package is an experimental feature 
* This version of DataFlowGraph consists of features that mainly cater to the needs of DOTS Animation
* _GetNodeData_ for incompatibly typed node handles may return incorrect results
* There is currently no support for port forwarding to multiple nodes

## Scripting defines
* DFG_PER_NODE_PROFILING: Enables profiler markers for individual kernel rendering invocations. This has a non-trivial performance penalty on the order of many milliseconds per 100k nodes, but it is more efficient than explicit profile markers in GraphKernel code. May also lead to more indeterministic runtime.
* DFG_ASSERTIONS: Enable additional internal confidence / consistency checks. High performance penalty.

## Package contents

|Location|Description|
|---|---|
|`<folder>`|Contains &lt;describe what the folder contains&gt;.|
|`<file>`|Contains &lt;describe what the file represents or implements&gt;.|


|Folder Location|Description|
|---|---|

## Document revision history
 
|Date|Reason|
|---|---|
|May 29, 2020|Updated known limitations.|
|February 13, 2020|Added scripting define usages, updated known limitations.|
|August 30, 2019|Unedited. Published to package.|
