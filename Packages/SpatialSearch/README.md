# SpatialSearch

English | [日本語](README_JP.md)

A Unity package that finds an object specified by a word or a point in the camera image, and displays it combined with a depth point cloud (`com.yoshinaga.spatialsearch`, namespace `SpatialSearch`).

The work of finding objects is done by a Python server on a PC (this repository's `server/`, which uses SAM 3.1 in ComfyUI). This package handles building what is sent to the server, receiving the returned result, and displaying that result. Capturing the camera image and depth, and communicating with the server (WebSocket and so on), differ from device to device and environment to environment, so the project using this package provides them (for Meta Quest, the examples are `SearchCapture.cs` and `WebSocketSearchTransport.cs` in `UnityMetaQuest/Assets/Scripts/SpatialSearchLite/`).

It has no UI such as buttons or text. The search status and the number of objects found are announced through Observables, so how to display them is up to the project using the package.

```text
Capturing class (provided by the project) ─CaptureData─▶ SearchClient ─JSON─▶ Transport (provided by the project) ─▶ Python server
Class that shows results                  ◀─SearchResult─ SearchClient ◀─JSON─ Transport (provided by the project) ◀─ Python server
              (capture, result, and connection state are exchanged through SpatialSearchDataSource)
```

## Requirements

| Package | How to add it |
|---|---|
| R3 (`com.cysharp.r3` and `org.nuget.r3`) | In the using project's `manifest.json`, set up the OpenUPM and UnityNuGet scoped registries and add both packages |
| URP (`com.unity.render-pipelines.universal`) | The shaders are for URP |

This package's `package.json` has empty `dependencies`, so R3 and URP are not added automatically. Add them in the project that uses the package. An example of the registry settings and versions is in [this repository's manifest.json](../../UnityMetaQuest/Packages/manifest.json).

For the concrete steps to install R3, see the [R3 and UniTask Installation Guide (with images)](../../docs/ExternalTools/R3_UniTask_Installation.md).

It does not depend on any communication library. The packages that the transport (a class inheriting `SearchTransportBase`) uses should be added by the project using the package.

Here is an example of the entries to add to `dependencies` in `manifest.json`. The paths are relative to `manifest.json`, so change them to match the layout in another project. WebSocketSharpUtl is needed when you use this repository's WebSocket transport. Keep your existing dependencies.

```json
{
  "dependencies": {
    "com.yoshinaga.spatialsearch": "file:../../Packages/SpatialSearch",
    "com.yoshinaga.websocketsharputl": "file:../../Packages/WebSocketSharpUtl"
  }
}
```

## Contents

| Class / asset | Role |
|---|---|
| `SpatialSearchDataSource` | The place where data is handed between the classes. It emits the capture data (`CaptureObservable`), the search result (`ResultObservable`), and the connection state (`ConnectedObservable`) |
| `CaptureData` / `ImageProjection` | The data at the moment of capture, and the calculation that projects world coordinates onto the camera image's UV (`SearchData.cs`) |
| `SearchResult` / `Detection` | The search result. A mask image for each object found (`SearchData.cs`) |
| `SearchClient` | Receives the capture data, turns it into JSON, sends it to the server through the transport, and emits the returned masks as a search result. It automatically finds the transport on the same GameObject (if it is on a different GameObject, set it in the Inspector's Transport) |
| `SearchTransportBase` | The common entry point for the communication path to the server. It defines only sending and receiving strings and the connection state; implementations such as WebSocket or HTTP are made by inheriting it in the project using the package (see "Implementing a transport" below) |
| `SearchResultViewBase` | The common entry point for classes that show results. When it finishes displaying, it announces the displayed result and the number of objects (`ShownResult`) through `ShownObservable` |
| `SearchResultView` | Shows the points that fall on the mask together as a single-color point cloud |
| `GrabbableSearchResultView` | Clones the template for each found object and displays it as quads textured with the captured camera image. It also fits the grabbable range (`BoxCollider`) to the object |
| `SearchResultMeshBuilder` | A helper used by `GrabbableSearchResultView` that builds the mesh of quads textured with the camera image |
| `Shaders/SpatialSearchHighlightPointsURP`, `Materials/SpatialSearchHighlightMat` | For `SearchResultView`'s point cloud |
| `Shaders/SpatialSearchColoredPointsURP`, `Materials/SpatialSearchColoredPointsMat` | For `GrabbableSearchResultView`'s quads |

## Usage

1. Prepare a capturing class, build a `CaptureData`, and feed it to `SpatialSearchDataSource.SetCapture` (see "The capture data contract" below)
2. Prepare a transport. Create a class that inherits `SearchTransportBase` (see "Implementing a transport" below)
3. Place `SearchClient`, the transport, and a class that shows results (either `SearchResultView` or `GrabbableSearchResultView`, only one) in the scene. Attach `SearchClient` and the transport to the same GameObject. If you place them on different GameObjects, set the transport in the `SearchClient` Inspector's Transport
4. At startup, pass the same `SpatialSearchDataSource` to each class's `Initialize` (not to the transport; `SearchClient` uses the transport)
5. To show the status, subscribe to `CaptureObservable` (a search started) and `ShownObservable` (a result finished displaying). **Subscribe before `SearchClient.Initialize`.** When not connected, `SearchClient` emits an error result the moment it receives a capture, so if you subscribe later, the "searching" display overwrites the error display
6. Connect with `SearchClient.Connect(server IP)` (the details of the destination such as the port are decided by the transport implementation; for this repository's WebSocket version it is Server Port in the Inspector, 8765 by default)

Here is an excerpt of the initialization done in a `MonoBehaviour`'s `Awake` or similar. `capture`, `resultView`, `client`, and `status` are references to the capturing class, the display class, the `SearchClient`, and a text for showing the status, which the project using the package provides.

```csharp
using R3;
using SpatialSearch;

var dataSource = new SpatialSearchDataSource();
capture.Initialize(dataSource);    // the capturing class provided by the project
resultView.Initialize(dataSource); // SearchResultViewBase

// Show the status (optional). Subscribe before SearchClient
dataSource.CaptureObservable.Subscribe(c => status.text = $"Searching \"{c.Query}\"...").AddTo(this);
resultView.ShownObservable.Subscribe(shown => status.text = shown.Result.IsSuccess
    ? $"Found {shown.ObjectCount}"
    : $"Error: {shown.Result.Error}").AddTo(this);

client.Initialize(dataSource);     // SearchClient
client.Connect("192.168.0.10");
```

`ShownResult.ObjectCount` is the number of detected objects for `SearchResultView` (0 if not a single point could be shown), and the number of objects actually generated for `GrabbableSearchResultView`. In the Grab version, an object with no depth points at the mask's position is not generated.

Feed the capture data to `SetCapture` from Unity's main thread. A capture fed in while waiting for the previous search's result is ignored by `SearchClient`, and that capture's image is destroyed.

### Display settings

- `SearchResultView`: set `SpatialSearchHighlightMat` as the Point Material. You can change the points' color and size with Color / Point Size
- `GrabbableSearchResultView`: set a template for a single object as the Object Template. Give the template a `MeshFilter` and a `MeshRenderer` (with the material `SpatialSearchColoredPointsMat`). To make it grabbable, also give the template a grab mechanism (such as the Meta XR Interaction SDK's `Grabbable` / `GrabInteractable`) and a `BoxCollider`. This package does not depend on the grab mechanism

### Implementing a transport

Create a class that inherits `SearchTransportBase` (a `MonoBehaviour`) and exchanges strings with the server. `SearchClient` builds the JSON, reads the result, and manages the search in progress, so the transport only carries strings.

| What to implement | Description |
|---|---|
| `IsConnected` | Whether it is connected now |
| `Connect(serverIp)` | Connects to the server at the given IP. If already connected, it disconnects first and reconnects. Building the port and URL is up to the implementation |
| `Reconnect()` | Reconnects to the same server as last time. `SearchClient` calls it when a search starts while not connected |
| `Send(message, completed)` | Sends a string and always reports whether it could be sent through `completed(true / false)`. When it is `false`, `SearchClient` treats the search as failed |
| `NotifyConnected(bool)` | Call with `true` when connected, and with `false` when disconnected (also when connecting fails) |
| `NotifyMessage(string)` | Call when a string is received from the server |

`NotifyConnected` / `NotifyMessage` and `completed` may be called from another thread. `SearchClient` handles returning to the main thread.

An example of the WebSocket version for Meta Quest is in `UnityMetaQuest/Assets/Scripts/SpatialSearchLite/WebSocketSearchTransport.cs` (it wraps `WebSocketSharpUtl`'s `WebSocketClientManager`).

## The capture data contract (CaptureData)

| Item | Description |
|---|---|
| `Query` | The search term. For a search by point, a name for display |
| `HasPoint` / `PointUv` | When `HasPoint` is true, it finds the one object at `PointUv` (0–1, UV of the camera image with the origin at the bottom left) instead of using the search term |
| `Image` | The captured camera image. To convert it to PNG, create a new CPU-readable `Texture2D` for each capture. Because it keeps being used to display the result, do not reuse and overwrite it |
| `Points[i]` | World coordinates of each depth point |
| `ImageUvs[i]` | Where `Points[i]` appears in the camera image (0–1, **origin at the bottom left**). The mask is sampled at this UV to decide whether the point is on the object |
| `Depths[i]` | Depth of the point (m) |
| `Normals[i]` | Surface direction at the point (length 1, facing the camera side). `Vector3.zero` for points where it cannot be determined |
| `PixelRightPerMeter` / `PixelUpPerMeter` | The world-space vectors for the width of one depth pixel to the right and up, measured at 1 m (proportional to depth) |
| `Projection` | An `ImageProjection` that projects world coordinates onto the camera image's UV using the camera at the moment of capture |

Make `Points` and `ImageUvs` arrays of the same length, with the same index representing the same point. Exclude points that project outside the image, and keep each component of `ImageUvs` within 0–1. When using `GrabbableSearchResultView`, also prepare `Depths` and `Normals` with the same length, and set `PixelRightPerMeter`, `PixelUpPerMeter`, and `Projection`. Even if normals cannot be determined, the `Normals` array is still needed; set those points' values to `Vector3.zero`.

Only `SearchClient` uses `HasPoint` / `PointUv`. For display, `SearchResultView` uses `Query` (for logging), `Points`, and `ImageUvs`, and `GrabbableSearchResultView` uses all the other items too. To show the search term in the UI, refer to `ShownResult.Result.Capture.Query`.

Both display classes `Destroy` the previous result's `Image` and each `Detection.Mask` when they receive a new result. The same happens when the display class itself is destroyed. The image is not destroyed immediately after being displayed. If one result is shared by several display classes, destruction would be duplicated, so use only one display class in this implementation.

## Communication with the server

JSON strings are exchanged with `server/server.py`. `SearchClient` builds and reads the JSON, and the transport carries it. The server listens over WebSocket, so this repository's transport is WebSocket (to use another method such as HTTP, you also need a server that accepts the same JSON by that method).

| Direction | Message |
|---|---|
| Unity → server (search term) | `{"type": "search", "query": "monitor", "image": "<base64 of PNG>"}` |
| Unity → server (point) | `{"type": "point", "x": 640, "y": 480, "image": "<base64 of PNG>"}`. x, y are image pixels with the **origin at the top left** (`SearchClient` flips `PointUv` vertically before sending) |
| Server → Unity | `{"type": "result", "query": "...", "detections": [{"score": 0.93, "mask": "<base64 of PNG>"}, ...]}` or `{"type": "error", "message": "..."}` |

Do not make the search term a whitespace-only string, and do not include `,` or `:`. This repository's server returns an error for those. For a search by point, the `query` the server returns is an empty string, but `SearchResult.Capture.Query` on the Unity side keeps the display name from the time of capture.

The mask is the same size as the image you sent, and the white part is the object. When a search by point succeeds, `detections` has one entry.
