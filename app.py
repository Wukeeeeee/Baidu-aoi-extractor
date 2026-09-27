"""
百度地图 AOI 建筑轮廓 & POI 提取工具 - Web 后端服务
纯 Python 驱动 (FastAPI + Uvicorn + Leaflet)
"""

import os
import sys
import io
import time
import json
import threading
import webbrowser
import datetime
from typing import Optional, List, Dict, Any

import uvicorn
from fastapi import FastAPI, UploadFile, File, HTTPException
from fastapi.responses import HTMLResponse, JSONResponse
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
import pandas as pd

from core.crawler import search_place_candidates, extract_single_aoi
from core.exporter import (
    export_aoi_geojson,
    export_aoi_shp,
    export_aoi_csv,
    export_main_poi_csv,
    export_pois_csv,
    export_pois_shp,
    export_summary_excel,
)

app = FastAPI(title="Baidu AOI Extractor")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)

# 默认导出目录
_BASE_DIR = os.path.dirname(os.path.abspath(__file__))
_OUTPUT_DIR = os.path.join(_BASE_DIR, "output")
os.makedirs(_OUTPUT_DIR, exist_ok=True)


class SearchRequest(BaseModel):
    query: str


class ExtractSingleRequest(BaseModel):
    name: str
    uid: Optional[str] = None
    headless: bool = True


class ExportItemRequest(BaseModel):
    task: Dict[str, Any]
    type: str  # 'aoi', 'pois', 'main_poi'


class ExportSummaryRequest(BaseModel):
    tasks: List[Dict[str, Any]]


@app.get("/", response_class=HTMLResponse)
async def index():
    html_path = os.path.join(_BASE_DIR, "web", "index.html")
    if os.path.exists(html_path):
        with open(html_path, "r", encoding="utf-8") as f:
            return f.read()
    return "<h1>Web UI not found.</h1>"


@app.post("/api/search")
def api_search(req: SearchRequest):
    candidates = search_place_candidates(req.query, headless=True)
    return {"success": True, "candidates": candidates}


@app.post("/api/extract-single")
def api_extract_single(req: ExtractSingleRequest):
    result = extract_single_aoi(
        place_name=req.name,
        uid=req.uid,
        headless=req.headless
    )
    return result


@app.post("/api/import-excel")
async def api_import_excel(file: UploadFile = File(...)):
    try:
        contents = await file.read()
        filename = file.filename.lower()
        if filename.endswith(".csv"):
            try:
                df = pd.read_csv(io.BytesIO(contents), encoding="utf-8")
            except UnicodeDecodeError:
                df = pd.read_csv(io.BytesIO(contents), encoding="gbk")
        else:
            df = pd.read_excel(io.BytesIO(contents))

        # 智能匹配列名
        name_col = None
        uid_col = None
        addr_col = None

        for col in df.columns:
            c_lower = str(col).strip().lower()
            if not name_col and any(k in c_lower for k in ["名称", "地点", "name", "poi", "title", "建筑"]):
                name_col = col
            elif not uid_col and any(k in c_lower for k in ["uid", "id", "code"]):
                uid_col = col
            elif not addr_col and any(k in c_lower for k in ["地址", "address", "addr"]):
                addr_col = col

        if not name_col:
            name_col = df.columns[0]

        rows = []
        for _, row in df.iterrows():
            val = str(row[name_col]).strip() if pd.notna(row[name_col]) else ""
            if val and val != "nan":
                uid_val = str(row[uid_col]).strip() if uid_col and pd.notna(row[uid_col]) else ""
                addr_val = str(row[addr_col]).strip() if addr_col and pd.notna(row[addr_col]) else ""
                rows.append({
                    "name": val,
                    "uid": uid_val if uid_val != "nan" else "",
                    "address": addr_val if addr_val != "nan" else "",
                    "selected": True
                })

        return {"success": True, "rows": rows}
    except Exception as e:
        return {"success": False, "error": str(e)}


@app.post("/api/export-item")
def api_export_item(req: ExportItemRequest):
    task = req.task
    exp_type = req.type
    place_name = task.get("name", "未命名地点")
    safe_name = "".join([c for c in place_name if c not in r'\/:*?"<>|']).strip()

    target_dir = os.path.join(_OUTPUT_DIR, safe_name)
    os.makedirs(target_dir, exist_ok=True)

    try:
        if exp_type == "aoi":
            pts = task.get("wgs84_points", [])
            if not pts:
                return {"success": False, "error": "该记录无 AOI 多边形数据"}
            export_aoi_geojson(pts, safe_name, os.path.join(target_dir, f"{safe_name}_AOI范围.geojson"))
            export_aoi_csv(pts, os.path.join(target_dir, f"{safe_name}_AOI范围.csv"))
            export_aoi_shp(pts, safe_name, os.path.join(target_dir, f"{safe_name}_AOI范围.shp"))

        elif exp_type == "pois":
            pois = task.get("pois", [])
            if not pois:
                return {"success": False, "error": "该记录无周边 POI 数据"}
            export_pois_csv(pois, os.path.join(target_dir, f"{safe_name}_POI点.csv"))
            export_pois_shp(pois, os.path.join(target_dir, f"{safe_name}_POI点.shp"))

        elif exp_type == "main_poi":
            main_p = task.get("main_poi")
            if not main_p or main_p.get("longitude") is None:
                return {"success": False, "error": "该记录无核心 POI 点位坐标"}
            export_main_poi_csv(
                name=main_p.get("name", safe_name),
                lon=float(main_p["longitude"]),
                lat=float(main_p["latitude"]),
                output_path=os.path.join(target_dir, f"{safe_name}_该点POI.csv")
            )

        return {"success": True, "output_path": target_dir}
    except Exception as e:
        return {"success": False, "error": str(e)}


@app.post("/api/export-summary")
def api_export_summary(req: ExportSummaryRequest):
    tasks = req.tasks
    records = []
    for t in tasks:
        records.append({
            "地点名称": t.get("name", ""),
            "UID": t.get("uid", ""),
            "地址": t.get("address", ""),
            "提取状态": t.get("status", ""),
            "AOI顶点数": t.get("points_count", 0),
            "周边POI数": t.get("pois_count", 0),
            "经度(WGS84)": t.get("main_poi", {}).get("longitude", "") if t.get("main_poi") else "",
            "纬度(WGS84)": t.get("main_poi", {}).get("latitude", "") if t.get("main_poi") else "",
            "错误信息": t.get("error", "")
        })

    ts = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
    out_file = os.path.join(_OUTPUT_DIR, f"AOI导出汇总_{ts}.xlsx")
    ok = export_summary_excel(records, out_file)
    if ok:
        return {"success": True, "output_path": out_file}
    return {"success": False, "error": "汇总表生成失败"}


@app.post("/api/open-output-dir")
def api_open_output_dir():
    try:
        if sys.platform == "win32":
            os.startfile(_OUTPUT_DIR)
        return {"success": True}
    except Exception as e:
        return {"success": False, "error": str(e)}


def open_browser(url: str):
    time.sleep(1.2)
    try:
        webbrowser.open(url)
    except Exception:
        pass


def find_available_port(start_port: int = 8765, max_attempts: int = 20) -> int:
    """自动探测可用端口，若被占用则递增回退"""
    import socket
    for p in range(start_port, start_port + max_attempts):
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
            try:
                s.bind(("127.0.0.1", p))
                return p
            except OSError:
                continue
    return start_port


if __name__ == "__main__":
    port = find_available_port(8765)
    print("\n" + "=" * 60)
    print("  百度地图 AOI 建筑轮廓 & POI 提取工具 (Python Web 版)")
    print(f"  服务已启动: http://127.0.0.1:{port}")
    print("  正在自动打开浏览器...")
    print("=" * 60 + "\n")

    threading.Thread(target=open_browser, args=(f"http://127.0.0.1:{port}",), daemon=True).start()
    uvicorn.run(app, host="127.0.0.1", port=port, log_level="info")
