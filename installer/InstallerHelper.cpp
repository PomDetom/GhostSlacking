#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#define _WIN32_WINNT 0x0A00
#include <windows.h>
#include <tlhelp32.h>
#include <msi.h>
#include <bcrypt.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <filesystem>
#include <string>
#include <vector>
#include <stdexcept>
#include <cstdio>
#include <chrono>
#include <regex>
#include <fstream>

namespace fs = std::filesystem;
static constexpr wchar_t UpgradeCode[] = L"{AB92C57B-7434-401E-91B7-4878B250E0C7}";
#ifdef INSTALLER_TESTING
static constexpr wchar_t ProductKey[] = L"Software\\GhostSlacking.InstallerTests";
static constexpr wchar_t UninstallKey[] = L"Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\GhostSlacking.InstallerTests";
static constexpr wchar_t LinkName[] = L"GhostSlacking.InstallerTests.lnk";
#else
static constexpr wchar_t ProductKey[] = L"Software\\GhostSlacking";
static constexpr wchar_t UninstallKey[] = L"Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\GhostSlacking";
static constexpr wchar_t LinkName[] = L"GhostSlacking.lnk";
#endif

struct Handle {
    HANDLE value = INVALID_HANDLE_VALUE;
    explicit Handle(HANDLE h) : value(h) {}
    ~Handle() { if (value && value != INVALID_HANDLE_VALUE) CloseHandle(value); }
    Handle(const Handle&) = delete;
    Handle& operator=(const Handle&) = delete;
};

static void require(bool ok, const char* message) { if (!ok) throw std::runtime_error(message); }
static bool path_equals(const std::wstring& a, const std::wstring& b) {
    return CompareStringOrdinal(a.c_str(), -1, b.c_str(), -1, TRUE) == CSTR_EQUAL;
}
static std::wstring installation_path(const fs::path& path) {
    auto value = fs::absolute(path).lexically_normal().wstring();
    while (value.size() > 3 && value.back() == L'\\') value.pop_back();
    require(value.size() < MAX_PATH && value.size() > 3 && value[1] == L':' && value[2] == L'\\', "A local installation path shorter than MAX_PATH is required.");
    for (auto parent = fs::path(value); !parent.empty(); parent = parent.parent_path()) {
        const auto attributes = GetFileAttributesW(parent.c_str());
        require(attributes == INVALID_FILE_ATTRIBUTES || !(attributes & FILE_ATTRIBUTE_REPARSE_POINT), "Reparse points are not allowed in installation paths.");
        if (parent == parent.parent_path()) break;
    }
    return value;
}
static std::wstring hash_path(const fs::path& path) {
    auto input = installation_path(path);
    std::wstring upper(input.size(), L'\0');
    require(LCMapStringEx(LOCALE_NAME_INVARIANT, LCMAP_UPPERCASE, input.data(), static_cast<int>(input.size()),
        upper.data(), static_cast<int>(upper.size()), nullptr, nullptr, 0) != 0, "Could not normalize installation path.");
    BCRYPT_ALG_HANDLE algorithm = nullptr;
    require(BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) == 0, "SHA256 is unavailable.");
    unsigned char digest[32]{};
    const auto status = BCryptHash(algorithm, nullptr, 0, reinterpret_cast<PUCHAR>(upper.data()),
        static_cast<ULONG>(upper.size() * sizeof(wchar_t)), digest, sizeof(digest));
    BCryptCloseAlgorithmProvider(algorithm, 0);
    require(status == 0, "Path hashing failed.");
    constexpr wchar_t hex[] = L"0123456789ABCDEF";
    std::wstring result;
    for (auto byte : digest) { result += hex[byte >> 4]; result += hex[byte & 15]; }
    return result;
}
static std::wstring read_reg(const wchar_t* key, const wchar_t* name, HKEY hive = HKEY_CURRENT_USER) {
    wchar_t buffer[32768]{};
    DWORD bytes = sizeof(buffer);
    const auto status = RegGetValueW(hive, key, name, RRF_RT_REG_SZ | RRF_SUBKEY_WOW6464KEY, nullptr, buffer, &bytes);
    if (status == ERROR_FILE_NOT_FOUND || status == ERROR_PATH_NOT_FOUND) return L"";
    require(status == ERROR_SUCCESS, "Could not read installation registration.");
    return buffer;
}
static void write_reg(const wchar_t* key, const wchar_t* name, const std::wstring& value) {
    HKEY opened = nullptr;
    require(RegCreateKeyExW(HKEY_CURRENT_USER, key, 0, nullptr, 0, KEY_WRITE | KEY_WOW64_64KEY, nullptr, &opened, nullptr) == ERROR_SUCCESS,
        "Could not create installation registration.");
    const auto status = RegSetValueExW(opened, name, 0, REG_SZ, reinterpret_cast<const BYTE*>(value.c_str()),
        static_cast<DWORD>((value.size() + 1) * sizeof(wchar_t)));
    RegCloseKey(opened);
    require(status == ERROR_SUCCESS, "Could not write installation registration.");
}
static void write_dword(const wchar_t* key, const wchar_t* name, DWORD value) {
    HKEY opened = nullptr;
    require(RegOpenKeyExW(HKEY_CURRENT_USER, key, 0, KEY_WRITE | KEY_WOW64_64KEY, &opened) == ERROR_SUCCESS, "Registration is missing.");
    const auto status = RegSetValueExW(opened, name, 0, REG_DWORD, reinterpret_cast<const BYTE*>(&value), sizeof(value));
    RegCloseKey(opened);
    require(status == ERROR_SUCCESS, "Could not write registration protocol.");
}
static DWORD read_dword(const wchar_t* key, const wchar_t* name, DWORD fallback) {
    DWORD value = fallback, bytes = sizeof(value);
    RegGetValueW(HKEY_CURRENT_USER, key, name, RRF_RT_REG_DWORD | RRF_SUBKEY_WOW6464KEY, nullptr, &value, &bytes);
    return value;
}
static void write_journal(const fs::path& path, const wchar_t* name, const std::wstring& value) {
    require(WritePrivateProfileStringW(L"Transaction", name, value.c_str(), path.c_str()) != FALSE, "Could not persist installation transaction.");
    WritePrivateProfileStringW(nullptr, nullptr, nullptr, path.c_str());
    Handle file(CreateFileW(path.c_str(), GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr,
        OPEN_EXISTING, FILE_FLAG_WRITE_THROUGH, nullptr));
    require(file.value != INVALID_HANDLE_VALUE && FlushFileBuffers(file.value) != FALSE, "Could not flush installation transaction.");
}
static void journal(const fs::path& root, const wchar_t* name, const std::wstring& value) {
    write_journal(root / L"transaction.ini", name, value);
}
static std::wstring journal_read(const fs::path& root, const wchar_t* name) {
    wchar_t buffer[4096]{};
    GetPrivateProfileStringW(L"Transaction", name, L"", buffer, 4096, (root / L"transaction.ini").c_str());
    return buffer;
}
static bool owned_root(const fs::path& root) {
    wchar_t product[64]{}, protocol[16]{};
    const auto marker = root / L"installation.ini";
    GetPrivateProfileStringW(L"Transaction", L"Product", L"", product, 64, marker.c_str());
    GetPrivateProfileStringW(L"Transaction", L"Protocol", L"", protocol, 16, marker.c_str());
    return std::wstring(product) == L"GhostSlacking" && std::wstring(protocol) == L"2";
}
static void remove_program_tree(const fs::path& path) {
    installation_path(path);
    if (!fs::exists(path)) return;
    for (const auto& entry : fs::recursive_directory_iterator(path))
        require(!(GetFileAttributesW(entry.path().c_str()) & FILE_ATTRIBUTE_REPARSE_POINT), "Refusing to remove a program tree containing reparse points.");
    fs::remove_all(path);
}
static std::vector<int> release_version(const std::wstring& text) {
    static const std::wregex pattern(L"^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(?:-(alpha|beta|rc)\\.(0|[1-9][0-9]*))?$");
    std::wsmatch parts;
    require(std::regex_match(text, parts, pattern), "Invalid full release version.");
    int channel = !parts[4].matched ? 3 : parts[4] == L"alpha" ? 0 : parts[4] == L"beta" ? 1 : 2;
    return { std::stoi(parts[1]), std::stoi(parts[2]), std::stoi(parts[3]), channel, parts[5].matched ? std::stoi(parts[5]) : 0 };
}
static void move(const fs::path& from, const fs::path& to) {
    require(MoveFileExW(from.c_str(), to.c_str(), MOVEFILE_WRITE_THROUGH) != FALSE, "Program directory switch failed; files may still be in use.");
}
static std::wstring process_path(HANDLE process) {
    wchar_t buffer[32768]{};
    DWORD count = 32768;
    require(QueryFullProcessImageNameW(process, 0, buffer, &count) != FALSE, "Could not verify running process path.");
    return std::wstring(buffer, count);
}
static std::vector<DWORD> processes_in(const fs::path& directory) {
    std::vector<DWORD> result;
    Handle snapshot(CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0));
    require(snapshot.value != INVALID_HANDLE_VALUE, "Could not inspect running processes.");
    PROCESSENTRY32W entry{};
    entry.dwSize = sizeof(entry);
    require(Process32FirstW(snapshot.value, &entry) != FALSE, "Could not enumerate running processes.");
    do {
        if (_wcsicmp(entry.szExeFile, L"GhostSlacking.App.exe") != 0 && _wcsicmp(entry.szExeFile, L"GhostSlacking.Watchdog.exe") != 0) continue;
        Handle process(OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE, FALSE, entry.th32ProcessID));
        if (!process.value) {
            if (GetLastError() == ERROR_INVALID_PARAMETER) continue;
            throw std::runtime_error("A GhostSlacking process cannot be inspected. Exit elevated copies manually.");
        }
        auto image = process_path(process.value);
        if (path_equals(fs::path(image).parent_path().wstring(), directory.wstring())) result.push_back(entry.th32ProcessID);
    } while (Process32NextW(snapshot.value, &entry));
    return result;
}
static bool pipe_io(HANDLE pipe, bool write, char* bytes, DWORD count, DWORD& transferred, ULONGLONG deadline) {
    Handle event(CreateEventW(nullptr, TRUE, FALSE, nullptr));
    OVERLAPPED overlapped{};
    overlapped.hEvent = event.value;
    BOOL done = write ? WriteFile(pipe, bytes, count, &transferred, &overlapped) : ReadFile(pipe, bytes, count, &transferred, &overlapped);
    if (!done && GetLastError() == ERROR_IO_PENDING) {
        auto now = GetTickCount64();
        auto wait = WaitForSingleObject(event.value, now >= deadline ? 0 : static_cast<DWORD>(deadline - now));
        if (wait != WAIT_OBJECT_0) { CancelIoEx(pipe, &overlapped); GetOverlappedResult(pipe, &overlapped, &transferred, TRUE); return false; }
        done = GetOverlappedResult(pipe, &overlapped, &transferred, FALSE);
    }
    return done != FALSE;
}
static std::string pipe_line(HANDLE pipe, ULONGLONG deadline) {
    std::string value;
    for (int i = 0; i < 512; ++i) {
        char byte = 0;
        DWORD read = 0;
        require(pipe_io(pipe, false, &byte, 1, read, deadline) && read == 1, "Safe shutdown handshake timed out or disconnected.");
        if (byte == '\n') return value;
        if (byte != '\r') value += byte;
    }
    throw std::runtime_error("Invalid safe shutdown response.");
}
static void close_safely(const fs::path& directory) {
    const auto app = fs::path(installation_path(directory));
    if (processes_in(app).empty()) return;
    const auto deadline = GetTickCount64() + 30000;
    const auto pipeName = L"\\\\.\\pipe\\GhostSlacking.Install." + hash_path(app);
    Handle pipe(CreateFileW(pipeName.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, nullptr));
    require(pipe.value != INVALID_HANDLE_VALUE, "No safe shutdown endpoint is available. Exit the application and watchdog manually.");
    ULONG serverPid = 0;
    require(GetNamedPipeServerProcessId(pipe.value, &serverPid) != FALSE, "Could not verify the shutdown server.");
    Handle process(OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE, FALSE, serverPid));
    require(process.value && path_equals(process_path(process.value), (app / L"GhostSlacking.App.exe").wstring()), "The shutdown server is not the registered application.");
    unsigned long reportedPid = 0;
    unsigned long long reportedTicks = 0;
    auto identity = pipe_line(pipe.value, deadline);
    require(sscanf_s(identity.c_str(), "IDENTITY %lu %llu", &reportedPid, &reportedTicks) == 2 && reportedPid == serverPid, "Invalid application identity.");
    FILETIME created{}, exited{}, kernel{}, user{};
    require(GetProcessTimes(process.value, &created, &exited, &kernel, &user) != FALSE, "Could not verify process creation time.");
    ULARGE_INTEGER ticks{};
    ticks.LowPart = created.dwLowDateTime; ticks.HighPart = created.dwHighDateTime;
    require(ticks.QuadPart + 504911232000000000ULL == reportedTicks, "The process identity has changed.");
    char request[] = "CLOSE\n";
    DWORD written = 0;
    require(pipe_io(pipe.value, true, request, 6, written, deadline) && written == 6, "Could not request safe shutdown.");
    require(pipe_line(pipe.value, deadline) == "SAFE", "Window restoration or watchdog shutdown failed. Program files have not been changed.");
    auto now = GetTickCount64();
    require(WaitForSingleObject(process.value, now >= deadline ? 0 : static_cast<DWORD>(deadline - now)) == WAIT_OBJECT_0,
        "The application did not exit within the shutdown deadline.");
    require(processes_in(app).empty(), "Application or watchdog processes are still using this installation.");
}
static fs::path link_path(REFKNOWNFOLDERID folder) {
    PWSTR location = nullptr;
    require(SHGetKnownFolderPath(folder, 0, nullptr, &location) == S_OK, "Could not locate current-user shortcuts.");
    auto result = fs::path(location) / LinkName;
    CoTaskMemFree(location);
    return result;
}
static void shortcut(const fs::path& path, const fs::path& target, bool enabled) {
    if (!enabled) { fs::remove(path); return; }
    fs::create_directories(path.parent_path());
    IShellLinkW* link = nullptr;
    require(SUCCEEDED(CoCreateInstance(CLSID_ShellLink, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&link))), "Could not create shortcut.");
    link->SetPath(target.c_str()); link->SetWorkingDirectory(target.parent_path().c_str()); link->SetIconLocation(target.c_str(), 0);
    IPersistFile* file = nullptr;
    auto status = link->QueryInterface(IID_PPV_ARGS(&file));
    if (SUCCEEDED(status)) { status = file->Save(path.c_str(), TRUE); file->Release(); }
    link->Release();
    require(SUCCEEDED(status), "Could not save shortcut.");
}
static void registration(const fs::path& root, const std::wstring& version, bool menu, bool desktop) {
    auto target = root / L"app" / L"GhostSlacking.App.exe";
    shortcut(link_path(FOLDERID_Programs), target, menu);
    shortcut(link_path(FOLDERID_Desktop), target, desktop);
    write_reg(ProductKey, L"InstallRoot", root.wstring());
    write_reg(ProductKey, L"InstallLocation", (root / L"app").wstring());
    write_reg(ProductKey, L"InstallerKind", L"nsis");
    write_reg(ProductKey, L"InstallerVersion", version);
    write_dword(ProductKey, L"UpgradeProtocolVersion", 2);
    write_dword(ProductKey, L"StartMenuShortcut", menu ? 1 : 0);
    write_dword(ProductKey, L"DesktopShortcut", desktop ? 1 : 0);
    write_reg(UninstallKey, L"DisplayName", L"GhostSlacking");
    write_reg(UninstallKey, L"DisplayVersion", version);
    write_reg(UninstallKey, L"Publisher", L"GhostSlacking");
    write_reg(UninstallKey, L"InstallLocation", root.wstring());
    write_reg(UninstallKey, L"DisplayIcon", target.wstring());
    write_reg(UninstallKey, L"UninstallString", L"\"" + (root / L"uninstall.exe").wstring() + L"\"");
    write_reg(UninstallKey, L"QuietUninstallString", L"\"" + (root / L"uninstall.exe").wstring() + L"\" /S");
    write_dword(UninstallKey, L"NoModify", 1); write_dword(UninstallKey, L"NoRepair", 1);
}
static void remove_registration(const fs::path& root) {
    shortcut(link_path(FOLDERID_Programs), root / L"app" / L"GhostSlacking.App.exe", false);
    shortcut(link_path(FOLDERID_Desktop), root / L"app" / L"GhostSlacking.App.exe", false);
    if (path_equals(read_reg(ProductKey, L"InstallRoot"), root.wstring())) RegDeleteTreeW(HKEY_CURRENT_USER, ProductKey);
    if (path_equals(read_reg(UninstallKey, L"InstallLocation"), root.wstring())) RegDeleteTreeW(HKEY_CURRENT_USER, UninstallKey);
}
static void recover(const fs::path& root) {
    if (!fs::exists(root / L"transaction.ini")) return;
    require(owned_root(root), "Refusing to recover a transaction in an unrecognized directory.");
    if (journal_read(root, L"Phase") == L"committed") return;
    auto hadApp = journal_read(root, L"HadApp") == L"1";
    auto phase = journal_read(root, L"Phase");
    if (phase == L"switch" || phase == L"registration") {
        if (fs::exists(root / L"previous")) {
            remove_program_tree(root / L"app"); move(root / L"previous", root / L"app");
        } else if (!hadApp) remove_program_tree(root / L"app");
        if (fs::exists(root / L"uninstall-previous.exe")) {
            fs::remove(root / L"uninstall.exe"); move(root / L"uninstall-previous.exe", root / L"uninstall.exe");
        } else if (!hadApp) fs::remove(root / L"uninstall.exe");
    }
    if (hadApp) registration(root, journal_read(root, L"Version"), journal_read(root, L"Menu") == L"1", journal_read(root, L"Desktop") == L"1");
    else remove_registration(root);
    fs::remove(root / L"transaction.ini");
}
static void install(const fs::path& root, const std::wstring& version, bool menu, bool desktop) {
    require(owned_root(root), "The staging directory is not owned by this installer.");
    close_safely(root / L"app");
    recover(root);
    const auto registered = read_reg(ProductKey, L"InstallRoot");
    require(registered.empty() || path_equals(registered, root.wstring()), "An installation exists in a different location.");
    const bool hadApp = fs::exists(root / L"app");
    require(!hadApp || !registered.empty(), "Refusing to replace an unregistered program directory.");
    const auto candidate = release_version(version);
    require(registered.empty() || candidate >= release_version(read_reg(ProductKey, L"InstallerVersion")), "A newer release is already installed; downgrades are blocked.");
    require(fs::exists(root / L".staging" / L"GhostSlacking.App.exe") && fs::exists(root / L"uninstall-new.exe"), "The extracted application payload is incomplete.");
    const auto pendingJournal = root / L"transaction-new.ini";
    fs::remove(pendingJournal);
    write_journal(pendingJournal, L"HadApp", hadApp ? L"1" : L"0");
    write_journal(pendingJournal, L"Version", read_reg(ProductKey, L"InstallerVersion"));
    write_journal(pendingJournal, L"Menu", read_dword(ProductKey, L"StartMenuShortcut", 1) ? L"1" : L"0");
    write_journal(pendingJournal, L"Desktop", read_dword(ProductKey, L"DesktopShortcut", 0) ? L"1" : L"0");
    write_journal(pendingJournal, L"Phase", L"prepared");
    require(MoveFileExW(pendingJournal.c_str(), (root / L"transaction.ini").c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH) != FALSE, "Could not initialize the installation transaction.");
    try {
        remove_program_tree(root / L"previous"); fs::remove(root / L"uninstall-previous.exe");
        journal(root, L"Phase", L"switch");
        if (hadApp) move(root / L"app", root / L"previous");
        if (fs::exists(root / L"uninstall.exe")) move(root / L"uninstall.exe", root / L"uninstall-previous.exe");
        move(root / L".staging", root / L"app");
        move(root / L"uninstall-new.exe", root / L"uninstall.exe");
#ifdef INSTALLER_TESTING
        if (GetEnvironmentVariableW(L"GHOSTSLACKING_TEST_FAIL_SWITCH", nullptr, 0) != 0) throw std::runtime_error("Injected switch failure.");
        if (GetEnvironmentVariableW(L"GHOSTSLACKING_TEST_CRASH_SWITCH", nullptr, 0) != 0) ExitProcess(99);
#endif
        journal(root, L"Phase", L"registration");
        registration(root, version, menu, desktop);
        journal(root, L"Phase", L"committed");
    } catch (...) { recover(root); throw; }
}
static bool runtime_available() {
    auto location = read_reg(L"SOFTWARE\\dotnet\\Setup\\InstalledVersions\\x64", L"InstallLocation", HKEY_LOCAL_MACHINE);
    if (location.empty()) {
        wchar_t programFiles[32768]{};
        GetEnvironmentVariableW(L"ProgramW6432", programFiles, 32768);
        location = (fs::path(programFiles) / L"dotnet").wstring();
    }
    auto shared = fs::path(location) / L"shared" / L"Microsoft.NETCore.App";
    if (!fs::is_directory(shared)) return false;
    for (const auto& item : fs::directory_iterator(shared)) {
        auto version = item.path().filename().wstring();
        if (version.rfind(L"8.0.", 0) != 0 || version.size() <= 4 || version.find_first_not_of(L"0123456789", 4) != std::wstring::npos) continue;
        if (fs::exists(item.path() / L"coreclr.dll") && fs::exists(item.path() / L"hostpolicy.dll")) return true;
    }
    return false;
}
[[maybe_unused]] static bool elevated() {
    Handle token(nullptr);
    require(OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token.value) != FALSE, "Could not inspect installer privileges.");
    TOKEN_ELEVATION state{}; DWORD size = sizeof(state);
    require(GetTokenInformation(token.value, TokenElevation, &state, sizeof(state), &size) != FALSE, "Could not inspect installer privileges.");
    return state.TokenIsElevated != 0;
}
struct OperationLog {
    ULONGLONG started = GetTickCount64();
    std::wstring operation;
    bool success = false;
    explicit OperationLog(const std::wstring& command) : operation(command) { append("started", 0); }
    void append(const char* state, ULONGLONG elapsed) noexcept {
        try {
            PWSTR local = nullptr;
            if (SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &local) != S_OK) return;
            const auto directory = fs::path(local) / L"GhostSlacking" / L"logs";
            CoTaskMemFree(local);
            fs::create_directories(directory);
            std::ofstream output(directory / L"installer.log", std::ios::app);
            SYSTEMTIME time{}; GetSystemTime(&time);
            char stamp[80]{};
            sprintf_s(stamp, "%04u-%02u-%02uT%02u:%02u:%02uZ pid=%lu ", time.wYear, time.wMonth, time.wDay,
                time.wHour, time.wMinute, time.wSecond, GetCurrentProcessId());
            std::string label;
            for (auto character : operation) label += character <= 127 ? static_cast<char>(character) : '?';
            output << stamp << label << " " << state << " elapsedMs=" << elapsed << "\n";
        } catch (...) { }
    }
    ~OperationLog() { append(success ? "completed" : "failed", GetTickCount64() - started); }
};
int wmain(int argc, wchar_t** argv) {
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    auto started = GetTickCount64();
    OperationLog log(argc >= 2 ? argv[1] : L"unknown");
    try {
        require(argc >= 2, "Missing installer operation.");
        const std::wstring command = argv[1];
        if (command == L"legacy") {
            wchar_t product[39]{};
            auto result = MsiEnumRelatedProductsW(UpgradeCode, 0, 0, product);
            require(result == ERROR_SUCCESS || result == ERROR_NO_MORE_ITEMS, "Could not check legacy MSI identity.");
            log.success = result == ERROR_NO_MORE_ITEMS;
            return log.success ? 0 : 10;
        }
        if (command == L"runtime") { log.success = runtime_available(); return log.success ? 0 : 11; }
        require(argc >= 3, "Missing installation directory.");
        const fs::path root(installation_path(argv[2]));
        if (command == L"id") { wprintf(L"%s", hash_path(root).c_str()); log.success = true; return 0; }
        if (command == L"close") { close_safely(root); log.success = true; return 0; }
#ifndef INSTALLER_TESTING
        require(!elevated(), "Run the installer as a standard user, not as administrator.");
#endif
        auto mutexName = L"Local\\GhostSlacking.Installation." + hash_path(root);
        Handle mutex(CreateMutexW(nullptr, FALSE, mutexName.c_str()));
        require(mutex.value != nullptr, "Could not create installation lock.");
        auto wait = WaitForSingleObject(mutex.value, 0);
        require(wait == WAIT_OBJECT_0 || wait == WAIT_ABANDONED, "Another operation is modifying this installation.");
        if (command == L"recover") { close_safely(root / L"app"); recover(root); }
        else if (command == L"stage") {
            const auto registered = read_reg(ProductKey, L"InstallRoot");
            require(registered.empty() || path_equals(registered, root.wstring()), "An installation exists in a different location.");
            require(owned_root(root) || !fs::exists(root) || fs::is_empty(root), "Choose an empty directory; this folder is not a GhostSlacking installation.");
            fs::create_directories(root);
            write_journal(root / L"installation.ini", L"Product", L"GhostSlacking");
            write_journal(root / L"installation.ini", L"Protocol", L"2");
            installation_path(root / L".staging");
            remove_program_tree(root / L".staging");
            fs::create_directories(root / L".staging");
        }
        else if (command == L"validate") {
            require(owned_root(root) || !fs::exists(root) || fs::is_empty(root), "Choose an empty directory; this folder is not a GhostSlacking installation.");
            const auto created = !fs::exists(root);
            fs::create_directories(root);
            try {
                auto probe = root / (L".write-probe-" + std::to_wstring(GetCurrentProcessId()) + L"-" + std::to_wstring(GetTickCount64()));
                Handle file(CreateFileW(probe.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW,
                    FILE_ATTRIBUTE_TEMPORARY | FILE_FLAG_DELETE_ON_CLOSE, nullptr));
                require(file.value != INVALID_HANDLE_VALUE, "Choose a directory writable by the current user.");
            } catch (...) { if (created) fs::remove(root); throw; }
            if (created) fs::remove(root);
        }
        else if (command == L"install") {
            require(argc == 6, "Missing install version or shortcut choices.");
            install(root, argv[3], std::wstring(argv[4]) == L"1", std::wstring(argv[5]) == L"1");
        } else if (command == L"confirm") {
            require(path_equals(read_reg(ProductKey, L"InstallRoot"), root.wstring()) && journal_read(root, L"Phase") == L"committed", "No committed installation to confirm.");
            remove_program_tree(root / L"previous"); fs::remove(root / L"uninstall-previous.exe"); fs::remove(root / L"transaction.ini");
        } else if (command == L"uninstall") {
            require(path_equals(read_reg(ProductKey, L"InstallRoot"), root.wstring()), "The uninstall directory is not registered.");
            close_safely(root / L"app"); recover(root);
            remove_program_tree(root / L"app"); remove_program_tree(root / L"previous"); remove_program_tree(root / L".staging");
            remove_registration(root);
            constexpr wchar_t runKey[] = L"Software\\Microsoft\\Windows\\CurrentVersion\\Run";
            if (path_equals(read_reg(runKey, L"GhostSlacking"), L"\"" + (root / L"app" / L"GhostSlacking.App.exe").wstring() + L"\"")) {
                HKEY run = nullptr;
                if (RegOpenKeyExW(HKEY_CURRENT_USER, runKey, 0, KEY_SET_VALUE, &run) == ERROR_SUCCESS) {
                    RegDeleteValueW(run, L"GhostSlacking"); RegCloseKey(run);
                }
            }
            fs::remove(root / L"transaction.ini"); fs::remove(root / L"uninstall-previous.exe"); fs::remove(root / L"uninstall-new.exe");
            fs::remove(root / L"installation.ini");
        } else throw std::runtime_error("Unknown installer operation.");
        ReleaseMutex(mutex.value);
        printf("InstallationPhase completed: %llu ms\n", GetTickCount64() - started);
        log.success = true;
        return 0;
    } catch (const std::exception& error) {
        log.append(error.what(), GetTickCount64() - started);
        fprintf(stderr, "InstallationPhase failed after %llu ms: %s\n", GetTickCount64() - started, error.what());
        return 20;
    }
}
