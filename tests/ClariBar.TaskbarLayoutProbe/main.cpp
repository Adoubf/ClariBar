#include <windows.h>
#undef GetCurrentTime
#include <winrt/base.h>
#include <winrt/Windows.UI.Xaml.Hosting.h>
#include <windows.ui.xaml.hosting.desktopwindowxamlsource.h>
#include <iostream>
#include <stdexcept>
#include "../../src/ClariBar.ExplorerTap/taskbar_host.h"

namespace xaml=winrt::Windows::UI::Xaml;
namespace controls=xaml::Controls;
using claribar::taskbar::HostSlot;
unsigned checks{};
xaml::Hosting::DesktopWindowXamlSource testSource{nullptr};
void Display(xaml::FrameworkElement root) {
    testSource.Content(root);
    for(int attempt=0;attempt<50;attempt++) {
        MSG message{};while(PeekMessageW(&message,nullptr,0,0,PM_REMOVE)){TranslateMessage(&message);DispatchMessageW(&message);}
        root.UpdateLayout();if(root.IsLoaded())return;Sleep(10);
    }
    throw std::runtime_error("Synthetic tray did not load into its test XAML island");
}
void Require(bool success,const char* text) {
    if(!success)throw std::runtime_error(text);++checks;std::cout<<"PASS "<<text<<"\n";
}
controls::Grid Monitor() {controls::Grid result;result.Name(L"ClariBarEmbeddedMonitor");result.Width(120);result.Height(40);return result;}
controls::Border Item(double width) {controls::Border result;result.Width(width);result.Height(40);return result;}
bool SameColumnValue(winrt::Windows::Foundation::IInspectable left,winrt::Windows::Foundation::IInspectable right) {
    auto unset=xaml::DependencyProperty::UnsetValue();
    if(left==unset || right==unset)return left==right;
    return winrt::unbox_value<int>(left)==winrt::unbox_value<int>(right);
}
void CheckGrid(unsigned definitions) {
    controls::Grid grid;grid.Name(L"SystemTrayFrameGrid");
    for(unsigned index=0;index<definitions;index++){controls::ColumnDefinition column;column.Width({0,xaml::GridUnitType::Auto});grid.ColumnDefinitions().Append(column);}
    auto left=Item(24),right=Item(64);controls::Grid::SetColumn(right,definitions?definitions-1:0);
    grid.Children().Append(left);grid.Children().Append(right);
    Display(grid);
    auto local=left.ReadLocalValue(controls::Grid::ColumnProperty());auto rightLocal=right.ReadLocalValue(controls::Grid::ColumnProperty());
    for(int cycle=0;cycle<4;cycle++) {
        HostSlot slot;auto monitor=Monitor();Require(slot.Insert(grid,monitor),"Named tray Grid accepts an embedded monitor");
        grid.Measure({1000,48});
        Require(slot.Attached() && slot.method==(definitions?L"tray-grid":L"tray-grid-implicit"),"The detected Grid strategy matches its actual column definitions");
        Require(grid.ColumnDefinitions().Size()==std::max(definitions,1u)+1 && controls::Grid::GetColumn(left)==1 && controls::Grid::GetColumn(right)==static_cast<int>(definitions?definitions:1),"The monitor gets a separate Auto column and system children shift without overlap");
        grid.Measure({1000,48});Require(grid.DesiredSize().Width>=184,"Grid measurement reserves the monitor width alongside system tray contents");
        slot.Remove();
        Require(grid.Children().Size()==2 && grid.ColumnDefinitions().Size()==definitions && !xaml::Media::VisualTreeHelper::GetParent(monitor),"Removal restores the original explicit or implicit Grid columns");
        Require(SameColumnValue(left.ReadLocalValue(controls::Grid::ColumnProperty()),local) && SameColumnValue(right.ReadLocalValue(controls::Grid::ColumnProperty()),rightLocal),"Removal restores original local attached-property values");
    }
}
int wmain() {
    try {
        winrt::init_apartment(winrt::apartment_type::single_threaded);
        auto manager=xaml::Hosting::WindowsXamlManager::InitializeForCurrentThread();
        WNDCLASSW type{};type.hInstance=GetModuleHandleW(nullptr);type.lpfnWndProc=DefWindowProcW;type.lpszClassName=L"ClariBar.TaskbarLayoutFixture";RegisterClassW(&type);
        HWND window=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE,type.lpszClassName,L"ClariBar layout fixture",WS_POPUP,-32000,-32000,1000,48,nullptr,nullptr,type.hInstance,nullptr);
        if(!window)winrt::throw_last_error();
        testSource=xaml::Hosting::DesktopWindowXamlSource();auto native=testSource.as<IDesktopWindowXamlSourceNative>();
        winrt::check_hresult(native->AttachToWindow(window));HWND island{};winrt::check_hresult(native->get_WindowHandle(&island));
        SetWindowPos(island,nullptr,0,0,1000,48,SWP_NOACTIVATE|SWP_SHOWWINDOW);ShowWindow(window,SW_SHOWNOACTIVATE);
        {
            controls::StackPanel stack;stack.Name(L"SystemTrayFrameGrid");stack.Orientation(controls::Orientation::Horizontal);
            auto child=Item(40);stack.Children().Append(child);Display(stack);HostSlot slot;auto monitor=Monitor();
            Require(slot.Insert(stack,monitor) && stack.Children().GetAt(0)==monitor,"The horizontal StackPanel taskbar variant embeds before tray icons");
            stack.Measure({1000,48});Require(stack.DesiredSize().Width>=160,"StackPanel layout reserves the embedded width");
            slot.Remove();Require(stack.Children().Size()==1 && stack.Children().GetAt(0)==child,"StackPanel removal preserves the original icons");
        }
        for(unsigned count:{0u,1u,4u})CheckGrid(count);
        {
            controls::Grid grid;for(int i=0;i<3;i++)grid.ColumnDefinitions().Append(controls::ColumnDefinition());
            auto backdrop=Item(1),tray=Item(40),tail=Item(20);controls::Grid::SetColumnSpan(backdrop,3);controls::Grid::SetColumn(tray,1);controls::Grid::SetColumn(tail,2);
            grid.Children().Append(backdrop);grid.Children().Append(tray);grid.Children().Append(tail);Display(grid);HostSlot slot;
            Require(slot.Insert(tray,Monitor()) && slot.method==L"ancestor-grid","A shared ancestor Grid remains a supported tray layout");
            Require(controls::Grid::GetColumnSpan(backdrop)==4 && controls::Grid::GetColumn(tray)==2 && controls::Grid::GetColumn(tail)==3,"Insertion preserves background spans across the new monitor column");
            slot.Remove();Require(controls::Grid::GetColumnSpan(backdrop)==3 && controls::Grid::GetColumn(tray)==1 && controls::Grid::GetColumn(tail)==2,"Ancestor restoration preserves existing row and column placement");
        }
        {
            controls::StackPanel stack;stack.Orientation(controls::Orientation::Horizontal);auto left=Item(20),tray=Item(40);stack.Children().Append(left);stack.Children().Append(tray);
            Display(stack);
            HostSlot slot;auto monitor=Monitor();Require(slot.Insert(tray,monitor) && slot.method==L"ancestor-stack" && stack.Children().GetAt(1)==monitor,"An unnamed horizontal parent inserts the monitor immediately before its tray anchor");
            slot.Remove();Require(stack.Children().Size()==2 && stack.Children().GetAt(1)==tray,"Unnamed parent removal leaves its sibling order intact");
        }
        {
            controls::Grid grid;grid.Name(L"SystemTrayFrameGrid");grid.ColumnDefinitions().Append(controls::ColumnDefinition());grid.ColumnDefinitions().Append(controls::ColumnDefinition());
            auto child=Item(40);xaml::Data::Binding binding;binding.Source(winrt::box_value(1));child.SetBinding(controls::Grid::ColumnProperty(),binding);grid.Children().Append(child);Display(grid);
            HostSlot slot;Require(slot.Insert(grid,Monitor()),"A tray Grid with a bound column can embed monitoring");slot.Remove();
            Require(child.GetBindingExpression(controls::Grid::ColumnProperty()) && child.GetBindingExpression(controls::Grid::ColumnProperty()).ParentBinding()==binding,"Removing monitoring restores the system binding instead of replacing it with a fixed number");
        }
        {
            controls::StackPanel stack;stack.Name(L"SystemTrayFrameGrid");stack.Orientation(controls::Orientation::Vertical);auto child=Item(40);stack.Children().Append(child);Display(stack);HostSlot slot;
            Require(!slot.Insert(stack,Monitor()) && stack.Children().Size()==1,"An incompatible vertical tray is left intact");
            controls::Canvas canvas;auto anchor=Item(40);canvas.Children().Append(anchor);
            Display(canvas);
            Require(!slot.Insert(anchor,Monitor()) && canvas.Children().Size()==1,"An unrecognized layout fails without modifying unrelated controls");
        }
        testSource.Content(nullptr);testSource.Close();testSource=nullptr;DestroyWindow(window);
        manager.Close();std::cout<<"Taskbar layout checks passed: "<<checks<<"\n";return 0;
    }catch(const winrt::hresult_error& error){std::wcerr<<L"HRESULT "<<std::hex<<static_cast<unsigned>(error.code())<<L": "<<error.message().c_str()<<L"\n";return 2;}
    catch(const std::exception& error){std::cerr<<error.what()<<"\n";return 1;}
}
