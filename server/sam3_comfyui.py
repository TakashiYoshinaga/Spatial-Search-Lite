"""ComfyUIのSAM3で画像から物体のマスクを得る。

ComfyUIのHTTP APIを使う。ComfyUIは起動しておくだけでよく、画面で操作する必要はない。

    POST /upload/image   画像を置く
    POST /prompt         workflowを実行キューへ入れる
    GET  /history/<id>   実行が終わるのを待つ
    GET  /view           出力されたマスクPNGを取り出す

使うnodeはすべてComfyUI本体に入っているもので、custom nodeは不要。

    LoadImage ─┐
               ├→ SAM3_Detect ─ masks ─→ MaskToImage → PreviewImage   (マスク画像)
    Checkpoint ┤                └ bboxes ──────────→ PreviewAny     (スコアのJSON)
      Loader   └ CLIP → CLIPTextEncode(prompt) ┘
"""

import asyncio
import json
import time
import uuid
from dataclasses import dataclass
from urllib.parse import urlencode
from urllib.request import Request, urlopen

# workflow内のnode番号
LOAD_IMAGE = "1"
CHECKPOINT = "2"
TEXT_ENCODE = "3"
DETECT = "4"
MASK_TO_IMAGE = "5"
MASK_OUTPUT = "6"
DETECTION_OUTPUT = "7"

INPUT_IMAGE_NAME = "spatial-search-lite.png"
"""ComfyUIのinputフォルダへ置く名前。毎回上書きするのでファイルは増えない。"""


@dataclass
class Detection:
    score: float
    mask_png: bytes  # 送った画像と同じサイズ。白い部分が物体


@dataclass
class Sam3Settings:
    comfyui_url: str = "http://127.0.0.1:8188"
    checkpoint: str = "sam3.1_multiplex_fp16.safetensors"
    threshold: float = 0.5  # これより低いスコアの検出は捨てる
    max_detections: int = 8  # 1回の検索で返す物体の最大数
    refine_iterations: int = 2  # マスクの輪郭を整える回数
    timeout_seconds: float = 120.0


class Sam3Client:
    def __init__(self, settings: Sam3Settings) -> None:
        self.settings = settings

    async def segment(self, image_png: bytes, query: str) -> list[Detection]:
        """画像の中から query に当たる物体を探し、物体ごとのマスクを返す。"""
        image_name = await self._upload_image(image_png)

        # まずスコアだけを取る。
        # 1件も見つからないときにマスク出力(PreviewImage)まで実行すると、空の画像で
        # ComfyUIが落ちるため。2回目は共通部分がComfyUIのキャッシュに乗るので、
        # SAM3の推論は1回ぶんしか走らない。
        outputs = await self._run(self._workflow(image_name, query, with_masks=False))
        if not _read_scores(outputs):
            return []

        outputs = await self._run(self._workflow(image_name, query, with_masks=True))
        scores = _read_scores(outputs)
        mask_files = outputs[MASK_OUTPUT]["images"]
        if len(scores) != len(mask_files):
            raise RuntimeError(f"got {len(mask_files)} masks but {len(scores)} scores")

        detections = []
        for score, mask_file in zip(scores, mask_files):
            mask_png = await self._fetch_image(mask_file)
            detections.append(Detection(score=score, mask_png=mask_png))
        return detections

    async def segment_at(self, image_png: bytes, x: int, y: int) -> Detection:
        """画像の (x, y) に写っている物体1つのマスクを返す。座標は画像のピクセルで、左上が原点。

        点で指したときは、SAMが候補の中から一番確からしいマスクを1枚だけ返す。
        必ず1枚返るので、segment のようにスコアを先に確かめる必要はない。
        点のときはSAMがスコアを出さないので score は 1.0 にする。
        """
        image_name = await self._upload_image(image_png)
        outputs = await self._run(self._point_workflow(image_name, x, y))
        mask_files = outputs[MASK_OUTPUT]["images"]
        if len(mask_files) != 1:
            raise RuntimeError(f"expected 1 mask but got {len(mask_files)}")
        return Detection(score=1.0, mask_png=await self._fetch_image(mask_files[0]))

    def _point_workflow(self, image_name: str, x: int, y: int) -> dict:
        """点で指した物体を切り出すworkflow。テキストを使わないのでCLIPTextEncodeは無い。"""
        s = self.settings
        return {
            LOAD_IMAGE: {"class_type": "LoadImage", "inputs": {"image": image_name}},
            CHECKPOINT: {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": s.checkpoint}},
            DETECT: {
                "class_type": "SAM3_Detect",
                "inputs": {
                    "model": [CHECKPOINT, 0],
                    "image": [LOAD_IMAGE, 0],
                    "positive_coords": json.dumps([{"x": x, "y": y}]),
                    "threshold": s.threshold,
                    "refine_iterations": s.refine_iterations,
                    "individual_masks": True,
                },
            },
            MASK_TO_IMAGE: {"class_type": "MaskToImage", "inputs": {"mask": [DETECT, 0]}},
            MASK_OUTPUT: {"class_type": "PreviewImage", "inputs": {"images": [MASK_TO_IMAGE, 0]}},
        }

    def _workflow(self, image_name: str, query: str, *, with_masks: bool) -> dict:
        s = self.settings
        workflow = {
            LOAD_IMAGE: {"class_type": "LoadImage", "inputs": {"image": image_name}},
            CHECKPOINT: {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": s.checkpoint}},
            TEXT_ENCODE: {
                "class_type": "CLIPTextEncode",
                "inputs": {"text": _sam3_prompt(query, s.max_detections), "clip": [CHECKPOINT, 1]},
            },
            DETECT: {
                "class_type": "SAM3_Detect",
                "inputs": {
                    "model": [CHECKPOINT, 0],
                    "image": [LOAD_IMAGE, 0],
                    "conditioning": [TEXT_ENCODE, 0],
                    "threshold": s.threshold,
                    "refine_iterations": s.refine_iterations,
                    "individual_masks": True,  # 物体ごとに1枚ずつマスクを出す
                },
            },
            DETECTION_OUTPUT: {"class_type": "PreviewAny", "inputs": {"source": [DETECT, 1]}},
        }
        if with_masks:
            workflow[MASK_TO_IMAGE] = {"class_type": "MaskToImage", "inputs": {"mask": [DETECT, 0]}}
            workflow[MASK_OUTPUT] = {"class_type": "PreviewImage", "inputs": {"images": [MASK_TO_IMAGE, 0]}}
        return workflow

    async def _upload_image(self, png: bytes) -> str:
        body, content_type = _multipart_image(png, INPUT_IMAGE_NAME)
        response = json.loads(await self._request("/upload/image", body, content_type))
        subfolder = response.get("subfolder") or ""
        return f"{subfolder}/{response['name']}" if subfolder else response["name"]

    async def _run(self, workflow: dict) -> dict:
        """workflowを実行し、終わったら outputs を返す。"""
        body = json.dumps({"prompt": workflow}).encode("utf-8")
        prompt_id = json.loads(await self._request("/prompt", body, "application/json"))["prompt_id"]

        deadline = time.monotonic() + self.settings.timeout_seconds
        while time.monotonic() < deadline:
            history = json.loads(await self._request(f"/history/{prompt_id}"))
            entry = history.get(prompt_id)
            if entry is not None:
                status = entry.get("status") or {}
                if status.get("status_str") == "error":
                    raise RuntimeError(f"ComfyUI execution failed: {_error_summary(status)}")
                if status.get("completed"):
                    return entry.get("outputs") or {}
            await asyncio.sleep(0.25)
        raise TimeoutError(f"ComfyUI did not finish within {self.settings.timeout_seconds} seconds")

    async def _fetch_image(self, file: dict) -> bytes:
        query = urlencode({
            "filename": file.get("filename", ""),
            "subfolder": file.get("subfolder", ""),
            "type": file.get("type", "output"),
        })
        return await self._request(f"/view?{query}")

    async def _request(self, path: str, body: bytes | None = None, content_type: str | None = None) -> bytes:
        url = self.settings.comfyui_url.rstrip("/") + path
        headers = {"Content-Type": content_type} if content_type else {}

        def send() -> bytes:
            with urlopen(Request(url, data=body, headers=headers), timeout=30) as response:
                return response.read()

        # urllibは同期なので、別スレッドで動かしてWebSocketの処理を止めない。
        return await asyncio.to_thread(send)


def _sam3_prompt(query: str, max_detections: int) -> str:
    """SAM3のプロンプトは「label:N」で最大N個を探す。:N を付けないと1個しか返さない。"""
    label = query.strip()
    if not label:
        raise ValueError("query is empty")
    # , と : はSAM3のプロンプト記法で意味を持つので、検索語には使えない。
    if "," in label or ":" in label:
        raise ValueError("query must not contain ',' or ':'")
    return f"{label}:{max_detections}"


def _read_scores(outputs: dict) -> list[float]:
    """PreviewAnyが出したJSONから、検出ごとのスコアを読む。"""
    text = outputs[DETECTION_OUTPUT]["text"][0]
    frames = json.loads(text)  # 画像ごとのリスト。1枚しか送っていないので先頭だけ見る
    if not frames:
        return []
    return [float(detection.get("score", 0.0)) for detection in frames[0]]


def _error_summary(status: dict) -> str:
    for name, detail in status.get("messages") or []:
        if name == "execution_error":
            return f"{detail.get('node_type')}: {detail.get('exception_message')}"
    return "unknown error"


def _multipart_image(png: bytes, filename: str) -> tuple[bytes, str]:
    """/upload/image へ送る multipart/form-data を作る。"""
    boundary = uuid.uuid4().hex
    lines = [
        f"--{boundary}".encode(),
        b'Content-Disposition: form-data; name="overwrite"',
        b"",
        b"true",
        f"--{boundary}".encode(),
        f'Content-Disposition: form-data; name="image"; filename="{filename}"'.encode(),
        b"Content-Type: image/png",
        b"",
        png,
        f"--{boundary}--".encode(),
        b"",
    ]
    return b"\r\n".join(lines), f"multipart/form-data; boundary={boundary}"
