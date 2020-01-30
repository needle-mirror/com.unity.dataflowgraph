
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

* 2019.3 and later (recommended)


## Known limitations

DataFlowGraph version 0.12 includes the following known limitations:

* The DataFlowGraph package is an experimental feature 
* This version of DataFlowGraph consists of features that mainly cater to the needs of DOTS Animation
* _GetNodeData_ for incompatibly typed node handles may return incorrect results
* Nested buffers in Ports inside nested structures will not be detected
* Multiple data inputs to a port, while not supported, is not handled gracefully
* _SendMessage_ APIs do not detect invalid port IDs
* Default initialized port definitions/IDs may currently be valid
* Cycles between Data ports, while not supported, is not handled gracefully
* NodeHandle can be valid in multiple node sets
* There is currently no support for port forwarding to multiple nodes


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
|August 30, 2019|Unedited. Published to package.|
