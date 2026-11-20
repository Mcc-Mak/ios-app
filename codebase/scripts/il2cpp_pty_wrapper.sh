#!/bin/sh
# il2cpp_pty_wrapper.sh — 以 PTY 包裹 Tuanjie 的 il2cpp(.NET),修正崩潰與 hang
#
# 採 /bin/sh 外殼 + 內嵌 python3(heredoc)。建置階段(Run Script phase)以 exec 呼叫
# 本檔,核心恆接受 #!/bin/sh;先前純 .py(#!/usr/bin/env python3)未被實際執行,
# 導致 il2cpp.real 直接以 pipe fd 執行而 ConsolePal 崩潰(run 44653433207 驗證)。
#
# 修正兩個問題:
#   (1) ConsolePal 崩潰(ARCHIVE FAILED):
#       Tuanjie 的 il2cpp 為 .NET 應用程式。ConsolePal..cctor 以 ioctl(TIOCGWINSZ)
#       查詢 stdin(fd 0) 尺寸;在 pipe(非 TTY)上失敗,改用垃圾值配置陣列 →
#       OutOfMemoryException。實測 fd 0 必須是「真正的 controlling terminal」,僅
#       dup2 PTY slave 而未設 controlling terminal 仍會崩潰(run 44654998542 驗證)。
#       解法:pty.fork()(底層 os.forkpty() 核心呼叫,做 openpty + fork + login_tty,
#             login_tty 做 setsid + TIOCSCTTY + dup2)——與 pty.spawn 完全相同的子行程
#             設定,經實證可防崩潰。手動 fork+dup2+setsid 不設 TIOCSCTTY,故失敗。
#   (2) 6 小時 hang(前版 pty.spawn,run 44498856814):
#       pty._copy 在 stdout 緩衝達 4096 bytes 時停止讀取 master → PTY 緩衝填滿 →
#       il2cpp 阻塞於寫入 → 死結。解法:父行程「持續」讀 master(無 high-water),
#       內部 pending 緩衝(上限 64MB),stdout 設非阻塞,可寫時才排空;附 15 分鐘
#       無輸出看門狗防回歸。
#
# 安裝(CI build:ipa job):
#   mv il2cpp il2cpp.real && chmod +x il2cpp.real
#   cp il2cpp_pty_wrapper.sh il2cpp && chmod +x il2cpp
#
# 呼叫鏈:建置階段腳本 → il2cpp(本 wrapper)→ python3 → pty.fork → il2cpp.real <原始參數>
DIR="$(dirname "$0")"
exec python3 - "$DIR/il2cpp.real" "$@" <<'PYEOF'
import os, pty, fcntl, termios, struct, sys, select, errno, signal, time

real = sys.argv[1]
real_args = sys.argv[1:]

WINSIZE = struct.pack("HHHH", 24, 80, 0, 0)

sys.stderr.write("IL2CPP_WRAPPER start pid=%d real=%s argv=%r\n"
                 % (os.getpid(), real, sys.argv[2:]))
sys.stderr.flush()

# pty.fork():底層呼叫 os.forkpty()(macOS 核心函式),做 openpty + fork + login_tty。
# login_tty 在子行程執行 setsid + ioctl(TIOCSCTTY) 設定 controlling terminal + dup2
# slave 至 0/1/2。這是 pty.spawn 的子行程設定,經實證可防 ConsolePal 崩潰。
# 手動 fork+dup2+setsid 不設 TIOCSCTTY,故 fd 0 不足以讓 .NET 視為有效終端 → 崩潰。
pid, master_fd = pty.fork()

if pid == 0:
    # ── 子行程(il2cpp.real)──
    # pty.fork 已完成:setsid + TIOCSCTTY + dup2(slave, 0/1/2) + close(slave)。
    # 設定視窗尺寸 24×80,讓 ConsolePal 的 TIOCGWINSZ 回傳有效值(非 0×0)。
    for fd in (0, 1, 2):
        try:
            fcntl.ioctl(fd, termios.TIOCSWINSZ, WINSIZE)
        except OSError:
            pass
    os.execv(real, real_args)
    os._exit(127)

# ── 父行程:非阻塞排空 PTY master → 真實 stdout(取代 pty._copy)──
# 設定視窗尺寸於 master(TIOCSWINSZ 在 master/slave 兩端皆可設;propagates)。
try:
    fcntl.ioctl(master_fd, termios.TIOCSWINSZ, WINSIZE)
except OSError:
    pass

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
    rfds = [master_fd]
    wfds = [STDOUT] if pending else []
    try:
        r, w, _ = select.select(rfds, wfds, [], 1.0)
    except (OSError, ValueError):
        break

    # 持續讀 master(無 high-water)→ PTY 緩衝永不填滿 → il2cpp 永不阻塞(修正 hang)
    if master_fd in r:
        try:
            data = os.read(master_fd, 65536)
        except OSError:
            break  # EIO(macOS:slave 關閉)→ EOF
        if not data:
            break  # EOF
        pending += data
        if len(pending) > MAX_PENDING:
            del pending[: len(pending) - MAX_PENDING]  # 丟棄最舊
        last_output = time.monotonic()

    # stdout 可寫時才排空 pending(stdout 非阻塞,不會死結)
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
                data = os.read(master_fd, 65536)
                if not data:
                    break
                pending += data
        except OSError:
            pass
        flush_blocking()
        break

    # 看門狗:長時間無輸出 → 判定卡住,強殺以避免 6h hang
    if (time.monotonic() - last_output) > WATCHDOG_S:
        sys.stderr.write("IL2CPP_WRAPPER: %ds 無輸出,判定 il2cpp 卡住,送 SIGKILL\n"
                         % WATCHDOG_S)
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
    os.close(master_fd)
except OSError:
    pass

sys.exit(exit_code)
PYEOF
