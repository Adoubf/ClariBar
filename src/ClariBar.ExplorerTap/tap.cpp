#include <windows.h>
#include <ocidl.h>
#include <xamlOM.h>
#undef GetCurrentTime
#include <winrt/base.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.UI.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Media.h>
#include <winrt/Windows.UI.Xaml.Shapes.h>
#include <winrt/Windows.UI.Xaml.Data.h>
#include <winrt/Windows.UI.Xaml.Automation.h>
#include <algorithm>
#include <atomic>
#include <mutex>
#include <memory>
#include <string>
#include <vector>
#include <unordered_map>
#include <thread>
#include <functional>
#include <cmath>
#include <limits>
#include <filesystem>
#include <winrt/Windows.UI.Text.h>
#include "../ClariBar.Native/explorer_protocol.h"
#include "../ClariBar.Native/text_ink.h"

namespace wx=winrt::Windows::UI::Xaml;
namespace wc=winrt::Windows::UI::Xaml::Controls;
namespace wm=winrt::Windows::UI::Xaml::Media;
namespace wf=winrt::Windows::Foundation;
using claribar::ExplorerConfig;
using claribar::ExplorerStatus;
namespace {
void BootstrapLog(const std::wstring& value) noexcept {
    HMODULE module{};
    if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(&BootstrapLog),&module))return;
    wchar_t modulePath[32768]{};
    DWORD length=GetModuleFileNameW(module,modulePath,32768);
    if(!length || length>=32768)return;
    auto logPath=std::filesystem::path(modulePath).parent_path()/L"tap-bootstrap.log";
    HANDLE file=CreateFileW(logPath.c_str(),FILE_APPEND_DATA,FILE_SHARE_READ|FILE_SHARE_WRITE,nullptr,OPEN_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(file==INVALID_HANDLE_VALUE)return;
    auto line=L"PID "+std::to_wstring(GetCurrentProcessId())+L" TID "+std::to_wstring(GetCurrentThreadId())+L" "+value+L"\r\n";
    DWORD bytes{};WriteFile(file,line.data(),static_cast<DWORD>(line.size()*sizeof(wchar_t)),&bytes,nullptr);CloseHandle(file);
}
struct Mapping {
    HANDLE handle{};claribar::SharedPage* page{};
    ~Mapping(){if(page)UnmapViewOfFile(page);if(handle)CloseHandle(handle);}
};
std::mutex globalGate;
std::shared_ptr<Mapping> activeMapping;
std::atomic<bool> advised{};
std::unordered_map<DWORD,ExplorerStatus> statuses;
std::unordered_map<DWORD,std::wstring> dumps;

std::wstring Type(const wf::IInspectable& object) {return object?std::wstring(winrt::get_class_name(object)):L"";}
std::wstring Name(const wx::DependencyObject& object) {auto fe=object.try_as<wx::FrameworkElement>();return fe?std::wstring(fe.Name()):L"";}
wm::SolidColorBrush Brush(uint32_t argb) {return wm::SolidColorBrush(winrt::Windows::UI::Color{static_cast<uint8_t>(argb>>24),static_cast<uint8_t>(argb>>16),static_cast<uint8_t>(argb>>8),static_cast<uint8_t>(argb)});}
wx::DependencyObject Parent(const wx::DependencyObject& object){return wm::VisualTreeHelper::GetParent(object);}
bool Within(const wx::DependencyObject& object,const std::wstring& fragment) {
    auto current=object;for(int i=0;current && i<32;i++,current=Parent(current))if(Type(current).find(fragment)!=std::wstring::npos)return true;return false;
}
bool IsTaskbar(const wx::DependencyObject& object) {return Within(object,L"TaskbarFrame") || Within(object,L"SystemTrayFrame");}
void Visit(const wx::DependencyObject& node,const std::function<void(wx::DependencyObject,int)>& action,int depth=0) {
    if(!node || depth>45)return;action(node,depth);
    int count=std::min(wm::VisualTreeHelper::GetChildrenCount(node),2500);
    for(int i=0;i<count;i++)Visit(wm::VisualTreeHelper::GetChild(node,i),action,depth+1);
}

// XAML strings may contain embedded NULs (notably localized date text).
// Keep diagnostic lines printable so the shared UTF-16 transport cannot truncate them.
std::wstring Printable(const winrt::hstring& value) {
    std::wstring text(value.c_str(),value.size());
    std::replace_if(text.begin(),text.end(),[](wchar_t c){return c<L' ';},L' ');
    return text;
}
void Describe(std::wstring& output,const wx::DependencyObject& object,int depth) {
    if(output.size()>=claribar::dumpCapacity-1000)return;
    output+=std::wstring(depth*2,L' ')+Type(object)+L" #"+Name(object);
    double elementY=0;
    bool metric=Name(object).rfind(L"ClariBarMetric",0)==0;
    if(auto fe=object.try_as<wx::FrameworkElement>()) {
        output+=L" ["+std::to_wstring(static_cast<int>(fe.ActualWidth()))+L"x"+std::to_wstring(static_cast<int>(fe.ActualHeight()))+L"]";
        if(Name(object).rfind(L"ClariBar",0)==0) {
            auto relative=Parent(object);
            if(metric)while(relative && Name(relative)!=L"ClariBarEmbeddedMonitor")relative=Parent(relative);
            if(auto parent=relative.try_as<wx::UIElement>()) {
                auto origin=fe.TransformToVisual(parent).TransformPoint({0,0});
                elementY=origin.Y;
                output+=L" y="+std::to_wstring(origin.Y)+L" height="+std::to_wstring(fe.ActualHeight());
            }
        }
    }
    if(auto tb=object.try_as<wc::TextBlock>()) {
        output+=L" text="+Printable(tb.Text())+L" font="+std::to_wstring(tb.FontSize())+L" family="+Printable(tb.FontFamily().Source())+L" weight="+std::to_wstring(tb.FontWeight().Weight)+L" trimmed="+std::to_wstring(tb.IsTextTrimmed());
        claribar::TextInk ink;
        if(metric && claribar::MeasureTextInk(tb.Text().c_str(),tb.FontFamily().Source().c_str(),tb.FontSize(),tb.FontWeight().Weight,ink))
            output+=L" inkTop="+std::to_wstring(elementY+tb.BaselineOffset()-ink.above)+L" inkBottom="+std::to_wstring(elementY+tb.BaselineOffset()+ink.below);
        if(auto brush=tb.Foreground().try_as<wm::SolidColorBrush>()) {
            auto c=brush.Color();wchar_t hex[16]{};swprintf_s(hex,L"%02X%02X%02X%02X",c.A,c.R,c.G,c.B);
            output+=L" foreground=#"+std::wstring(hex);
        }
    }
    if(auto icon=object.try_as<wc::FontIcon>())if(auto brush=icon.Foreground().try_as<wm::SolidColorBrush>()) {
        auto c=brush.Color();wchar_t hex[16]{};swprintf_s(hex,L"%02X%02X%02X%02X",c.A,c.R,c.G,c.B);
        output+=L" foreground=#"+std::wstring(hex);
    }
    if(auto shape=object.try_as<wx::Shapes::Shape>())if(auto brush=shape.Fill().try_as<wm::SolidColorBrush>()) {
        auto c=brush.Color();wchar_t hex[16]{};swprintf_s(hex,L"%02X%02X%02X%02X",c.A,c.R,c.G,c.B);
        output+=L" fill=#"+std::wstring(hex);
    }
    if(auto ui=object.try_as<wx::FrameworkElement>())
        output+=L" col="+std::to_wstring(wc::Grid::GetColumn(ui))+L" row="+std::to_wstring(wc::Grid::GetRow(ui))+L" span="+std::to_wstring(wc::Grid::GetColumnSpan(ui));
    if(auto grid=object.try_as<wc::Grid>()) {
        output+=L" columns="+std::to_wstring(grid.ColumnDefinitions().Size())+L" rows="+std::to_wstring(grid.RowDefinitions().Size());
        if(Name(object)==L"ClariBarEmbeddedMonitor" && grid.Tag())output+=L" "+std::wstring(winrt::unbox_value<winrt::hstring>(grid.Tag()));
    }
    if(auto ui=object.try_as<wx::UIElement>())output+=ui.IsHitTestVisible()?L" hitTest=1":L" hitTest=0";
    output+=L"\r\n";
}

struct Patch {
    winrt::weak_ref<wx::DependencyObject> object;
    wx::DependencyProperty property{nullptr};
    wf::IInspectable originalLocal{nullptr},originalEffective{nullptr};
    wx::Data::Binding binding{nullptr};
    uint32_t group{};
    void Restore() noexcept {
        try {if(auto target=object.get()) {
            auto fe=target.try_as<wx::FrameworkElement>();
            if(binding && fe)fe.SetBinding(property,binding);
            else if(originalLocal==wx::DependencyProperty::UnsetValue())target.ClearValue(property);
            else target.SetValue(property,originalLocal);
        }}catch(...){try{if(auto target=object.get())target.SetValue(property,originalEffective);}catch(...){}}
    }
};
struct LayoutValue {winrt::weak_ref<wx::FrameworkElement> object;wf::IInspectable column{nullptr},span{nullptr};};
struct Embedded {
    wc::Grid parent{nullptr},panel{nullptr};wc::ColumnDefinition column{nullptr};
    wc::Panel host{nullptr};
    winrt::weak_ref<wx::DependencyObject> tray;
    winrt::weak_ref<wx::FrameworkElement> background;
    std::vector<LayoutValue> originals;
    std::vector<wc::TextBlock> texts;
    std::vector<wc::Canvas> cells;
    uint32_t layout=UINT32_MAX,itemCount=UINT32_MAX;
    void Remove() noexcept {
        try {
            if(host && panel) {uint32_t index{};if(host.Children().IndexOf(panel,index))host.Children().RemoveAt(index);}
            if(parent && column){uint32_t index{};if(parent.ColumnDefinitions().IndexOf(column,index))parent.ColumnDefinitions().RemoveAt(index);}
            for(auto& entry:originals)if(auto item=entry.object.get()) {
                if(entry.column==wx::DependencyProperty::UnsetValue())item.ClearValue(wc::Grid::ColumnProperty());else item.SetValue(wc::Grid::ColumnProperty(),entry.column);
                if(entry.span==wx::DependencyProperty::UnsetValue())item.ClearValue(wc::Grid::ColumnSpanProperty());else item.SetValue(wc::Grid::ColumnSpanProperty(),entry.span);
            }
        }catch(...){}
        texts.clear();cells.clear();originals.clear();panel=nullptr;column=nullptr;parent=nullptr;host=nullptr;tray=nullptr;background=nullptr;layout=itemCount=UINT32_MAX;
    }
    bool Insert(wx::DependencyObject target) {
        tray=winrt::make_weak(target);
        wc::StackPanel stack{nullptr};
        Visit(target,[&](wx::DependencyObject item,int){if(Name(item)==L"SystemTrayFrameGrid")stack=item.try_as<wc::StackPanel>();});
        if(stack && stack.Orientation()==wc::Orientation::Horizontal) {
            host=stack;panel=wc::Grid();panel.Name(L"ClariBarEmbeddedMonitor");
            panel.Margin({4,0,6,0});panel.VerticalAlignment(wx::VerticalAlignment::Center);panel.Background(Brush(0));
            stack.Children().InsertAt(0,panel);return true;
        }
        wx::FrameworkElement anchor=target.try_as<wx::FrameworkElement>();
        for(int step=0;anchor && step<20;step++) {
            auto candidate=Parent(anchor);if(!candidate)break;
            auto grid=candidate.try_as<wc::Grid>();
            if(grid && grid.ColumnDefinitions().Size()>1 && IsTaskbar(grid)) {
                int index=wc::Grid::GetColumn(anchor);
                if(index>=0 && index<static_cast<int>(grid.ColumnDefinitions().Size())) {
                    parent=grid;host=grid;column=wc::ColumnDefinition();column.Width({0,wx::GridUnitType::Auto});
                    for(auto ui:grid.Children()) {
                        auto child=ui.try_as<wx::FrameworkElement>();if(!child)continue;
                        LayoutValue old{winrt::make_weak(child),child.ReadLocalValue(wc::Grid::ColumnProperty()),child.ReadLocalValue(wc::Grid::ColumnSpanProperty())};
                        originals.push_back(old);
                    }
                    grid.ColumnDefinitions().InsertAt(index,column);
                    for(auto& entry:originals)if(auto item=entry.object.get()) {
                        int c=wc::Grid::GetColumn(item),span=wc::Grid::GetColumnSpan(item);
                        if(c>=index)wc::Grid::SetColumn(item,c+1);
                        else if(c+span>index)wc::Grid::SetColumnSpan(item,span+1);
                    }
                    panel=wc::Grid();panel.Name(L"ClariBarEmbeddedMonitor");panel.Margin({4,0,6,0});
                    panel.VerticalAlignment(wx::VerticalAlignment::Center);
                    panel.Background(Brush(0));wc::Grid::SetColumn(panel,index);grid.Children().Append(panel);
                    return true;
                }
            }
            anchor=candidate.try_as<wx::FrameworkElement>();
        }
        return false;
    }
    wf::Rect VisibleArea() {
        wf::Rect area{0,0,static_cast<float>(host.ActualWidth()),static_cast<float>(host.ActualHeight()>8?host.ActualHeight():48)};
        auto fill=background.get();
        if(!fill) {
            auto root=host.as<wx::DependencyObject>();for(int i=0;i<40;i++){auto ancestor=Parent(root);if(!ancestor)break;root=ancestor;}
            Visit(root,[&](wx::DependencyObject object,int){if(Name(object)==L"BackgroundFill" && Within(object,L"TaskbarBackground"))fill=object.try_as<wx::FrameworkElement>();});
            if(fill)background=winrt::make_weak(fill);
        }
        if(fill && fill.ActualHeight()>8) {
            auto bounds=fill.TransformToVisual(host).TransformBounds({0,0,static_cast<float>(fill.ActualWidth()),static_cast<float>(fill.ActualHeight())});
            float top=std::max(0.f,bounds.Y),bottom=std::min(area.Height,bounds.Y+bounds.Height);
            if(bottom-top>=16){area.Y=top;area.Height=bottom-top;}
        }
        return area;
    }
    void Update(const ExplorerConfig& config) {
        if(!panel)return;
        panel.IsHitTestVisible(config.clickThrough==0);
        uint32_t count=std::min(config.metricCount,claribar::maxMetrics);
        if(layout!=config.layout || itemCount!=count) {
            panel.Children().Clear();panel.ColumnDefinitions().Clear();panel.RowDefinitions().Clear();texts.clear();cells.clear();
            uint32_t cols=config.layout==1?2u:config.layout==2?(count+1)/2:std::max(count,1u);
            cols=std::max(1u,cols);uint32_t rows=std::max(1u,(count+cols-1)/cols);
            for(uint32_t i=0;i<cols;i++){wc::ColumnDefinition c;c.Width({1,wx::GridUnitType::Star});panel.ColumnDefinitions().Append(c);}
            for(uint32_t i=0;i<rows;i++){wc::RowDefinition r;r.Height({0,wx::GridUnitType::Auto});panel.RowDefinitions().Append(r);}
            for(uint32_t i=0;i<count;i++) {
                wc::TextBlock text;text.Name(L"ClariBarMetric"+std::to_wstring(i));
                text.TextTrimming(wx::TextTrimming::CharacterEllipsis);text.TextWrapping(wx::TextWrapping::NoWrap);
                text.TextLineBounds(wx::TextLineBounds::Full);text.MaxLines(1);
                wc::Canvas cell;cell.Name(L"ClariBarCell"+std::to_wstring(i));cell.Children().Append(text);
                wc::Grid::SetColumn(cell,i%cols);wc::Grid::SetRow(cell,i/cols);
                wc::Grid::SetColumn(text,i%cols);wc::Grid::SetRow(text,i/cols);
                panel.Children().Append(cell);texts.push_back(text);cells.push_back(cell);
            }
            layout=config.layout;itemCount=count;
        }
        auto rows=std::max(panel.RowDefinitions().Size(),1u);
        auto area=VisibleArea();double available=area.Height;
        double size=std::clamp(static_cast<double>(config.fontSize),8.,24.);
        auto family=wm::FontFamily(config.fontFamily[0]?config.fontFamily:L"Microsoft YaHei UI");
        auto weight=winrt::Windows::UI::Text::FontWeight{static_cast<uint16_t>(config.fontWeight>=600?700:400)};
        auto cols=std::max(panel.ColumnDefinitions().Size(),1u);
        std::vector<double> widths(cols,16.),heights(rows,0.),above(rows,0.),below(rows,0.);
        for(uint32_t i=0;i<texts.size();i++) {
            auto text=texts[i];text.Text(config.metrics[i].text);text.Foreground(Brush(config.metrics[i].color));
            text.FontFamily(family);text.FontWeight(weight);
        }
        // Lay out visible glyphs around a common baseline for each row.
        // Tight XAML line boxes omit descenders and can overlap adjacent rows.
        constexpr double gap=4.;
        double contentHeight=0;
        for(;;) {
            std::fill(widths.begin(),widths.end(),16.);std::fill(above.begin(),above.end(),0.);std::fill(below.begin(),below.end(),-1000.);
            for(uint32_t i=0;i<texts.size();i++) {
                auto text=texts[i];text.FontSize(size);text.Width(std::numeric_limits<double>::quiet_NaN());
                // Star columns have no finite space to divide in the tray StackPanel.
                // Give each column its measured content width, including margins.
                text.Measure({std::numeric_limits<float>::infinity(),std::numeric_limits<float>::infinity()});
                widths[i%cols]=std::max(widths[i%cols],std::ceil((static_cast<double>(text.DesiredSize().Width)+12)/4)*4);
                claribar::TextInk ink{text.BaselineOffset(),text.DesiredSize().Height-text.BaselineOffset()};
                claribar::MeasureTextInk(text.Text().c_str(),family.Source().c_str(),size,weight.Weight,ink);
                above[i/cols]=std::max(above[i/cols],ink.above);below[i/cols]=std::max(below[i/cols],ink.below);
            }
            for(uint32_t row=0;row<rows;row++)heights[row]=std::max(0.,std::ceil(above[row]+below[row]));
            contentHeight=gap*(rows-1);for(double height:heights)contentHeight+=height;
            if(contentHeight<=available-4 || size<=7.)break;
            size-=.5;
        }
        double padding=std::max(0.,(available-contentHeight)/2);
        double up=rows>1?std::min(2.,std::max(0.,padding-2)):0;
        double fullHeight=host.ActualHeight()>8?host.ActualHeight():available;
        panel.Height(fullHeight);panel.Padding({0,area.Y+padding-up,0,std::max(0.,fullHeight-area.Y-area.Height)+padding+up});panel.RowSpacing(gap);
        panel.Tag(winrt::box_value(winrt::hstring(L"areaTop="+std::to_wstring(area.Y)+L" areaHeight="+std::to_wstring(area.Height)+L" up="+std::to_wstring(up)+L" gap="+std::to_wstring(gap))));
        for(uint32_t i=0;i<rows;i++)panel.RowDefinitions().GetAt(i).Height({heights[i],wx::GridUnitType::Pixel});
        double rootWidth=1280;
        if(auto root=panel.XamlRoot())rootWidth=root.Size().Width;
        double limit=std::max(100.,rootWidth*.60);
        double total=0;for(double width:widths)total+=width;
        double width=config.autoWidth?std::min(total,limit):std::min(static_cast<double>(std::clamp(config.width,100u,900u)),limit);
        for(uint32_t i=0;i<cols;i++)panel.ColumnDefinitions().GetAt(i).Width({config.autoWidth?widths[i]*width/total:width/cols,wx::GridUnitType::Pixel});
        for(uint32_t i=0;i<texts.size();i++) {
            double cellWidth=panel.ColumnDefinitions().GetAt(i%cols).Width().Value;
            texts[i].Width(std::max(1.,cellWidth-8));
            wc::Canvas::SetLeft(texts[i],4);wc::Canvas::SetTop(texts[i],above[i/cols]-texts[i].BaselineOffset());
        }
        panel.Width(width);
    }
};

struct ThreadState : std::enable_shared_from_this<ThreadState> {
    wx::DispatcherTimer timer{nullptr};
    std::vector<winrt::weak_ref<wx::DependencyObject>> roots;
    std::vector<Patch> patches;
    std::vector<std::unique_ptr<Embedded>> embedded;
    bool busy=false,hasApplied=false;
    unsigned ticks{};
    std::shared_ptr<Mapping> lastMapping;
    void Start(){
        if(timer){timer.Start();return;}timer=wx::DispatcherTimer();timer.Interval(std::chrono::milliseconds(400));
        std::weak_ptr<ThreadState> weak=shared_from_this();timer.Tick([weak](auto&&,auto&&){if(auto self=weak.lock())self->Tick();});timer.Start();
    }
    void Track(wx::DependencyObject object) {
        // Reach the complete taskbar's XAML root; only weak references are retained.
        auto root=object;for(int i=0;i<40;i++){auto p=Parent(root);if(!p)break;root=p;}
        for(auto& existing:roots)if(existing.get()==root)return;
        roots.push_back(winrt::make_weak(root));
    }
    void Restore(uint32_t group=0xffffffff) {
        for(auto it=patches.begin();it!=patches.end();) {
            if(it->group&group){it->Restore();it=patches.erase(it);}else ++it;
        }
        if(group==0xffffffff){for(auto& item:embedded)item->Remove();embedded.clear();}
    }
    void Set(wx::DependencyObject target,wx::DependencyProperty property,wf::IInspectable value,uint32_t group) {
        auto found=std::find_if(patches.begin(),patches.end(),[&](auto& entry){return entry.object.get()==target && entry.property==property;});
        if(found==patches.end()) {
            Patch patch{winrt::make_weak(target),property,target.ReadLocalValue(property),target.GetValue(property),nullptr,group};
            if(auto fe=target.try_as<wx::FrameworkElement>()){auto expression=fe.GetBindingExpression(property);if(expression)patch.binding=expression.ParentBinding();}
            patches.push_back(std::move(patch));
        }
        target.SetValue(property,value);
    }
    void Publish(const std::shared_ptr<Mapping>& mapping,const ExplorerStatus& state,const std::wstring& dump) {
        std::lock_guard lock(globalGate);statuses[GetCurrentThreadId()]=state;
        if(!dump.empty())dumps[GetCurrentThreadId()]=dump;
        ExplorerStatus total=state;total.frames=total.backgrounds=total.times=total.dates=total.systemIcons=0;
        total.networks=total.volumes=total.inputs=total.batteries=total.bitmapIcons=total.panels=total.metricCount=total.patchedProperties=0;
        for(auto& [id,status]:statuses) {
            total.frames+=status.frames;total.backgrounds+=status.backgrounds;total.times+=status.times;total.dates+=status.dates;
            total.systemIcons+=status.systemIcons;total.networks+=status.networks;total.volumes+=status.volumes;total.inputs+=status.inputs;
            total.batteries+=status.batteries;total.bitmapIcons+=status.bitmapIcons;total.panels+=status.panels;
            total.metricCount+=status.metricCount;total.patchedProperties+=status.patchedProperties;
        }
        claribar::WriteStable(mapping->page->statusSequence,mapping->page->status,total);
        if(!dump.empty()) {
            std::wstring all;for(auto& [id,text]:dumps)all+=L"UI thread "+std::to_wstring(id)+L"\r\n"+text;
            InterlockedIncrement(&mapping->page->dumpSequence);
            wcsncpy_s(mapping->page->dump,all.c_str(),_TRUNCATE);
            InterlockedIncrement(&mapping->page->dumpSequence);
        }
    }
    void Tick() noexcept {
        if(busy)return;busy=true;
        struct Reset{bool& value;~Reset(){value=false;}}reset{busy};
        std::shared_ptr<Mapping> mapping;{std::lock_guard lock(globalGate);mapping=activeMapping;}
        if(!mapping)return;
        ExplorerStatus status{};status.connected=1;status.explorerPid=GetCurrentProcessId();status.tick=GetTickCount64();
        try {
            ExplorerConfig config{};
            if(!claribar::ReadStable(mapping->page->configSequence,mapping->page->config,config))return;
            auto heartbeat=static_cast<ULONGLONG>(InterlockedCompareExchange64(&mapping->page->heartbeat,0,0));
            bool alive=heartbeat && GetTickCount64()-heartbeat<6000;
            if(lastMapping && lastMapping!=mapping){Restore();hasApplied=false;}
            lastMapping=mapping;
            if(!alive || !config.enabled) {
                Restore();status.restored=1;status.appliedRevision=InterlockedCompareExchange(&mapping->page->configSequence,0,0);
                std::wstring restoredTree;
                for(auto& weak:roots)if(auto root=weak.get())Visit(root,[&](wx::DependencyObject object,int depth){Describe(restoredTree,object,depth);});
                Publish(mapping,status,restoredTree);if(!alive)timer.Stop();return;
            }
            if(config.backgroundMode==0)Restore(0x10000);
            for(auto flag:{claribar::TimeColor,claribar::DateColor,claribar::TrayColor,claribar::NetworkColor,claribar::VolumeColor,claribar::InputColor,claribar::BatteryColor})
                if(!(config.colorFlags&flag))Restore(flag);
            if(!config.embedded){for(auto& item:embedded)item->Remove();embedded.clear();}
            std::vector<wx::DependencyObject> trayRoots;
            std::wstring dump;
            bool writeDump=(ticks++%15)==0;
            for(auto it=roots.begin();it!=roots.end();) {
                auto root=it->get();if(!root){it=roots.erase(it);continue;}++it;
                Visit(root,[&](wx::DependencyObject object,int depth){
                    auto type=Type(object),name=Name(object);
                    bool taskbar=IsTaskbar(object);
                    if(writeDump)Describe(dump,object,depth);
                    if(name.rfind(L"ClariBar",0)==0)return;
                    if(type==L"Taskbar.TaskbarFrame")status.frames++;
                    if(type==L"SystemTray.SystemTrayFrame")trayRoots.push_back(object);
                    if(!taskbar)return;
                    if((name==L"BackgroundFill" || name==L"BackgroundStroke") && Within(object,L"TaskbarBackground")) {
                        if(auto shape=object.try_as<wx::Shapes::Shape>()) {
                            status.backgrounds++;
                            if(config.backgroundMode)Set(object,wx::Shapes::Shape::FillProperty(),Brush(name==L"BackgroundStroke"?0:config.backgroundArgb),0x10000);
                        }
                    }
                    uint32_t group=0,color{};
                    if(name==L"TimeInnerTextBlock"){status.times++;group=claribar::TimeColor;color=config.timeArgb;}
                    else if(name==L"DateInnerTextBlock"){status.dates++;group=claribar::DateColor;color=config.dateArgb;}
                    else if(Within(object,L"SystemTray") && (object.try_as<wc::TextBlock>() || object.try_as<wc::FontIcon>() || object.try_as<wx::Shapes::Path>())) {
                        if(Within(object,L"DateTimeIconContent"))return;
                        status.systemIcons++;
                        std::wstring context=type+L" "+name;
                        auto ancestor=Parent(object);for(int j=0;ancestor && j<10;j++,ancestor=Parent(ancestor)) {
                            context+=L" "+Type(ancestor)+L" "+Name(ancestor);
                            auto ui=ancestor.try_as<wx::UIElement>();if(ui)context+=L" "+std::wstring(wx::Automation::AutomationProperties::GetName(ui));
                        }
                        if(context.find(L"Network")!=std::wstring::npos || context.find(L"网络")!=std::wstring::npos){status.networks++;group=claribar::NetworkColor;color=config.networkArgb;}
                        else if(context.find(L"Volume")!=std::wstring::npos || context.find(L"音量")!=std::wstring::npos || context.find(L"声音")!=std::wstring::npos){status.volumes++;group=claribar::VolumeColor;color=config.volumeArgb;}
                        else if(context.find(L"Language")!=std::wstring::npos || context.find(L"InputIndicator")!=std::wstring::npos || context.find(L"输入")!=std::wstring::npos){status.inputs++;group=claribar::InputColor;color=config.inputArgb;}
                        else if(context.find(L"Battery")!=std::wstring::npos || context.find(L"电池")!=std::wstring::npos){status.batteries++;group=claribar::BatteryColor;color=config.batteryArgb;}
                        if(!(config.colorFlags&group) && (config.colorFlags&claribar::TrayColor)){group=claribar::TrayColor;color=config.trayArgb;}
                    }
                    if(type.find(L"Image")!=std::wstring::npos && Within(object,L"NotifyIconView"))status.bitmapIcons++;
                    if(group && (config.colorFlags&group)) {
                        if(object.try_as<wc::TextBlock>())Set(object,wc::TextBlock::ForegroundProperty(),Brush(color),group);
                        else if(object.try_as<wc::FontIcon>())Set(object,wc::IconElement::ForegroundProperty(),Brush(color),group);
                        else if(object.try_as<wx::Shapes::Path>())Set(object,wx::Shapes::Shape::FillProperty(),Brush(color),group);
                    }
                });
            }
            if(config.embedded) {
                for(auto it=embedded.begin();it!=embedded.end();){if(!(*it)->tray.get() || !(*it)->panel.XamlRoot()){(*it)->Remove();it=embedded.erase(it);}else ++it;}
                for(auto tray:trayRoots) {
                    auto found=std::find_if(embedded.begin(),embedded.end(),[&](auto& entry){return entry->tray.get()==tray && entry->host && entry->panel && wm::VisualTreeHelper::GetParent(entry->panel)==entry->host;});
                    if(found==embedded.end()) {auto panel=std::make_unique<Embedded>();if(panel->Insert(tray))embedded.push_back(std::move(panel));}
                }
                for(auto& panel:embedded){panel->Update(config);status.panels++;status.metricCount+=static_cast<uint32_t>(panel->texts.size());}
            }
            patches.erase(std::remove_if(patches.begin(),patches.end(),[](auto& patch){return !patch.object.get();}),patches.end());
            status.patchedProperties=static_cast<uint32_t>(patches.size());status.appliedRevision=InterlockedCompareExchange(&mapping->page->configSequence,0,0);
            status.restored=patches.empty() && embedded.empty();hasApplied=!status.restored;
            Publish(mapping,status,dump);
        }catch(...){status.lastError=winrt::to_hresult();Restore();status.restored=1;Publish(mapping,status,L"");}
    }
};
thread_local std::shared_ptr<ThreadState> threadState;

struct Watcher:winrt::implements<Watcher,IVisualTreeServiceCallback2,winrt::non_agile> {
    winrt::com_ptr<IXamlDiagnostics> diagnostics;
    explicit Watcher(winrt::com_ptr<IXamlDiagnostics> value):diagnostics(std::move(value)){}
    HRESULT __stdcall OnVisualTreeChange(ParentChildRelation,VisualElement element,VisualMutationType mutation) noexcept override {
        try {
            if(mutation!=Add)return S_OK;
            std::wstring type=element.Type?element.Type:L"";
            static std::atomic<unsigned> seen{};if(seen++<8 || type.find(L"TaskbarFrame")!=std::wstring::npos)BootstrapLog(L"Visual "+type);
            if(type.find(L"TaskbarFrame")==std::wstring::npos && type!=L"SystemTray.SystemTrayFrame")return S_OK;
            wf::IInspectable value{nullptr};if(FAILED(diagnostics->GetIInspectableFromHandle(element.Handle,reinterpret_cast<::IInspectable**>(winrt::put_abi(value)))))return S_OK;
            auto object=value.try_as<wx::DependencyObject>();if(!object)return S_OK;
            if(!threadState)threadState=std::make_shared<ThreadState>();
            threadState->Track(object);threadState->Start();
        }catch(...){}
        return S_OK;
    }
    HRESULT __stdcall OnElementStateChanged(InstanceHandle,VisualElementState,LPCWSTR) noexcept override{return S_OK;}
};
winrt::com_ptr<Watcher> watcher;
struct Tap:winrt::implements<Tap,IObjectWithSite,winrt::non_agile> {
    winrt::com_ptr<IUnknown> site;
    HRESULT __stdcall SetSite(IUnknown* value) noexcept override {
        try {
            BootstrapLog(L"SetSite");
            site.copy_from(value);if(!site)return S_OK;
            auto diagnostics=site.as<IXamlDiagnostics>();BSTR data{};
            winrt::check_hresult(diagnostics->GetInitializationData(&data));
            std::wstring name=data?data:L"";SysFreeString(data);
            BootstrapLog(L"Initialization data "+name);
            if(name.rfind(L"Local\\ClariBar.Xaml.",0)!=0)return E_INVALIDARG;
            auto session=std::make_shared<Mapping>();session->handle=OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,name.c_str());
            if(!session->handle)return HRESULT_FROM_WIN32(GetLastError());
            session->page=static_cast<claribar::SharedPage*>(MapViewOfFile(session->handle,FILE_MAP_ALL_ACCESS,0,0,0));
            if(!session->page || session->page->magic!=claribar::protocolMagic || session->page->version!=claribar::protocolVersion || session->page->bytes!=sizeof(claribar::SharedPage))return E_INVALIDARG;
            {std::lock_guard lock(globalGate);activeMapping=session;statuses.clear();dumps.clear();}
            // Keep code resident while XAML callbacks/timers exist. Disable restores all
            // visual state; Windows unloads this inert module when Explorer exits.
            HMODULE keep{};GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_PIN,reinterpret_cast<LPCWSTR>(&tapClsidDummy),&keep);
            auto next=winrt::make_self<Watcher>(diagnostics);
            auto previous=watcher;watcher=next;
            std::thread([next,previous] {
                CoInitializeEx(nullptr,COINIT_MULTITHREADED);
                BootstrapLog(L"Advising visual tree");
                if(previous){try{previous->diagnostics.as<IVisualTreeService3>()->UnadviseVisualTreeChange(previous.get());}catch(...){}}
                HRESULT hr=next->diagnostics.as<IVisualTreeService3>()->AdviseVisualTreeChange(next.get());advised=SUCCEEDED(hr);
                BootstrapLog(L"Advise result "+std::to_wstring(hr));
                if(FAILED(hr)){std::lock_guard lock(globalGate);if(activeMapping)activeMapping->page->status.lastError=hr;}
                CoUninitialize();
            }).detach();
            return S_OK;
        }catch(...){auto hr=winrt::to_hresult();BootstrapLog(L"SetSite error "+std::to_wstring(hr));return hr;}
    }
    static void tapClsidDummy(){}
    HRESULT __stdcall GetSite(REFIID iid,void** result) noexcept override {return site?site->QueryInterface(iid,result):E_NOINTERFACE;}
};
struct Factory:winrt::implements<Factory,IClassFactory> {
    HRESULT __stdcall CreateInstance(IUnknown* outer,REFIID iid,void** result) noexcept override {
        if(!result)return E_POINTER;*result=nullptr;if(outer)return CLASS_E_NOAGGREGATION;
        try{return winrt::make<Tap>().as(iid,result);}catch(...){return winrt::to_hresult();}
    }
    HRESULT __stdcall LockServer(BOOL) noexcept override {return S_OK;}
};
}
STDAPI DllGetClassObject(REFCLSID clsid,REFIID iid,void** result) {
    if(!result)return E_POINTER;*result=nullptr;if(clsid!=claribar::tapClsid)return CLASS_E_CLASSNOTAVAILABLE;
    try{return winrt::make<Factory>().as(iid,result);}catch(...){return winrt::to_hresult();}
}
STDAPI DllCanUnloadNow() {return S_FALSE;}
