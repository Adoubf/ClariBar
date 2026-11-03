#pragma once
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Data.h>
#include <winrt/Windows.UI.Xaml.Media.h>
#include <algorithm>
#include <string>
#include <vector>

namespace claribar::taskbar {
namespace xaml=winrt::Windows::UI::Xaml;
namespace controls=xaml::Controls;
namespace foundation=winrt::Windows::Foundation;

// Detect layout capabilities instead of maintaining a Windows-build allowlist.
// This slot reserves real layout space and restores only its own changes.
struct HostSlot {
    static xaml::DependencyObject LayoutParent(xaml::DependencyObject object) {
        auto parent=xaml::Media::VisualTreeHelper::GetParent(object);
        if(!parent)if(auto element=object.try_as<xaml::FrameworkElement>())parent=element.Parent();
        return parent;
    }
    struct Value {
        winrt::weak_ref<xaml::FrameworkElement> object;
        foundation::IInspectable column{nullptr},span{nullptr};
        xaml::Data::Binding columnBinding{nullptr},spanBinding{nullptr};
    };
    controls::Panel host{nullptr};
    controls::Grid parent{nullptr},panel{nullptr};
    controls::ColumnDefinition column{nullptr},implicitColumn{nullptr};
    std::vector<Value> originals;
    std::wstring method;

    void Remove() noexcept {
        try{if(host && panel){uint32_t index{};if(host.Children().IndexOf(panel,index))host.Children().RemoveAt(index);}}catch(...){}
        for(auto& entry:originals)try{if(auto item=entry.object.get()) {
            auto restore=[&](auto property,auto value,auto binding){
                if(binding)item.SetBinding(property,binding);
                else if(value==xaml::DependencyProperty::UnsetValue())item.ClearValue(property);
                else item.SetValue(property,value);
            };
            restore(controls::Grid::ColumnProperty(),entry.column,entry.columnBinding);
            restore(controls::Grid::ColumnSpanProperty(),entry.span,entry.spanBinding);
        }}catch(...){}
        for(auto definition:{column,implicitColumn})try{if(parent && definition) {
            uint32_t index{};if(parent.ColumnDefinitions().IndexOf(definition,index))parent.ColumnDefinitions().RemoveAt(index);
        }}catch(...){}
        originals.clear();host=nullptr;parent=nullptr;panel=nullptr;column=nullptr;implicitColumn=nullptr;method.clear();
    }
    bool Attached() const {
        uint32_t index{};return host && panel && host.Children().IndexOf(panel,index);
    }
    bool Stack(controls::StackPanel stack,controls::Grid content,uint32_t index,const wchar_t* description) {
        if(!stack || stack.Orientation()!=controls::Orientation::Horizontal || index>stack.Children().Size())return false;
        host=stack;panel=content;method=description;stack.Children().InsertAt(index,panel);return true;
    }
    bool Grid(controls::Grid grid,controls::Grid content,int index,const wchar_t* description,int row=0,int rows=1) {
        if(!grid || index<0 || index>static_cast<int>(grid.ColumnDefinitions().Size()))return false;
        parent=grid;host=grid;panel=content;method=description;
        for(auto ui:grid.Children())if(auto child=ui.try_as<xaml::FrameworkElement>()) {
            Value old{winrt::make_weak(child),child.ReadLocalValue(controls::Grid::ColumnProperty()),child.ReadLocalValue(controls::Grid::ColumnSpanProperty())};
            if(auto binding=child.GetBindingExpression(controls::Grid::ColumnProperty()))old.columnBinding=binding.ParentBinding();
            if(auto binding=child.GetBindingExpression(controls::Grid::ColumnSpanProperty()))old.spanBinding=binding.ParentBinding();
            originals.push_back(old);
        }
        // No definitions means one implicit Star column. Materialize it while
        // our Auto column exists, then remove both definitions on restoration.
        if(grid.ColumnDefinitions().Size()==0) {
            implicitColumn=controls::ColumnDefinition();implicitColumn.Width({1,xaml::GridUnitType::Star});grid.ColumnDefinitions().Append(implicitColumn);
        }
        column=controls::ColumnDefinition();column.Width({0,xaml::GridUnitType::Auto});grid.ColumnDefinitions().InsertAt(index,column);
        for(auto& entry:originals)if(auto child=entry.object.get()) {
            int at=controls::Grid::GetColumn(child),span=controls::Grid::GetColumnSpan(child);
            if(at>=index)controls::Grid::SetColumn(child,at+1);
            else if(at+span>index)controls::Grid::SetColumnSpan(child,span+1);
        }
        controls::Grid::SetColumn(panel,index);controls::Grid::SetRow(panel,row);controls::Grid::SetRowSpan(panel,rows);
        grid.Children().Append(panel);return true;
    }
    static xaml::DependencyObject NamedHost(xaml::DependencyObject target,int depth=0) {
        if(!target || depth>8)return nullptr;
        if(auto element=target.try_as<xaml::FrameworkElement>())
            if(element.Name()==L"SystemTrayFrameGrid" || element.Name()==L"SystemTrayFrameStackPanel")return target;
        int count=std::min(xaml::Media::VisualTreeHelper::GetChildrenCount(target),256);
        for(int index=0;index<count;index++)if(auto match=NamedHost(xaml::Media::VisualTreeHelper::GetChild(target,index),depth+1))return match;
        return nullptr;
    }
    bool Insert(xaml::DependencyObject target,controls::Grid content) {
        Remove();if(!target || !content || LayoutParent(content))return false;
        try {
            if(auto named=NamedHost(target)) {
                if(Stack(named.try_as<controls::StackPanel>(),content,0,L"tray-stack"))return true;
                if(auto grid=named.try_as<controls::Grid>())
                    return Grid(grid,content,0,grid.ColumnDefinitions().Size()?L"tray-grid":L"tray-grid-implicit",0,std::max(1u,grid.RowDefinitions().Size()));
            }
            // Retain support for taskbars that place the tray in a shared layout.
            auto anchor=target.try_as<xaml::FrameworkElement>();
            for(int step=0;anchor && step<20;step++) {
                auto ancestor=LayoutParent(anchor);if(!ancestor)break;
                if(auto stack=ancestor.try_as<controls::StackPanel>()) {
                    uint32_t index{};if(stack.Children().IndexOf(anchor,index) && Stack(stack,content,index,L"ancestor-stack"))return true;
                }
                if(auto grid=ancestor.try_as<controls::Grid>();grid && grid.ColumnDefinitions().Size()>1) {
                    int index=controls::Grid::GetColumn(anchor);
                    if(index<static_cast<int>(grid.ColumnDefinitions().Size()))return Grid(grid,content,index,L"ancestor-grid",controls::Grid::GetRow(anchor),controls::Grid::GetRowSpan(anchor));
                }
                anchor=ancestor.try_as<xaml::FrameworkElement>();
            }
        }catch(...){Remove();throw;}
        return false;
    }
};
}
