#include "CompositionAbi.h"
#include <xamlom.h>
#include <ocidl.h>
#include <algorithm>
#include <unordered_set>
#include <map>
#include <mutex>

#define GLASS_BUILD L"DispCtrl.TaskbarGlass"

namespace glass {
constexpr wchar_t WindowClass[] = L"DispCtrl.TaskbarGlass.1";
constexpr UINT Configure = WM_APP + 0x351, Retire = WM_APP + 0x352, OwnerExited = WM_APP + 0x353;
constexpr GUID SiteClass{0x2f7ab148,0xbe3a,0x4f53,{0x8e,0x54,0x87,0xfa,0xa9,0x63,0x6c,0x20}};
HMODULE module;
std::atomic<bool> retired{false};

void log(const char* operation, HRESULT hr) noexcept {
    wchar_t path[MAX_PATH];
    if (!GetEnvironmentVariableW(L"LOCALAPPDATA", path, MAX_PATH)) return;
    wcscat_s(path, L"\\DispCtrl\\taskbar-glass-native.log");
    HANDLE file = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return;
    char line[300];
    int count = snprintf(line, sizeof(line), "pid=%lu thread=%lu %s: 0x%08lx\r\n", GetCurrentProcessId(), GetCurrentThreadId(), operation, static_cast<unsigned long>(hr));
    DWORD written; if (count > 0) WriteFile(file, line, std::min<size_t>(count, sizeof(line) - 1), &written, nullptr);
    CloseHandle(file);
}

struct Background {
    InstanceHandle handle = 0;
    Ptr<Shape> shape;
    Ptr<IInspectable> original, applied;
};

struct ThreadState {
    Ptr<IXamlDiagnostics> diagnostics;
    Ptr<IVisualTreeService2> tree;
    std::unordered_set<InstanceHandle> descendants;
    std::map<InstanceHandle, Background> backgrounds;
    HWND window = nullptr;
    DWORD owner = 0;
    HANDLE process = nullptr, wait = nullptr;
    unsigned config = 0;
    HRESULT error = S_OK;
    bool active = false;

    void restore() noexcept {
        for (auto& [_, bg] : backgrounds) {
            if (!bg.applied) continue;
            HRESULT hr = bg.shape->put_Fill(bg.original.get());
            if (FAILED(hr)) log("Restore Fill", hr);
            else bg.applied = {};
        }
        active = false;
    }
    void releaseOwner() noexcept {
        if (wait) { UnregisterWaitEx(wait, INVALID_HANDLE_VALUE); wait = nullptr; }
        if (process) { CloseHandle(process); process = nullptr; }
        owner = 0;
    }
    static void CALLBACK onExit(void* context, BOOLEAN) {
        auto* state = static_cast<ThreadState*>(context);
        PostMessageW(state->window, OwnerExited, state->owner, 0);
    }
    void setOwner(DWORD pid) {
        if (owner == pid && process) return;
        if (process && WaitForSingleObject(process, 0) == WAIT_TIMEOUT)
            throw Failure{HRESULT_FROM_WIN32(ERROR_BUSY), "Another engine owns taskbar glass"};
        releaseOwner();
        process = OpenProcess(SYNCHRONIZE, FALSE, pid);
        if (!process) throw Failure{HRESULT_FROM_WIN32(GetLastError()), "Open owner"};
        owner = pid;
        if (!RegisterWaitForSingleObject(&wait, process, onExit, this, INFINITE, WT_EXECUTEONLYONCE)) {
            HRESULT hr = HRESULT_FROM_WIN32(GetLastError()); releaseOwner(); throw Failure{hr, "Watch owner"};
        }
    }
    void apply(Background& bg) {
        Ptr<IInspectable> element;
        check(diagnostics->GetIInspectableFromHandle(bg.handle, element.put()), "Get background");
        auto brush = makeBrush(element.get(), config & 0xff, (config >> 8) & 0xff, (config & 0x10000) != 0);
        check(bg.shape->put_Fill(brush.get()), "Set Fill");
        bg.applied = std::move(brush);
    }
    LRESULT configure(DWORD pid, unsigned value) noexcept {
        try {
            if (!(value & 0x1000000)) { restore(); releaseOwner(); return static_cast<LRESULT>(backgrounds.size()); }
            if ((value & 0xff) > 120 || ((value >> 8) & 0xff) > 100) return E_INVALIDARG;
            setOwner(pid);
            if (active && config == value && SUCCEEDED(error)) return static_cast<LRESULT>(backgrounds.size());
            config = value;
            for (auto& [_, bg] : backgrounds) apply(bg);
            active = true; error = S_OK;
            return static_cast<LRESULT>(backgrounds.size());
        } catch (const Failure& e) { error = e.hr; log(e.operation, e.hr); }
          catch (...) { error = E_FAIL; log("Configure exception", error); }
        restore(); return static_cast<LRESULT>(error);
    }
};
thread_local ThreadState* local = nullptr;

LRESULT CALLBACK windowProc(HWND hwnd, UINT msg, WPARAM w, LPARAM l) noexcept {
    auto* state = reinterpret_cast<ThreadState*>(GetWindowLongPtrW(hwnd, GWLP_USERDATA));
    if (msg == WM_NCCREATE) {
        state = static_cast<ThreadState*>(reinterpret_cast<CREATESTRUCTW*>(l)->lpCreateParams);
        SetWindowLongPtrW(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(state));
    }
    if (state) {
        if (msg == Configure) return retired ? E_ABORT : state->configure(static_cast<DWORD>(w), static_cast<unsigned>(l));
        if (msg == OwnerExited && state->owner == w) { state->restore(); state->releaseOwner(); return 0; }
        if (msg == Retire) { retired = true; state->restore(); state->releaseOwner(); DestroyWindow(hwnd); return 1; }
    }
    return DefWindowProcW(hwnd, msg, w, l);
}

class Site final : public IObjectWithSite, public IVisualTreeServiceCallback2 {
    std::atomic<ULONG> refs{1};
    Ptr<IXamlDiagnostics> diagnostics;
    Ptr<IVisualTreeService2> tree;
public:
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        if (iid == IID_IUnknown || iid == IID_IObjectWithSite) *value = static_cast<IObjectWithSite*>(this);
        else if (iid == __uuidof(IVisualTreeServiceCallback) || iid == __uuidof(IVisualTreeServiceCallback2)) *value = static_cast<IVisualTreeServiceCallback2*>(this);
        else return E_NOINTERFACE;
        AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++refs; }
    ULONG STDMETHODCALLTYPE Release() override { ULONG n = --refs; if (!n) delete this; return n; }
    HRESULT STDMETHODCALLTYPE SetSite(IUnknown* site) override {
        if (!site) return S_OK;
        try {
            diagnostics = query<IXamlDiagnostics>(site);
            tree = query<IVisualTreeService2>(site);
            // Callbacks and window procedures can outlive the diagnostics host.
            // Pin only this tiny native module; no managed runtime enters Explorer.
            HMODULE pin;
            GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
                reinterpret_cast<LPCWSTR>(&windowProc), &pin);
            AddRef();
            HANDLE worker = CreateThread(nullptr, 0, [](void* context) -> DWORD {
                auto* self = static_cast<Site*>(context);
                HRESULT hr = self->tree->AdviseVisualTreeChange(self);
                log("AdviseVisualTreeChange", hr);
                self->Release(); return 0;
            }, this, 0, nullptr);
            if (!worker) { Release(); return HRESULT_FROM_WIN32(GetLastError()); }
            CloseHandle(worker); return S_OK;
        } catch (const Failure& e) { log(e.operation, e.hr); return e.hr; }
          catch (...) { return E_FAIL; }
    }
    HRESULT STDMETHODCALLTYPE GetSite(REFIID iid, void** value) override {
        if (!diagnostics) { *value = nullptr; return E_FAIL; }
        return diagnostics->QueryInterface(iid, value);
    }
    HRESULT STDMETHODCALLTYPE OnVisualTreeChange(ParentChildRelation relation, VisualElement element, VisualMutationType mutation) override {
        // Only Handle is initialized for Remove. Strings on Add belong to us.
        struct Strings {
            VisualElement& e; bool owns;
            ~Strings() { if (owns) { SysFreeString(e.SrcInfo.FileName); SysFreeString(e.SrcInfo.Hash); SysFreeString(e.Type); SysFreeString(e.Name); } }
        } strings{element, mutation == Add};
        if (retired) return S_OK;
        try {
            if (mutation == Remove) {
                if (local) { local->descendants.erase(element.Handle); local->backgrounds.erase(element.Handle); }
                return S_OK;
            }
            const std::wstring_view type(element.Type ? element.Type : L""), name(element.Name ? element.Name : L"");
            const bool frame = type == L"Taskbar.TaskbarFrame";
            if (frame && !local) {
                local = new ThreadState(); local->diagnostics = diagnostics; local->tree = tree;
                WNDCLASSEXW cls{sizeof(cls)}; cls.lpfnWndProc = windowProc; cls.hInstance = module; cls.lpszClassName = WindowClass;
                RegisterClassExW(&cls);
                local->window = CreateWindowExW(0, WindowClass, GLASS_BUILD, 0, 0, 0, 0, 0, HWND_MESSAGE, nullptr, module, local);
                if (!local->window) throw Failure{HRESULT_FROM_WIN32(GetLastError()), "Create endpoint"};
                log("Taskbar frame found", S_OK);
            }
            if (!local || (!frame && !local->descendants.contains(relation.Parent))) return S_OK;
            local->descendants.insert(element.Handle);
            if (type == L"Windows.UI.Xaml.Shapes.Rectangle" && name == L"BackgroundFill" && !local->backgrounds.contains(element.Handle)) {
                Background bg; bg.handle = element.Handle;
                Ptr<IInspectable> elementObject;
                check(diagnostics->GetIInspectableFromHandle(bg.handle, elementObject.put()), "Get rectangle");
                bg.shape = query<Shape>(elementObject.get(), ShapeId);
                check(bg.shape->get_Fill(bg.original.put()), "Get original Fill");
                auto [entry, _] = local->backgrounds.emplace(bg.handle, std::move(bg));
                log("Taskbar background found", S_OK);
                if (local->active) local->apply(entry->second);
            }
            return S_OK;
        } catch (const Failure& e) { log(e.operation, e.hr); if (local) local->error = e.hr; }
          catch (...) { log("Visual tree exception", E_FAIL); }
        // Never propagate a feature failure into Explorer's visual-tree update.
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE OnElementStateChanged(InstanceHandle, VisualElementState, LPCWSTR) override { return S_OK; }
};

class ClassFactory final : public IClassFactory {
    std::atomic<ULONG> refs{1};
public:
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** value) override {
        if (!value) return E_POINTER; *value = nullptr;
        if (iid != IID_IUnknown && iid != IID_IClassFactory) return E_NOINTERFACE;
        *value = this; AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++refs; }
    ULONG STDMETHODCALLTYPE Release() override { ULONG n = --refs; if (!n) delete this; return n; }
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID iid, void** value) override {
        if (!value) return E_POINTER; *value = nullptr;
        if (outer) return CLASS_E_NOAGGREGATION;
        auto* site = new(std::nothrow) Site(); if (!site) return E_OUTOFMEMORY;
        HRESULT hr = site->QueryInterface(iid, value); site->Release(); return hr;
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL) override { return S_OK; }
};
} // namespace glass

extern "C" __declspec(dllexport) HRESULT WINAPI DllGetClassObject(REFCLSID clsid, REFIID iid, void** value) {
    if (!value) return E_POINTER; *value = nullptr;
    if (clsid != glass::SiteClass) return CLASS_E_CLASSNOTAVAILABLE;
    auto* factory = new(std::nothrow) glass::ClassFactory(); if (!factory) return E_OUTOFMEMORY;
    HRESULT hr = factory->QueryInterface(iid, value); factory->Release(); return hr;
}
extern "C" __declspec(dllexport) HRESULT WINAPI DllCanUnloadNow() { return S_FALSE; }

// Called only from the engine, on a worker. No loader-lock work in DllMain.
extern "C" __declspec(dllexport) HRESULT WINAPI GlassAttach(DWORD pid) {
    using namespace glass;
    wchar_t path[32768];
    if (!GetModuleFileNameW(module, path, ARRAYSIZE(path))) return HRESULT_FROM_WIN32(GetLastError());
    HMODULE xaml = LoadLibraryExW(L"Windows.UI.Xaml.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!xaml) return HRESULT_FROM_WIN32(GetLastError());
    auto initialize = reinterpret_cast<decltype(&InitializeXamlDiagnosticsEx)>(GetProcAddress(xaml, "InitializeXamlDiagnosticsEx"));
    if (!initialize) { FreeLibrary(xaml); return E_NOINTERFACE; }
    HRESULT hr = E_FAIL;
    // Every attempt gets a fresh thread: XAML caches initialization per thread.
    for (unsigned connection = 1; connection <= 8; ++connection) {
        struct Attempt { decltype(initialize) fn; DWORD pid; const wchar_t* path; unsigned connection; HRESULT result; } attempt{initialize, pid, path, connection, E_FAIL};
        HANDLE thread = CreateThread(nullptr, 0, [](void* context) -> DWORD {
            auto& a = *static_cast<Attempt*>(context); wchar_t endpoint[64];
            swprintf_s(endpoint, L"VisualDiagConnection%u", a.connection);
            a.result = a.fn(endpoint, a.pid, nullptr, a.path, SiteClass, nullptr); return 0;
        }, &attempt, 0, nullptr);
        if (!thread) { hr = HRESULT_FROM_WIN32(GetLastError()); break; }
        WaitForSingleObject(thread, INFINITE); CloseHandle(thread); hr = attempt.result;
        if (SUCCEEDED(hr) || hr == E_ACCESSDENIED) break;
    }
    FreeLibrary(xaml); log("Attach", hr); return hr;
}

// Positive = confirmed background count; zero = waiting for a taskbar;
// negative = HRESULT. Numeric messages only, never cross-process pointers.
extern "C" __declspec(dllexport) int WINAPI GlassUpdate(DWORD pid, DWORD owner, unsigned config) {
    using namespace glass;
    HWND window = nullptr; int count = 0; HRESULT error = S_OK;
    while ((window = FindWindowExW(HWND_MESSAGE, window, WindowClass, nullptr))) {
        DWORD process; GetWindowThreadProcessId(window, &process); if (process != pid) continue;
        wchar_t revision[100]; GetWindowTextW(window, revision, ARRAYSIZE(revision));
        DWORD_PTR result = 0;
        if (wcscmp(revision, GLASS_BUILD) != 0) {
            SendMessageTimeoutW(window, Retire, 0, 0, SMTO_ABORTIFHUNG | SMTO_BLOCK, 200, &result); continue;
        }
        if (!SendMessageTimeoutW(window, Configure, owner, config, SMTO_ABORTIFHUNG | SMTO_BLOCK, 200, &result)) {
            error = HRESULT_FROM_WIN32(ERROR_TIMEOUT); continue;
        }
        int value = static_cast<int>(result);
        if (value < 0) error = static_cast<HRESULT>(value); else count += value;
    }
    return FAILED(error) ? static_cast<int>(error) : count;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, void*) {
    if (reason == DLL_PROCESS_ATTACH) { glass::module = instance; DisableThreadLibraryCalls(instance); }
    return TRUE;
}
