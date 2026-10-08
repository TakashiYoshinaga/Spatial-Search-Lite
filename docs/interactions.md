# Grab and copy with a finger frame

English | [日本語](interactions_JP.md)

[Back to the main guide](../README.md) · [Settings and how it works](development.md)

Start ComfyUI and the Python server, and connect from the Quest app, before you begin. How to choose a scene and build it is in [README's "Build the Quest app and install it"](../README.md#3-build-the-quest-app-and-install-it).

- **Grab found objects:** `1-SpatialSearchLiteGrab` or `2-SpatialSearchLiteFrame(Experimental)`
- **Copy an object with a finger frame:** `2-SpatialSearchLiteFrame(Experimental)` (experimental feature)

## Grab found objects

In the `1-SpatialSearchLiteGrab` scene, each found object becomes its own surface that you can grab and move with the controllers. The surface is textured with the camera image from the moment of capture.

1. Bring a controller close to the object. When it is within grabbing distance, the object turns whitish
2. Pull the **trigger button** (the button you pull with your index finger) to grab it. While held, the object takes on a yellowish tint
3. Move your hand while holding the trigger and the object follows. Release and it stays where it is

If you grab the same object with both controllers, you can also resize it. In this scene you cannot grab with your hands (hand tracking); in the `2-SpatialSearchLiteFrame(Experimental)` scene you can also grab with your hands.

## Copy an object with a finger frame

In the `2-SpatialSearchLiteFrame(Experimental)` scene, make the frame you would use to take a photo with both hands and point at an object, and only that one object is cut out and duplicated at the position where it was captured. No search term is needed. You can grab and move the copy with either your hands or the controllers.

Use the controllers until you are connected to the server, then put them down.

1. In front of your face (about with your arms slightly extended), make an L shape with the thumb and index finger of both hands. Curl the other fingers loosely
   - The left hand is the **top-left** corner of the frame: point the index finger to the right and the thumb down
   - The right hand is the **bottom-right** corner of the frame: point the index finger to the left and the thumb up
2. When it is recognized as a frame, a white rectangle appears, and a dot appears on the surface of whatever is beyond the center of the frame. Aim the dot at the middle of the object
3. Hold still for about 2 seconds. The ring around the dot completes a full circle and turns green, and `Lower your hands` is shown (lock)
4. Lower both hands so the camera cannot see them. When the hands are no longer visible, the capture is taken and `Copying...` is shown
5. After a few seconds, a copy of the object you pointed at appears at that location. Pinch or grab it with your hand and move it wherever you like

- When the dot is **orange**, you are pointing at the edge of the object or a hole. It will not lock, so move the dot to the middle of the object
- If you do not lower your hands within 3 seconds after the lock, it is canceled (`Canceled`). It is also canceled if the locked point leaves your field of view (`Lost the target`)
- It is fine if your head moves while you lower your hands. The locked point is remembered as a position in the real world, and that position is pointed at on the captured image
- Only a part of the object (just a face, just a handle, and so on) may be cut out. Pointing at the middle of the object makes it easier to get the whole thing
- In this scene too, you can search by word from **Search** on the left controller. Making a new copy or searching clears the previous result

## Troubleshooting

| Symptom | What to check |
|---|---|
| The object looks see-through or disappears | The object becomes more transparent the further you are from its front (surface direction), seen from the side or behind. Turn the surface back toward you, or adjust the material's Fade Start / Fade End |
| You cannot grab the point cloud | Check that you are in the `1-SpatialSearchLiteGrab` / `2-SpatialSearchLiteFrame(Experimental)` scene, and pull the trigger button with the controller inside the point cloud. Hands can grab only in the `2-SpatialSearchLiteFrame(Experimental)` scene |
| The white rectangle does not appear when you make the frame with your fingers | Put the controllers down and check that your hands are recognized (if hand tracking is turned off in the Quest's settings, turn it back on). Also check that the left hand is top-left and the right hand is bottom-right, with the index finger horizontal and the thumb vertical (down for the left hand, up for the right hand) |
| The frame appears but the dot does not | Check that the distance to the object beyond the frame is within the range of Search Capture's Min / Max Depth Meters (default 0.1–5 m) |
| It stays at `Copying...` | If the capture overlaps with waiting for the result of a word search, it is ignored. It returns to normal after 30 seconds, so try again |

To change fine values for the display and operation, see [Configurable settings](development.md#configurable-settings).
