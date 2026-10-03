"""用 Windows 鼠标输入复现发布 EXE；只触碰随机验收夹具。"""
import ctypes as c
from ctypes import wintypes as w
import pathlib
import subprocess
import sys
import time
import struct
import zlib
import json
import hashlib

u = c.windll.user32
g = c.windll.gdi32
u.SetProcessDPIAware()
for name in ('GetDC', 'GetDesktopWindow'):
    getattr(u, name).restype = w.HANDLE
for name in ('CreateCompatibleDC', 'CreateCompatibleBitmap', 'SelectObject'):
    getattr(g, name).restype = w.HANDLE
g.CreateCompatibleDC.argtypes = [w.HANDLE]
g.CreateCompatibleBitmap.argtypes = [w.HANDLE, c.c_int, c.c_int]
g.SelectObject.argtypes = [w.HANDLE, w.HANDLE]
g.BitBlt.argtypes = [w.HANDLE, c.c_int, c.c_int, c.c_int, c.c_int, w.HANDLE, c.c_int, c.c_int, w.DWORD]
g.GetDIBits.argtypes = [w.HANDLE, w.HANDLE, w.UINT, w.UINT, c.c_void_p, c.c_void_p, w.UINT]
g.DeleteObject.argtypes = [w.HANDLE]
g.DeleteDC.argtypes = [w.HANDLE]
u.ReleaseDC.argtypes = [w.HWND, w.HANDLE]

def capture(path):
    width, height = u.GetSystemMetrics(0), u.GetSystemMetrics(1)
    dc = u.GetDC(0)
    mem = g.CreateCompatibleDC(dc)
    bmp = g.CreateCompatibleBitmap(dc, width, height)
    old = g.SelectObject(mem, bmp)
    g.BitBlt(mem, 0, 0, width, height, dc, 0, 0, 0x00CC0020)
    g.SelectObject(mem, old)
    info = struct.pack('<IiiHHIIiiII', 40, width, -height, 1, 32, 0, width * height * 4, 0, 0, 0, 0)
    pixels = c.create_string_buffer(width * height * 4)
    g.GetDIBits(mem, bmp, 0, height, pixels, info, 0)
    rows = bytearray()
    raw = pixels.raw
    for y in range(height):
        rows.append(0)
        for x in range(width):
            i = (y * width + x) * 4
            rows.extend((raw[i + 2], raw[i + 1], raw[i]))
    def chunk(tag, data):
        return struct.pack('>I', len(data)) + tag + data + struct.pack('>I', zlib.crc32(tag + data))
    path.write_bytes(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 2, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(rows)) + chunk(b'IEND', b''))
    g.DeleteObject(bmp)
    g.DeleteDC(mem)
    u.ReleaseDC(0, dc)

def find(title):
    found = []
    callback_type = c.WINFUNCTYPE(w.BOOL, w.HWND, w.LPARAM)
    def callback(hwnd, _):
        text = c.create_unicode_buffer(512)
        u.GetWindowTextW(hwnd, text, 512)
        if text.value == title:
            found.append(hwnd)
        return True
    u.EnumChildWindows(u.GetDesktopWindow(), callback_type(callback), 0)
    return found[0] if found else None

def click(x, y, twice=False, button=2):
    u.SetCursorPos(round(x), round(y))
    for _ in range(2 if twice else 1):
        u.mouse_event(button, 0, 0, 0, 0)
        time.sleep(.04)
        u.mouse_event(button * 2, 0, 0, 0, 0)
        time.sleep(.08)

def desktop():
    u.keybd_event(0x5B, 0, 0, 0)
    u.keybd_event(0x44, 0, 0, 0)
    u.keybd_event(0x44, 0, 2, 0)
    u.keybd_event(0x5B, 0, 2, 0)
    time.sleep(.6)

if __name__ == '__main__':
    exe = pathlib.Path(sys.argv[1]).resolve()
    evidence = pathlib.Path('.scratch/desktop-folder/verification').resolve()
    evidence.mkdir(parents=True, exist_ok=True)
    log = evidence / 'manual-desktop-session.txt'
    previous = log.read_text(encoding='utf-8-sig') if log.exists() else ''
    process = subprocess.Popen([str(exe), '--manual-desktop-check'])
    desktop_shown = False
    try:
        for _ in range(80):
            time.sleep(.2)
            text = log.read_text(encoding='utf-8-sig') if log.exists() else ''
            if text != previous and len(text.splitlines()) == 2 and '夹具就绪' in text:
                break
            if process.poll() is not None:
                raise RuntimeError('隔离进程初始化失败：' + text)
        fixture = pathlib.Path(text.splitlines()[1])
        marker = fixture / 'physical-open.txt'
        folder = fixture / '内容' / '人工验收 A'
        (folder / '000-probe.cmd').write_text('@echo off\r\necho opened>"' + str(marker) + '"\r\n', encoding='utf-8')
        time.sleep(3.6)
        hwnd = find('Kage · 人工验收 A')
        if not hwnd:
            raise RuntimeError('未找到真实桌面 Folder 窗口')
        pid = w.DWORD()
        u.GetWindowThreadProcessId(hwnd, c.byref(pid))
        if pid.value != process.pid:
            raise RuntimeError('拒绝操作其他进程的窗口')
        rect = w.RECT()
        u.GetWindowRect(hwnd, c.byref(rect))
        desktop()
        desktop_shown = True
        prefix = 'physical-1.0.0' if '1.0.0' in str(exe) else 'physical-fixed'
        capture(evidence / (prefix + '-before.png'))
        print('窗口边界', rect.left, rect.top, rect.right, rect.bottom, flush=True)
        # 默认网格第一格图像中心；不调用 WPF RaiseEvent。
        scale = (rect.right - rect.left) / 300
        hit = u.WindowFromPoint(w.POINT(round(rect.left + 60 * scale), round(rect.top + 112 * scale)))
        if hit != hwnd and not u.IsChild(hwnd, hit):
            raise RuntimeError('真实鼠标落点被其他窗口遮挡，未执行双击')
        click(rect.left + 60 * scale, rect.top + 112 * scale, True)
        time.sleep(2)
        print('实际鼠标双击启动标记存在：', marker.exists(), flush=True)
        capture(evidence / (prefix + '-after.png'))
        (evidence / (prefix + '-result.json')).write_text(json.dumps({
            'exe': str(exe), 'sha256': hashlib.sha256(exe.read_bytes()).hexdigest(),
            'bounds': [rect.left, rect.top, rect.right, rect.bottom], 'scale': scale,
            'marker_exists': marker.exists(), 'input': 'Windows mouse_event; no WPF RaiseEvent'
        }, ensure_ascii=False, indent=2), encoding='utf-8')
        sys.exit(0 if marker.exists() else 1)
    finally:
        if desktop_shown:
            desktop()
        # 只停止本脚本启动的进程；PostThreadMessage 让 Application 正常清理夹具。
        hwnd = find('Kage · 人工验收 A')
        if hwnd:
            pid = w.DWORD()
            thread = u.GetWindowThreadProcessId(hwnd, c.byref(pid))
            if pid.value == process.pid:
                u.PostThreadMessageW(thread, 0x12, 0, 0)
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            process.terminate()
            process.wait(timeout=5)
