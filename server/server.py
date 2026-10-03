"""Spatial-Search-Lite のサーバー。

Unityから画像と検索語を受け取り、ComfyUIのSAM3で物体を探して、物体ごとのマスクを返す。

Unity → サーバー
    {"type": "search", "query": "monitor", "image": "<PNGのbase64>"}
    {"type": "point", "x": 640, "y": 480, "image": "<PNGのbase64>"}   (x, y は画像のピクセル、左上が原点)

point のときは、その点に写っている物体1つだけを返す（detections は1件、query は空）。

サーバー → Unity（message は英語。Quest側に日本語フォントが無いため）
    {"type": "result", "query": "monitor", "detections": [{"score": 0.93, "mask": "<PNGのbase64>"}, ...]}
    {"type": "error", "message": "理由"}

起動:
    python server/server.py --host 0.0.0.0
"""

import argparse
import asyncio
import base64
import json
import logging

from websockets.asyncio.server import serve

from sam3_comfyui import Sam3Client, Sam3Settings

MAX_MESSAGE_BYTES = 32 * 1024 * 1024
"""受け取るメッセージの上限。PNGのbase64は1MBを超えるので、websocketsの既定値(1MB)では足りない。"""

log = logging.getLogger("spatial-search-lite")


async def handle(websocket, sam3: Sam3Client) -> None:
    log.info("connected: %s", websocket.remote_address)
    async for message in websocket:
        response = await search(message, sam3)
        await websocket.send(json.dumps(response))
    log.info("disconnected: %s", websocket.remote_address)


async def search(message: str, sam3: Sam3Client) -> dict:
    try:
        request = json.loads(message)
        image_png = base64.b64decode(request["image"])

        if request.get("type") == "point":
            query = ""
            x, y = int(request["x"]), int(request["y"])
            log.info("point: (%d, %d) image=%d bytes", x, y, len(image_png))
            detections = [await sam3.segment_at(image_png, x, y)]
        else:
            query = request["query"]
            log.info("search: query=%r image=%d bytes", query, len(image_png))
            detections = await sam3.segment(image_png, query)
            log.info("found %d: scores=%s", len(detections), [round(d.score, 2) for d in detections])

        return {
            "type": "result",
            "query": query,
            "detections": [
                {"score": d.score, "mask": base64.b64encode(d.mask_png).decode("ascii")}
                for d in detections
            ],
        }
    except Exception as error:
        log.exception("search failed")
        return {"type": "error", "message": str(error)}


async def main() -> None:
    parser = argparse.ArgumentParser(description="Spatial-Search-Lite server")
    parser.add_argument("--host", default="127.0.0.1", help="Questから繋ぐときは 0.0.0.0")
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--comfyui", default="http://127.0.0.1:8188", help="ComfyUIのURL")
    parser.add_argument("--checkpoint", default="sam3.1_multiplex_fp16.safetensors")
    parser.add_argument("--threshold", type=float, default=0.5, help="これより低いスコアの検出は捨てる")
    parser.add_argument("--max-detections", type=int, default=8)
    args = parser.parse_args()

    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")

    sam3 = Sam3Client(Sam3Settings(
        comfyui_url=args.comfyui,
        checkpoint=args.checkpoint,
        threshold=args.threshold,
        max_detections=args.max_detections,
    ))

    async with serve(lambda ws: handle(ws, sam3), args.host, args.port, max_size=MAX_MESSAGE_BYTES):
        log.info("listening on ws://%s:%d/  (ComfyUI: %s)", args.host, args.port, args.comfyui)
        await asyncio.Future()  # Ctrl+C まで動かし続ける


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass
