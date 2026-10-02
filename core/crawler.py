"""
核心爬虫与 Playwright 自动化拦截模块
支持关键字联想、候选 POI 解析、AOI 轮廓抓取及周围 POI 提取
"""

import os
import json
import time
import hashlib
import re
from typing import List, Dict, Any, Optional, Tuple
from playwright.sync_api import sync_playwright

from core.converter import (
    parse_geo_to_points,
    convert_points_to_wgs84,
    extract_pois_from_raw_json,
    bd09mc_to_wgs84,
    bd09_to_wgs84,
    smart_convert_coords,
)
from core.exporter import export_aoi_geojson

# 本地缓存目录
_CACHE_DIR = os.path.join(os.path.dirname(os.path.dirname(__file__)), ".cache")


def _get_cache(prefix: str, key: str) -> Optional[Any]:
    try:
        os.makedirs(_CACHE_DIR, exist_ok=True)
        h = hashlib.md5(key.encode("utf-8")).hexdigest()[:16]
        path = os.path.join(_CACHE_DIR, f"{prefix}_{h}.json")
        if os.path.exists(path):
            with open(path, "r", encoding="utf-8") as f:
                return json.load(f)
    except Exception:
        pass
    return None


def _set_cache(prefix: str, key: str, data: Any):
    try:
        os.makedirs(_CACHE_DIR, exist_ok=True)
        h = hashlib.md5(key.encode("utf-8")).hexdigest()[:16]
        path = os.path.join(_CACHE_DIR, f"{prefix}_{h}.json")
        with open(path, "w", encoding="utf-8") as f:
            json.dump(data, f, ensure_ascii=False, indent=2)
    except Exception:
        pass


def _launch_browser(p, headless: bool = True):
    """启动浏览器，优先使用系统已安装 Chrome，兜底 Playwright Chromium"""
    opts = dict(
        headless=headless,
        args=[
            "--no-sandbox",
            "--disable-setuid-sandbox",
            "--disable-blink-features=AutomationControlled",
            "--disable-dev-shm-usage",
            "--disable-gpu",
            "--disable-software-rasterizer",
        ],
    )
    try:
        return p.chromium.launch(channel="chrome", **opts)
    except Exception:
        return p.chromium.launch(**opts)


def _create_context(browser):
    """创建反检测浏览器上下文"""
    context = browser.new_context(
        user_agent=(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
            "AppleWebKit/537.36 (KHTML, like Gecko) "
            "Chrome/124.0.0.0 Safari/537.36"
        ),
        viewport={"width": 1920, "height": 1080},
        locale="zh-CN",
    )
    return context


def _add_anti_detect_script(page):
    page.add_init_script(
        "Object.defineProperty(navigator, 'webdriver', { get: () => false });"
    )


def extract_candidates_from_search_json(data: Any) -> List[Dict[str, Any]]:
    """从百度搜索响应中提取所有候选 POI"""
    candidates = []
    seen = set()

    try:
        content = data.get("content", [])
        if isinstance(content, dict):
            content = [content]
        elif not isinstance(content, list):
            content = []

        for item in content:
            if not isinstance(item, dict):
                continue

            name = item.get("name") or item.get("std_tag") or ""
            uid = item.get("uid") or item.get("primary_uid") or ""
            addr = item.get("addr") or item.get("address") or ""
            di_tag = item.get("di_tag") or item.get("tag") or item.get("std_tag") or ""

            px = item.get("x") or item.get("diPointX")
            py = item.get("y") or item.get("diPointY")

            lon, lat = None, None
            if px is not None and py is not None:
                try:
                    lon, lat = smart_convert_coords(float(px), float(py))
                except Exception:
                    pass

            if name:
                key = f"{name}_{uid}"
                if key not in seen:
                    seen.add(key)
                    candidates.append({
                        "name": str(name),
                        "uid": str(uid),
                        "address": str(addr),
                        "tag": str(di_tag),
                        "longitude": lon,
                        "latitude": lat
                    })
    except Exception:
        pass

    return candidates


def extract_geo_from_detail_json(data: Any) -> Optional[str]:
    """从百度详情响应 JSON 中提取 guoke_geo / geo 字符串"""
    if not isinstance(data, dict):
        return None

    # 路径1: content.ext.detail_info.guoke_geo.geo
    try:
        content = data.get("content", {})
        if isinstance(content, list):
            content = content[0] if content else {}
        if isinstance(content, dict):
            # 尝试多种层级
            ext = content.get("ext", {})
            if isinstance(ext, dict):
                dinfo = ext.get("detail_info", {})
                if isinstance(dinfo, dict):
                    ggeo = dinfo.get("guoke_geo", {})
                    if isinstance(ggeo, dict) and ggeo.get("geo"):
                        return str(ggeo["geo"])
                    if dinfo.get("geo"):
                        return str(dinfo["geo"])
            if content.get("geo"):
                return str(content["geo"])
    except Exception:
        pass

    # 路径2: 直接递归搜索包含 'geo' 键且长度显著的字符串
    def _search_geo(node):
        if isinstance(node, dict):
            if "geo" in node and isinstance(node["geo"], str) and "|" in node["geo"] and len(node["geo"]) > 20:
                return node["geo"]
            if "guoke_geo" in node and isinstance(node["guoke_geo"], dict):
                g = node["guoke_geo"].get("geo")
                if isinstance(g, str) and "|" in g:
                    return g
            for v in node.values():
                res = _search_geo(v)
                if res:
                    return res
        elif isinstance(node, list):
            for item in node:
                res = _search_geo(item)
                if res:
                    return res
        return None

    return _search_geo(data)


def search_place_candidates(query: str, headless: bool = True, timeout_ms: int = 15000, logger=None) -> List[Dict[str, Any]]:
    """搜索地点，返回候选列表"""
    q = (query or "").strip()
    if not q:
        return []

def fetch_fast_suggestions(query: str) -> List[Dict[str, Any]]:
    """快速从百度地图全国联想接口获取候选地点 (耗时 < 100ms)"""
    import requests
    import urllib.parse
    q = (query or "").strip()
    if not q:
        return []
    try:
        headers = {
            "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
            "Referer": "https://map.baidu.com/"
        }
        url = f"https://map.baidu.com/su?wd={urllib.parse.quote(q)}&cid=1&type=0"
        resp = requests.get(url, headers=headers, timeout=4)
        resp.encoding = "utf-8"
        text = resp.text.strip()
        if "(" in text and text.endswith(")"):
            text = text[text.find("(") + 1 : text.rfind(")")]
        data = json.loads(text)
        sugs = data.get("s", [])
        candidates = []
        for s in sugs:
            # 格式: '城市$区$$名称$cid$'
            parts = s.split("$")
            if len(parts) >= 4:
                city = parts[0]
                district = parts[1]
                name = parts[3]
                addr = f"{city} {district}".strip()
                if name:
                    candidates.append({
                        "name": name,
                        "uid": "",
                        "address": addr,
                        "tag": "联想地点",
                        "longitude": None,
                        "latitude": None
                    })
        return candidates
    except Exception:
        return []


def search_place_candidates(query: str, headless: bool = True, timeout_ms: int = 15000, logger=None) -> List[Dict[str, Any]]:
    """搜索地点，返回候选列表（多层融合策略：快速全国联想 + 浏览器深度检索）"""
    q = (query or "").strip()
    if not q:
        return []

    cached = _get_cache("search", q)
    if cached:
        if logger:
            logger(f"命中本地搜索缓存: {q} ({len(cached)} 个候选)")
        return cached

    candidates = []
    if logger:
        logger(f"正在搜索地点候选: {q} ...")

    # 1. 优先调用快速联想接口 (毫秒级响应)
    fast_cands = fetch_fast_suggestions(q)
    if fast_cands:
        candidates.extend(fast_cands)

    # 2. 调用 Playwright 深度检索获取丰富属性与精确 UID
    try:
        with sync_playwright() as p:
            browser = _launch_browser(p, headless=headless)
            context = _create_context(browser)
            page = context.new_page()
            _add_anti_detect_script(page)

            def on_resp(response):
                if "qt=s" in response.url or "qt=con" in response.url or "qt=cur" in response.url:
                    try:
                        data = response.json()
                        cands = extract_candidates_from_search_json(data)
                        if cands:
                            candidates.extend(cands)
                    except Exception:
                        pass

            page.on("response", on_resp)
            page.goto("https://map.baidu.com/", wait_until="domcontentloaded", timeout=timeout_ms)
            page.wait_for_timeout(800)

            sb = page.locator("#sole-input")
            sb.wait_for(state="visible", timeout=8000)
            sb.click()
            page.wait_for_timeout(150)
            sb.fill(q)
            page.wait_for_timeout(200)
            
            search_btn = page.locator("#search-button")
            if search_btn.is_visible():
                search_btn.click()
            else:
                page.keyboard.press("Enter")
            page.wait_for_timeout(3000)

            # 兜底：如果没拦截到候选，点击首个搜索联想条目
            if not candidates:
                try:
                    link = page.locator("a").filter(has_text=q[:2]).first
                    if link.is_visible():
                        link.click()
                        page.wait_for_timeout(2000)
                except Exception:
                    pass

            browser.close()
    except Exception as e:
        if logger:
            logger(f"搜索候选异常: {e}")

    # 去重并排序（有 UID 或详细地址的排在前面）
    unique = []
    seen = set()
    # 优先保留有 UID 的
    candidates.sort(key=lambda x: (1 if x.get("uid") else 0), reverse=True)
    for c in candidates:
        k = c["name"]
        if k not in seen:
            seen.add(k)
            unique.append(c)

    if unique:
        _set_cache("search", q, unique)
    return unique


def extract_single_aoi(
    place_name: str,
    uid: Optional[str] = None,
    headless: bool = True,
    debug: bool = False,
    logger=None
) -> Dict[str, Any]:
    """
    提取单个地点的 AOI 轮廓及周边 POI
    """
    result = {
        "success": False,
        "name": place_name,
        "uid": uid or "",
        "geo_raw": "",
        "points_count": 0,
        "wgs84_points": [],
        "geojson": None,
        "pois": [],
        "main_poi": None,
        "error": ""
    }

    cache_key = f"{place_name}_{uid or ''}"
    cached = _get_cache("extract", cache_key)
    if cached and cached.get("success"):
        if logger:
            logger(f"命中本地提取缓存: {place_name}")
        return cached

    captured = {
        "geo": None,
        "uid": uid,
        "detail_json": None,
        "all_json_responses": []
    }

    if logger:
        logger(f"开始提取 [{place_name}] (UID: {uid or '待检索'})...")

    try:
        with sync_playwright() as p:
            browser = _launch_browser(p, headless=headless)
            context = _create_context(browser)
            page = context.new_page()
            _add_anti_detect_script(page)

            def on_resp(response):
                url = response.url
                # 捕获详情响应
                if "detailConInfo" in url or "qt=ext" in url or "qt=inf" in url:
                    try:
                        data = response.json()
                        captured["all_json_responses"].append(data)
                        geo = extract_geo_from_detail_json(data)
                        if geo and not captured["geo"]:
                            captured["geo"] = geo
                            if logger:
                                logger(f"成功拦截到 AOI 轮廓数据!")
                        if not captured["detail_json"]:
                            captured["detail_json"] = data
                    except Exception:
                        pass

                # 捕获搜索响应以获取 UID
                if "qt=s" in url and not captured["uid"]:
                    try:
                        data = response.json()
                        captured["all_json_responses"].append(data)
                        cands = extract_candidates_from_search_json(data)
                        if cands and cands[0].get("uid"):
                            captured["uid"] = cands[0]["uid"]
                            if logger:
                                logger(f"从搜索结果获取到 UID: {captured['uid']}")
                    except Exception:
                        pass

            page.on("response", on_resp)
            page.goto("https://map.baidu.com/", wait_until="domcontentloaded", timeout=20000)
            page.wait_for_timeout(1000)

            # 搜索框输入
            sb = page.locator("#sole-input")
            sb.wait_for(state="visible", timeout=8000)
            sb.click()
            page.wait_for_timeout(200)
            sb.fill(place_name)
            page.wait_for_timeout(300)
            page.keyboard.press("Enter")
            page.wait_for_timeout(3500)

            target_uid = captured["uid"] or uid

            # 若有 UID 且未直接拿到 geo，发起直接请求
            if target_uid and not captured["geo"]:
                api_url = (
                    f"https://map.baidu.com/?uid={target_uid}"
                    f"&ugc_type=3&ugc_ver=1&qt=detailConInfo&device_ratio=1&compat=1"
                )
                if logger:
                    logger(f"请求详情 API: uid={target_uid}")

                for attempt in range(2):
                    try:
                        resp = context.request.get(api_url, timeout=12000)
                        if resp.ok:
                            data = resp.json()
                            captured["all_json_responses"].append(data)
                            geo = extract_geo_from_detail_json(data)
                            if geo:
                                captured["geo"] = geo
                                captured["detail_json"] = data
                                break
                    except Exception:
                        page.wait_for_timeout(800)

                # 页面内 fetch 兜底
                if not captured["geo"]:
                    for attempt in range(2):
                        js_code = (
                            f"fetch('{api_url}')"
                            f".then(r => r.text())"
                            f".catch(e => 'FETCH_ERROR')"
                        )
                        raw_str = page.evaluate(js_code)
                        if raw_str and not raw_str.startswith("FETCH_ERROR"):
                            try:
                                data = json.loads(raw_str)
                                captured["all_json_responses"].append(data)
                                geo = extract_geo_from_detail_json(data)
                                if geo:
                                    captured["geo"] = geo
                                    captured["detail_json"] = data
                                    break
                            except Exception:
                                pass
                        page.wait_for_timeout(1000)

            # 兜底：点击搜索结果第一个条目
            if not captured["geo"]:
                try:
                    link = page.locator("a").filter(has_text=place_name[:2]).first
                    if link.is_visible():
                        link.click()
                        page.wait_for_timeout(3000)
                except Exception:
                    pass

            browser.close()
    except Exception as e:
        result["error"] = f"浏览器自动化运行异常: {str(e)}"
        if logger:
            logger(result["error"])
        return result

    # 提取周边 POI
    all_pois = []
    for resp_data in captured["all_json_responses"]:
        pois = extract_pois_from_raw_json(resp_data)
        if pois:
            all_pois.extend(pois)

    # POI 去重
    unique_pois = []
    poi_seen = set()
    for p in all_pois:
        pk = f"{p['name']}_{p['longitude']}_{p['latitude']}"
        if pk not in poi_seen:
            poi_seen.add(pk)
            unique_pois.append(p)

    result["pois"] = unique_pois
    result["uid"] = captured["uid"] or uid or ""

    # 解析 AOI 多边形
    raw_geo = captured["geo"]
    if raw_geo:
        try:
            mc_points = parse_geo_to_points(raw_geo)
            if len(mc_points) >= 3:
                wgs_points = convert_points_to_wgs84(mc_points)
                result["geo_raw"] = raw_geo
                result["wgs84_points"] = wgs_points
                result["points_count"] = len(wgs_points)
                result["geojson"] = export_aoi_geojson(wgs_points, place_name)
                result["success"] = True

                # 计算多边形中心点作为主 POI
                avg_lon = round(sum(p[0] for p in wgs_points) / len(wgs_points), 6)
                avg_lat = round(sum(p[1] for p in wgs_points) / len(wgs_points), 6)
                result["main_poi"] = {
                    "name": place_name,
                    "longitude": avg_lon,
                    "latitude": avg_lat
                }
                if logger:
                    logger(f"成功提取 AOI 多边形: {len(wgs_points)} 个顶点，提取到 {len(unique_pois)} 个周边 POI")
            else:
                result["error"] = f"提取到的坐标点不足 3 个 ({len(mc_points)} 点)"
        except Exception as e:
            result["error"] = f"坐标转换异常: {str(e)}"
    else:
        result["error"] = "未能从百度地图详情中获取到 guoke_geo 建筑轮廓数据（可能该地点无官方 AOI 边界）"
        if unique_pois:
            # 尽管没有 AOI，但如果拿到了 POI，设定首个 POI 为主 POI
            result["main_poi"] = unique_pois[0]

    if logger:
        logger(f"[{place_name}] 处理完成: {'成功' if result['success'] else '失败 - ' + result['error']}")

    if result["success"]:
        _set_cache("extract", cache_key, result)

    return result


# ---------------------------------------------------------------------------
# 住宅小区边界提取（Community AOI）
# 流程：搜索拿 uid → 详情收集 guoke_geo_bud 建筑（WKT）→ 用 belong_aoi 查
# AOI 组拿全部建筑 → 合并成小区边界。所有请求带退避与风控识别。
# ---------------------------------------------------------------------------


def extract_community_aoi(place_name: str,
                          uid: Optional[str] = None,
                          headless: bool = True,
                          delay_s: float = 2.5,
                          max_retries: int = 3,
                          logger=None) -> Dict[str, Any]:
    """
    提取住宅小区的真实边界与建筑轮廓。

    返回:
        {
            success, name, uid, aoi_uid,
            buildings: [ [ (lng,lat), ... ] ... ]  # WGS84 建筑环
            boundary:  [ [ (lng,lat), ... ] ... ]  # WGS84 合并后的小区边界环
            buildings_count, error
        }
    """
    _log = logger or (lambda *a: None)
    result: Dict[str, Any] = {
        "success": False, "name": place_name, "uid": uid or "",
        "aoi_uid": "", "buildings": [], "boundary": [],
        "buildings_count": 0, "error": "",
    }

    cache_key = f"community_{place_name}_{uid or ''}"
    cached = _get_cache("community", cache_key)
    if cached is not None:
        _log(f"[cache] 命中缓存: {place_name}")
        return cached

    from core.converter import extract_buildings_from_detail_json, extract_belong_aoi, parse_wkt_polygon, merge_building_polygons

    captured = {"uid": uid, "buildings": [], "captcha_hits": 0}

    try:
        with sync_playwright() as p:
            browser = _launch_browser(p, headless=headless)
            context = _create_context(browser)
            page = context.new_page()
            _add_anti_detect_script(page)

            def _collect_from_json(d: Any) -> None:
                """任何响应里都尝试收集建筑与 belong_aoi，不挑接口形态"""
                try:
                    captured["buildings"].extend(extract_buildings_from_detail_json(d))
                except Exception:
                    pass

            def on_resp(response):
                url = response.url
                try:
                    if "qt=s" in url and not captured["uid"]:
                        data = response.json()
                        cands = extract_candidates_from_search_json(data)
                        if cands and cands[0].get("uid"):
                            captured["uid"] = cands[0]["uid"]
                            _log(f"从搜索结果获取到 UID: {captured['uid']}")
                        _collect_from_json(data)
                    elif any(t in url for t in ("detailConInfo", "qt=ext", "qt=inf", "qt=con", "qt=cur")):
                        data = response.json()
                        _collect_from_json(data)
                        anti = (data.get("result") or {}).get("anti_session") or {}
                        if anti.get("need_recaptcha"):
                            captured["captcha_hits"] += 1
                except Exception:
                    pass

            page.on("response", on_resp)
            page.goto("https://map.baidu.com/", wait_until="domcontentloaded", timeout=20000)
            page.wait_for_timeout(1000)

            sb = page.locator("#sole-input")
            sb.wait_for(state="visible", timeout=8000)
            sb.click()
            page.wait_for_timeout(200)
            sb.fill(place_name)
            page.wait_for_timeout(300)
            page.keyboard.press("Enter")
            page.wait_for_timeout(3500)

            target_uid = captured["uid"] or uid

            def fetch_detail(u: str) -> Optional[Dict[str, Any]]:
                api_url = (
                    f"https://map.baidu.com/?uid={u}"
                    f"&ugc_type=3&ugc_ver=1&qt=detailConInfo&device_ratio=1&compat=1"
                )
                for attempt in range(max_retries):
                    try:
                        resp = context.request.get(api_url, timeout=15000)
                        if resp.ok:
                            data = resp.json()
                            _collect_from_json(data)
                            anti = (data.get("result") or {}).get("anti_session") or {}
                            if anti.get("need_recaptcha"):
                                captured["captcha_hits"] += 1
                                _log(f"[warn] 触发风控，退避 {delay_s * 2:.0f}s 后重试 ({attempt + 1}/{max_retries})")
                                page.wait_for_timeout(int(delay_s * 2 * 1000))
                                continue
                            return data
                    except Exception:
                        page.wait_for_timeout(1200)
                return None

            if target_uid:
                _log(f"请求小区详情: uid={target_uid}")
                page.wait_for_timeout(int(delay_s * 1000))
                fetch_detail(target_uid)

            aoi_uid = ""
            for b in captured["buildings"]:
                if b.get("belong_aoi"):
                    aoi_uid = b["belong_aoi"]
                    break

            if not aoi_uid and target_uid:
                aoi_uid = target_uid  # 有的小区自身就是 AOI 级条目

            if aoi_uid and aoi_uid != target_uid:
                _log(f"发现 AOI 组: {aoi_uid}，拉取整组建筑…")
                page.wait_for_timeout(int(delay_s * 1000))
                fetch_detail(aoi_uid)

            browser.close()
    except Exception as e:
        result["error"] = f"浏览器自动化运行异常: {str(e)}"
        _log(result["error"])
        return result

    # 建筑去重（face_id 或 wkt 前 80 字符）
    unique = []
    seen = set()
    for b in captured["buildings"]:
        key = b.get("face_id") or b.get("wkt", "")[:80]
        if key and key not in seen and b.get("wkt"):
            seen.add(key)
            unique.append(b)

    if captured["captcha_hits"] and not unique:
        result["error"] = "百度风控拦截（need_recaptcha），未取到建筑数据。请增大 delay_s 或稍后重试。"
        _log(result["error"])
        return result

    # WKT(BD09MC) → WGS84 环
    polys = []
    for b in unique:
        mc = parse_wkt_polygon(b["wkt"])
        if len(mc) >= 3:
            polys.append(convert_points_to_wgs84(mc))

    result["uid"] = captured["uid"] or uid or ""
    result["aoi_uid"] = aoi_uid
    result["buildings"] = polys
    result["buildings_count"] = len(polys)

    if not polys:
        result["error"] = "详情中未发现 guoke_geo_bud 建筑轮廓（该小区可能无 AOI 数据）"
        _log(result["error"])
        return result

    merged = merge_building_polygons(polys)
    result["boundary"] = merged
    result["success"] = len(merged) > 0
    if not result["success"]:
        result["error"] = "建筑轮廓合并失败"

    _set_cache("community", cache_key, result)
    return result
