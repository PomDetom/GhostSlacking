// Research-only host: no target activation, injection, clipboard replacement, or global layout change.
#define UNICODE
#define _UNICODE
#define NOMINMAX
#include <windows.h>
#include <msctf.h>
#include <textstor.h>
#include <olectl.h>
#include <wrl/client.h>
#include <algorithm>
#include <array>
#include <fstream>
#include <string>
#include <vector>
#include <map>
using Microsoft::WRL::ComPtr;
constexpr UINT KeyMessage = WM_APP + 1, CommitMessage = WM_APP + 2, LockMessage = WM_APP + 3, StopMessage = WM_APP + 4;
HWND host{}, target{}, protectedWindow{};
DWORD targetPid{};
FILETIME targetStart{};
HHOOK keyboardHook{};
HWINEVENTHOOK foregroundHook{};
bool active{}, independent{}, english{}, shiftTap{}, composing{};
unsigned generation{}, focusLosses{}, commits{}, eatenKeys{}, uneatenKeys{}, compositionStarts{}, compositionEnds{}, layoutChanges{};
DWORD protectedThread{};
HKL protectedLayout{};
std::array<bool, 256> borrowed{}, owned{};
BYTE keyState[256]{};
BYTE originalKeyState[256]{};
bool profileSelected{};
ULONGLONG drainDeadline{};
std::wstring status = L"Ctrl+Alt+F9: point at B and bind. Ctrl+Alt+F10: start/end. Ctrl+Alt+F12: exit.";
std::wstring pinyin;
std::vector<std::wstring> candidates;
UINT candidateSelection{};
std::map<std::wstring, std::vector<std::wstring>> dictionary;
ComPtr<ITfThreadMgrEx> manager;
ComPtr<ITfDocumentMgr> document;
ComPtr<ITfContext> context;
ComPtr<ITfKeystrokeMgr> keystrokes;
ComPtr<ITfUIElementMgr> uiManager;
DWORD uiCookie = TF_INVALID_COOKIE, compositionCookie = TF_INVALID_COOKIE;
TfClientId client{};
HRESULT lastResult = S_OK;
class Store;
Store* store{};
void Stop();
void PaintStatus() { InvalidateRect(host, nullptr, TRUE); }

bool Identity() {
    DWORD pid{};
    if (!target || !IsWindow(target) || !GetWindowThreadProcessId(target, &pid) || pid != targetPid) return false;
    HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
    FILETIME start{}, exit{}, kernel{}, user{};
    bool same = process && GetProcessTimes(process, &start, &exit, &kernel, &user) && CompareFileTime(&start, &targetStart) == 0;
    if (process) CloseHandle(process);
    return same;
}
bool Guard() {
    bool layoutSame = !protectedThread || GetKeyboardLayout(protectedThread) == protectedLayout;
    if (active && !layoutSame) ++layoutChanges;
    return active && layoutSame && Identity() && GetForegroundWindow() == protectedWindow;
}
void Commit(const std::wstring& text) {
    if (text.empty() || !Guard()) return;
    wchar_t name[128]{}; GetClassName(target, name, 128);
    DWORD_PTR result{};
    if (_wcsicmp(name, L"Edit") == 0 || _wcsnicmp(name, L"RichEdit", 8) == 0) {
        if (!IsWindowUnicode(target)) { status = L"ANSI controls are unsupported by this Unicode probe."; PaintStatus(); return; }
        if (!Guard() || !SendMessageTimeout(target, EM_REPLACESEL, TRUE, reinterpret_cast<LPARAM>(text.c_str()), SMTO_ABORTIFHUNG | SMTO_BLOCK, 100, &result)) {
            status = L"Text delivery failed; result unverified."; Stop(); return;
        }
    } else if (_wcsicmp(name, L"Scintilla") == 0) {
        for (wchar_t ch : text) {
            if (!Guard() || !SendMessageTimeout(target, WM_CHAR, ch, 1, SMTO_ABORTIFHUNG | SMTO_BLOCK, 100, &result)) {
                status = L"Character delivery failed; partial delivery possible."; Stop(); return;
            }
        }
    } else { status = L"No validated insertion adapter. Bind Edit, RichEdit, or a Scintilla text area."; PaintStatus(); return; }
    ++commits;
    status = L"Commit dispatched. Verify B and A's full focus record.";
    PaintStatus();
}

class Store final : public ITextStoreACP, public ITfContextOwnerCompositionSink, public ITfUIElementSink {
    LONG references = 1;
    ComPtr<ITextStoreACPSink> sink;
    DWORD lock{}, pending{};
public:
    std::wstring text;
    LONG start{}, end{};
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id, void** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        if (id == IID_IUnknown || id == IID_ITextStoreACP) *value = static_cast<ITextStoreACP*>(this);
        else if (id == IID_ITfContextOwnerCompositionSink) *value = static_cast<ITfContextOwnerCompositionSink*>(this);
        else if (id == IID_ITfUIElementSink) *value = static_cast<ITfUIElementSink*>(this);
        if (!*value) return E_NOINTERFACE;
        AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&references); }
    ULONG STDMETHODCALLTYPE Release() override { auto count = InterlockedDecrement(&references); if (!count) delete this; return count; }
    HRESULT STDMETHODCALLTYPE AdviseSink(REFIID id, IUnknown* value, DWORD) override {
        if (id != IID_ITextStoreACPSink) return E_INVALIDARG;
        ComPtr<ITextStoreACPSink> next;
        auto hr = value->QueryInterface(IID_PPV_ARGS(&next));
        if (FAILED(hr)) return hr;
        if (sink && sink.Get() != next.Get()) return CONNECT_E_ADVISELIMIT;
        sink = next; return S_OK;
    }
    HRESULT STDMETHODCALLTYPE UnadviseSink(IUnknown* value) override {
        ComPtr<ITextStoreACPSink> current;
        if (!value || FAILED(value->QueryInterface(IID_PPV_ARGS(&current))) || current.Get() != sink.Get()) return CONNECT_E_NOCONNECTION;
        sink.Reset(); return S_OK;
    }
    HRESULT STDMETHODCALLTYPE RequestLock(DWORD flags, HRESULT* session) override {
        if (!session) return E_POINTER;
        if (!sink) { *session = E_UNEXPECTED; return E_UNEXPECTED; }
        if (lock) {
            if (flags & TS_LF_SYNC) *session = TS_E_SYNCHRONOUS;
            else { pending |= flags; *session = TS_S_ASYNC; PostMessage(host, LockMessage, 0, 0); }
            return S_OK;
        }
        lock = flags; *session = sink->OnLockGranted(flags); lock = 0;
        if (pending) PostMessage(host, LockMessage, 0, 0);
        return S_OK;
    }
    void GrantPending() { if (pending && !lock) { DWORD flags = pending; pending = 0; HRESULT result{}; RequestLock(flags, &result); } }
    HRESULT STDMETHODCALLTYPE GetStatus(TS_STATUS* value) override { if (!value) return E_POINTER; value->dwDynamicFlags = 0; value->dwStaticFlags = TS_SS_NOHIDDENTEXT; return S_OK; }
    HRESULT STDMETHODCALLTYPE QueryInsert(LONG a, LONG b, ULONG, LONG* outA, LONG* outB) override {
        if (!outA || !outB || a < 0 || b < a || b > static_cast<LONG>(text.size())) return E_INVALIDARG;
        *outA = a; *outB = b; return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetSelection(ULONG index, ULONG count, TS_SELECTION_ACP* values, ULONG* fetched) override {
        if (!fetched || !values || !count) return E_INVALIDARG;
        if (!lock) return TS_E_NOLOCK;
        if (index != 0 && index != TS_DEFAULT_SELECTION) return TS_E_NOSELECTION;
        values[0] = { start, end, { TS_AE_END, FALSE } }; *fetched = 1; return S_OK;
    }
    HRESULT STDMETHODCALLTYPE SetSelection(ULONG count, const TS_SELECTION_ACP* values) override {
        if ((lock & TS_LF_READWRITE) != TS_LF_READWRITE) return TS_E_NOLOCK;
        if (count != 1 || !values || values[0].acpStart < 0 || values[0].acpEnd < values[0].acpStart || values[0].acpEnd > static_cast<LONG>(text.size())) return E_INVALIDARG;
        start = values[0].acpStart; end = values[0].acpEnd; return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetText(LONG a, LONG b, WCHAR* plain, ULONG capacity, ULONG* copied, TS_RUNINFO* runs, ULONG runCapacity, ULONG* runCount, LONG* next) override {
        if (!lock) return TS_E_NOLOCK;
        if (!copied || !runCount || !next) return E_POINTER;
        if (b == -1) b = static_cast<LONG>(text.size());
        if (a < 0 || b < a || b > static_cast<LONG>(text.size())) return TS_E_INVALIDPOS;
        ULONG count = capacity ? std::min(capacity, static_cast<ULONG>(b - a)) : runCapacity ? static_cast<ULONG>(b - a) : 0;
        if (capacity && count && !plain) return E_POINTER;
        if (capacity && count) std::copy_n(text.data() + a, count, plain);
        *copied = capacity ? count : 0; *next = a + count; *runCount = 0;
        if (runCapacity && runs && count) { runs[0] = { count, TS_RT_PLAIN }; *runCount = 1; }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE SetText(DWORD, LONG a, LONG b, const WCHAR* value, ULONG count, TS_TEXTCHANGE* change) override {
        if ((lock & TS_LF_READWRITE) != TS_LF_READWRITE) return TS_E_NOLOCK;
        if (!change || (count && !value) || a < 0 || b < a || b > static_cast<LONG>(text.size())) return E_INVALIDARG;
        text.replace(a, b - a, value ? value : L"", count);
        start = end = a + count; *change = { a, b, a + static_cast<LONG>(count) };
        PaintStatus(); return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetFormattedText(LONG, LONG, IDataObject** value) override { if (value) *value = nullptr; return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE GetEmbedded(LONG, REFGUID, REFIID, IUnknown** value) override { if (value) *value = nullptr; return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE QueryInsertEmbedded(const GUID*, const FORMATETC*, BOOL* value) override { if (!value) return E_POINTER; *value = FALSE; return S_OK; }
    HRESULT STDMETHODCALLTYPE InsertEmbedded(DWORD, LONG, LONG, IDataObject*, TS_TEXTCHANGE*) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE InsertTextAtSelection(DWORD flags, const WCHAR* value, ULONG count, LONG* a, LONG* b, TS_TEXTCHANGE* change) override {
        if (a) *a = start; if (b) *b = end;
        if (flags & TS_IAS_QUERYONLY) return S_OK;
        return SetText(0, start, end, value, count, change);
    }
    HRESULT STDMETHODCALLTYPE InsertEmbeddedAtSelection(DWORD, IDataObject*, LONG*, LONG*, TS_TEXTCHANGE*) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE RequestSupportedAttrs(DWORD, ULONG, const TS_ATTRID*) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE RequestAttrsAtPosition(LONG, ULONG, const TS_ATTRID*, DWORD) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE RequestAttrsTransitioningAtPosition(LONG, ULONG, const TS_ATTRID*, DWORD) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE FindNextAttrTransition(LONG, LONG halt, ULONG, const TS_ATTRID*, DWORD, LONG* next, BOOL* found, LONG* offset) override {
        if (!next || !found || !offset) return E_POINTER; *next = halt; *found = FALSE; *offset = 0; return S_OK;
    }
    HRESULT STDMETHODCALLTYPE RetrieveRequestedAttrs(ULONG, TS_ATTRVAL*, ULONG* fetched) override { if (!fetched) return E_POINTER; *fetched = 0; return S_OK; }
    HRESULT STDMETHODCALLTYPE GetEndACP(LONG* value) override { if (!value) return E_POINTER; *value = static_cast<LONG>(text.size()); return S_OK; }
    HRESULT STDMETHODCALLTYPE GetActiveView(TsViewCookie* value) override { if (!value) return E_POINTER; *value = 1; return S_OK; }
    HRESULT STDMETHODCALLTYPE GetACPFromPoint(TsViewCookie, const POINT*, DWORD, LONG* value) override { if (!value) return E_POINTER; *value = end; return S_OK; }
    HRESULT STDMETHODCALLTYPE GetTextExt(TsViewCookie, LONG, LONG, RECT* rect, BOOL* clipped) override {
        if (!rect || !clipped) return E_POINTER; GetWindowRect(host, rect); rect->right = rect->left + 16; rect->bottom = rect->top + 24; *clipped = FALSE; return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetScreenExt(TsViewCookie, RECT* rect) override { if (!rect) return E_POINTER; GetWindowRect(host, rect); return S_OK; }
    HRESULT STDMETHODCALLTYPE GetWnd(TsViewCookie, HWND* value) override { if (!value) return E_POINTER; *value = host; return S_OK; }
    HRESULT STDMETHODCALLTYPE OnStartComposition(ITfCompositionView*, BOOL* accepted) override { if (!accepted) return E_POINTER; *accepted = active; composing = active; if (active) ++compositionStarts; return S_OK; }
    HRESULT STDMETHODCALLTYPE OnUpdateComposition(ITfCompositionView*, ITfRange*) override { PaintStatus(); return S_OK; }
    HRESULT STDMETHODCALLTYPE OnEndComposition(ITfCompositionView*) override { ++compositionEnds; composing = false; if (active) PostMessage(host, CommitMessage, generation, 0); PaintStatus(); return S_OK; }
    void Candidates(DWORD id) {
        candidates.clear();
        if (!uiManager) return;
        ComPtr<ITfUIElement> element;
        ComPtr<ITfCandidateListUIElement> list;
        if (FAILED(uiManager->GetUIElement(id, &element)) || FAILED(element.As(&list))) return;
        UINT count{}, page{}, selected{}, needed{}; list->GetCount(&count); list->GetCurrentPage(&page); list->GetSelection(&selected);
        std::array<UINT, 256> pages{};
        UINT first = 0, last = count;
        if (SUCCEEDED(list->GetPageIndex(pages.data(), static_cast<UINT>(pages.size()), &needed)) && page < std::min(needed, static_cast<UINT>(pages.size()))) {
            first = pages[page];
            if (page + 1 < std::min(needed, static_cast<UINT>(pages.size()))) last = pages[page + 1];
        }
        candidateSelection = selected >= first ? selected - first : 0;
        for (UINT i = first; i < std::min(last, first + 9); ++i) {
            BSTR value{};
            if (SUCCEEDED(list->GetString(i, &value))) { candidates.emplace_back(value ? value : L"", SysStringLen(value)); SysFreeString(value); }
        }
        PaintStatus();
    }
    HRESULT STDMETHODCALLTYPE BeginUIElement(DWORD id, BOOL* show) override { if (!show) return E_POINTER; *show = FALSE; Candidates(id); return S_OK; }
    HRESULT STDMETHODCALLTYPE UpdateUIElement(DWORD id) override { Candidates(id); return S_OK; }
    HRESULT STDMETHODCALLTYPE EndUIElement(DWORD) override { candidates.clear(); PaintStatus(); return S_OK; }
    void Clear() {
        LONG old = static_cast<LONG>(text.size()); text.clear(); start = end = 0; pending = 0;
        if (sink && old && !lock) { TS_TEXTCHANGE change{0, old, 0}; sink->OnTextChange(0, &change); sink->OnSelectionChange(); }
    }
};

bool StartTsf() {
    lastResult = CoCreateInstance(CLSID_TF_ThreadMgr, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&manager));
    if (FAILED(lastResult)) return false;
    lastResult = manager->ActivateEx(&client, TF_TMAE_UIELEMENTENABLEDONLY);
    if (FAILED(lastResult)) return false;
    profileSelected = false;
    // Select an installed Microsoft Pinyin profile only inside this research process.
    ComPtr<ITfInputProcessorProfiles> profiles;
    if (SUCCEEDED(CoCreateInstance(CLSID_TF_InputProcessorProfiles, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&profiles)))) {
        ComPtr<IEnumTfLanguageProfiles> entries;
        if (SUCCEEDED(profiles->EnumLanguageProfiles(0x0804, &entries))) {
            TF_LANGUAGEPROFILE profile{}; ULONG fetched{};
            while (entries->Next(1, &profile, &fetched) == S_OK) {
                BSTR description{};
                if (SUCCEEDED(profiles->GetLanguageProfileDescription(profile.clsid, profile.langid, profile.guidProfile, &description))) {
                    std::wstring name(description ? description : L"", SysStringLen(description)); SysFreeString(description);
                    if (name.find(L"微软拼音") != std::wstring::npos || name.find(L"Microsoft Pinyin") != std::wstring::npos) {
                        profiles->ChangeCurrentLanguage(profile.langid);
                        lastResult = profiles->ActivateLanguageProfile(profile.clsid, profile.langid, profile.guidProfile);
                        profileSelected = SUCCEEDED(lastResult);
                        break;
                    }
                }
            }
        }
    }
    if (!profileSelected) { if (SUCCEEDED(lastResult)) lastResult = HRESULT_FROM_WIN32(ERROR_NOT_FOUND); return false; }
    if (!Guard()) return false;
    store = new Store();
    lastResult = manager->CreateDocumentMgr(&document); if (FAILED(lastResult)) return false;
    TfEditCookie edit{};
    lastResult = document->CreateContext(client, 0, static_cast<ITextStoreACP*>(store), &context, &edit); if (FAILED(lastResult)) return false;
    ComPtr<ITfSource> source;
    if (SUCCEEDED(context.As(&source))) source->AdviseSink(IID_ITfContextOwnerCompositionSink, static_cast<ITfContextOwnerCompositionSink*>(store), &compositionCookie);
    lastResult = document->Push(context.Get()); if (FAILED(lastResult)) return false;
    lastResult = manager->SetFocus(document.Get()); if (FAILED(lastResult)) return false;
    manager.As(&keystrokes); manager.As(&uiManager);
    source.Reset();
    if (SUCCEEDED(manager.As(&source))) source->AdviseSink(IID_ITfUIElementSink, static_cast<ITfUIElementSink*>(store), &uiCookie);
    return Guard() && keystrokes;
}
void EndTsf() {
    ComPtr<ITfSource> source;
    if (context && compositionCookie != TF_INVALID_COOKIE && SUCCEEDED(context.As(&source))) source->UnadviseSink(compositionCookie);
    source.Reset();
    if (manager && uiCookie != TF_INVALID_COOKIE && SUCCEEDED(manager.As(&source))) source->UnadviseSink(uiCookie);
    compositionCookie = uiCookie = TF_INVALID_COOKIE;
    if (document) document->Pop(TF_POPF_ALL);
    context.Reset(); document.Reset(); keystrokes.Reset(); uiManager.Reset();
    if (manager) manager->Deactivate(); manager.Reset();
    if (store) { store->Clear(); store->Release(); store = nullptr; }
    composing = false;
}
void Stop() {
    active = false; ++generation;
    pinyin.clear(); candidates.clear(); english = false; shiftTap = false;
    EndTsf();
    for (unsigned key = 0; key < 256; ++key) keyState[key] = originalKeyState[key] & 1;
    SetKeyboardState(keyState); // No stale held startup modifiers; this host thread only.
    drainDeadline = GetTickCount64() + 2000;
    PaintStatus();
}
void CALLBACK ForegroundEvent(HWINEVENTHOOK, DWORD, HWND window, LONG, LONG, DWORD, DWORD) {
    if (active && window != protectedWindow) { active = false; ++generation; ++focusLosses; PostMessage(host, StopMessage, 0, 0); }
}
LRESULT CALLBACK Keyboard(int code, WPARAM message, LPARAM data) {
    if (code < 0) return CallNextHookEx(keyboardHook, code, message, data);
    auto packet = *reinterpret_cast<KBDLLHOOKSTRUCT*>(data);
    unsigned key = packet.vkCode;
    if (key >= 256) return CallNextHookEx(keyboardHook, code, message, data);
    bool down = message == WM_KEYDOWN || message == WM_SYSKEYDOWN;
    if (borrowed[key]) { if (!down) borrowed[key] = false; return down ? 1 : CallNextHookEx(keyboardHook, code, message, data); }
    if (!active) { if (owned[key]) { if (!down) owned[key] = false; return 1; } return CallNextHookEx(keyboardHook, code, message, data); }
    bool control = owned[VK_LCONTROL] || owned[VK_RCONTROL] || borrowed[VK_CONTROL] || borrowed[VK_LCONTROL] || borrowed[VK_RCONTROL];
    bool alt = owned[VK_LMENU] || owned[VK_RMENU] || borrowed[VK_MENU] || borrowed[VK_LMENU] || borrowed[VK_RMENU];
    if (down && control && alt && (key == VK_F10 || key == VK_F12 || key == VK_F8)) {
        owned[key] = true;
        PostMessage(host, key == VK_F10 ? StopMessage : key == VK_F12 ? WM_CLOSE : WM_HOTKEY, key == VK_F8 ? 4 : 0, 0);
        return 1;
    }
    if (key == VK_ESCAPE) { if (down) { owned[key] = true; PostMessage(host, StopMessage, 0, 0); } else owned[key] = false; return 1; }
    if (down) owned[key] = true;
    else if (!owned[key]) return CallNextHookEx(keyboardHook, code, message, data);
    else owned[key] = false;
    // Post metadata only. TSF and target calls happen outside the hook.
    LPARAM flags = static_cast<LPARAM>((packet.scanCode << 16) | ((packet.flags & LLKHF_EXTENDED) ? 1U << 24 : 0) | (down ? 0 : (1U << 30 | 1U << 31)) | 1);
    PostMessage(host, KeyMessage, (static_cast<WPARAM>(generation) << 8) | key, flags);
    return 1;
}
void UpdateCandidates() {
    candidates.clear(); candidateSelection = 0;
    if (pinyin.empty()) { PaintStatus(); return; }
    for (const auto& entry : dictionary) if (entry.first.rfind(pinyin, 0) == 0) {
        for (const auto& word : entry.second) if (candidates.size() < 9) candidates.push_back(word);
    }
    PaintStatus();
}
void Key(unsigned key, LPARAM flags) {
    if (!Guard()) { status = L"Stopped: foreground/target identity changed."; Stop(); return; }
    bool down = !(static_cast<ULONG_PTR>(flags) & (1ULL << 31));
    keyState[key] = down ? 0x80 : 0;
    keyState[VK_SHIFT] = (keyState[VK_LSHIFT] | keyState[VK_RSHIFT]) & 0x80;
    keyState[VK_CONTROL] = (keyState[VK_LCONTROL] | keyState[VK_RCONTROL]) & 0x80;
    keyState[VK_MENU] = (keyState[VK_LMENU] | keyState[VK_RMENU]) & 0x80;
    if (!independent && keystrokes) {
        SetKeyboardState(keyState); // TSF reads this host thread's modifier state.
        BOOL eaten{};
        HRESULT hr = down ? keystrokes->TestKeyDown(key, flags, &eaten) : keystrokes->TestKeyUp(key, flags, &eaten);
        if (SUCCEEDED(hr) && eaten) hr = down ? keystrokes->KeyDown(key, flags, &eaten) : keystrokes->KeyUp(key, flags, &eaten);
        lastResult = hr;
        if (SUCCEEDED(hr) && eaten) ++eatenKeys;
        else { ++uneatenKeys; status = L"System TIP did not consume this event. Native route remains unverified."; }
        if (!Guard()) { status = L"Native TSF changed foreground: unsupported."; Stop(); return; }
        PaintStatus(); return;
    }
    if (key == VK_LSHIFT || key == VK_RSHIFT || key == VK_SHIFT) {
        if (down) shiftTap = true;
        else if (shiftTap) { english = !english; pinyin.clear(); candidates.clear(); status = english ? L"Independent probe: English" : L"Independent probe: pinyin (small research dictionary)"; PaintStatus(); }
        return;
    }
    if (!down) return;
    shiftTap = false;
    if (key == VK_SPACE && (keyState[VK_CONTROL] || keyState[VK_LWIN] || keyState[VK_RWIN])) {
        english = !english; pinyin.clear(); candidates.clear(); status = english ? L"Independent probe: English" : L"Independent probe: pinyin"; PaintStatus(); return;
    }
    if (keyState[VK_CONTROL] || keyState[VK_MENU] || keyState[VK_LWIN] || keyState[VK_RWIN]) { status = L"System shortcut blocked inside the research session."; PaintStatus(); return; }
    if (english) {
        if (key >= 'A' && key <= 'Z') Commit(std::wstring(1, static_cast<wchar_t>(key + (keyState[VK_SHIFT] ? 0 : 32))));
        else if (key >= '0' && key <= '9' || key == VK_SPACE) Commit(std::wstring(1, static_cast<wchar_t>(key)));
        return;
    }
    if (key >= 'A' && key <= 'Z' && pinyin.size() < 64) pinyin.push_back(static_cast<wchar_t>(key + 32));
    else if (key == VK_BACK && !pinyin.empty()) pinyin.pop_back();
    else if ((key == VK_SPACE || key == VK_RETURN || key >= '1' && key <= '9') && !candidates.empty()) {
        size_t index = key >= '1' && key <= '9' ? key - '1' : 0;
        if (index < candidates.size()) { auto text = candidates[index]; pinyin.clear(); candidates.clear(); Commit(text); }
    }
    UpdateCandidates();
}
void Bind() {
    if (active) Stop();
    POINT cursor{}; GetCursorPos(&cursor); target = WindowFromPoint(cursor);
    wchar_t name[128]{}; GetClassName(target, name, 128);
    // Bind the actual text HWND, not a Chromium top-level fallback.
    GetWindowThreadProcessId(target, &targetPid);
    HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, targetPid);
    FILETIME exit{}, kernel{}, user{};
    bool captured = process && GetProcessTimes(process, &targetStart, &exit, &kernel, &user);
    if (process) CloseHandle(process);
    status = captured ? L"Target bound. Keep A foreground, then Ctrl+Alt+F10 to start." : L"Cannot capture target identity.";
    if (!captured) target = nullptr;
    PaintStatus();
}
void Start() {
    if (active) { status = L"Ended. Activate B normally and verify Shift/Win+Space."; Stop(); return; }
    if (!Identity() || target == host || GetAncestor(target, GA_ROOT) == GetForegroundWindow()) { status = L"Bind B's text area while a separate A is foreground."; PaintStatus(); return; }
    protectedWindow = GetForegroundWindow();
    protectedThread = GetWindowThreadProcessId(protectedWindow, nullptr);
    protectedLayout = GetKeyboardLayout(protectedThread);
    ++generation;
    borrowed.fill(false); owned.fill(false); GetKeyboardState(originalKeyState); ZeroMemory(keyState, sizeof keyState);
    for (int key = 1; key < 256; ++key) borrowed[key] = (GetAsyncKeyState(key) & 0x8000) != 0;
    active = true;
    keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, Keyboard, GetModuleHandle(nullptr), 0);
    if (!keyboardHook || (!independent && !StartTsf())) { status = L"Native TSF/host unavailable. HRESULT recorded; no text committed."; Stop(); return; }
    status = independent ? L"Independent pinyin prototype active. Esc ends; Shift toggles English." : L"System-IME prototype active. Esc ends. Verify candidates and A's focus.";
    PaintStatus();
}
void Report() {
    wchar_t filename[MAX_PATH]{}; GetModuleFileName(nullptr, filename, MAX_PATH);
    std::wstring path(filename); path = path.substr(0, path.find_last_of(L"\\/") + 1) + L"probe-metadata.txt";
    std::ofstream file(path);
    file << "engine=" << (independent ? "independent-small-dictionary" : "system-tsf") << "\nhr=" << lastResult
         << "\nfocusLosses=" << focusLosses << "\ncommitsDispatched=" << commits << "\neaten=" << eatenKeys << "\nuneaten=" << uneatenKeys
         << "\ncompositionStarts=" << compositionStarts << "\ncompositionEnds=" << compositionEnds << "\nprotectedLayoutChanges=" << layoutChanges
         << "\nprofileSelected=" << profileSelected << "\nactive=" << active << "\n";
    status = L"Metadata report saved; contains no input or document text."; PaintStatus();
}
LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM first, LPARAM second) {
    if (message == WM_MOUSEACTIVATE) return MA_NOACTIVATE;
    if (message == WM_HOTKEY) { if (first == 1) Bind(); else if (first == 2) Start(); else if (first == 3) DestroyWindow(window); else if (first == 4) Report(); return 0; }
    if (message == KeyMessage) { if ((first >> 8) == generation) Key(static_cast<unsigned>(first & 255), second); return 0; }
    if (message == CommitMessage) {
        if (Guard() && first == generation && store && !composing) { auto text = store->text; store->Clear(); Commit(text); }
        return 0;
    }
    if (message == LockMessage) { if (active && store) store->GrantPending(); return 0; }
    if (message == StopMessage) { status = L"Stopped: focus changed or Esc pressed. No automatic retry."; Stop(); return 0; }
    if (message == WM_TIMER) {
        if (active && !Guard()) { status = L"Foreground or target changed."; Stop(); }
        if (!active && keyboardHook && (GetTickCount64() >= drainDeadline || std::none_of(owned.begin(), owned.end(), [](bool held) { return held; }))) {
            if (UnhookWindowsHookEx(keyboardHook)) keyboardHook = nullptr;
        }
        return 0;
    }
    if (message == WM_PAINT) {
        PAINTSTRUCT paint{}; HDC dc = BeginPaint(window, &paint); RECT rect{}; GetClientRect(window, &rect);
        FillRect(dc, &rect, reinterpret_cast<HBRUSH>(COLOR_WINDOW + 1)); SetBkMode(dc, TRANSPARENT); SetTextColor(dc, RGB(30, 35, 38));
        HFONT font = CreateFont(-16, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET, 0, 0, CLEARTYPE_QUALITY, 0, L"Segoe UI");
        auto previous = SelectObject(dc, font);
        rect.left = 12; rect.top = 10; rect.right -= 12;
        std::wstring display = (independent ? L"Independent pinyin research\n" : L"System TSF research\n") + status + L"\n" +
            (store ? store->text : pinyin) + L"\n";
        for (size_t i = 0; i < candidates.size(); ++i) display += (i == candidateSelection ? L"> " : L"") + std::to_wstring(i + 1) + L". " + candidates[i] + L"   ";
        display += L"\nCtrl+Alt+F8: export metadata. Prototype does not establish compatibility.";
        DrawText(dc, display.c_str(), -1, &rect, DT_LEFT | DT_WORDBREAK);
        SelectObject(dc, previous); DeleteObject(font); EndPaint(window, &paint); return 0;
    }
    if (message == WM_DESTROY) { Stop(); if (keyboardHook) UnhookWindowsHookEx(keyboardHook); keyboardHook = nullptr; PostQuitMessage(0); return 0; }
    return DefWindowProc(window, message, first, second);
}
int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR command, int) {
    independent = std::wstring(command).find(L"--independent") != std::wstring::npos;
    if (FAILED(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED))) return 1;
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    // Small, original dictionary for transport/candidate proof only, not a production language model.
    dictionary = { {L"ni", {L"你",L"呢"}}, {L"hao",{L"好",L"号"}}, {L"nihao",{L"你好"}}, {L"zhongwen",{L"中文"}},
        {L"shuru",{L"输入"}}, {L"ceshi",{L"测试"}}, {L"shurufa",{L"输入法"}}, {L"zhongguo",{L"中国"}}, {L"xiexie",{L"谢谢"}},
        {L"zaijian",{L"再见"}}, {L"gongzuo",{L"工作"}}, {L"xuexi",{L"学习"}}, {L"shijie",{L"世界"}}, {L"wenzi",{L"文字"}},
        {L"fuzhi",{L"复制"}}, {L"zhantie",{L"粘贴"}}, {L"chuangkou",{L"窗口"}}, {L"jiaodian",{L"焦点"}}, {L"baochi",{L"保持"}}, {L"chenggong",{L"成功"}} };
    WNDCLASS wc{}; wc.hInstance = instance; wc.lpfnWndProc = WindowProc; wc.lpszClassName = L"GhostSlacking.ImeResearch"; wc.hCursor = LoadCursor(nullptr, IDC_ARROW);
    if (!RegisterClass(&wc)) { CoUninitialize(); return 2; }
    RECT area{}; SystemParametersInfo(SPI_GETWORKAREA, 0, &area, 0);
    host = CreateWindowEx(WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST, wc.lpszClassName, L"Background IME Research", WS_POPUP | WS_BORDER,
        area.left + 20, area.bottom - 190, 760, 170, nullptr, nullptr, instance, nullptr);
    if (!host) { CoUninitialize(); return 3; }
    bool hotkeys = RegisterHotKey(host, 1, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_F9) && RegisterHotKey(host, 2, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_F10) &&
        RegisterHotKey(host, 3, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_F12) && RegisterHotKey(host, 4, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_F8);
    if (!hotkeys) { DestroyWindow(host); CoUninitialize(); return 4; }
    foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, nullptr, ForegroundEvent, 0, 0, WINEVENT_OUTOFCONTEXT);
    if (!foregroundHook) { DestroyWindow(host); CoUninitialize(); return 5; }
    SetTimer(host, 1, 20, nullptr); ShowWindow(host, SW_SHOWNOACTIVATE);
    MSG message{};
    while (GetMessage(&message, nullptr, 0, 0) > 0) { TranslateMessage(&message); DispatchMessage(&message); }
    UnhookWinEvent(foregroundHook);
    for (int id = 1; id <= 4; ++id) UnregisterHotKey(host, id);
    CoUninitialize(); return 0;
}
