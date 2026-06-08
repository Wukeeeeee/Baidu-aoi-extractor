"""
百度地图建筑轮廓提取工具

从百度地图公开页面提取建筑轮廓多边形，转换到 WGS84 坐标系，
导出 CSV + Shapefile，供 GIS 使用。

用法:
  python aoi_extractor.py                                # 交互输入
  python aoi_extractor.py "广州塔"                        # 直接指定
  python aoi_extractor.py "广州塔" --show                 # 显示浏览器
  python aoi_extractor.py "广州塔" --debug                # 调试模式

依赖: pip install playwright transbigdata geopandas
"""

import re
import csv
import json
import os
import sys

# Windows 中文不乱搞
if sys.platform == "win32" and not os.environ.get("PYCHARM_HOSTED"):
    import io
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

import transbigdata
import geopandas as gpd
from shapely.geometry import Polygon
from playwright.sync_api import sync_playwright


# ============================================================
# 坐标处理
# ============================================================

def parse_geo_to_points(geo_str):
    """
    百度地图返回的 geo 字符串 → 坐标点列表
    格式: "空|空|a坐标x,坐标y,坐标x,坐标y,..."
    """
    parts = geo_str.split("|")
    if len(parts) < 3:
        raise ValueError("geo 格式异常: 缺少第3段")

    coords_str = parts[2]
    if coords_str.startswith("a"):
        coords_str = coords_str[1:]

    tokens = coords_str.split(",")
    points = []
    for i in range(0, len(tokens) - 1, 2):
        try:
            x = float(tokens[i])
            y = float(tokens[i + 1])
            points.append((x, y))
        except (ValueError, IndexError):
            continue

    return points


def bd09mc_to_wgs84(points):
    """BD09MC → BD09 → GCJ02 → WGS84，三层脱壳"""
    result = []
    for x, y in points:
        bd09_lon, bd09_lat = transbigdata.bd09mctobd09(x, y)
        gcj02_lon, gcj02_lat = transbigdata.bd09togcj02(bd09_lon, bd09_lat)
        wgs84_lon, wgs84_lat = transbigdata.gcj02towgs84(gcj02_lon, gcj02_lat)
        result.append((round(wgs84_lon, 6), round(wgs84_lat, 6)))
    return result


# ============================================================
# 导出
# ============================================================

def save_as_csv(points, filepath):
    with open(filepath, "w", encoding="utf-8", newline="") as f:
        writer = csv.writer(f)
        writer.writerow(["X", "Y"])
        writer.writerows(points)
    print(f"  [OK] CSV:  {filepath}  ({len(points)} 个点)")


def save_as_shp(points, filepath):
    if len(points) < 3:
        print(f"  [WARN] 点数不足 3 个, 跳过 SHP")
        return False

    polygon = Polygon(points)
    gdf = gpd.GeoDataFrame(
        {"name": ["building_outline"]},
        geometry=[polygon],
        crs="EPSG:4326",
    )
    # 文件被锁就加时间戳重试
    for attempt in range(3):
        try:
            gdf.to_file(filepath, encoding="utf-8")
            print(f"  [OK] SHP:  {filepath}  (面要素, WGS84)")
            return True
        except PermissionError:
            import time as _time
            base, ext = os.path.splitext(filepath)
            filepath = f"{base}_{_time.strftime('%H%M%S')}{ext}"
            if attempt < 2:
                _time.sleep(1)
                continue
            print(f"  [WARN] SHP 被锁, 尝试另存: {filepath}")
            try:
                gdf.to_file(filepath, encoding="utf-8")
                print(f"  [OK] SHP:  {filepath}")
                return True
            except PermissionError as e2:
                print(f"  [FAIL] SHP 保存失败: {e2}")
                return False


# ============================================================
# API 响应解析
# ============================================================

def extract_geo_from_detail(data):
    """
    从百度详情 API (qt=detailConInfo) 的 JSON 里抠出 geo 轮廓
    data → content → ext → detail_info → guoke_geo → geo
    """
    try:
        content = data.get("content", {})
        if isinstance(content, list):
            content = content[0] if content else {}
        geo = (
            content.get("ext", {})
            .get("detail_info", {})
            .get("guoke_geo", {})
            .get("geo", "")
        )
        return geo if geo else None
    except (KeyError, IndexError, TypeError, AttributeError):
        return None


def extract_uid_from_search(data):
    """从搜索 API (qt=s) 响应里拿第一个结果的 UID"""
    try:
        content = data.get("content", [])
        if isinstance(content, dict):
            return content.get("uid")
        elif isinstance(content, list) and len(content) > 0:
            first = content[0]
            if isinstance(first, dict):
                return first.get("uid")
        return None
    except (KeyError, IndexError, TypeError, AttributeError):
        return None


# ============================================================
# Playwright 自动化
# ============================================================

def crawl_building_outline(place_name, output_dir=None, headless=True, debug=False):
    """
    自动获取百度地图上指定地点的建筑轮廓

    流程:
      1. 打开百度地图
      2. 搜索地名 → 拦截 API 拿 UID
      3. 用 UID 请求详情 API 拿轮廓坐标
      4. 坐标转换 → 导出 CSV + SHP
    """
    print(f"\n{'='*50}")
    print(f"  搜索: {place_name}")
    print(f"{'='*50}")

    if output_dir is None:
        output_dir = os.path.dirname(os.path.abspath(__file__))
    os.makedirs(output_dir, exist_ok=True)

    result = {"geo": None, "uid": None}

    with sync_playwright() as p:
        # 优先用系统 Chrome，没有就 fallback 到 Playwright 自带的 Chromium
        launch_opts = dict(
            headless=headless,
            args=[
                "--no-sandbox",
                "--disable-setuid-sandbox",
                "--disable-blink-features=AutomationControlled",
                "--disable-dev-shm-usage",
                "--disable-gpu",
                "--disable-software-rasterizer",
                "--js-flags=--max-old-space-size=512",
            ],
        )
        try:
            browser = p.chromium.launch(channel="chrome", **launch_opts)
        except Exception:
            browser = p.chromium.launch(**launch_opts)
        context = browser.new_context(
            user_agent=(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
                "AppleWebKit/537.36 (KHTML, like Gecko) "
                "Chrome/120.0.0.0 Safari/537.36"
            ),
            viewport={"width": 1920, "height": 1080},
            locale="zh-CN",
        )
        page = context.new_page()

        # 反检测
        page.add_init_script(
            "Object.defineProperty(navigator,'webdriver',{get:()=>false})"
        )

        # 网络拦截
        def on_response(response):
            if result["geo"] is not None:
                return

            url = response.url

            if "detailConInfo" in url:
                if debug:
                    print(f"  [NET] detailConInfo: {url[:120]}...")
                try:
                    data = response.json()
                    geo = extract_geo_from_detail(data)
                    if geo:
                        result["geo"] = geo
                        print(f"  [OK] 捕获到建筑轮廓数据!")
                except Exception as e:
                    if debug:
                        print(f"  [WARN] detailConInfo 解析失败: {e}")

            if "qt=s" in url and result["uid"] is None:
                try:
                    data = response.json()
                    uid = extract_uid_from_search(data)
                    if uid:
                        result["uid"] = uid
                        if debug:
                            print(f"  [NET] 搜索 API → UID: {uid}")
                except Exception:
                    pass

        page.on("response", on_response)

        # 打开百度地图
        print(f"  打开百度地图首页...")
        page.goto("https://map.baidu.com/", wait_until="domcontentloaded", timeout=20000)
        page.wait_for_timeout(3000)

        # 搜索
        print(f"  搜索: {place_name}")
        try:
            page.wait_for_timeout(1000)
            search_box = page.locator("#sole-input")
            if not search_box.is_visible():
                page.wait_for_timeout(2000)
            search_box.click()
            page.wait_for_timeout(300)
            search_box.fill(place_name)
            page.wait_for_timeout(500)
            page.keyboard.press("Enter")
        except Exception as e:
            print(f"  [FAIL] 搜索框操作失败: {e}")
            browser.close()
            return False

        print(f"  等待搜索结果...")
        page.wait_for_timeout(4000)

        if result["uid"]:
            print(f"  [UID] {result['uid']}")

        # 用 UID 请求详情
        if result["uid"] and result["geo"] is None:
            print(f"  请求详情 API...")
            api_url = (
                'https://map.baidu.com/?uid=' + result["uid"] +
                '&ugc_type=3&ugc_ver=1&qt=detailConInfo&device_ratio=1&compat=1'
            )
            # 方式 A: 用 browser context 请求（带 cookies）
            for retry in range(2):
                try:
                    resp = context.request.get(api_url, timeout=15000)
                    if resp.ok:
                        data = resp.json()
                        geo = extract_geo_from_detail(data)
                        if geo:
                            result["geo"] = geo
                            print(f"  [OK] 通过 API 获取到轮廓!")
                            break
                except Exception as e:
                    if debug:
                        print(f"  [WARN] 方式A 失败 (第{retry+1}次): {e}")
                    page.wait_for_timeout(1000)

            # 方式 B: 页面内 fetch 兜底
            if result["geo"] is None:
                for retry in range(3):
                    json_str = page.evaluate("""(uid) => {
                        const url = 'https://map.baidu.com/?uid=' + uid +
                            '&ugc_type=3&ugc_ver=1&qt=detailConInfo&device_ratio=1&compat=1';
                        return fetch(url).then(r => r.text()).catch(e => 'FETCH_ERROR: ' + e.message);
                    }""", result["uid"])

                    if not json_str.startswith('FETCH_ERROR'):
                        import json as _json
                        try:
                            data = _json.loads(json_str)
                            geo = extract_geo_from_detail(data)
                            if geo:
                                result["geo"] = geo
                                print(f"  [OK] 通过 fetch 获取到轮廓!")
                                break
                        except Exception as e:
                            if debug:
                                print(f"  [WARN] 方式B 解析失败 (第{retry+1}次): {e}")
                    else:
                        if debug:
                            print(f"  [WARN] 方式B fetch 失败 (第{retry+1}次)")
                    page.wait_for_timeout(1500)

        # 兜底: 点搜索结果
        if result["geo"] is None:
            print(f"  尝试点击搜索结果...")
            try:
                link = page.locator("a").filter(has_text=place_name[:2]).first
                link.wait_for(state="visible", timeout=3000)
                link.click()
                page.wait_for_timeout(3000)
                print(f"  [OK] 已点击搜索结果")
            except Exception:
                pass

        # 等 API 回来
        if result["geo"] is None:
            print(f"  等待建筑轮廓数据...")
            for _ in range(15):
                if result["geo"] is not None:
                    break
                page.wait_for_timeout(1000)

        if result["geo"] is None and debug:
            print(f"\n  [DBG] URL: {page.url[:100]}")
            print(f"  [DBG] 标题: {page.title()}")

        browser.close()

    geo_string = result["geo"]
    if not geo_string:
        print(f"\n  [FAIL] 没拿到 [{place_name}] 的建筑轮廓")
        print(f"     可能原因:")
        print(f"     1. 该地点没有建筑轮廓数据")
        print(f"     2. 被反爬了 (试试 --show)")
        print(f"     3. 网络问题")
        print(f"     建议: 手动打开 map.baidu.com 搜一下确认")
        return False

    print(f"\n  解析坐标...")
    try:
        bd09mc_points = parse_geo_to_points(geo_string)
        print(f"    → {len(bd09mc_points)} 个原始坐标点")
    except Exception as e:
        print(f"  [FAIL] 坐标解析失败: {e}")
        return False

    print(f"  坐标系转换: BD09MC → BD09 → GCJ02 → WGS84")
    wgs84_points = bd09mc_to_wgs84(bd09mc_points)

    safe_name = re.sub(r'[\\/:*?"<>|]', "_", place_name)
    csv_path = os.path.join(output_dir, f"{safe_name}_轮廓.csv")
    shp_path = os.path.join(output_dir, f"{safe_name}_轮廓.shp")

    save_as_csv(wgs84_points, csv_path)
    save_as_shp(wgs84_points, shp_path)

    print(f"\n{'='*50}")
    print(f"  [DONE] 共导出 {len(wgs84_points)} 个坐标点")
    print(f"  CSV: {csv_path}")
    print(f"  SHP: {shp_path}")
    print(f"{'='*50}")
    return True


# ============================================================
# 入口
# ============================================================

if __name__ == "__main__":
    import argparse

    parser = argparse.ArgumentParser(
        description="百度地图建筑轮廓爬取工具"
    )
    parser.add_argument("place", nargs="?", help="地点名称")
    parser.add_argument("--show", action="store_true",
                        help="显示浏览器窗口")
    parser.add_argument("--debug", action="store_true",
                        help="输出调试信息")
    args = parser.parse_args()

    # 检查环境
    print("检查运行环境...")
    try:
        with sync_playwright() as p:
            try:
                browser = p.chromium.launch(channel="chrome", headless=True)
            except Exception:
                browser = p.chromium.launch(headless=True)
            browser.close()
        print("OK\n")
    except Exception as e:
        print(f"Playwright 启动失败: {e}")
        print("请运行: python -m playwright install chromium")
        sys.exit(1)

    place = args.place
    if not place:
        place = input("地点名称: ").strip()

    if place:
        crawl_building_outline(
            place,
            headless=not args.show,
            debug=args.debug,
        )
    else:
        print("请输入有效的地点名称")
