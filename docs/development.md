# Settings and how it works (for developers)

English | [日本語](development_JP.md)

[Back to the main guide](../README.md) · [Grab and copy with a finger frame](interactions.md)

The preparation and startup steps needed to try the app are in the README. This page is a supplement for adjusting settings and reading the implementation.

For the steps to install R3 and the like, see the [R3 and UniTask Installation Guide (with images)](ExternalTools/R3_UniTask_Installation.md).

## Server options

You can add the following options to `python server/server.py`. Run the command at the repository root.

| Option | Default | Description |
|---|---|---|
| `--host` | `127.0.0.1` | Address to listen on. Use `0.0.0.0` when connecting from the Quest |
| `--port` | `8765` | Port to listen on |
| `--comfyui` | `http://127.0.0.1:8188` | URL of ComfyUI |
| `--checkpoint` | `sam3.1_multiplex_fp16.safetensors` | Name of the SAM 3.1 checkpoint |
| `--threshold` | `0.5` | Detections less confident than this are discarded. Lower it if nothing is found |
| `--max-detections` | `8` | Maximum number of objects found in one search |

## Configurable settings

| Where (Hierarchy) | Item | Description |
|---|---|---|
| Main Coordinator on `[MainCoordinator]` | Default Server Ip | Value pre-filled in the dialog when no IP has ever been entered |
| Search Capture on `Logic/Search` | Min / Max Depth Meters | Only points within this distance range are used |
| Web Socket Search Transport on `Logic/WebSocket` | Server Port | Port of the Python server |
| Head Follow on `SearchWindow` / `ServerDialog` | Distance / Height Offset / Follow Speed | How far and how high the window appears, and how fast it catches up with your head |
| `Logic/SearchResultView` (0-SpatialSearchLite) | Color / Point Size | Color (yellow by default) and size of the result points |
| `Logic/SearchResultView` (1-SpatialSearchLiteGrab) | Outlier Ratio / Min Collider Size | Ratio of edge points excluded when determining the grabbable box, and the box's minimum size |
| `Packages/SpatialSearch/Materials/SpatialSearchColoredPointsMat` (in the Project window: Packages > SpatialSearch) | Depth Tolerance | Depth range within which overlapping quads are drawn as the same surface (as a multiple of the quad size). Raise it if flickering occurs; lower it if rear quads show through |
| Same material | Fade Start / Fade End | Dot product of the object's front (the surface direction obtained from the point cloud) and the current view direction (1 = from the front, 0 = edge-on, negative = from behind). Fully opaque at or above Fade Start, fully transparent at or below Fade End, and smoothly blended in between. Lower Fade End to keep the object visible from more oblique angles |
| Interactable Color Visual on `Assets/Prefabs/GrabbableSearchResult` | Hover / Select Color State | Color overlaid when approached or grabbed (A is the overlay strength) |
| Frame Copy Controller on `Logic/FrameCopy` (2-SpatialSearchLiteFrame(Experimental)) | Stable Angle / Lock Seconds | The point locks when it has stayed within this angle (degrees) for this many seconds |
| Same component | Hands Gone Seconds / Lower Hands Timeout | Time from when both hands disappear from the camera image until the capture, and time to wait for the hands to be lowered after the lock (seconds) |
| Same component | Result Timeout | Time to wait for the server's result (seconds). After it passes, the next operation is accepted |
| Same component | Dot Angle / Ring Angle / Aim / Edge / Locked Color | Apparent size of the dot and ring (degrees), and the colors while aiming, while pointing at an edge, and when locked |

## How it works

```text
Quest : capture camera image + depth ──(WebSocket)──▶ PC : Python server ──▶ ComfyUI (SAM 3.1)
Quest : show the depth points covered by the mask ◀──(WebSocket)── PC : one mask image per object
```

The Unity scripts are split across two places.

| Location | Contents |
|---|---|
| `Packages/SpatialSearch/` (package `com.yoshinaga.spatialsearch`, namespace `SpatialSearch`) | The part reusable in other projects: `SpatialSearchDataSource`, the capture-data and search-result types (`SearchData`), `SearchClient`, the common entry point for transports (`SearchTransportBase`), result display (`SearchResultView` / `GrabbableSearchResultView` / `SearchResultMeshBuilder`), shaders and materials |
| `UnityMetaQuest/Assets/Scripts/SpatialSearchLite/` | The part specific to this project (Meta Quest): `MainCoordinator`, `SearchCapture`, `WebSocketSearchTransport`, `FrameCopyController`, `HeadFollow` |

Like `WebSocketSharpUtl`, the package is loaded from `UnityMetaQuest/Packages/manifest.json` with `file:../../Packages/SpatialSearch`. How to use it is in [Packages/SpatialSearch/README.md](../Packages/SpatialSearch/README.md).

`MainCoordinator` hands the same `SpatialSearchDataSource` to three classes, and each class works by receiving the data that flows through it. UI operations such as buttons and input fields are all handled by `MainCoordinator`; the three classes know nothing about the UI. In the `2-SpatialSearchLiteFrame(Experimental)` scene, `FrameCopyController`, which handles the hand frame, joins them.

| Class | Role |
|---|---|
| `MainCoordinator` | Connects the three classes and handles UI operations (IP dialog, opening/closing the search window, starting a search) and the status display on the controller's panel (searching, number found, errors) |
| `SearchCapture` | When `Capture(search term)` is called, captures the camera image and depth. It also computes where each depth point appears in the image (UV) and the surface direction (normal) at that point, and calls `SetCapture`. When capturing with `CaptureAt(position)`, it also puts where that position appears in the image into the capture data (search by point). It also does a raycast toward the depth for the hand frame |
| `SearchClient` | On receiving the captured data, turns the image and the search term (or, for a search by point, the point on the image) into JSON and sends it to the server through the transport. It calls `SetResult` with the masks that come back. The communication itself is left to the transport below, and `SearchClient` knows nothing about WebSocket |
| `WebSocketSearchTransport` | A WebSocket implementation of `SearchClient`'s transport (`SearchTransportBase`). It wraps `WebSocketSharpUtl`'s `WebSocketClientManager` and handles connecting to `ws://IP:port/`, sending and receiving strings, and notifying the connection state. To use something else such as HTTP, swap in a class that inherits `SearchTransportBase` |
| `FrameCopyController` | Detects the shape of the two-handed frame and shows a dot where the ray from the head through the frame's center hits the depth. Once the dot locks, it waits for the hands to be lowered and then captures with `SearchCapture.CaptureAt` (2-SpatialSearchLiteFrame(Experimental) scene) |
| `SearchResultView` | Collects only the points that fall on the white part of the mask and shows them together in a single color (0-SpatialSearchLite scene) |
| `GrabbableSearchResultView` | Clones the template prefab `GrabbableSearchResult` for each found object and displays the object's points as quads textured with the captured camera image. It also fits the grabbable box to the object (1-SpatialSearchLiteGrab scene; in the 2-SpatialSearchLiteFrame(Experimental) scene the template is `GrabbableCopy`) |
| `SearchResultMeshBuilder` | A helper used by `GrabbableSearchResultView`. For each point (one depth pixel), it builds a quad covering the area that pixel sees, and projects its four corners onto the camera image to get the UVs |

Instead of assigning one color per point, the camera image's own pixels are sampled and pasted inside each quad (the shader is the package's `SpatialSearchColoredPointsURP`). So the result does not turn into coarse dots even when you move close.

The shader turns the quads toward the viewpoint every frame (billboarding), so the quads always face you even when you tilt the object. The correspondence between the four corners and the image stays as it was at capture time, so it fits perfectly when seen from the captured direction, and the pasted image looks slightly shifted when tilted a lot.

That is why the object is made more transparent the further you are from its front. The front is the surface direction obtained from the point cloud (the face of a book or a screen), and transparency is decided by the dot product between it and the direction seen from the current viewpoint: almost transparent edge-on, and invisible from behind. Because it is based on the surface direction rather than the direction of the capturing camera, even an object shot from an angle, like a book on a desk, fades at the same angle whether you tilt it up, down, left, or right from a pose where the surface faces you. When you grab and move or rotate the object, its front moves with it, so turning the surface back toward you makes it visible again.

The grabbing mechanism uses components of the Meta XR Interaction SDK as they are. The prefab `GrabbableSearchResult` has `Grabbable` / `GrabFreeTransformer` / `GrabInteractable` attached, and it can be grabbed with the controller's `ControllerGrabInteractor` in the scene's interaction rig. The prefab `GrabbableCopy` in the `2-SpatialSearchLiteFrame(Experimental)` scene also has `HandGrabInteractable` attached, so it can be grabbed with the hand's `HandGrabInteractor` too.

The point indicated with the hand frame is remembered as a real-world position (where the ray hit the depth). After the hands are lowered, this position is projected again onto the captured image, so the hands that made the frame do not appear in the captured image. The ray is cast from the center of the head, so it is slightly offset from the center of the frame as seen with both eyes, but this is not noticeable if you align it with the object while watching the dot.

The server side is in `server/`. `server.py` handles the WebSocket communication with Unity, and `sam3_comfyui.py` handles calls to ComfyUI. When it receives a point on the image (`{"type": "point", "x": .., "y": .., "image": ..}`, pixels with the origin at the top left) instead of a search term, it returns the mask of the one object at that point, using SAM 3.1's point prompt (`positive_coords` of `SAM3_Detect`).
