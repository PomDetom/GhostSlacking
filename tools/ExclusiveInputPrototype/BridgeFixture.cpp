#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstdio>
#include <string>
#include <array>

namespace {
constexpr UINT Inspect = WM_APP + 21, ReadResult = WM_APP + 22, ReadShortcuts = WM_APP + 23,
    ReadDrag = WM_APP + 24, ReadImports = WM_APP + 25, PauseInput = WM_APP + 26, ReadCleanup = WM_APP + 27,
    CreatePopups = WM_APP + 28, ReadPopup = WM_APP + 29, ReadSubmenu = WM_APP + 30,
    ReadOtherPopup = WM_APP + 31, ReadDialog = WM_APP + 32, ReadHover = WM_APP + 33, ReadCursor = WM_APP + 34,
    ReadActivation = WM_APP + 35, ReadMenuRaises = WM_APP + 36, ReadPopupCursor = WM_APP + 37;
HWND popup = nullptr, submenu = nullptr, otherPopup = nullptr, dialog = nullptr;
int hoverCount = 0;
int cursorCount = 0;
int popupCursorCount = 0;
int activationCount = 0;
int menuRaiseAttempts = 0;
LRESULT CALLBACK PopupProc(HWND hwnd, UINT message, WPARAM wparam, LPARAM lparam) {
    if (message == WM_WINDOWPOSCHANGED && (reinterpret_cast<WINDOWPOS*>(lparam)->flags & SWP_SHOWWINDOW)) {
        ++menuRaiseAttempts;
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }
    if (message == WM_SETCURSOR) { ++cursorCount; ++popupCursorCount; SetCursor(LoadCursorW(nullptr, IDC_ARROW)); return TRUE; }
    if (message == WM_LBUTTONDOWN) { DestroyWindow(hwnd); return 0; }
    if (message == WM_MOUSEMOVE) {
        ++hoverCount;
        SetCapture(hwnd);
        SetForegroundWindow(hwnd); // Scoped popup activation must also be blocked.
        return 0;
    }
    return DefWindowProcW(hwnd, message, wparam, lparam);
}
HWND editor = nullptr;
LRESULT result = 0;
int shortcuts = 0, dragMoves = 0;
bool coldClick = true;
int keyUps = 0, mouseUps = 0;
LRESULT CALLBACK WindowProc(HWND hwnd, UINT message, WPARAM wparam, LPARAM lparam) {
    if (message == WM_ACTIVATEAPP && wparam) { ++activationCount; return 0; }
    if (message == WM_SETCURSOR) { ++cursorCount; SetCursor(LoadCursorW(nullptr, IDC_ARROW)); return TRUE; }
    if (message == WM_CREATE) {
        editor = CreateWindowExW(WS_EX_CLIENTEDGE, L"EDIT", L"", WS_CHILD | WS_VISIBLE | ES_MULTILINE | ES_AUTOVSCROLL,
            12, 12, 430, 200, hwnd, nullptr, GetModuleHandleW(nullptr), nullptr);
        return 0;
    }
    if (message == Inspect) {
        BYTE keyboard[256]{}; GetKeyboardState(keyboard);
        POINT position{}; GetCursorPos(&position);
        result = (GetFocus() == editor ? 1 : 0) | (GetForegroundWindow() == hwnd ? 2 : 0) |
            (GetActiveWindow() == hwnd ? 4 : 0) | (GetKeyState(VK_CONTROL) & 0x8000 ? 8 : 0) |
            (GetAsyncKeyState(VK_CONTROL) & 0x8000 ? 16 : 0) | (keyboard[VK_CONTROL] & 0x80 ? 32 : 0) |
            (position.x == 123 && position.y == 145 ? 64 : 0);
        // This is deliberately attempted inside our own disposable target.
        // The bridge must intercept it rather than switch the real foreground.
        SetForegroundWindow(hwnd);
        return result;
    }
    if (message == ReadResult) return result;
    if (message == ReadShortcuts) return shortcuts;
    if (message == ReadDrag) return dragMoves;
    if (message == ReadImports) return reinterpret_cast<void*>(GetFocus) ==
        reinterpret_cast<void*>(GetProcAddress(GetModuleHandleW(L"user32.dll"), "GetFocus"));
    if (message == ReadCleanup) return keyUps * 100 + mouseUps;
    if (message == ReadPopup) return reinterpret_cast<LRESULT>(popup);
    if (message == ReadSubmenu) return reinterpret_cast<LRESULT>(submenu);
    if (message == ReadOtherPopup) return reinterpret_cast<LRESULT>(otherPopup);
    if (message == ReadDialog) return reinterpret_cast<LRESULT>(dialog);
    if (message == ReadHover) return hoverCount;
    if (message == ReadCursor) return cursorCount;
    if (message == ReadActivation) return activationCount;
    if (message == ReadMenuRaises) return menuRaiseAttempts;
    if (message == ReadPopupCursor) return popupCursorCount;
    if (message == CreatePopups) {
        if (IsWindow(otherPopup)) DestroyWindow(otherPopup);
        if (IsWindow(dialog)) DestroyWindow(dialog);
        const HINSTANCE instance = GetModuleHandleW(nullptr);
        popup = CreateWindowExW(WS_EX_NOACTIVATE, L"Chrome_WidgetWin_1", L"", WS_POPUP | WS_VISIBLE,
            620, 160, 120, 140, hwnd, nullptr, instance, nullptr);
        submenu = CreateWindowExW(WS_EX_NOACTIVATE, L"Chrome_WidgetWin_1", L"", WS_POPUP | WS_VISIBLE,
            740, 200, 120, 80, popup, nullptr, instance, nullptr);
        otherPopup = CreateWindowExW(WS_EX_NOACTIVATE, L"Chrome_WidgetWin_1", L"", WS_POPUP | WS_VISIBLE,
            1000, 200, 120, 80, nullptr, nullptr, instance, nullptr);
        dialog = CreateWindowExW(WS_EX_NOACTIVATE, L"Chrome_WidgetWin_1", L"", WS_POPUP | WS_CAPTION | WS_VISIBLE,
            1200, 200, 120, 80, hwnd, nullptr, instance, nullptr);
        return 0;
    }
    if (message == WM_CANCELMODE) {
        if (IsWindow(submenu)) DestroyWindow(submenu);
        if (IsWindow(popup)) DestroyWindow(popup);
        return 0;
    }
    if (message == WM_KEYUP && wparam == VK_CONTROL) { ++keyUps; return 0; }
    if (message == PauseInput) {
        wchar_t name[100]{};
        swprintf_s(name, L"Local\\GhostSlacking.QueueFixture.Pause.%lu", GetCurrentProcessId());
        const HANDLE signal = OpenEventW(EVENT_MODIFY_STATE, FALSE, name);
        if (signal) { SetEvent(signal); CloseHandle(signal); }
        Sleep(450);
        SetForegroundWindow(hwnd); // Must stay guarded after host cancellation.
        return 0;
    }
    if (message == WM_LBUTTONDOWN) {
        if (coldClick) { coldClick = false; Sleep(450); }
        SetCapture(hwnd);
        return 0;
    }
    if (message == WM_MOUSEMOVE && (wparam & MK_LBUTTON) && GetCapture() == hwnd &&
        (GetKeyState(VK_LBUTTON) & 0x8000) && (GetAsyncKeyState(VK_LBUTTON) & 0x8000)) {
        const DWORD point = GetMessagePos();
        if (static_cast<short>(LOWORD(point)) == 123 && static_cast<short>(HIWORD(point)) == 145) ++dragMoves;
        return 0;
    }
    if (message == WM_LBUTTONUP) { ++mouseUps; ReleaseCapture(); return 0; }
    if (message == WM_DESTROY) { PostQuitMessage(0); return 0; }
    return DefWindowProcW(hwnd, message, wparam, lparam);
}
HWND CreateFixture() {
    WNDCLASSW popupClass{};
    popupClass.lpfnWndProc = PopupProc; popupClass.hInstance = GetModuleHandleW(nullptr);
    popupClass.lpszClassName = L"Chrome_WidgetWin_1";
    RegisterClassW(&popupClass);
    WNDCLASSW wc{};
    wc.lpfnWndProc = WindowProc; wc.hInstance = GetModuleHandleW(nullptr);
    wc.lpszClassName = L"GhostSlacking.ExclusiveInputFixture"; wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    RegisterClassW(&wc);
    return CreateWindowExW(WS_EX_NOACTIVATE, wc.lpszClassName, L"GhostSlacking disposable input target",
        WS_OVERLAPPEDWINDOW | WS_VISIBLE, 100, 100, 470, 290, nullptr, nullptr, wc.hInstance, nullptr);
}
bool Launch(const wchar_t* arguments, const wchar_t* desktop, PROCESS_INFORMATION& process) {
    wchar_t executable[MAX_PATH]{}; GetModuleFileNameW(nullptr, executable, MAX_PATH);
    std::wstring command = L"\"" + std::wstring(executable) + L"\" " + arguments;
    STARTUPINFOW startup{}; startup.cb = sizeof(startup); startup.lpDesktop = const_cast<wchar_t*>(desktop);
    return CreateProcessW(executable, command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW,
        nullptr, nullptr, &startup, &process) != FALSE;
}
LRESULT Query(HWND hwnd, UINT message) {
    DWORD_PTR value = 0;
    return SendMessageTimeoutW(hwnd, message, 0, 0, SMTO_ABORTIFHUNG | SMTO_BLOCK, 500, &value)
        ? static_cast<LRESULT>(value) : -1;
}
int Check() {
    FILE* log = nullptr;
    freopen_s(&log, "bridge-self-test.log", "w", stdout);
    using Begin = DWORD (*)(HWND, HWND, DWORD, ULONGLONG);
    using Send = DWORD (*)(HWND, UINT, WPARAM, LPARAM, const BYTE*, LONG, LONG);
    using End = void (*)();
    using Flush = DWORD (*)(DWORD);
    HMODULE library = LoadLibraryW(L"GhostSlacking.InputBridge.dll");
    if (!library) return 10;
    auto begin = reinterpret_cast<Begin>(GetProcAddress(library, "GS_Begin"));
    auto send = reinterpret_cast<Send>(GetProcAddress(library, "GS_Dispatch"));
    auto end = reinterpret_cast<End>(GetProcAddress(library, "GS_End"));
    auto heartbeat = reinterpret_cast<End>(GetProcAddress(library, "GS_Heartbeat"));
    auto flush = reinterpret_cast<Flush>(GetProcAddress(library, "GS_Flush"));
    using IsMenu = BOOL (*)(HWND);
    using MenuAt = HWND (*)(LONG, LONG);
    auto isMenu = reinterpret_cast<IsMenu>(GetProcAddress(library, "GS_IsMenu"));
    auto menuAt = reinterpret_cast<MenuAt>(GetProcAddress(library, "GS_MenuAt"));
    using PresentMenus = DWORD (*)(const HWND*, DWORD);
    auto presentMenus = reinterpret_cast<PresentMenus>(GetProcAddress(library, "GS_PresentMenus"));


    if (!begin || !send || !end || !heartbeat || !flush || !isMenu || !menuAt || !presentMenus) return 11;
    wchar_t desktop[256]{}; DWORD needed = 0;
    if (!GetUserObjectInformationW(GetThreadDesktop(GetCurrentThreadId()), UOI_NAME, desktop, sizeof(desktop), &needed)) return 12;
    PROCESS_INFORMATION child{};
    if (!Launch(L"--target", desktop, child)) return 13;
    HWND target = nullptr;
    for (int attempt = 0; attempt < 100 && !target; ++attempt) {
        struct Search { DWORD pid; HWND hwnd; } search{child.dwProcessId, nullptr};
        EnumWindows([](HWND hwnd, LPARAM data) -> BOOL {
            auto value = reinterpret_cast<Search*>(data);
            DWORD pid = 0; GetWindowThreadProcessId(hwnd, &pid);
            if (pid == value->pid && !GetWindow(hwnd, GW_OWNER)) { value->hwnd = hwnd; return FALSE; }
            return TRUE;
        }, reinterpret_cast<LPARAM>(&search));
        target = search.hwnd;
        if (!target) Sleep(20);
    }
    int failed = 0;
    auto expect = [&](bool passed, const char* name) {
        printf("%s %s\n", passed ? "PASS" : "FAIL", name); fflush(stdout);
        if (!passed) ++failed;
    };
    expect(target != nullptr, "disposable-target-created");
    if (target) {
        FILETIME start{}, exit{}, kernel{}, user{};
        GetProcessTimes(child.hProcess, &start, &exit, &kernel, &user);
        ULARGE_INTEGER identity{}; identity.LowPart = start.dwLowDateTime; identity.HighPart = start.dwHighDateTime;
        const HWND foreground = GetForegroundWindow();
        HWND edit = nullptr;
        for (int attempt = 0; attempt < 100 && !edit; ++attempt) {
            edit = FindWindowExW(target, nullptr, L"EDIT", nullptr);
            if (!edit) Sleep(10);
        }
        expect(edit != nullptr, "fixture-editor-created");
        std::array<BYTE, 256> keys{};
        expect(begin(target, foreground, child.dwProcessId + 1, identity.QuadPart) != 0, "wrong-pid-rejected");
        expect(begin(target, foreground, child.dwProcessId, identity.QuadPart + 1) != 0, "wrong-process-start-rejected");
        const DWORD started = begin(target, foreground, child.dwProcessId, identity.QuadPart);
        printf("begin-error=%lu\n", started);
        expect(started == 0, "target-thread-attached");
        if (started == 0) {
            expect(send(target, WM_MOUSEMOVE, 0, MAKELPARAM(15, 15), keys.data(), 123, 145) == 0 && flush(1000) == 0 &&
                Query(target, ReadCursor) > 0 && Query(target, ReadActivation) > 0,
                "initial-hover-prepares-target-cursor-without-click-or-real-activation");
            const ULONGLONG before = GetTickCount64();
            expect(send(target, WM_LBUTTONDOWN, MK_LBUTTON, MAKELPARAM(15, 15), keys.data(), 123, 145) == 0, "cold-click-admitted");
            expect(GetTickCount64() - before < 100, "cold-click-does-not-block-host");
            expect(send(target, WM_MOUSEMOVE, MK_LBUTTON, MAKELPARAM(25, 15), keys.data(), 123, 145) == 0, "drag-move-admitted");
            expect(send(target, WM_LBUTTONUP, 0, MAKELPARAM(25, 15), keys.data(), 123, 145) == 0, "drag-up-admitted");
            expect(flush(1500) == 0, "cold-click-and-drag-drain-through-message-pump");
            expect(Query(target, ReadDrag) == 1, "drag-keeps-capture-button-state-and-message-position");
            expect(send(edit, WM_LBUTTONDOWN, MK_LBUTTON, MAKELPARAM(15, 15), keys.data(), 123, 145) == 0, "mouse-down");
            expect(send(edit, WM_LBUTTONUP, 0, MAKELPARAM(15, 15), keys.data(), 123, 145) == 0, "mouse-up");
            expect(send(edit, WM_CHAR, L'a', 1, keys.data(), 123, 145) == 0, "character-delivery");
            expect(flush(1500) == 0, "character-queue-drained");
            expect(Query(edit, WM_GETTEXTLENGTH) == 1, "native-edit-received-character");
            keys[VK_CONTROL] = keys[VK_LCONTROL] = 0x80;
            expect(send(target, Inspect, 0, 0, keys.data(), 123, 145) == 0, "virtual-state-query");
            expect(flush(1500) == 0, "state-query-drained");
            expect(Query(target, ReadResult) == 127, "private-focus-keys-cursor-and-activation-block");
            for (const WPARAM key : {WPARAM('A'), WPARAM('C'), WPARAM('V')}) {
                expect(send(edit, WM_KEYDOWN, key, 1, keys.data(), 123, 145) == 0, "shortcut-key-admitted");
                expect(send(edit, WM_KEYUP, key, (1L << 30) | (1L << 31), keys.data(), 123, 145) == 0, "shortcut-up-admitted");
            }
            expect(flush(1500) == 0, "accelerators-drained");
            expect(Query(target, ReadShortcuts) == 3, "ctrl-a-c-v-reach-app-message-loop-without-touching-clipboard");
            expect(Query(edit, WM_GETTEXTLENGTH) == 1, "accelerator-consumed-keys-do-not-generate-extra-characters");
            keys.fill(0);
            expect(send(edit, WM_KEYDOWN, 'B', 1, keys.data(), 123, 145) == 0, "printable-key-down");
            expect(send(edit, WM_KEYUP, 'B', (1L << 30) | (1L << 31), keys.data(), 123, 145) == 0, "printable-key-up");
            expect(flush(1500) == 0 && Query(edit, WM_GETTEXTLENGTH) == 2, "translate-message-generates-exactly-one-character");
            expect(GetForegroundWindow() == foreground, "system-foreground-preserved");
            expect(send(target, CreatePopups, 0, 0, keys.data(), 630, 180) == 0 && flush(1000) == 0, "owned-popup-fixtures-created");
            const HWND menu = reinterpret_cast<HWND>(Query(target, ReadPopup));
            const HWND sub = reinterpret_cast<HWND>(Query(target, ReadSubmenu));
            const HWND other = reinterpret_cast<HWND>(Query(target, ReadOtherPopup));
            const HWND modal = reinterpret_cast<HWND>(Query(target, ReadDialog));
            expect(menu && sub && other && modal, "all-popup-fixtures-exist");
            expect(isMenu(menu) && isMenu(sub), "owned-popup-and-submenu-accepted");
            expect(!isMenu(other) && !isMenu(modal), "unrelated-popup-and-captioned-dialog-rejected");
            expect(!IsWindowVisible(menu) && !IsWindowVisible(sub), "visible-at-creation-popups-deferred-before-presentation");
            auto presentFixtures = [&](HWND parent, HWND childMenu) {
                const HWND windows[] = {parent, childMenu};
                for (HWND window : windows) {
                    RECT rect{}; GetWindowRect(window, &rect);
                    HRGN region = CreateRectRgn(0, 0, rect.right - rect.left, rect.bottom - rect.top);
                    if (!SetWindowRgn(window, region, TRUE)) { DeleteObject(region); return false; }
                }
                return presentMenus(windows, 2) == 0 && flush(1000) == 0;
            };
            const HWND overlay = CreateWindowExW(WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST,
                L"STATIC", L"", WS_POPUP, 600, 140, 280, 180, target, nullptr, GetModuleHandleW(nullptr), nullptr);
            expect(overlay && SetWindowPos(overlay, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_SHOWWINDOW),
                "host-prepares-its-own-overlay-before-menu-show");
            expect(presentFixtures(menu, sub), "fixture-popups-presented-after-host-readiness");
            auto belowOverlay = [&](HWND window) {
                for (HWND above = GetWindow(window, GW_HWNDPREV); above; above = GetWindow(above, GW_HWNDPREV))
                    if (above == overlay) return true;
                return false;
            };
            expect(belowOverlay(menu) && belowOverlay(sub), "menu-show-and-nested-raise-retain-host-overlay-order");
            expect(Query(target, ReadMenuRaises) >= 2, "fixture-exercises-framework-raise-during-menu-show");
            expect(Query(target, ReadHover) > 0 && Query(target, ReadPopupCursor) > 0,
                "menu-hover-refreshes-cursor-and-hover-without-another-physical-event");
            if (overlay) DestroyWindow(overlay);
            expect(menuAt(630, 180) == menu && menuAt(750, 220) == sub, "live-popup-hit-test-beyond-target-bounds");
            const LRESULT hoverBeforeMove = Query(target, ReadHover);
            expect(send(menu, WM_MOUSEMOVE, 0, MAKELPARAM(10, 20), keys.data(), 630, 180) == 0 &&
                flush(1000) == 0 && Query(target, ReadHover) == hoverBeforeMove + 1, "button-free-hover-delivered-to-popup");
            expect(Query(target, ReadCursor) > 0, "cursor-updated-on-hover-before-any-click");
            expect(GetForegroundWindow() == foreground, "popup-activation-attempt-preserves-foreground");
            expect(send(menu, WM_LBUTTONDOWN, MK_LBUTTON, MAKELPARAM(10, 20), keys.data(), 630, 180) == 0 &&
                flush(1000) == 0 && !IsWindow(menu), "menu-can-close-on-button-down");
            expect(send(menu, WM_LBUTTONUP, 0, 0, keys.data(), 630, 180) == 0 && flush(1000) == 0,
                "up-for-destroyed-menu-is-consumed-without-ending-session");
            expect(send(target, CreatePopups, 0, 0, keys.data(), 630, 180) == 0 && flush(1000) == 0,
                "new-menu-still-works-after-closed-menu-release");
            const HWND secondMenu = reinterpret_cast<HWND>(Query(target, ReadPopup));
            const HWND secondSub = reinterpret_cast<HWND>(Query(target, ReadSubmenu));
            const HWND secondOther = reinterpret_cast<HWND>(Query(target, ReadOtherPopup));
            const HWND secondDialog = reinterpret_cast<HWND>(Query(target, ReadDialog));
            expect(presentFixtures(secondMenu, secondSub), "recreated-popup-presentation");
            end();
            for (int attempt = 0; attempt < 100 && Query(target, ReadImports) != 1; ++attempt) Sleep(10);
            expect(!IsWindow(secondMenu) && !IsWindow(secondSub), "cancel-closes-popup-and-submenu");
            expect(IsWindow(secondOther) && IsWindow(secondDialog), "cancel-does-not-touch-unmanaged-windows");
            expect(begin(target, foreground, child.dwProcessId, identity.QuadPart) == 0, "session-reopens-after-popup-cancellation");
            const LRESULT cleanupBefore = Query(target, ReadCleanup);
            expect(send(target, WM_LBUTTONDOWN, MK_LBUTTON, MAKELPARAM(15, 15), keys.data(), 123, 145) == 0, "held-mouse-before-cancel");
            keys[VK_CONTROL] = keys[VK_LCONTROL] = 0x80;
            expect(send(target, WM_KEYDOWN, VK_CONTROL, 1, keys.data(), 123, 145) == 0 && flush(1500) == 0, "held-key-before-cancel");
            wchar_t signalName[100]{};
            swprintf_s(signalName, L"Local\\GhostSlacking.QueueFixture.Pause.%lu", child.dwProcessId);
            const HANDLE signal = CreateEventW(nullptr, TRUE, FALSE, signalName);
            expect(send(target, PauseInput, 0, 0, keys.data(), 123, 145) == 0 &&
                WaitForSingleObject(signal, 500) == WAIT_OBJECT_0, "target-event-in-flight-before-cancel");
            expect(send(edit, WM_CHAR, L'q', 1, keys.data(), 123, 145) == 0, "queued-character-before-cancel");
            end();
            if (signal) CloseHandle(signal);
            for (int attempt = 0; attempt < 100 && Query(target, ReadImports) != 1; ++attempt) Sleep(10);
            expect(Query(target, ReadImports) == 1, "normal-stop-restores-imports-at-pump-boundary");
            expect(Query(edit, WM_GETTEXTLENGTH) == 2, "cancelled-queued-character-is-not-replayed");
            expect(Query(target, ReadCleanup) == cleanupBefore + 101, "cancel-releases-only-delivered-held-inputs");
            keys.fill(0);
            expect(send(edit, WM_CHAR, L'b', 1, keys.data(), 123, 145) != 0, "delivery-after-stop-rejected");
            // Read normal API state without asking the target to activate itself.
            expect(Query(target, WM_NULL) == 0, "target-alive-after-detach");
            expect(GetForegroundWindow() == foreground, "detach-preserves-foreground");
            expect(begin(target, foreground, child.dwProcessId, identity.QuadPart) == 0, "second-session-starts");
            Sleep(2200); // No heartbeat: the virtual APIs must stop impersonating focus.
            Query(target, Inspect);
            expect((Query(target, ReadResult) & 2) == 0, "expired-lease-passes-through");
            heartbeat();
            expect(send(edit, WM_CHAR, L'b', 1, keys.data(), 123, 145) != 0, "late-heartbeat-does-not-revive-session");
            end();
        }
        PostMessageW(target, WM_CLOSE, 0, 0);
    }
    // The fixture exits normally. Never terminate a user process or Watchdog.
    expect(WaitForSingleObject(child.hProcess, 3000) == WAIT_OBJECT_0, "target-exits-normally");
    CloseHandle(child.hThread); CloseHandle(child.hProcess);
    end(); FreeLibrary(library);
    return failed ? 1 : 0;
}
int IsolatedCheck() {
    wchar_t name[100]{};
    swprintf_s(name, L"GhostSlacking.InputPrototype.Tests.%lu.%llu", GetCurrentProcessId(), GetTickCount64());
    // No DESKTOP_SWITCHDESKTOP and never call SwitchDesktop.
    HDESK desktop = CreateDesktopW(name, nullptr, nullptr, 0,
        DESKTOP_CREATEWINDOW | DESKTOP_READOBJECTS | DESKTOP_WRITEOBJECTS | DESKTOP_ENUMERATE | DESKTOP_HOOKCONTROL, nullptr);
    if (!desktop) return 20;
    PROCESS_INFORMATION process{};
    if (!Launch(L"--check", name, process)) { CloseDesktop(desktop); return 21; }
    const DWORD waited = WaitForSingleObject(process.hProcess, 15000);
    DWORD code = 22;
    if (waited == WAIT_OBJECT_0) GetExitCodeProcess(process.hProcess, &code);
    CloseHandle(process.hThread); CloseHandle(process.hProcess); CloseDesktop(desktop);
    printf("Isolated bridge self-test exit code: %lu (details: bridge-self-test.log)\n", code);
    return static_cast<int>(code);
}
}
int wmain(int argc, wchar_t** argv) {
    if (argc > 1 && wcscmp(argv[1], L"--self-test") == 0) return IsolatedCheck();
    if (argc > 1 && wcscmp(argv[1], L"--check") == 0) return Check();
    const HWND window = CreateFixture();
    if (!window) return 30;
    if (argc > 1 && wcscmp(argv[1], L"--foreground") == 0) SetForegroundWindow(window);
    MSG message{};
    while (GetMessageW(&message, nullptr, 0, 0) > 0) {
        // Deliberately handle shortcuts before DispatchMessage, like real apps.
        // No system clipboard is read or modified by these fixture commands.
        if (message.message == WM_KEYDOWN && (GetKeyState(VK_CONTROL) & 0x8000) &&
            (message.wParam == 'A' || message.wParam == 'C' || message.wParam == 'V')) { ++shortcuts; continue; }
        TranslateMessage(&message); DispatchMessageW(&message);
    }
    return 0;
}
