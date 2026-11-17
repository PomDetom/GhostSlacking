#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <tlhelp32.h>
#include <bcrypt.h>
#include <array>
#include <vector>
#include <cstring>
#include <cwchar>
#include <deque>

// Experimental input bridge: x64, one registered window and one UI thread. No global
// injection, system input, clipboard access or persistent input diagnostics.
namespace {
constexpr DWORD Magic = 0x47534931;
constexpr ULONGLONG LeaseMs = 2000;
constexpr ULONG QueueCapacity = 128;
struct InputPacket {
    HWND recipient;
    UINT message;
    WPARAM wparam;
    LPARAM lparam;
    POINT cursor;
    DWORD time;
    BYTE keyboard[256];
};
struct Shared {
    DWORD magic, ownerPid, targetPid, threadId;
    ULONGLONG ownerStart, targetStart;
    HWND root, foreground;
    volatile LONG active, stopping, command, sequence, completed, error, patches, blocked;
    volatile LONG written, read, barrier, flushed;
    alignas(8) volatile LONG64 heartbeat;
    alignas(8) volatile LONG64 pendingMenus[32];
    volatile LONG pendingReady[32];
    volatile LONG errorStage;
    InputPacket queue[QueueCapacity];
};
struct Patch { void** slot; void* original; void* replacement; };
struct DelayImports { DWORD attributes, name, module, iat, names, bound, unload, timestamp; };
Shared* host = nullptr;
HANDLE hostMapping = nullptr;
HANDLE hostTarget = nullptr;
HHOOK hostHook = nullptr;
DWORD nonce = 0;
Shared* guest = nullptr;
HANDLE guestMapping = nullptr, ownerProcess = nullptr;
DWORD guestThread = 0;
DWORD guestKey = 0;
HHOOK guestCbt = nullptr;
HHOOK guestMessages = nullptr, guestReturns = nullptr;
HWND focus = nullptr, capture = nullptr;
POINT cursor = {};
BYTE keys[256]{}, originalKeys[256]{};
DWORD inputTime = 0;
WORD mouseFlags = 0;
MSG currentInput{};
bool injectedMessage = false, inFlight = false, cleaning = false, windowActivated = false;
int dispatchDepth = 0;
bool stopRequested = false;
HKL englishLayout = nullptr;
std::array<HWND, 256> heldKeys{};
std::array<HWND, 5> heldButtons{};
std::array<HWND, 5> heldMouseRecipients{};
std::array<HWND, 5> heldMenuRoots{};
std::array<bool, 5> heldMenuButtons{};
std::array<bool, 5> closedMenuButtons{};
struct Character { ULONG serial; InputPacket packet; };
std::deque<Character> characters;
ULONG characterSerial = 0;
std::vector<Patch> patches;
std::vector<HMODULE> patchedModules;
struct Presentation { HWND window; bool alive; };
std::vector<Presentation> presentations;
void CloseGuest();
LRESULT CALLBACK QueuedInput(int code, WPARAM wparam, LPARAM lparam);
LRESULT CALLBACK ReturnedInput(int code, WPARAM wparam, LPARAM lparam);

ULONGLONG StartTime(HANDLE process) {
    FILETIME start{}, exit{}, kernel{}, user{};
    if (!GetProcessTimes(process, &start, &exit, &kernel, &user)) return 0;
    ULARGE_INTEGER value{};
    value.LowPart = start.dwLowDateTime; value.HighPart = start.dwHighDateTime;
    return value.QuadPart;
}
UINT Message() { static const UINT value = RegisterWindowMessageW(L"GhostSlacking.ExclusiveInputBridge.queue.v1"); return value; }
UINT InputMessage() { static const UINT value = RegisterWindowMessageW(L"GhostSlacking.ExclusiveInputBridge.input.v1"); return value; }
UINT StopMessage() { static const UINT value = RegisterWindowMessageW(L"GhostSlacking.ExclusiveInputBridge.stop.v1"); return value; }
UINT CharacterMessage() { static const UINT value = RegisterWindowMessageW(L"GhostSlacking.ExclusiveInputBridge.character.v1"); return value; }
UINT BarrierMessage() { static const UINT value = RegisterWindowMessageW(L"GhostSlacking.ExclusiveInputBridge.barrier.v1"); return value; }
UINT MenuReadyMessage() { static const UINT value = RegisterWindowMessageW(L"GhostSlacking.ExclusiveInputBridge.menu-ready.v1"); return value; }
void MappingName(wchar_t (&name)[100], DWORD pid, DWORD key) {
    swprintf_s(name, L"Local\\GhostSlacking.InputBridge.%lu.%08lx", pid, key);
}
bool HostIdentity() {
    DWORD pid = 0;
    return host && hostTarget && WaitForSingleObject(hostTarget, 0) == WAIT_TIMEOUT &&
        GetWindowThreadProcessId(host->root, &pid) == host->threadId && pid == host->targetPid;
}
bool SameThread(HWND hwnd, DWORD thread, DWORD process) {
    DWORD pid = 0;
    return hwnd && GetWindowThreadProcessId(hwnd, &pid) == thread && pid == process;
}
bool OwnedByTarget(HWND hwnd, HWND root, DWORD thread, DWORD process) {
    for (int depth = 0; hwnd && depth < 16; ++depth) {
        if (!SameThread(hwnd, thread, process)) return false;
        if (hwnd == root || IsChild(root, hwnd)) return true;
        hwnd = GetWindow(GetAncestor(hwnd, GA_ROOT), GW_OWNER);
    }
    return false;
}
bool PendingMenu(HWND hwnd, Shared* state) {
    if (!state) return false;
    for (auto& value : state->pendingMenus)
        if (InterlockedCompareExchange64(&value, 0, 0) == reinterpret_cast<LONG64>(hwnd)) return hwnd != nullptr;
    return false;
}
void RemovePending(HWND hwnd) {
    if (!guest) return;
    for (int index = 0; index < 32; ++index)
        if (InterlockedCompareExchange64(&guest->pendingMenus[index], 0, reinterpret_cast<LONG64>(hwnd)) == reinterpret_cast<LONG64>(hwnd))
            InterlockedExchange(&guest->pendingReady[index], 0);
}
bool DeferMenu(HWND hwnd) {
    if (PendingMenu(hwnd, guest)) return true;
    for (int index = 0; index < 32; ++index)
        if (InterlockedCompareExchange64(&guest->pendingMenus[index], reinterpret_cast<LONG64>(hwnd), 0) == 0) return true;
    guest->error = ERROR_NOT_ENOUGH_QUOTA;
    return false;
}
HWND MenuRoot(HWND hwnd, Shared* state, bool allowHidden = false) {
    if (!state || !SameThread(hwnd, state->threadId, state->targetPid)) return nullptr;
    const HWND popup = GetAncestor(hwnd, GA_ROOT);
    if (popup == state->root || (!allowHidden && !IsWindowVisible(popup) && !PendingMenu(popup, state)) || !IsWindowEnabled(popup)) return nullptr;
    const LONG_PTR style = GetWindowLongPtrW(popup, GWL_STYLE);
    if (!(style & WS_POPUP) || (style & WS_CHILD) || (style & WS_CAPTION) == WS_CAPTION) return nullptr;
    if (GetWindowLongPtrW(popup, GWL_EXSTYLE) & WS_EX_TRANSPARENT) return nullptr;
    wchar_t name[128]{}; GetClassNameW(popup, name, 128);
    if (wcscmp(name, L"Chrome_WidgetWin_1") != 0) return nullptr;
    if (OwnedByTarget(GetWindow(popup, GW_OWNER), state->root, state->threadId, state->targetPid)) return popup;
    return nullptr;
}
HWND FindMenu(Shared* state, const POINT* point = nullptr) {
    // Walk current Z order, with no cached HWNDs carried across sessions.
    HWND window = GetTopWindow(nullptr);
    for (int count = 0; window && count < 4096; ++count, window = GetWindow(window, GW_HWNDNEXT)) {
        if (!MenuRoot(window, state) || (point && !IsWindowVisible(window))) continue;
        RECT bounds{};
        if (!point || (GetWindowRect(window, &bounds) && PtInRect(&bounds, *point))) return window;
    }
    return nullptr;
}
bool Belongs(HWND hwnd) {
    DWORD pid = 0;
    return guest && hwnd && GetWindowThreadProcessId(hwnd, &pid) == guestThread &&
        pid == GetCurrentProcessId() && (hwnd == guest->root || IsChild(guest->root, hwnd) || MenuRoot(hwnd, guest));
}
bool Scoped() {
    // Other threads always pass through, including threads in the same process.
    if (GetCurrentThreadId() != guestThread || !guest || !guest->active) return false;
    const bool live = GetTickCount64() - static_cast<ULONGLONG>(InterlockedCompareExchange64(&guest->heartbeat, 0, 0)) < LeaseMs &&
        ownerProcess && WaitForSingleObject(ownerProcess, 0) == WAIT_TIMEOUT &&
        GetForegroundWindow() == guest->foreground && IsWindow(guest->root);
    if (!live) InterlockedExchange(&guest->active, 0);
    return live;
}
bool Virtualized() {
    return GetCurrentThreadId() == guestThread && guest && (inFlight || dispatchDepth || cleaning || Scoped());
}
void MarkMenuClosed(HWND window) {
    for (auto& frame : presentations) if (frame.window == window) frame.alive = false;
    for (size_t button = 0; button < heldMenuRoots.size(); ++button)
        if (heldMenuButtons[button] && (heldMenuRoots[button] == window || heldMouseRecipients[button] == window))
            closedMenuButtons[button] = true;
}
LRESULT CALLBACK ActivationGuard(int code, WPARAM wparam, LPARAM lparam) {
    if (code == HCBT_DESTROYWND && guest) {
        const HWND window = reinterpret_cast<HWND>(wparam);
        MarkMenuClosed(window); RemovePending(window);
    }
    if ((code == HCBT_ACTIVATE || code == HCBT_SETFOCUS) && Virtualized()) {
        InterlockedIncrement(&guest->blocked);
        return 1;
    }
    return CallNextHookEx(nullptr, code, wparam, lparam);
}
HWND WINAPI FakeGetFocus() { return Virtualized() ? focus : GetFocus(); }
HWND WINAPI FakeCreateWindowExW(DWORD extended, LPCWSTR name, LPCWSTR title, DWORD style, int x, int y,
    int width, int height, HWND owner, HMENU menu, HINSTANCE instance, LPVOID parameter) {
    bool defer = false;
    if (Virtualized() && (style & WS_VISIBLE) && (style & WS_POPUP) && !(style & WS_CHILD) &&
        (style & WS_CAPTION) != WS_CAPTION && OwnedByTarget(owner, guest->root, guestThread, guest->targetPid)) {
        if (reinterpret_cast<ULONG_PTR>(name) > 0xffff) defer = wcscmp(name, L"Chrome_WidgetWin_1") == 0;
        else {
            WNDCLASSEXW expected{}, requested{}; expected.cbSize = requested.cbSize = sizeof(WNDCLASSEXW);
            defer = GetClassInfoExW(instance, L"Chrome_WidgetWin_1", &expected) && GetClassInfoExW(instance, name, &requested) &&
                requested.lpfnWndProc == expected.lpfnWndProc;
        }
    }
    const HWND window = CreateWindowExW(extended, name, title, defer ? style & ~WS_VISIBLE : style,
        x, y, width, height, owner, menu, instance, parameter);
    if (defer && window) {
        if (MenuRoot(window, guest, true)) DeferMenu(window);
        else ShowWindow(window, SW_SHOWNOACTIVATE);
    }
    return window;
}
BOOL WINAPI FakeIsWindowVisible(HWND hwnd) {
    if (Virtualized() && MenuRoot(hwnd, guest, true) && PendingMenu(GetAncestor(hwnd, GA_ROOT), guest)) return TRUE;
    return IsWindowVisible(hwnd);
}
BOOL WINAPI FakeShowWindow(HWND hwnd, int command) {
    if (Virtualized() && hwnd == GetAncestor(hwnd, GA_ROOT) && MenuRoot(hwnd, guest, true)) {
        const bool pending = PendingMenu(hwnd, guest);
        if (command == SW_HIDE) { MarkMenuClosed(hwnd); RemovePending(hwnd); return ShowWindow(hwnd, command) || pending; }
        if (!IsWindowVisible(hwnd)) return DeferMenu(hwnd) ? FALSE : ShowWindow(hwnd, SW_HIDE);
    }
    return ShowWindow(hwnd, command);
}
BOOL WINAPI FakeSetWindowPos(HWND hwnd, HWND after, int x, int y, int width, int height, UINT flags) {
    if (Virtualized() && hwnd == GetAncestor(hwnd, GA_ROOT) && MenuRoot(hwnd, guest, true)) {
        // The host raises its own overlay before presentation. A target must
        // never reference that potentially higher-integrity HWND or raise a
        // menu above it from a nested framework show callback.
        after = nullptr;
        flags |= SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_NOACTIVATE;
        if (flags & SWP_HIDEWINDOW) { MarkMenuClosed(hwnd); RemovePending(hwnd); }
        else if ((flags & SWP_SHOWWINDOW) && !IsWindowVisible(hwnd)) {
            if (!DeferMenu(hwnd)) return FALSE;
            const BOOL result = SetWindowPos(hwnd, after, x, y, width, height, (flags & ~SWP_SHOWWINDOW) | SWP_NOACTIVATE);
            if (!result) RemovePending(hwnd);
            return result;
        }
    }
    return SetWindowPos(hwnd, after, x, y, width, height, flags);
}
HWND WINAPI FakeGetForegroundWindow() { return Virtualized() ? guest->root : GetForegroundWindow(); }
HWND WINAPI FakeGetActiveWindow() { return Virtualized() ? guest->root : GetActiveWindow(); }
HWND WINAPI FakeSetFocus(HWND hwnd) {
    if (!Virtualized()) return SetFocus(hwnd);
    if (!Belongs(hwnd)) return nullptr;
    const HWND previous = focus;
    focus = hwnd;
    if (previous != hwnd) {
        if (Belongs(previous)) SendMessageW(previous, WM_KILLFOCUS, reinterpret_cast<WPARAM>(hwnd), 0);
        SendMessageW(hwnd, WM_SETFOCUS, reinterpret_cast<WPARAM>(previous), 0);
    }
    return previous;
}
BOOL WINAPI FakeSetForegroundWindow(HWND hwnd) {
    if (!Virtualized()) return SetForegroundWindow(hwnd);
    InterlockedIncrement(&guest->blocked);
    return Belongs(hwnd) ? TRUE : FALSE;
}
HWND WINAPI FakeSetActiveWindow(HWND hwnd) {
    if (!Virtualized()) return SetActiveWindow(hwnd);
    InterlockedIncrement(&guest->blocked);
    return Belongs(hwnd) ? guest->root : nullptr;
}
SHORT WINAPI FakeGetKeyState(int key) {
    if (!Virtualized()) return GetKeyState(key);
    return key >= 0 && key < 256 ? static_cast<SHORT>((keys[key] & 0x80 ? 0x8000 : 0) | (keys[key] & 1)) : 0;
}
SHORT WINAPI FakeGetAsyncKeyState(int key) {
    if (!Virtualized()) return GetAsyncKeyState(key);
    return key >= 0 && key < 256 && (keys[key] & 0x80) ? static_cast<SHORT>(0x8000) : 0;
}
BOOL WINAPI FakeGetKeyboardState(PBYTE state) {
    if (!Virtualized()) return GetKeyboardState(state);
    if (!state) return FALSE;
    memcpy(state, keys, 256); return TRUE;
}
BOOL WINAPI FakeGetCursorPos(LPPOINT point) {
    if (!Virtualized()) return GetCursorPos(point);
    if (!point) return FALSE;
    *point = cursor; return TRUE;
}
BOOL WINAPI FakeSetCursorPos(int x, int y) {
    if (!Virtualized()) return SetCursorPos(x, y);
    cursor = {x, y}; return TRUE;
}
HWND WINAPI FakeGetCapture() { return Virtualized() ? capture : GetCapture(); }
HWND WINAPI FakeSetCapture(HWND hwnd) {
    if (!Virtualized()) return SetCapture(hwnd);
    const HWND previous = capture;
    if (Belongs(hwnd)) capture = hwnd;
    return previous;
}
BOOL WINAPI FakeReleaseCapture() {
    if (!Virtualized()) return ReleaseCapture();
    const HWND previous = capture; capture = nullptr;
    if (Belongs(previous)) SendMessageW(previous, WM_CAPTURECHANGED, 0, 0);
    return TRUE;
}
BOOL WINAPI FakeClipCursor(const RECT* rectangle) { return Virtualized() ? TRUE : ClipCursor(rectangle); }
DWORD WINAPI FakeGetMessagePos() { return Virtualized() && injectedMessage ? static_cast<DWORD>(MAKELPARAM(cursor.x, cursor.y)) : GetMessagePos(); }
LONG WINAPI FakeGetMessageTime() { return Virtualized() && injectedMessage ? static_cast<LONG>(inputTime) : GetMessageTime(); }
BOOL WINAPI FakeGetGUIThreadInfo(DWORD thread, PGUITHREADINFO info) {
    if (!Virtualized() || (thread && thread != guestThread)) return GetGUIThreadInfo(thread, info);
    if (!info || info->cbSize != sizeof(GUITHREADINFO)) return FALSE;
    GUITHREADINFO original{}; original.cbSize = sizeof(original);
    GetGUIThreadInfo(guestThread, &original);
    *info = original;
    info->hwndActive = guest->root; info->hwndFocus = focus; info->hwndCapture = capture;
    return TRUE;
}
BOOL WINAPI FakeTranslateMessage(const MSG* message) {
    if (!Virtualized() || !injectedMessage || !message || message->hwnd != currentInput.hwnd ||
        message->message != currentInput.message || message->wParam != currentInput.wParam || message->time != currentInput.time)
        return TranslateMessage(message);
    if (message->message != WM_KEYDOWN) return FALSE;
    wchar_t text[8]{};
    const UINT key = static_cast<UINT>(message->wParam);
    int count = englishLayout ? ToUnicodeEx(key, (static_cast<ULONG_PTR>(message->lParam) >> 16) & 0xff,
        keys, text, 8, 4, englishLayout) : 0;
    if (!englishLayout) {
        const bool shift = (keys[VK_SHIFT] & 0x80) != 0;
        if (key >= 'A' && key <= 'Z') {
            text[0] = (keys[VK_CONTROL] & 0x80) ? static_cast<wchar_t>(key & 0x1f)
                : static_cast<wchar_t>(shift != ((keys[VK_CAPITAL] & 1) != 0) ? key : key + 32);
            count = 1;
        } else if (key == VK_SPACE || key == VK_RETURN || key == VK_BACK || key == VK_TAB) {
            text[0] = static_cast<wchar_t>(key); count = 1;
        } else if (key >= '0' && key <= '9' && !shift) { text[0] = static_cast<wchar_t>(key); count = 1; }
    }
    if (count < 0 || count > 8) return FALSE;
    for (int index = 0; index < count; ++index) {
        if (characters.size() >= QueueCapacity) { guest->error = ERROR_NOT_ENOUGH_QUOTA; break; }
        InputPacket packet{};
        packet.recipient = message->hwnd; packet.message = WM_CHAR; packet.wparam = text[index]; packet.lparam = message->lParam;
        packet.cursor = cursor; packet.time = inputTime; memcpy(packet.keyboard, keys, 256);
        const ULONG serial = ++characterSerial;
        characters.push_back({serial, packet});
        if (!PostMessageW(guest->root, CharacterMessage(), serial, guestKey)) {
            characters.pop_back(); guest->error = GetLastError(); break;
        }
    }
    return count > 0;
}
LRESULT WINAPI FakeDispatchMessageW(const MSG* message) {
    if (!Virtualized()) return DispatchMessageW(message);
    ++dispatchDepth;
    const LRESULT result = DispatchMessageW(message);
    if (--dispatchDepth == 0) {
        inFlight = false;
        if (stopRequested && guest) PostMessageW(guest->root, StopMessage(), guest->ownerPid, guestKey);
    }
    return result;
}
LRESULT WINAPI FakeDispatchMessageA(const MSG* message) {
    if (!Virtualized()) return DispatchMessageA(message);
    ++dispatchDepth;
    const LRESULT result = DispatchMessageA(message);
    if (--dispatchDepth == 0) {
        inFlight = false;
        if (stopRequested && guest) PostMessageW(guest->root, StopMessage(), guest->ownerPid, guestKey);
    }
    return result;
}

void* Replacement(const char* name) {
#define ENTRY(api) if (strcmp(name, #api) == 0) return reinterpret_cast<void*>(&Fake##api)
    ENTRY(GetFocus); ENTRY(SetFocus); ENTRY(GetForegroundWindow); ENTRY(SetForegroundWindow);
    ENTRY(GetActiveWindow); ENTRY(SetActiveWindow); ENTRY(GetKeyState); ENTRY(GetAsyncKeyState);
    ENTRY(GetKeyboardState); ENTRY(GetCursorPos); ENTRY(SetCursorPos); ENTRY(GetCapture);
    ENTRY(SetCapture); ENTRY(ReleaseCapture); ENTRY(ClipCursor);
    ENTRY(GetMessagePos); ENTRY(GetMessageTime); ENTRY(GetGUIThreadInfo); ENTRY(TranslateMessage);
    ENTRY(DispatchMessageW); ENTRY(DispatchMessageA);
    ENTRY(IsWindowVisible); ENTRY(ShowWindow); ENTRY(SetWindowPos);
    ENTRY(CreateWindowExW);
#undef ENTRY
    return nullptr;
}
bool WriteSlot(void** slot, void* expected, void* value) {
    DWORD protection = 0;
    if (!VirtualProtect(slot, sizeof(void*), PAGE_READWRITE, &protection)) return false;
    const bool changed = InterlockedCompareExchangePointer(slot, value, expected) == expected;
    DWORD ignored = 0;
    VirtualProtect(slot, sizeof(void*), protection, &ignored);
    return changed;
}
void RestoreImports() {
    for (const auto& patch : patches) {
        MEMORY_BASIC_INFORMATION memory{};
        if (VirtualQuery(patch.slot, &memory, sizeof(memory)) && memory.State == MEM_COMMIT)
            WriteSlot(patch.slot, patch.replacement, patch.original);
    }
    patches.clear();
    for (const HMODULE module : patchedModules) FreeLibrary(module);
    patchedModules.clear();
}
void PatchImports() {
    wchar_t windows[MAX_PATH]{};
    const UINT length = GetWindowsDirectoryW(windows, MAX_PATH);
    HMODULE self = nullptr;
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
        reinterpret_cast<LPCWSTR>(&PatchImports), &self);
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE, GetCurrentProcessId());
    if (snapshot == INVALID_HANDLE_VALUE) return;
    MODULEENTRY32W module{}; module.dwSize = sizeof(module);
    for (BOOL next = Module32FirstW(snapshot, &module); next; next = Module32NextW(snapshot, &module)) {
        // Patch only the application and non-Windows modules. Never user32,
        // system DLLs, our own imports, executable code, or another process.
        if (module.hModule == self || _wcsnicmp(module.szModule, L"GhostSlacking.Input", wcslen(L"GhostSlacking.Input")) == 0 ||
            (length && _wcsnicmp(module.szExePath, windows, length) == 0 &&
            module.szExePath[length] == L'\\')) continue;
        HMODULE retained = nullptr;
        if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
            reinterpret_cast<LPCWSTR>(module.modBaseAddr), &retained)) continue;
        // A retained reference keeps an application's delay/import table alive
        // until it is restored, including during plugin unload requests.
        patchedModules.push_back(retained);
        BYTE* base = module.modBaseAddr;
        const auto dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base);
        if (dos->e_magic != IMAGE_DOS_SIGNATURE) continue;
        const auto nt = reinterpret_cast<IMAGE_NT_HEADERS*>(base + dos->e_lfanew);
        if (nt->Signature != IMAGE_NT_SIGNATURE) continue;
        auto patchTable = [&](const char* dll, DWORD nameRva, DWORD slotRva) {
            if (_stricmp(dll, "user32.dll") != 0 && _strnicmp(dll, "api-ms-win-ntuser-", 18) != 0 &&
                _strnicmp(dll, "ext-ms-win-ntuser-", 18) != 0) return;
            if (!nameRva || !slotRva) return;
            auto names = reinterpret_cast<IMAGE_THUNK_DATA*>(base + nameRva);
            auto slots = reinterpret_cast<IMAGE_THUNK_DATA*>(base + slotRva);
            for (; names->u1.AddressOfData; ++names, ++slots) {
                if (IMAGE_SNAP_BY_ORDINAL(names->u1.Ordinal)) continue;
                const char* name = reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(base + names->u1.AddressOfData)->Name;
                void* replacement = Replacement(name);
                auto slot = reinterpret_cast<void**>(&slots->u1.Function);
                void* original = *slot;
                if (replacement && original != replacement && WriteSlot(slot, original, replacement))
                    patches.push_back({slot, original, replacement});
            }
        };
        const DWORD rva = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT].VirtualAddress;
        if (rva) for (auto descriptor = reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(base + rva); descriptor->Name; ++descriptor) {
            const char* dll = reinterpret_cast<const char*>(base + descriptor->Name);
            patchTable(dll, descriptor->OriginalFirstThunk, descriptor->FirstThunk);
        }
        const DWORD delayed = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_DELAY_IMPORT].VirtualAddress;
        if (delayed) for (auto descriptor = reinterpret_cast<DelayImports*>(base + delayed); descriptor->name; ++descriptor) {
            // Modern x64 delay descriptors use RVAs. Preserve unresolved delay
            // thunks as-is so normal loading resumes after the session ends.
            if ((descriptor->attributes & 1) == 0) continue;
            patchTable(reinterpret_cast<const char*>(base + descriptor->name), descriptor->names, descriptor->iat);
        }
    }
    CloseHandle(snapshot);
}
void CancelMenus() {
    if (!guest || !FindMenu(guest)) return;
    const bool previous = cleaning; cleaning = true;
    // Cancel through the owning framework while activation is still guarded.
    // Do this even in a nested menu loop so deferred teardown can finish.
    SendMessageW(guest->root, WM_CANCELMODE, 0, 0);
    cleaning = previous;
}
void CloseGuest() {
    // Only called at a target message-pump boundary. An in-flight event keeps
    // activation protection until it returns, even if its owner has stopped.
    cleaning = true;
    CancelMenus();
    characters.clear();
    ZeroMemory(keys, sizeof(keys));
    if (guest) {
        for (size_t key = 0; key < heldKeys.size(); ++key) if (Belongs(heldKeys[key]))
            SendMessageW(heldKeys[key], WM_KEYUP, key, (1L << 30) | (1L << 31));
        constexpr UINT ups[] = {WM_LBUTTONUP, WM_RBUTTONUP, WM_MBUTTONUP, WM_XBUTTONUP, WM_XBUTTONUP};
        for (size_t button = 0; button < heldButtons.size(); ++button) if (Belongs(heldButtons[button])) {
            POINT point = cursor; ScreenToClient(heldButtons[button], &point);
            SendMessageW(heldButtons[button], ups[button], button >= 3 ? MAKEWPARAM(0, button == 3 ? XBUTTON1 : XBUTTON2) : 0,
                MAKELPARAM(point.x, point.y));
        }
        if (Belongs(focus)) SendMessageW(focus, WM_KILLFOCUS, 0, 0);
        if (windowActivated && IsWindow(guest->root)) {
            SendMessageW(guest->root, WM_ACTIVATE, WA_INACTIVE, 0);
            SendMessageW(guest->root, WM_ACTIVATEAPP, FALSE, 0);
        }
        SetKeyboardState(originalKeys);
    }
    if (guest) InterlockedExchange(&guest->active, 0);
    if (guest) for (auto& value : guest->pendingMenus) InterlockedExchange64(&value, 0);
    if (guestCbt) UnhookWindowsHookEx(guestCbt);
    if (guestMessages) UnhookWindowsHookEx(guestMessages);
    if (guestReturns) UnhookWindowsHookEx(guestReturns);
    guestCbt = nullptr;
    guestMessages = guestReturns = nullptr;
    RestoreImports();
    if (ownerProcess) CloseHandle(ownerProcess);
    if (guest) UnmapViewOfFile(guest);
    if (guestMapping) CloseHandle(guestMapping);
    guest = nullptr; guestMapping = nullptr; ownerProcess = nullptr;
    focus = nullptr; capture = nullptr; guestThread = 0; guestKey = 0;
    heldKeys.fill(nullptr); heldButtons.fill(nullptr);
    heldMouseRecipients.fill(nullptr);
    heldMenuRoots.fill(nullptr); closedMenuButtons.fill(false);
    heldMenuButtons.fill(false);
    mouseFlags = 0;
    injectedMessage = inFlight = cleaning = windowActivated = false;
    stopRequested = false;
}
bool OpenGuest(DWORD pid, DWORD key, HWND root) {
    if (dispatchDepth || inFlight) return false;
    wchar_t name[100]{}; MappingName(name, pid, key);
    HANDLE mapping = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, name);
    if (!mapping) return false;
    auto state = static_cast<Shared*>(MapViewOfFile(mapping, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(Shared)));
    HANDLE owner = OpenProcess(SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
    const bool valid = state && owner && state->magic == Magic && state->ownerPid == pid &&
        state->targetPid == GetCurrentProcessId() && state->threadId == GetCurrentThreadId() &&
        state->root == root && state->ownerStart == StartTime(owner) &&
        state->targetStart == StartTime(GetCurrentProcess()) && state->command == 1;
    if (!valid) {
        if (state) UnmapViewOfFile(state);
        if (owner) CloseHandle(owner);
        CloseHandle(mapping); return false;
    }
    CloseGuest();
    guest = state; guestMapping = mapping; ownerProcess = owner; guestThread = GetCurrentThreadId(); guestKey = key;
    GUITHREADINFO info{}; info.cbSize = sizeof(info);
    GetGUIThreadInfo(guestThread, &info);
    focus = Belongs(info.hwndFocus) ? info.hwndFocus : root;
    GetKeyboardState(originalKeys);
    ZeroMemory(keys, sizeof(keys));
    std::array<HKL, 64> layouts{};
    const int layoutCount = GetKeyboardLayoutList(static_cast<int>(layouts.size()), layouts.data());
    englishLayout = nullptr;
    for (int index = 0; index < layoutCount; ++index)
        if ((reinterpret_cast<ULONG_PTR>(layouts[index]) & 0xffff) == 0x0409) { englishLayout = layouts[index]; break; }
    PatchImports();
    guestCbt = SetWindowsHookExW(WH_CBT, ActivationGuard, nullptr, guestThread);
    guestMessages = SetWindowsHookExW(WH_GETMESSAGE, QueuedInput, nullptr, guestThread);
    guestReturns = SetWindowsHookExW(WH_CALLWNDPROCRET, ReturnedInput, nullptr, guestThread);
    if (!guestCbt || !guestMessages || !guestReturns) guest->error = GetLastError();
    guest->patches = static_cast<LONG>(patches.size());
    return !patches.empty();
}

void DeliverPacket(MSG* message, const InputPacket& packet) {
    const auto button = packet.message == WM_LBUTTONDOWN || packet.message == WM_LBUTTONDBLCLK || packet.message == WM_LBUTTONUP ? 0 :
        packet.message == WM_RBUTTONDOWN || packet.message == WM_RBUTTONDBLCLK || packet.message == WM_RBUTTONUP ? 1 :
        packet.message == WM_MBUTTONDOWN || packet.message == WM_MBUTTONDBLCLK || packet.message == WM_MBUTTONUP ? 2 :
        packet.message == WM_XBUTTONDOWN || packet.message == WM_XBUTTONDBLCLK || packet.message == WM_XBUTTONUP ?
            (HIWORD(packet.wparam) == XBUTTON1 ? 3 : 4) : -1;
    constexpr int buttons[] = {VK_LBUTTON, VK_RBUTTON, VK_MBUTTON, VK_XBUTTON1, VK_XBUTTON2};
    const bool up = packet.message == WM_LBUTTONUP || packet.message == WM_RBUTTONUP ||
        packet.message == WM_MBUTTONUP || packet.message == WM_XBUTTONUP;
    if (up && button >= 0) {
        if (heldMouseRecipients[button] == packet.recipient && heldMenuButtons[button] &&
            (closedMenuButtons[button] || !MenuRoot(packet.recipient, guest))) {
            // A menu command can close/destroy its window on button-down.
            // Consume its matching up without touching a reused HWND or A.
            heldButtons[button] = nullptr; heldMouseRecipients[button] = nullptr; heldMenuButtons[button] = false;
            heldMenuRoots[button] = nullptr; closedMenuButtons[button] = false;
            if (capture == packet.recipient) capture = nullptr;
            mouseFlags = LOWORD(packet.wparam);
            keys[buttons[button]] = 0; SetKeyboardState(keys);
            message->message = WM_NULL;
            return;
        }
    }
    HWND receiver = packet.recipient;
    if (packet.message >= WM_KEYFIRST && packet.message <= WM_KEYLAST && Belongs(focus)) receiver = focus;
    if (packet.message >= WM_MOUSEFIRST && packet.message <= WM_MOUSELAST && Belongs(capture)) receiver = capture;
    if (!Belongs(receiver)) { guest->error = ERROR_INVALID_WINDOW_HANDLE; message->message = WM_NULL; return; }
    if (PendingMenu(GetAncestor(receiver, GA_ROOT), guest) && packet.message >= WM_MOUSEFIRST && packet.message <= WM_MOUSELAST) {
        if (up && button >= 0) {
            heldButtons[button] = heldMouseRecipients[button] = nullptr; heldMenuButtons[button] = false;
            heldMenuRoots[button] = nullptr; closedMenuButtons[button] = false;
            mouseFlags = LOWORD(packet.wparam);
            keys[buttons[button]] = 0;
            SetKeyboardState(keys);
        }
        message->message = WM_NULL; return; // No click/hover before a registered region has been presented.
    }
    cursor = packet.cursor; inputTime = packet.time; memcpy(keys, packet.keyboard, 256);
    if (packet.message >= WM_MOUSEFIRST && packet.message <= WM_MOUSELAST)
        mouseFlags = LOWORD(packet.wparam);
    keys[VK_LBUTTON] = mouseFlags & MK_LBUTTON ? 0x80 : 0;
    keys[VK_RBUTTON] = mouseFlags & MK_RBUTTON ? 0x80 : 0;
    keys[VK_MBUTTON] = mouseFlags & MK_MBUTTON ? 0x80 : 0;
    keys[VK_XBUTTON1] = mouseFlags & MK_XBUTTON1 ? 0x80 : 0;
    keys[VK_XBUTTON2] = mouseFlags & MK_XBUTTON2 ? 0x80 : 0;
    SetKeyboardState(keys);
    injectedMessage = inFlight = true;
    if (!windowActivated && ((packet.message >= WM_MOUSEFIRST && packet.message <= WM_MOUSELAST) ||
        (packet.message >= WM_KEYFIRST && packet.message <= WM_KEYLAST))) {
        windowActivated = true;
        SendMessageW(guest->root, WM_ACTIVATEAPP, TRUE, 0);
        SendMessageW(guest->root, WM_ACTIVATE, WA_ACTIVE, 0);
    }
    if (packet.message == WM_MOUSEMOVE)
        SendMessageW(receiver, WM_SETCURSOR, reinterpret_cast<WPARAM>(receiver), MAKELPARAM(HTCLIENT, WM_MOUSEMOVE));
    if (packet.message == WM_LBUTTONDOWN || packet.message == WM_LBUTTONDBLCLK) {
        if (!MenuRoot(receiver, guest)) FakeSetFocus(receiver);
    }
    message->hwnd = receiver; message->message = packet.message;
    message->wParam = packet.wparam; message->lParam = packet.lparam;
    message->time = packet.time; message->pt = packet.cursor;
    if (receiver != packet.recipient && packet.message >= WM_MOUSEFIRST && packet.message <= WM_MOUSELAST &&
        packet.message != WM_MOUSEWHEEL && packet.message != WM_MOUSEHWHEEL) {
        POINT point = packet.cursor; ScreenToClient(receiver, &point);
        message->lParam = MAKELPARAM(point.x, point.y);
    }
    if (packet.message == WM_KEYDOWN && packet.wparam < heldKeys.size()) heldKeys[packet.wparam] = receiver;
    if (packet.message == WM_KEYUP && packet.wparam < heldKeys.size()) heldKeys[packet.wparam] = nullptr;
    if (button >= 0) {
        heldButtons[button] = up ? nullptr : receiver;
        heldMouseRecipients[button] = up ? nullptr : packet.recipient;
        const HWND menu = MenuRoot(packet.recipient, guest);
        heldMenuRoots[button] = up ? nullptr : menu ? menu : MenuRoot(receiver, guest);
        heldMenuButtons[button] = heldMenuRoots[button] != nullptr;
        closedMenuButtons[button] = false;
    }
    injectedMessage = inFlight = true;
    currentInput = *message;
}
LRESULT CALLBACK ReturnedInput(int code, WPARAM wparam, LPARAM lparam) {
    if (code >= 0 && guest && inFlight) {
        const auto packet = reinterpret_cast<CWPRETSTRUCT*>(lparam);
        if (packet->hwnd == currentInput.hwnd && packet->message == currentInput.message && packet->wParam == currentInput.wParam)
            inFlight = false;
    }
    return CallNextHookEx(nullptr, code, wparam, lparam);
}
LRESULT CALLBACK QueuedInput(int code, WPARAM wparam, LPARAM lparam) {
    if (code < 0 || !guest) return CallNextHookEx(nullptr, code, wparam, lparam);
    const auto message = reinterpret_cast<MSG*>(lparam);
    if (wparam != PM_REMOVE) return CallNextHookEx(nullptr, code, wparam, lparam);
    // A new pump iteration also completes an accelerator that consumed the
    // previous key without dispatching it to a window procedure.
    if (!dispatchDepth) inFlight = false;
    injectedMessage = false;
    if (message->hwnd == guest->root && static_cast<DWORD>(message->lParam) == guestKey) {
        if (message->message == StopMessage()) {
            message->message = WM_NULL;
            CancelMenus();
            if (dispatchDepth) stopRequested = true;
            else CloseGuest();
        } else if (message->message == MenuReadyMessage()) {
            const HWND menu = reinterpret_cast<HWND>(message->wParam);
            message->message = WM_NULL;
            if (!guest->stopping && Scoped() && PendingMenu(menu, guest) && MenuRoot(menu, guest, true)) {
                ++dispatchDepth; // Window-position callbacks can pump a nested stop.
                bool ready = false;
                const size_t frame = presentations.size(); presentations.push_back({menu, true});
                HRGN region = CreateRectRgn(0, 0, 0, 0);
                const bool clipped = region && GetWindowRgn(menu, region) != ERROR;
                if (!clipped) {
                    InterlockedExchange(&guest->errorStage, 1);
                    InterlockedExchange(&guest->error, ERROR_INVALID_STATE);
                }
                else {
                    RemovePending(menu);
                    SetLastError(ERROR_SUCCESS);
                    const BOOL shown = SetWindowPos(menu, nullptr, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_SHOWWINDOW | SWP_NOZORDER);
                    DWORD error = shown ? ERROR_SUCCESS : GetLastError();
                    LONG stage = shown ? 0 : 2;
                    if (shown && presentations[frame].alive && MenuRoot(menu, guest)) {
                        SetLastError(ERROR_SUCCESS);
                        if (SetWindowRgn(menu, region, TRUE)) { region = nullptr; ready = true; }
                        else { error = GetLastError(); stage = 3; }
                    }
                    if (stage) {
                        InterlockedExchange(&guest->errorStage, stage);
                        InterlockedExchange(&guest->error, error ? error : ERROR_GEN_FAILURE);
                        if (presentations[frame].alive && MenuRoot(menu, guest)) {
                            MarkMenuClosed(menu); ShowWindow(menu, SW_HIDE);
                        }
                    }
                }
                if (region) DeleteObject(region);
                if (ready && !stopRequested && Scoped()) {
                    // A menu can clear the cursor while being shown. Refresh
                    // through the normal message pump immediately, even when
                    // the physical pointer has not moved since the right-up.
                    HWND receiver = FindMenu(guest, &cursor);
                    RECT rootBounds{};
                    if (!receiver && GetWindowRect(guest->root, &rootBounds) && PtInRect(&rootBounds, cursor)) receiver = guest->root;
                    POINT local = cursor;
                    if (receiver && ScreenToClient(receiver, &local)) {
                        InputPacket hover{};
                        hover.recipient = receiver; hover.message = WM_MOUSEMOVE;
                        hover.wparam = mouseFlags | (keys[VK_SHIFT] & 0x80 ? MK_SHIFT : 0) | (keys[VK_CONTROL] & 0x80 ? MK_CONTROL : 0);
                        hover.lparam = MAKELPARAM(local.x, local.y); hover.cursor = cursor; hover.time = GetTickCount();
                        memcpy(hover.keyboard, keys, 256);
                        DeliverPacket(message, hover);
                    }
                }
                presentations.pop_back();
                if (stopRequested) message->message = WM_NULL;
                if (--dispatchDepth == 0 && stopRequested) PostMessageW(guest->root, StopMessage(), guest->ownerPid, guestKey);
            }
        } else if (message->message == InputMessage()) {
            message->message = WM_NULL;
            const ULONG read = static_cast<ULONG>(guest->read), written = static_cast<ULONG>(guest->written);
            if (read != written) {
                const InputPacket packet = guest->queue[read % QueueCapacity];
                InterlockedExchange(&guest->read, static_cast<LONG>(read + 1));
                if (!guest->stopping && Scoped()) DeliverPacket(message, packet);
            }
        } else if (message->message == CharacterMessage()) {
            const ULONG serial = static_cast<ULONG>(message->wParam);
            message->message = WM_NULL;
            for (auto iterator = characters.begin(); iterator != characters.end(); ++iterator) if (iterator->serial == serial) {
                const InputPacket packet = iterator->packet;
                characters.erase(iterator);
                if (!guest->stopping && Scoped()) DeliverPacket(message, packet);
                break;
            }
        } else if (message->message == BarrierMessage()) {
            const LONG serial = static_cast<LONG>(message->wParam);
            message->message = WM_NULL;
            if (guest->read != guest->written || !characters.empty())
                PostMessageW(guest->root, BarrierMessage(), serial, guestKey);
            else InterlockedExchange(&guest->flushed, serial);
        }
    } else if (Scoped() && Belongs(message->hwnd) &&
        ((message->message >= WM_MOUSEFIRST && message->message <= WM_MOUSELAST) ||
        message->message == WM_MOUSELEAVE || message->message == WM_MOUSEHOVER)) {
        // System cursor motion still drives Reveal; do not deliver its duplicate
        // WM_MOUSEMOVE with different button flags to the target's renderer.
        message->message = WM_NULL;
    }
    return CallNextHookEx(nullptr, code, wparam, lparam);
}
}

extern "C" __declspec(dllexport) LRESULT CALLBACK GS_Hook(int code, WPARAM wparam, LPARAM lparam) {
    if (code >= 0) {
        const auto packet = reinterpret_cast<CWPSTRUCT*>(lparam);
        if (packet->message == Message()) {
            if (!guest || guestKey != static_cast<DWORD>(packet->lParam) ||
                guest->ownerPid != static_cast<DWORD>(packet->wParam) || guest->root != packet->hwnd)
                OpenGuest(static_cast<DWORD>(packet->wParam), static_cast<DWORD>(packet->lParam), packet->hwnd);
            if (guest && guestKey == static_cast<DWORD>(packet->lParam) && guest->root == packet->hwnd &&
                guest->ownerPid == static_cast<DWORD>(packet->wParam)) {
                const LONG sequence = guest->sequence;
                guest->error = guestCbt && guestMessages && guestReturns ? 0 : ERROR_INVALID_HOOK_HANDLE;
                if (!Scoped()) guest->error = ERROR_INVALID_STATE;
                InterlockedExchange(&guest->completed, sequence);
            }
        }
    }
    return CallNextHookEx(nullptr, code, wparam, lparam);
}

extern "C" __declspec(dllexport) DWORD GS_ABIVersion() { return 1; }
extern "C" __declspec(dllexport) void GS_End() {
    if (host && HostIdentity()) {
        InterlockedExchange(&host->stopping, 1);
        PostMessageW(host->root, StopMessage(), GetCurrentProcessId(), nonce);
    }
    if (host) InterlockedExchange(&host->active, 0);
    if (hostHook) UnhookWindowsHookEx(hostHook);
    if (host) UnmapViewOfFile(host);
    if (hostMapping) CloseHandle(hostMapping);
    if (hostTarget) CloseHandle(hostTarget);
    host = nullptr; hostMapping = nullptr; hostHook = nullptr; hostTarget = nullptr;
}
extern "C" __declspec(dllexport) DWORD GS_Begin(HWND root, HWND foreground, DWORD expectedPid, ULONGLONG expectedStart) {
    GS_End();
    DWORD pid = 0, foregroundPid = 0;
    const DWORD thread = GetWindowThreadProcessId(root, &pid);
    GetWindowThreadProcessId(foreground, &foregroundPid);
    if (!thread || pid != expectedPid || pid == GetCurrentProcessId() || pid == foregroundPid ||
        root == foreground || GetForegroundWindow() != foreground) return ERROR_INVALID_PARAMETER;
    HANDLE target = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE, FALSE, pid);
    if (!target) return GetLastError();
    BOOL wow = FALSE;
    const ULONGLONG targetStart = StartTime(target);
    const BOOL architecture = IsWow64Process(target, &wow);
    if (!architecture || wow || !targetStart || targetStart != expectedStart) { CloseHandle(target); return ERROR_NOT_SUPPORTED; }
    hostTarget = target;
    if (BCryptGenRandom(nullptr, reinterpret_cast<PUCHAR>(&nonce), sizeof(nonce), BCRYPT_USE_SYSTEM_PREFERRED_RNG) < 0)
        { GS_End(); return ERROR_GEN_FAILURE; }
    wchar_t name[100]{}; MappingName(name, GetCurrentProcessId(), nonce);
    hostMapping = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, sizeof(Shared), name);
    if (!hostMapping) { const DWORD error = GetLastError(); GS_End(); return error; }
    host = static_cast<Shared*>(MapViewOfFile(hostMapping, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(Shared)));
    if (!host) { const DWORD error = GetLastError(); GS_End(); return error; }
    *host = {};
    host->magic = Magic; host->ownerPid = GetCurrentProcessId(); host->targetPid = pid; host->threadId = thread;
    host->ownerStart = StartTime(GetCurrentProcess()); host->targetStart = targetStart;
    host->root = root; host->foreground = foreground; host->command = 1; host->sequence = 1;
    InterlockedExchange64(&host->heartbeat, static_cast<LONG64>(GetTickCount64()));
    InterlockedExchange(&host->active, 1);
    HMODULE module = nullptr;
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(&GS_Hook), &module);
    hostHook = SetWindowsHookExW(WH_CALLWNDPROC, GS_Hook, module, thread);
    DWORD_PTR ignored = 0;
    const bool sent = hostHook && SendMessageTimeoutW(root, Message(), GetCurrentProcessId(), nonce,
        SMTO_ABORTIFHUNG | SMTO_ERRORONEXIT, 1000, &ignored);
    if (!sent || host->completed != 1 || !host->patches || host->error) {
        const DWORD error = host->error ? static_cast<DWORD>(host->error) : !hostHook ? GetLastError() :
            !sent ? ERROR_TIMEOUT : host->completed != 1 ? ERROR_DLL_INIT_FAILED : ERROR_PROC_NOT_FOUND;
        GS_End(); return error;
    }
    return ERROR_SUCCESS;
}
extern "C" __declspec(dllexport) DWORD GS_Heartbeat() {
    if (!host || !host->active) return ERROR_INVALID_STATE;
    const ULONGLONG now = GetTickCount64();
    if (now - static_cast<ULONGLONG>(InterlockedCompareExchange64(&host->heartbeat, 0, 0)) >= LeaseMs)
        InterlockedExchange(&host->active, 0);
    else InterlockedExchange64(&host->heartbeat, static_cast<LONG64>(now));
    const ULONG read = static_cast<ULONG>(host->read), written = static_cast<ULONG>(host->written);
    if (read != written && static_cast<DWORD>(now) - host->queue[read % QueueCapacity].time > 1500) {
        host->error = ERROR_TIMEOUT; InterlockedExchange(&host->active, 0);
    }
    return host->error ? static_cast<DWORD>(host->error) : host->active ? ERROR_SUCCESS : ERROR_INVALID_STATE;
}
extern "C" __declspec(dllexport) HWND GS_MenuAt(LONG x, LONG y) {
    if (!HostIdentity() || !host->active || GetForegroundWindow() != host->foreground) return nullptr;
    const POINT point{x, y};
    return FindMenu(host, &point);
}
extern "C" __declspec(dllexport) BOOL GS_IsMenu(HWND hwnd) {
    return HostIdentity() && host->active && MenuRoot(hwnd, host) != nullptr;
}
extern "C" __declspec(dllexport) BOOL GS_HasMenus() {
    return HostIdentity() && host->active && FindMenu(host) != nullptr;
}
extern "C" __declspec(dllexport) DWORD GS_GetMenus(HWND* windows, DWORD capacity) {
    if (!windows || !HostIdentity() || !host->active) return 0;
    DWORD count = 0;
    HWND window = GetTopWindow(nullptr);
    for (int seen = 0; window && seen < 4096; ++seen, window = GetWindow(window, GW_HWNDNEXT))
        if (MenuRoot(window, host)) {
            if (count == capacity) return capacity + 1;
            windows[count++] = window;
        }
    return count;
}
extern "C" __declspec(dllexport) DWORD GS_ErrorStage() {
    return host ? static_cast<DWORD>(InterlockedCompareExchange(&host->errorStage, 0, 0)) : 0;
}
extern "C" __declspec(dllexport) DWORD GS_PresentMenus(const HWND* menus, DWORD count) {
    if (!menus || count > 64 || !HostIdentity() || !host->active) return ERROR_INVALID_STATE;
    for (DWORD ready = 0; ready < count; ++ready)
        for (int index = 0; index < 32; ++index) {
            const HWND menu = reinterpret_cast<HWND>(InterlockedCompareExchange64(&host->pendingMenus[index], 0, 0));
            if (menu && menu == menus[ready] && MenuRoot(menu, host, true) && InterlockedCompareExchange(&host->pendingReady[index], 1, 0) == 0 &&
                !PostMessageW(host->root, MenuReadyMessage(), reinterpret_cast<WPARAM>(menu), nonce)) return GetLastError();
        }
    return ERROR_SUCCESS;
}
extern "C" __declspec(dllexport) DWORD GS_Dispatch(HWND recipient, UINT message, WPARAM wparam, LPARAM lparam,
    const BYTE* keyboard, LONG x, LONG y) {
    if (!host || !host->active || !keyboard) return ERROR_INVALID_STATE;
    if (!HostIdentity()) return ERROR_INVALID_WINDOW_HANDLE;
    if (GetForegroundWindow() != host->foreground) return ERROR_INVALID_STATE;
    const DWORD health = GS_Heartbeat();
    if (health) return health;
    const ULONG written = static_cast<ULONG>(host->written), read = static_cast<ULONG>(host->read);
    if (written - read >= QueueCapacity) {
        InterlockedExchange(&host->active, 0); return ERROR_NOT_ENOUGH_QUOTA;
    }
    InputPacket packet{};
    packet.recipient = recipient; packet.message = message; packet.wparam = wparam; packet.lparam = lparam;
    packet.cursor = {x, y}; packet.time = static_cast<DWORD>(GetTickCount64()); memcpy(packet.keyboard, keyboard, 256);
    host->queue[written % QueueCapacity] = packet;
    InterlockedExchange(&host->written, static_cast<LONG>(written + 1));
    if (!PostMessageW(host->root, InputMessage(), written + 1, nonce)) {
        const DWORD error = GetLastError(); InterlockedExchange(&host->active, 0); return error;
    }
    return ERROR_SUCCESS; // Queue admission, not proof of application execution.
}
extern "C" __declspec(dllexport) DWORD GS_Flush(DWORD timeout) {
    if (!host || !host->active || !HostIdentity()) return ERROR_INVALID_STATE;
    const LONG serial = InterlockedIncrement(&host->barrier);
    if (!PostMessageW(host->root, BarrierMessage(), serial, nonce)) return GetLastError();
    const ULONGLONG deadline = GetTickCount64() + (timeout > 1500 ? 1500 : timeout);
    while (host->flushed != serial && GetTickCount64() < deadline) {
        const DWORD health = GS_Heartbeat();
        if (health) return health;
        Sleep(1);
    }
    const DWORD health = GS_Heartbeat();
    return health ? health : host->flushed == serial ? ERROR_SUCCESS : ERROR_TIMEOUT;
}
