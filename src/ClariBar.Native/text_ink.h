#pragma once
#include <dwrite.h>
#include <wrl/client.h>
#include <algorithm>
#include <cmath>
#include <cwchar>

namespace claribar {
// Distances from the baseline to the visible glyph bounds, in logical pixels.
// Unlike a cap-height line box, these include accents, CJK glyphs and descenders.
struct TextInk { double above{},below{}; };
inline bool MeasureTextInk(const wchar_t* text,const wchar_t* family,double size,unsigned weight,TextInk& ink) {
    using Microsoft::WRL::ComPtr;
    static const auto factory=[] {
        ComPtr<IDWriteFactory> result;
        DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED,__uuidof(IDWriteFactory),reinterpret_cast<IUnknown**>(result.GetAddressOf()));
        return result;
    }();
    if(!factory || !text || !*text)return false;
    wchar_t locale[LOCALE_NAME_MAX_LENGTH]{};GetUserDefaultLocaleName(locale,LOCALE_NAME_MAX_LENGTH);
    ComPtr<IDWriteTextFormat> format;
    if(FAILED(factory->CreateTextFormat(family && *family?family:L"Microsoft YaHei UI",nullptr,
        weight>=600?DWRITE_FONT_WEIGHT_BOLD:DWRITE_FONT_WEIGHT_NORMAL,DWRITE_FONT_STYLE_NORMAL,
        DWRITE_FONT_STRETCH_NORMAL,static_cast<float>(size),locale,format.GetAddressOf())))return false;
    format->SetWordWrapping(DWRITE_WORD_WRAPPING_NO_WRAP);
    ComPtr<IDWriteTextLayout> layout;
    if(FAILED(factory->CreateTextLayout(text,static_cast<UINT32>(wcslen(text)),format.Get(),10000,1000,layout.GetAddressOf())))return false;
    DWRITE_TEXT_METRICS metrics{};DWRITE_OVERHANG_METRICS overhang{};DWRITE_LINE_METRICS line{};UINT32 lines{};
    if(FAILED(layout->GetMetrics(&metrics)) || FAILED(layout->GetOverhangMetrics(&overhang)) ||
        FAILED(layout->GetLineMetrics(&line,1,&lines)))return false;
    TextInk measured{line.baseline+overhang.top,metrics.layoutHeight+overhang.bottom-line.baseline};
    if(!std::isfinite(measured.above) || !std::isfinite(measured.below) || measured.above+measured.below<=0)return false;
    ink=measured;return true;
}
}
