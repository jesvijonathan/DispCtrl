#pragma once
// The small ABI surface absent from older MinGW SDKs. Signatures and interface
// IDs come from Microsoft's Windows SDK; no dependency on a C++/WinRT runtime.
#include <windows.h>
#include <inspectable.h>
#include <roapi.h>
#include <winstring.h>
// WIDL 11.5 aliases boolean to BYTE and emits the same IReference template
// twice. We do not use IReference<boolean>; skip its duplicate specialization.
#define ____FIReference_1_boolean_INTERFACE_DEFINED__
#include <windows.foundation.h>
#include <windows.graphics.effects.h>
#include <windows.ui.composition.h>
#include <d2d1effects.h>
#include <atomic>
#include <utility>
#include <vector>
#include <string>

namespace glass {
namespace comp = ABI::Windows::UI::Composition;
namespace fx = ABI::Windows::Graphics::Effects;
namespace foundation = ABI::Windows::Foundation;

struct Failure { HRESULT hr; const char* operation; };
inline void check(HRESULT hr, const char* operation) { if (FAILED(hr)) throw Failure{hr, operation}; }
template<class T> class Ptr {
    T* p = nullptr;
public:
    Ptr() = default;
    explicit Ptr(T* value) : p(value) { if (p) p->AddRef(); }
    Ptr(const Ptr& other) : Ptr(other.p) {}
    Ptr(Ptr&& other) noexcept : p(std::exchange(other.p, nullptr)) {}
    ~Ptr() { if (p) p->Release(); }
    Ptr& operator=(Ptr other) noexcept { std::swap(p, other.p); return *this; }
    T* get() const { return p; }
    T* operator->() const { return p; }
    explicit operator bool() const { return p != nullptr; }
    T** put() { if (p) p->Release(); p = nullptr; return &p; }
    void** out() { return reinterpret_cast<void**>(put()); }
    T* detach() { return std::exchange(p, nullptr); }
};
template<class T> Ptr<T> query(IUnknown* from, REFIID iid = __uuidof(T)) {
    Ptr<T> result; check(from->QueryInterface(iid, result.out()), "QueryInterface"); return result;
}
class String {
    HSTRING s = nullptr;
public:
    explicit String(const wchar_t* text) { check(WindowsCreateString(text, static_cast<UINT32>(wcslen(text)), &s), "WindowsCreateString"); }
    ~String() { WindowsDeleteString(s); }
    operator HSTRING() const { return s; }
};
template<class T> Ptr<T> factory(const wchar_t* name, REFIID iid = __uuidof(T)) {
    Ptr<T> result; check(RoGetActivationFactory(String(name), iid, result.out()), "RoGetActivationFactory"); return result;
}

inline constexpr GUID PreviewId{0x08c92b38,0xec99,0x4c55,{0xbc,0x85,0xa1,0xc1,0x80,0xb2,0x76,0x46}};
struct Preview : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE GetElementVisual(IInspectable*, comp::IVisual**) = 0;
};
inline constexpr GUID UIElementId{0x676d0be9,0xb65c,0x41c6,{0xba,0x40,0x58,0xcf,0x87,0xf2,0x01,0xc1}};
inline constexpr GUID ShapeId{0x786f2b75,0x9aa0,0x454d,{0xae,0x06,0xa2,0x46,0x6e,0x37,0xc8,0x32}};
inline constexpr GUID XamlBrushId{0x8806a321,0x1e06,0x422c,{0xa1,0xcc,0x01,0x69,0x65,0x59,0xe0,0x21}};
struct Shape : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE get_Fill(IInspectable**) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_Fill(IInspectable*) = 0;
};
inline constexpr GUID BrushFactoryId{0x394f0823,0x2451,0x4ed8,{0xbd,0x24,0x48,0x81,0x49,0xb3,0x42,0x8d}};
struct BrushFactory : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE CreateInstance(IInspectable*, IInspectable**, IInspectable**) = 0;
};
inline constexpr GUID BrushProtectedId{0x1513f3d8,0x0457,0x4e1c,{0xad,0x77,0x11,0xc1,0xd9,0x87,0x97,0x43}};
struct BrushProtected : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE get_CompositionBrush(comp::ICompositionBrush**) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_CompositionBrush(comp::ICompositionBrush*) = 0;
};
inline constexpr GUID BrushOverridesId{0xd19127f1,0x38b4,0x4ea1,{0x8f,0x33,0x84,0x96,0x29,0xa4,0xc9,0xc1}};
struct BrushOverrides : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE OnConnected() = 0;
    virtual HRESULT STDMETHODCALLTYPE OnDisconnected() = 0;
};
// XamlCompositionBrushBase is composable, not directly activatable. The outer
// owns the nondelegating inner; its interfaces delegate identity back here.
class XamlBrush final : public BrushOverrides {
    std::atomic<ULONG> refs{1};
public:
    Ptr<IInspectable> inner;
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        if (iid == IID_IUnknown || iid == __uuidof(IInspectable) || iid == BrushOverridesId) {
            *value = static_cast<BrushOverrides*>(this); AddRef(); return S_OK;
        }
        return inner ? inner->QueryInterface(iid, value) : E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++refs; }
    ULONG STDMETHODCALLTYPE Release() override { ULONG n = --refs; if (!n) delete this; return n; }
    HRESULT STDMETHODCALLTYPE GetIids(ULONG* count, IID** ids) override { *count = 0; *ids = nullptr; return S_OK; }
    HRESULT STDMETHODCALLTYPE GetRuntimeClassName(HSTRING* name) override {
        constexpr wchar_t text[] = L"Windows.UI.Xaml.Media.XamlCompositionBrushBase";
        return WindowsCreateString(text, ARRAYSIZE(text) - 1, name);
    }
    HRESULT STDMETHODCALLTYPE GetTrustLevel(TrustLevel* trust) override { *trust = BaseTrust; return S_OK; }
    HRESULT STDMETHODCALLTYPE OnConnected() override { return S_OK; }
    HRESULT STDMETHODCALLTYPE OnDisconnected() override { return S_OK; }
};
inline constexpr GUID Compositor2Id{0x735081dc,0x5e24,0x45da,{0xa3,0x8f,0xe3,0x2c,0xc3,0x49,0xa9,0xa0}};
struct Compositor2 : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE CreateAmbientLight(IInspectable**) = 0;
    virtual HRESULT STDMETHODCALLTYPE CreateAnimationGroup(IInspectable**) = 0;
    virtual HRESULT STDMETHODCALLTYPE CreateBackdropBrush(comp::ICompositionBrush**) = 0;
};
inline constexpr GUID SourceFactoryId{0xb3d9f276,0xaba3,0x4724,{0xac,0xf3,0xd0,0x39,0x74,0x64,0xdb,0x1c}};
struct SourceFactory : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE Create(HSTRING, fx::IGraphicsEffectSource**) = 0;
};
inline constexpr GUID EffectInteropId{0x2fc57384,0xa068,0x44d7,{0xa3,0x31,0x30,0x98,0x2f,0xcf,0x71,0x77}};
struct EffectInterop : IUnknown {
    virtual HRESULT STDMETHODCALLTYPE GetEffectId(GUID*) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetNamedPropertyMapping(LPCWSTR, UINT*, UINT*) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetPropertyCount(UINT*) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetProperty(UINT, foundation::IPropertyValue**) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetSource(UINT, fx::IGraphicsEffectSource**) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetSourceCount(UINT*) = 0;
};

// An immutable effect description. Windows copies/compiles it when creating
// the effect factory; there are no frame callbacks or CPU-rendered pixels.
class Effect final : public fx::IGraphicsEffect, public fx::IGraphicsEffectSource, public EffectInterop {
    std::atomic<ULONG> refs{1};
    GUID id;
    std::vector<Ptr<IInspectable>> properties;
    std::vector<Ptr<fx::IGraphicsEffectSource>> sources;
public:
    Effect(GUID clsid, std::vector<Ptr<IInspectable>> values, std::vector<Ptr<fx::IGraphicsEffectSource>> inputs)
        : id(clsid), properties(std::move(values)), sources(std::move(inputs)) {}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        if (iid == IID_IUnknown || iid == __uuidof(IInspectable) || iid == __uuidof(fx::IGraphicsEffect)) *value = static_cast<fx::IGraphicsEffect*>(this);
        else if (iid == __uuidof(fx::IGraphicsEffectSource)) *value = static_cast<fx::IGraphicsEffectSource*>(this);
        else if (iid == EffectInteropId) *value = static_cast<EffectInterop*>(this);
        else if (iid == IID_IAgileObject) *value = static_cast<fx::IGraphicsEffect*>(this);
        else return E_NOINTERFACE;
        AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++refs; }
    ULONG STDMETHODCALLTYPE Release() override { ULONG n = --refs; if (!n) delete this; return n; }
    HRESULT STDMETHODCALLTYPE GetIids(ULONG* count, IID** ids) override { *count = 0; *ids = nullptr; return S_OK; }
    HRESULT STDMETHODCALLTYPE GetRuntimeClassName(HSTRING* name) override { *name = nullptr; return S_OK; }
    HRESULT STDMETHODCALLTYPE GetTrustLevel(TrustLevel* trust) override { *trust = BaseTrust; return S_OK; }
    HRESULT STDMETHODCALLTYPE get_Name(HSTRING* name) override { *name = nullptr; return S_OK; }
    HRESULT STDMETHODCALLTYPE put_Name(HSTRING) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE GetEffectId(GUID* value) override { *value = id; return S_OK; }
    HRESULT STDMETHODCALLTYPE GetNamedPropertyMapping(LPCWSTR, UINT*, UINT*) override { return E_INVALIDARG; }
    HRESULT STDMETHODCALLTYPE GetPropertyCount(UINT* count) override { *count = static_cast<UINT>(properties.size()); return S_OK; }
    HRESULT STDMETHODCALLTYPE GetProperty(UINT index, foundation::IPropertyValue** value) override {
        *value = nullptr;
        return index < properties.size() ? properties[index]->QueryInterface(__uuidof(foundation::IPropertyValue), reinterpret_cast<void**>(value)) : E_INVALIDARG;
    }
    HRESULT STDMETHODCALLTYPE GetSourceCount(UINT* count) override { *count = static_cast<UINT>(sources.size()); return S_OK; }
    HRESULT STDMETHODCALLTYPE GetSource(UINT index, fx::IGraphicsEffectSource** value) override {
        *value = nullptr; if (index >= sources.size()) return E_INVALIDARG;
        *value = sources[index].get(); (*value)->AddRef(); return S_OK;
    }
};

inline Ptr<fx::IGraphicsEffectSource> effect(GUID id, std::vector<Ptr<IInspectable>> props, std::vector<Ptr<fx::IGraphicsEffectSource>> sources) {
    Ptr<fx::IGraphicsEffectSource> result;
    *result.put() = new Effect(id, std::move(props), std::move(sources)); return result;
}

inline Ptr<IInspectable> makeBrush(IInspectable* element, unsigned radius, unsigned tint, bool light) {
    auto preview = factory<Preview>(L"Windows.UI.Xaml.Hosting.ElementCompositionPreview", PreviewId);
    auto ui = query<IInspectable>(element, UIElementId);
    Ptr<comp::IVisual> visual; check(preview->GetElementVisual(ui.get(), visual.put()), "GetElementVisual");
    auto object = query<comp::ICompositionObject>(visual.get());
    Ptr<comp::ICompositor> compositor; check(object->get_Compositor(compositor.put()), "get_Compositor");
    auto compositor2 = query<Compositor2>(compositor.get(), Compositor2Id);
    Ptr<comp::ICompositionBrush> backdrop;
    check(compositor2->CreateBackdropBrush(backdrop.put()), "CreateBackdropBrush");
    Ptr<fx::IGraphicsEffectSource> input;
    check(factory<SourceFactory>(L"Windows.UI.Composition.CompositionEffectSourceParameter", SourceFactoryId)->Create(String(L"Backdrop"), input.put()), "Create source");
    auto values = factory<foundation::IPropertyValueStatics>(L"Windows.Foundation.PropertyValue");
    Ptr<IInspectable> deviation, optimization, border, color, mode;
    // Expose the radius in DIPs; D2D uses sigma, one third of the visible radius.
    check(values->CreateSingle(radius / 3.0f, deviation.put()), "Box radius");
    check(values->CreateUInt32(1 /* D2D1_GAUSSIANBLUR_OPTIMIZATION_BALANCED */, optimization.put()), "Box optimization");
    check(values->CreateUInt32(D2D1_BORDER_MODE_HARD, border.put()), "Box border");
    auto blur = effect(CLSID_D2D1GaussianBlur, {deviation, optimization, border}, {input});
    FLOAT rgba[]{light ? 1.0f : 0.0f, light ? 1.0f : 0.0f, light ? 1.0f : 0.0f, tint / 100.0f};
    check(values->CreateSingleArray(4, rgba, color.put()), "Box tint");
    auto flood = effect(CLSID_D2D1Flood, {color}, {});
    check(values->CreateUInt32(0 /* D2D1_COMPOSITE_MODE_SOURCE_OVER */, mode.put()), "Box composite");
    auto graph = effect(CLSID_D2D1Composite, {mode}, {blur, flood});
    Ptr<comp::ICompositionEffectFactory> compiled;
    check(compositor->CreateEffectFactory(query<fx::IGraphicsEffect>(graph.get()).get(), compiled.put()), "CreateEffectFactory");
    Ptr<comp::ICompositionEffectBrush> effectBrush;
    check(compiled->CreateBrush(effectBrush.put()), "CreateBrush");
    check(effectBrush->SetSourceParameter(String(L"Backdrop"), backdrop.get()), "SetSourceParameter");
    Ptr<XamlBrush> outer; *outer.put() = new XamlBrush();
    Ptr<IInspectable> brush;
    check(factory<BrushFactory>(L"Windows.UI.Xaml.Media.XamlCompositionBrushBase", BrushFactoryId)->CreateInstance(outer.get(), outer->inner.put(), brush.put()), "Create XAML brush");
    check(query<BrushProtected>(brush.get(), BrushProtectedId)->put_CompositionBrush(query<comp::ICompositionBrush>(effectBrush.get()).get()), "Set composition brush");
    return query<IInspectable>(brush.get(), XamlBrushId);
}
} // namespace glass
