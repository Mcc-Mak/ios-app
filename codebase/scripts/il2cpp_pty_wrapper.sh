#!/bin/sh
# il2cpp_pty_wrapper.sh — 以 PTY 包裹 Tuanjie 的 il2cpp(.NET),修正崩潰與 hang
#
# 採 /bin/sh 外殼 + 內嵌 python3(以 heredoc 透過 stdin 餵程式碼,無引號跳脫問題)。
# 之所以用 .sh 外殼而非 .py shebang:建置階段(Run Script phase)以 exec 呼叫本檔,
# #!/bin/sh 恆為核心所接受;先前 .py(#!/usr/bin/env python3)版本未被實際執行,
# 導致 il2cpp.real 直接以 pipe fd 執行而 ConsolePal 崩潰。
#
# 修正兩個問題:
#   (1) ConsolePal 崩潰:.NET ConsolePal..cctor 以 ioctl(TIOCGWINSZ) 查詢 stdin
#       (fd 0) 尺寸,在非 TTY 上失敗並以垃圾值配置陣列 → OOM。解法:fd 0/1/2 全接
#       PTY slave(24×80),並以 pty.fork 之方式取得 controlling terminal。
#   (2) 6 小時 hang(前版 pty.spawn):pty.spawn 以阻塞式 os.write 排 PTY 輸出至
#       stdout,但 xcodebuild 不即時讀取該 pipe → os.write 阻塞 → PTY 緩衝填滿 →
#       il2cpp 阻塞 → 死結。解法:stdout 設非阻塞,內部 pending 緩衝,持續讀 master
#       釋放 PTY;附 15 分鐘無輸出看門狗防回歸。
#
# 安裝(CI build:ipa job):
#   mv il2cpp il2cpp.real && chmod +x il2cpp.real
#   cp il2cpp_pty_wrapper.sh il2cpp && chmod +x il2cpp
#
# 呼叫鏈:建置階段腳本 → il2cpp(本 wrapper)→ python3 → fork → il2cpp.real <原始參數>
DIR="$(dirname "$0")"
exec python3 - "$DIR/il2cpp.real" "$@" <<'PYEOF'
import os, pty, fcntl, termios, struct, sys, select, errno, signal, time

real = sys.argv[1]
real_args = sys.argv[1:]

sys.stderr.write("IL2CPP_WRAPPER start pid=%d real=%s argv=%r\n" % (os.getpid(), real, sys.argv[2:]))
sys.stderr.flush()

# 單一 PTY 供 stdin/stdout/stderr 共用(ConsolePal..cctor 查詢 fd 0,故 fd 0 須為 TTY)
master, slave = pty.openpty()
fcntl.ioctl(slave, termios.TIOCSWINSZ, struct.pack("HHHH", 24, 80, 0, 0))

pid = os.fork()
if pid == 0:
    # ── 子行程(il2cpp.real)──
    try:
        os.setsid()
    except OSError:
        pass
    # 取得 PTY 為 controlling terminal(比照 pty.fork;某些 .NET 路徑較穩)
    try:
        os.close(os.open(os.ttyname(slave), os.O_RDWR))
    except OSError:
        pass
    sys.stderr.write("IL2CPP_WRAPPER child pid=%d slave=%d pre-dup2 isatty=%r -> execv\n"
                     % (os.getpid(), slave, [os.isatty(i) for i in (0, 1, 2)]))
    sys.stderr.flush()
    os.dup2(slave, 0)  # stdin  = PTY(ConsolePal..cctor 的 TIOCGWINSZ 在此成功)
    os.dup2(slave, 1)  # stdout = PTY
    os.dup2(slave, 2)  # stderr = PTY
    if slave > 2:
        try:
            os.close(slave)
        except OSError:
            pass
    try:
        os.close(master)
    except OSError:
        pass
    os.execv(real, real_args)
    os._exit(127)

# ── 父行程:非阻塞排空 PTY master → 真實 stdout ──
os.close(slave)
STDOUT = 1
orig_fl = fcntl.fcntl(STDOUT, fcntl.F_GETFL)
fcntl.fcntl(STDOUT, fcntl.F_SETFL, orig_fl | os.O_NONBLOCK)

pending = bytearray()
exit_code = 1
reaped = False
last_output = time.monotonic()
WATCHDOG_S = 900         # 15 分鐘無輸出 → 判定卡住(il2cpp 正常數分鐘完成)
MAX_PENDING = 64 << 20   # 64 MB 上限,避免 stdout 失聯時 pending 無限增長


def reap_child(blocking):
    global exit_code, reaped
    if reaped:
        return True
    try:
        done, status = os.waitpid(pid, 0 if blocking else os.WNOHANG)
    except OSError:
        reaped = True
        return True
    if done:
        reaped = True
        if os.WIFEXITED(status):
            exit_code = os.WEXITSTATUS(status)
        elif os.WIFSIGNALED(status):
            exit_code = 128 + os.WTERMSIG(status)
        return True
    return False


def flush_blocking():
    try:
        fcntl.fcntl(STDOUT, fcntl.F_SETFL, orig_fl)
    except OSError:
        pass
    while pending:
        try:
            n = os.write(STDOUT, pending)
            del pending[:n]
        except OSError:
            break


while True:
    rfds = [master]
    wfds = [STDOUT] if pending else []
    try:
        r, w, _ = select.select(rfds, wfds, [], 1.0)
    except (OSError, ValueError):
        break

    if master in r:
        try:
            data = os.read(master, 65536)
        except OSError:
            break  # EIO(macOS:slave 關閉)→ EOF
        if not data:
            break  # EOF
        pending += data
        if len(pending) > MAX_PENDING:
            del pending[: len(pending) - MAX_PENDING]  # 丟棄最舊
        last_output = time.monotonic()

    if STDOUT in w and pending:
        try:
            n = os.write(STDOUT, pending)
            del pending[:n]
        except OSError as e:
            if e.errno not in (errno.EAGAIN, errno.EWOULDBLOCK):
                pending.clear()  # stdout 已壞,丟棄避免無限增長

    if reap_child(False):
        # 子行程已結束:排空 PTY 殘留後離開
        try:
            while True:
                data = os.read(master, 65536)
                if not data:
                    break
                pending += data
        except OSError:
            pass
        flush_blocking()
        break

    # 看門狗:長時間無輸出 → 判定卡住,強殺以避免 6h hang
    if (time.monotonic() - last_output) > WATCHDOG_S:
        sys.stderr.write("IL2CPP_WRAPPER: %ds 無輸出,判定 il2cpp 卡住,送 SIGKILL\n" % WATCHDOG_S)
        sys.stderr.flush()
        try:
            os.kill(pid, signal.SIGKILL)
        except OSError:
            pass
        reap_child(True)
        exit_code = 124
        break

if not reaped:
    reap_child(True)
flush_blocking()

try:
    os.close(master)
except OSError:
    pass

sys.exit(exit_code)
PYEOF
