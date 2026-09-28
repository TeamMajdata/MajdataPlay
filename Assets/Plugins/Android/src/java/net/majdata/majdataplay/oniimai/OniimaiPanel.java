package net.majdata.majdataplay.oniimai;

import android.app.*;
import android.content.SharedPreferences;
import android.graphics.Color;
import android.graphics.drawable.ColorDrawable;
import android.os.SystemClock;
import android.view.*;
import android.widget.*;
import java.util.*;

/** Game-styled, task-oriented settings. Every popup keeps physical gameplay input gated. */
final class OniimaiPanel {
    private final OniimaiController owner;
    private final Activity activity;
    private final SharedPreferences prefs;
    private Dialog dialog, learningDialog;
    private InitialSetup setup;
    private TextView entry, statusText, notice;
    private LinearLayout settingsBody;
    private ScrollView settingsScroll;
    private final TextView[] categoryTabs=new TextView[4];
    private final ArrayList<View> settingControls=new ArrayList<>();
    private final int[] scrollPositions=new int[4];
    private final ArrayList<Setting> settings=new ArrayList<>();
    private String displayed="";
    private final int[] positions=new int[4];
    private int tab, dialogs, epoch;
    private String lastStatus="";
    private boolean entryWanted;
    private final Set<Dialog> openDialogs=new HashSet<>();
    private final ArrayList<Runnable> bindings=new ArrayList<>();
    private final Runnable update=new Runnable(){ public void run(){
        if(dialog==null)return;
        long[] raw=owner.state.diagnostic(SystemClock.uptimeMillis());
        String feedback=owner.sensitivityBusy()?UiText.message(owner.status):owner.connectionDescription();
        GameUi.setText(statusText,feedback+"  ·  "+owner.leds.brief());
        statusText.setTextColor(feedback.contains("실패")||feedback.contains("오류")||feedback.contains("失败")||feedback.contains("错误")?GameUi.ERROR:GameUi.INK);
        for(View control:settingControls)GameUi.enabled(control,!owner.uiBusy());
        for(Runnable binding:new ArrayList<>(bindings))binding.run();
        refreshSetting();
        if(!lastStatus.equals(owner.status)){lastStatus=owner.status;if(lastStatus.startsWith("감도")||lastStatus.startsWith("컨트롤러에 감도"))flash(UiText.message(lastStatus));}
        if(learningDialog!=null&&owner.learning<0){ learningDialog.dismiss(); flash(tr("버튼을 저장했습니다","已保存按钮映射")); }
        owner.main.postDelayed(this,200);
    }};
    OniimaiPanel(OniimaiController owner){this.owner=owner;activity=owner.activity;prefs=owner.prefs;tab=Math.max(0,Math.min(3,prefs.getInt("uiCategory",0)));for(int i=0;i<4;i++){positions[i]=Math.max(0,prefs.getInt("uiItem"+i,0));scrollPositions[i]=Math.max(0,prefs.getInt("uiScroll"+i,0));}}
    String tr(String ko,String zh){return GameUi.tr(ko,zh);}
    boolean isOpen(){return dialogs>0;}
    void entry(boolean visible){
        entryWanted=visible;
        if(owner.display.active()){if(entry!=null)entry.setVisibility(View.GONE);return;}
        if(entry==null&&visible){
            entry=GameUi.action(activity,"Oniimai · USB / Display",true,this::open);
            FrameLayout.LayoutParams lp=new FrameLayout.LayoutParams(-2,dp(48),Gravity.TOP|Gravity.CENTER_HORIZONTAL);lp.topMargin=dp(18);activity.addContentView(entry,lp);
        }
        if(entry!=null)entry.setVisibility(visible?View.VISIBLE:View.GONE);
        if(!visible&&dialog!=null&&setup==null)closeAll();
    }
    void refreshEntry(){entry(entryWanted);}
    void textConfigurationChanged(){boolean wasSetup=setup!=null,wasOpen=dialog!=null;int selected=tab;closeAll();if(entry!=null){((ViewGroup)entry.getParent()).removeView(entry);entry=null;}refreshEntry();if(wasSetup)owner.main.post(this::openSetup);else if(wasOpen)owner.main.post(()->open(selected));}
    void openSetup(){if(setup==null){setup=new InitialSetup(owner,this);setup.show();}}
    void setupClosed(InitialSetup closed){if(setup==closed)setup=null;}
    void open(){open(tab);}
    void open(int section){
        if(dialog!=null){selectTab(section);return;}
        tab=Math.max(0,Math.min(3,section));owner.refresh();
        LinearLayout root=column();root.setBackgroundColor(GameUi.PAGE);
        if(android.os.Build.VERSION.SDK_INT>=30)root.setOnApplyWindowInsetsListener((v,insets)->{android.graphics.Insets safe=insets.getInsets(WindowInsets.Type.systemBars()|WindowInsets.Type.displayCutout());root.setPadding(safe.left,safe.top,safe.right,safe.bottom);return insets;});
        LinearLayout header=column();header.setPadding(dp(18),dp(12),dp(18),dp(12));
        LinearLayout navigation=new LinearLayout(activity);navigation.setGravity(Gravity.CENTER_VERTICAL);
        TextView back=GameUi.action(activity,tr("‹  뒤로","‹  返回"),false,()->{if(dialog!=null)dialog.dismiss();});navigation.addView(back);
        TextView title=GameUi.text(activity,tr("컨트롤러 설정","控制器设置"),21,GameUi.INK,true);title.setPadding(dp(14),0,0,0);navigation.addView(title,new LinearLayout.LayoutParams(0,-2,1));header.addView(navigation);
        statusText=GameUi.text(activity,"",12,GameUi.MUTED,false);statusText.setPadding(0,dp(16),0,dp(8));header.addView(statusText);
        LinearLayout footer=new LinearLayout(activity);footer.setGravity(Gravity.CENTER_VERTICAL);
        notice=GameUi.text(activity,tr("변경사항은 자동 저장됩니다","更改会自动保存"),12,GameUi.MUTED,false);footer.addView(notice,new LinearLayout.LayoutParams(0,-2,1));
        TextView language=GameUi.action(activity,"한국어 / 中文",false,this::language);language.setTextSize(12);footer.addView(language);header.addView(footer);GameUi.space(header,14);
        LinearLayout tabs=new LinearLayout(activity);String[] names={tr("연결","连接"),tr("입력","输入"),"LED",tr("화면","屏幕")};
        for(int i=0;i<4;i++){final int category=i;TextView button=GameUi.action(activity,names[i],false,()->selectTab(category));button.setTextSize(15);button.setPadding(dp(6),dp(12),dp(6),dp(12));categoryTabs[i]=button;LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(0,-2,1);if(i>0)lp.leftMargin=dp(6);tabs.addView(button,lp);}
        header.addView(tabs);root.addView(header);
        settingsScroll=new ScrollView(activity);settingsScroll.setFillViewport(false);settingsScroll.setVerticalScrollBarEnabled(false);
        settingsBody=column();settingsBody.setPadding(dp(18),0,dp(18),dp(20));settingsScroll.addView(settingsBody);root.addView(settingsScroll,new LinearLayout.LayoutParams(-1,0,1));
        Dialog d=new Dialog(activity,android.R.style.Theme_Material_Light_NoActionBar);dialog=d;d.setContentView(root);
        protect(d,()->{saveScroll();epoch++;dialog=null;owner.learning=-1;learningDialog=null;bindings.clear();settingControls.clear();owner.main.removeCallbacks(update);});
        show(d,true);root.requestApplyInsets();render();owner.main.post(update);
    }
    private void saveScroll(){if(settingsScroll!=null){scrollPositions[tab]=settingsScroll.getScrollY();prefs.edit().putInt("uiScroll"+tab,scrollPositions[tab]).apply();}}
    void language(){
        choose(tr("언어","语言"),new String[]{"한국어","简体中文"},"zh-Hans".equals(UiText.language())?1:0,i->{
            UiText.language(i==1?"zh-Hans":"ko");prefs.edit().putString("language",UiText.language()).apply();
            boolean wasSetup=setup!=null;int selected=tab;closeAll();owner.main.post(()->{owner.display.refreshLanguage();if(wasSetup)openSetup();else open(selected);});
        });
    }
    private String connectionSummary(long[] raw){
        String touch=(raw[3]&1)!=0?tr("터치 연결됨","触摸已连接"):tr("터치 대기","等待触摸");
        String buttons=(raw[3]&2)!=0?tr("버튼 연결됨","按钮已连接"):tr("버튼 대기","等待按钮");
        return touch+"  ·  "+buttons+"  ·  LED "+(owner.leds.running()?"ON":"OFF");
    }
    private void selectTab(int value){if(value==tab)return;saveScroll();epoch++;tab=value;owner.learning=-1;render();}
    private void render(){
        if(settingsBody==null)return;settings.clear();bindings.clear();settingControls.clear();populate();positions[tab]=Math.min(positions[tab],settings.size()-1);settingsBody.removeAllViews();
        for(int i=0;i<4;i++){boolean selected=i==tab;categoryTabs[i].setTextColor(selected?Color.WHITE:GameUi.INK);categoryTabs[i].setBackground(GameUi.ripple(activity,selected?GameUi.TAB:0xffe6eaed,14));categoryTabs[i].setSelected(selected);}
        LinearLayout group=column();GameUi.surface(group);group.setPadding(dp(16),dp(2),dp(16),dp(2));settingsBody.addView(group);
        for(int i=0;i<settings.size();i++){
            final int index=i;Setting item=settings.get(i);if(i>0)GameUi.divider(group);
            LinearLayout row=new LinearLayout(activity);row.setGravity(Gravity.CENTER_VERTICAL);row.setMinimumHeight(dp(76));row.setPadding(0,dp(14),0,dp(14));
            LinearLayout words=column();TextView name=GameUi.text(activity,item.name,16,GameUi.INK,true);words.addView(name);GameUi.space(words,5);
            TextView detail=GameUi.text(activity,item.toggle?item.hint:item.value.get(),13,GameUi.MUTED,false);words.addView(detail);row.addView(words,new LinearLayout.LayoutParams(0,-2,1));
            Runnable activate=()->{if(owner.uiBusy())return;epoch++;positions[tab]=index;item.action.run();refreshSetting();};
            if(item.toggle){
                Switch toggle=new Switch(new ContextThemeWrapper(activity,android.R.style.Theme_Material_Light_NoActionBar));toggle.setShowText(false);toggle.setChecked("ON".equals(item.value.get()));toggle.setMinHeight(dp(48));toggle.setContentDescription(item.name);toggle.setThumbTintList(new android.content.res.ColorStateList(new int[][]{{android.R.attr.state_checked},{}},new int[]{GameUi.BLUE,0xffa8b5c9}));toggle.setTrackTintList(new android.content.res.ColorStateList(new int[][]{{android.R.attr.state_checked},{}},new int[]{0xffc2dcf6,GameUi.LINE}));toggle.setOnClickListener(v->activate.run());row.addView(toggle);settingControls.add(toggle);bindings.add(()->toggle.setChecked("ON".equals(item.value.get())));
                row.setOnClickListener(v->activate.run());row.setBackground(GameUi.ripple(activity,Color.TRANSPARENT,12));settingControls.add(row);
            }else{
                detail.setTextColor(GameUi.BLUE);bindings.add(()->GameUi.setText(detail,item.value.get()));TextView arrow=GameUi.text(activity,"›",25,GameUi.MUTED,false);arrow.setGravity(Gravity.CENTER);row.addView(arrow,new LinearLayout.LayoutParams(dp(32),dp(48)));row.setBackground(GameUi.ripple(activity,Color.TRANSPARENT,12));row.setOnClickListener(v->activate.run());GameUi.buttonRole(row);settingControls.add(row);
            }
            group.addView(row,new LinearLayout.LayoutParams(-1,-2));
        }
        displayed="";refreshSetting();int section=tab,revision=epoch;settingsScroll.post(()->{if(dialog!=null&&tab==section&&epoch==revision)settingsScroll.scrollTo(0,scrollPositions[section]);});GameUi.appear(group);
    }
    private void rebuild(){render();}
    private static final class Setting {
        final String name,hint;final Value value;final Runnable action;final java.util.function.IntConsumer adjust;final boolean toggle;
        Setting(String n,Value v,String h,Runnable a,java.util.function.IntConsumer d){this(n,v,h,a,d,false);}
        Setting(String n,Value v,String h,Runnable a,java.util.function.IntConsumer d,boolean t){name=n;value=v;hint=h;action=a;adjust=d;toggle=t;}
    }
    private Setting current(){return settings.get(positions[tab]);}
    private void item(String name,Value value,String hint,Runnable action){settings.add(new Setting(name,value,hint,action,null));}
    private void flag(String name,String key,boolean fallback,String hint,Toggle action){
        java.util.function.IntConsumer change=n->{boolean value=n>0;prefs.edit().putBoolean(key,value).apply();action.accept(value);};
        settings.add(new Setting(name,()->prefs.getBoolean(key,fallback)?"ON":"OFF",hint,()->change.accept(prefs.getBoolean(key,fallback)?-1:1),change,true));
    }
    private void numberItem(String name,String key,int fallback,int min,int max,int step,String suffix,String hint,Runnable action){
        java.util.function.IntConsumer change=n->{int value=Math.max(min,Math.min(max,prefs.getInt(key,fallback)+n*step));prefs.edit().putInt(key,value).apply();action.run();};
        settings.add(new Setting(name,()->prefs.getInt(key,fallback)+suffix,hint,()->{LinearLayout body=column();slider(body,name,key,fallback,min,max,action);sheet(name,body,tr("완료","完成"));},change));
    }
    private void refreshSetting(){
        if(dialog==null||settings.isEmpty())return;String stamp=tab+"/"+positions[tab];if(stamp.equals(displayed))return;displayed=stamp;prefs.edit().putInt("uiCategory",tab).putInt("uiItem"+tab,positions[tab]).apply();
    }
    private void populate(){
        String pick=tr("눌러서 선택하세요","点击选择"),adjust=tr("눌러서 슬라이더로 조절하세요","点击后使用滑块调整");
        if(tab==0){
            item(tr("초기 설정","初始设置"),()->tr("연결 · 모니터 · 부가 설정","连接 · 显示器 · 更多设置"),tr("초기 설정 안내를 다시 엽니다","重新打开初始设置向导"),this::openSetup);
            item(tr("자동 연결","自动连接"),()->tr("연결하기","连接"),tr("이름으로 포트를 찾아 연결합니다","按端口名称自动查找并连接"),()->{owner.enableInput(true);owner.automaticPorts();flash(tr("컨트롤러 연결 중…","正在连接控制器…"));});
            flag(tr("컨트롤러 입력","控制器输入"),"input",false,tr("게임에 버튼과 터치 입력을 전달합니다","向游戏传送按钮和触摸输入"),owner::enableInput);
            item(tr("터치 포트","触摸端口"),()->portName("touchPort"),pick,()->ports("touchPort",false));
            item(tr("LED 포트","LED 端口"),()->portName("ledPort"),pick,()->ports("ledPort",false));
            flag(tr("다음에도 자동 연결","下次自动连接"),"autoConnect",true,tr("저장된 포트를 다음 실행에도 사용합니다","下次启动继续使用已保存端口"),v->owner.refresh());
            item(tr("USB 권한","USB 权限"),()->tr("장치 확인","查看设备"),pick,this::permissions);
            item(tr("다시 연결","重新连接"),()->tr("연결 재시도","重试连接"),tr("현재 포트로 다시 연결합니다","使用当前端口重新连接"),owner::connect);
            item(tr("연결 해제","断开连接"),()->tr("연결 끊기","断开"),tr("포트 선택은 저장된 상태로 유지됩니다","保留已保存的端口选择"),owner::disconnect);
            item(tr("연결 상태","连接状态"),owner::connectionDescription,tr("눌러서 상세 정보를 확인하세요","点击查看详细信息"),this::details);
        }else if(tab==1){
            item(tr("실시간 센서","实时传感器"),()->"34 TOUCH",tr("눌러서 원형 센서와 P1 입력 확인","点击检查圆形传感器与 P1 输入"),()->{LinearLayout body=column();body.addView(new SensorBoard(activity,owner.state),new LinearLayout.LayoutParams(-1,-2));sheet(tr("실시간 입력","实时输入"),body,tr("닫기","关闭"));});
            String[] modes={tr("사용 안 함","关闭"),"USB Keyboard","IO4 HID"};
            item(tr("버튼 입력 방식","按钮输入方式"),()->modes[Math.max(0,Math.min(2,prefs.getInt("buttonMode",2)))],pick,()->choose(tr("버튼 입력 방식","按钮输入方式"),modes,prefs.getInt("buttonMode",2),i->{prefs.edit().putInt("buttonMode",i).apply();owner.configurationChanged();rebuild();}));
            if(prefs.getInt("buttonMode",2)==1){
                item(tr("버튼 등록","映射按钮"),()->"A1 – A8",tr("실제 버튼을 눌러 키를 등록합니다","按实体按钮录入按键"),this::learn);
                item(tr("P1 버튼 등록","映射 P1 按钮"),()->keyName(owner.keyboard.map[8]),pick,()->learnKey(8));
            }else if(prefs.getInt("buttonMode",2)==2){
                item("IO4 HID",()->portName("hidPort"),pick,()->ports("hidPort",true));
                item(tr("플레이어 뱅크","玩家分组"),()->prefs.getInt("bank",0)==0?"P1":"P2",pick,()->choose("IO4",new String[]{"P1","P2"},prefs.getInt("bank",0),i->{prefs.edit().putInt("bank",i).apply();owner.configurationChanged();}));
            }
            String[] actions={tr("정렬 · 검색","排序 · 搜索"),tr("시작 · 확인","开始 · 确认")};
            item(tr("P1 동작","P1 功能"),()->actions[prefs.getBoolean("p1Start",false)?1:0],pick,()->choose("P1 / START",actions,prefs.getBoolean("p1Start",false)?1:0,i->prefs.edit().putBoolean("p1Start",i==1).apply()));
            String[] protocols={"Command · 115200","Mai2 Touch · 9600"};
            item(tr("터치 프로토콜","触摸协议"),()->protocols[prefs.getInt("touchMode",1)==1?1:0],pick,()->choose(tr("터치 프로토콜","触摸协议"),protocols,prefs.getInt("touchMode",1),i->{prefs.edit().putInt("touchMode",i).apply();owner.configurationChanged();}));
            item(tr("터치 감도","触摸灵敏度"),()->tr("센서 선택","选择传感器"),tr("값을 읽고 저장해야 장치에 적용됩니다","读取数值后需点击保存才写入设备"),this::sensitivity);
        }else if(tab==2){
            flag(tr("LED 연동","LED 联动"),"led",true,tr("게임의 조명 색상을 그대로 전달합니다","同步游戏的灯光颜色"),v->{owner.updateLedSettings();if(v)owner.startLeds();else owner.leds.stop();});
            flag(tr("상단 스피커 RGB","顶部扬声器 RGB"),"ceiling",true,tr("IO4 연결 · 게임 버튼 조명의 평균 색상을 표시합니다","连接 IO4 · 显示游戏按钮灯光的平均颜色"),v->owner.updateLedSettings());
            item(tr("스피커 조명 상태","扬声器灯光状态"),owner.ceiling::summary,tr("버튼 입력 방식을 IO4 HID로 연결하세요","请使用 IO4 HID 连接按钮输入"),()->flash(owner.ceiling.summary()));
            item(tr("스피커 색상 테스트","扬声器颜色测试"),()->"R · G · B",tr("2초 후 게임 조명으로 복귀합니다","2 秒后恢复游戏灯光"),()->choose(tr("스피커 색상 테스트","扬声器颜色测试"),new String[]{tr("빨강","红色"),tr("초록","绿色"),tr("파랑","蓝色")},-1,i->{if(prefs.getBoolean("led",true)&&prefs.getBoolean("ceiling",true))owner.ceiling.test(new int[]{0xff0000,0x00ff00,0x0000ff}[i]);else flash(tr("LED 연결을 먼저 켜세요","请先连接并启用 LED"));}));
            numberItem(tr("밝기","亮度"),"brightness",100,0,100,5,"%",adjust,owner::updateLedSettings);
            numberItem(tr("LED 회전","LED 旋转"),"ledRotation",0,0,7,1,"",tr("버튼 단위로 이동 · 0 = 기본 방향","按按钮步进 · 0 = 默认方向"),owner::updateLedSettings);
            flag(tr("순서 반전","反转顺序"),"ledReverse",false,tr("LED 순서를 반대로 바꿉니다","反转 LED 排列顺序"),v->owner.updateLedSettings());
            flag(tr("캐비닛 조명","机柜灯光"),"ring",false,tr("캐비닛 조명 출력을 연동합니다","同步机柜灯光输出"),v->owner.updateLedSettings());
            item(tr("색상 테스트","颜色测试"),()->"R · G · B",tr("2초 후 게임 조명으로 복귀합니다","2 秒后恢复游戏灯光"),()->choose(tr("색상 테스트","颜色测试"),new String[]{tr("빨강","红色"),tr("초록","绿色"),tr("파랑","蓝色")},-1,i->{if(owner.leds.running())owner.leds.test(new int[]{0xff0000,0x00ff00,0x0000ff}[i]);else flash(tr("LED 연결을 먼저 켜세요","请先连接并启用 LED"));}));
            item(tr("LED 포트","LED 端口"),()->portName("ledPort"),pick,()->ports("ledPort",false));
            item(tr("노드 주소","节点地址"),()->prefs.getInt("address",17)+" / "+prefs.getInt("base",0),pick,this::ledAddress);
        }else{
            flag(tr("외부 모니터 출력","外接屏幕输出"),"external",true,tr("폰에는 스탯 · 모니터에는 게임 출력","手机显示统计 · 显示器输出游戏"),v->owner.display.reconcile());
            item(tr("프레임 고정","固定帧率"),owner.display::frameRateDescription,tr("기본 60fps · 120fps는 지원되는 120Hz 모니터에서 사용","默认 60fps · 120fps 需支持 120Hz 的显示器"),()->owner.display.chooseFrameRate(this));
            item(tr("폰 대시보드 · 위젯 편집","手机仪表盘 · 编辑小组件"),()->tr("8종 위젯 · 이동 · 크기 · 저장","8 种小组件 · 移动 · 大小 · 保存"),tr("모니터 없이도 배치를 편집할 수 있습니다","无需显示器也可编辑布局"),owner.display::previewDashboard);
            item(tr("출력 장치","输出设备"),owner.display::deviceName,pick,()->owner.display.chooseMonitor(this));
            flag(tr("역방향 세로","反向竖屏"),"displayReverse",false,tr("OFF = 90°  ·  ON = 270°","OFF = 90°  ·  ON = 270°"),v->owner.display.reconcile());
            item(tr("게임 해상도","游戏分辨率"),()->"1080 × 1920",tr("세로 9:16 → 가로 16:9 화면 채우기","竖屏 9:16 → 铺满横屏 16:9"),()->{LinearLayout body=column();body.addView(new DisplayPreview(activity,()->prefs.getBoolean("displayReverse",false)),new LinearLayout.LayoutParams(-1,-2));text(body,owner.display.outputDescription());sheet(tr("외부 출력","外接输出"),body,tr("닫기","关闭"));});
            item(tr("출력 상태","输出状态"),owner.display::outputDescription,tr("눌러서 해상도와 주사율 확인","点击查看分辨率与刷新率"),()->info(tr("출력 정보","输出信息"),owner.display.outputDescription()+"\n\n"+tr("HDMI 해상도와 주사율은 폰·허브·모니터의 지원 모드를 사용합니다.","HDMI 分辨率和刷新率取决于手机、扩展坞和显示器支持的模式。")));
        }
    }
    String portName(String key){
        String id=prefs.getString(key,"");if(id.isEmpty())return tr("선택 안 됨","尚未选择");
        for(UsbIo.Port p:owner.ports)if(PortSelection.samePort(id,OniimaiController.portId(p)))return (p.hid?"IO4 HID":p.name)+" · IF"+p.controlId;
        return prefs.getString(key+"Name",tr("저장된 포트","已保存端口"))+" · "+tr("연결 대기","等待连接");
    }
    void permissions(){
        owner.refresh();List<android.hardware.usb.UsbDevice> devices=new ArrayList<>(owner.usb.getDeviceList().values());
        if(devices.isEmpty()){info(tr("USB 장치를 찾지 못했습니다","未找到 USB 设备"),tr("컨트롤러와 OTG 허브를 연결한 뒤 다시 시도하세요.","请连接控制器和 OTG 扩展坞后重试。"));return;}
        String[] labels=new String[devices.size()];for(int i=0;i<labels.length;i++){android.hardware.usb.UsbDevice d=devices.get(i);labels[i]=d.getProductName()+"\n"+(owner.usb.hasPermission(d)?tr("권한 허용됨","已获授权"):tr("눌러서 USB 권한 허용","点击授予 USB 权限"));}
        choose(tr("USB 장치","USB 设备"),labels,-1,i->{owner.permission(devices.get(i));owner.refresh();});
    }
    void ports(String key,boolean hid){
        List<UsbIo.Port> options=new ArrayList<>();for(UsbIo.Port p:owner.ports)if(p.hid==hid)options.add(p);
        String[] labels=new String[options.size()+1];labels[0]=tr("사용 안 함","关闭");int selected=0;
        for(int i=0;i<options.size();i++){UsbIo.Port p=options.get(i);labels[i+1]=p.name+"\nIF"+p.controlId+" · "+p.device.getProductName();if(PortSelection.samePort(prefs.getString(key,""),OniimaiController.portId(p)))selected=i+1;}
        choose(key.equals("touchPort")?tr("터치 포트","触摸端口"):key.equals("ledPort")?tr("LED 포트","LED 端口"):"IO4 HID",labels,selected,i->{owner.selectPort(key,i==0?null:options.get(i-1));rebuild();});
    }
    private String keyName(int code){return code<0?tr("등록 안 됨","未映射"):KeyEvent.keyCodeToString(code).replace("KEYCODE_","");}
    private void learn(){String[] labels=new String[8];synchronized(owner.keyboard){for(int i=0;i<8;i++)labels[i]="A"+(i+1)+"   ·   "+keyName(owner.keyboard.map[i]);}choose(tr("등록할 버튼","要映射的按钮"),labels,-1,this::learnKey);}
    private void learnKey(int index){
        owner.learning=index;LinearLayout body=column();TextView symbol=GameUi.text(activity,index==8?"P1":"A"+(index+1),44,GameUi.BLUE,true);symbol.setGravity(Gravity.CENTER);symbol.setPadding(0,dp(24),0,dp(24));body.addView(symbol);
        text(body,tr("컨트롤러의 해당 버튼을 한 번 누르세요.","请按一次控制器上的对应按钮。"));text(body,tr("등록 후 자동 저장됩니다. 게임에는 입력되지 않습니다.","映射后自动保存。不会向游戏发送此输入。"));
        Dialog d=sheet(tr("버튼 등록","映射按钮"),body,tr("취소","取消"));learningDialog=d;d.setOnCancelListener(which->owner.learning=-1);
    }
    private void sensitivity(){
        String[] zones=Arrays.copyOf(Protocol.ZONES,35);zones[34]=tr("전체 34개 센서","全部 34 个传感器");
        choose(tr("감도를 읽을 센서","选择要读取的传感器"),zones,-1,zone->{final int requestedEpoch=epoch;flash(tr("컨트롤러에서 감도를 읽는 중…","正在读取控制器灵敏度…"));owner.sensitivity(zone,config->{
            if(dialog==null||epoch!=requestedEpoch)return;LinearLayout body=column();text(body,zones[zone]);draftNotice(body);int index=Math.min(zone,33);
            if(zone==34)text(body,tr("초기값은 E8 기준입니다. 저장하면 모든 센서에 같은 값이 적용됩니다.","初始值来自 E8。保存后所有传感器将使用相同数值。"));
            EditText finger=numberDraft(body,"sensitivity."+zone+".finger","Finger",config.finger(index),0,512),noise=numberDraft(body,"sensitivity."+zone+".noise","Noise",config.noise(index),0,255),hyst=numberDraft(body,"sensitivity."+zone+".hyst","Hysteresis",config.hysteresis(index),0,128);
            final Dialog[] holder=new Dialog[1];button(body,tr("컨트롤러에 저장","保存到控制器"),()->{try{int f=GameUi.readNumber(finger,0,512),n=GameUi.readNumber(noise,0,255),h=GameUi.readNumber(hyst,0,128);if(owner.sensitivityBusy())return;owner.saveSensitivity(zone,f,n,h,()->clearDraft("sensitivity."+zone+"."));holder[0].dismiss();flash(tr("저장 후 장치 값을 검증합니다…","保存后将验证设备数值…"));}catch(NumberFormatException ignored){}},true);
            holder[0]=sheet(tr("터치 감도","触摸灵敏度"),body,tr("닫기","关闭"));
        });});
    }
    private void ledAddress(){
        LinearLayout body=column();draftNotice(body);text(body,tr("기본값: 노드 17 · 시작 번호 0","默认值：节点 17 · 起始编号 0"));
        EditText address=numberDraft(body,"led.address",tr("노드 주소","节点地址"),prefs.getInt("address",17),1,255),base=numberDraft(body,"led.base",tr("시작 번호","起始编号"),prefs.getInt("base",0),0,24);
        final Dialog[] holder=new Dialog[1];button(body,tr("저장하고 다시 연결","保存并重新连接"),()->{try{int a=GameUi.readNumber(address,1,255),b=GameUi.readNumber(base,0,24);owner.leds.stop();prefs.edit().putInt("address",a).putInt("base",b).apply();if(prefs.getBoolean("led",true))owner.startLeds();clearDraft("led.");holder[0].dismiss();rebuild();}catch(NumberFormatException ignored){}},true);
        holder[0]=sheet(tr("LED 주소","LED 地址"),body,tr("닫기","关闭"));
    }
    private void draftNotice(LinearLayout body){text(body,tr("작성 중인 값은 유지됩니다. 저장을 눌러야 장치에 적용됩니다.","保留未保存的输入。点击保存后才应用到设备。"));}
    private EditText numberDraft(LinearLayout body,String key,String label,int value,int min,int max){
        EditText input=GameUi.number(body,label,value,min,max);String saved=prefs.getString("draft:"+key,null);if(saved!=null)input.setText(saved);
        input.addTextChangedListener(new android.text.TextWatcher(){public void beforeTextChanged(CharSequence s,int start,int count,int after){}public void onTextChanged(CharSequence s,int start,int before,int count){}public void afterTextChanged(android.text.Editable text){prefs.edit().putString("draft:"+key,text.toString()).apply();}});
        return input;
    }
    private void clearDraft(String prefix){SharedPreferences.Editor edit=prefs.edit();for(String key:prefs.getAll().keySet())if(key.startsWith("draft:"+prefix))edit.remove(key);edit.apply();}
    private void details(){LinearLayout body=column();TextView detail=text(body,UiText.message(owner.status)+"\n\n"+owner.leds.diagnostic()+"\n"+owner.ceiling.diagnostic());detail.setTextIsSelectable(true);sheet(tr("연결 상세 정보","连接详细信息"),body,tr("닫기","关闭"));}
    void info(String title,String message){LinearLayout body=column();text(body,message);sheet(title,body,tr("확인","确定"));}
    interface Pick{void accept(int value);}interface Toggle{void accept(boolean value);}interface Value{String get();}
    void choose(String title,String[] labels,int selected,Pick action){
        if(labels.length==0){info(title,tr("연결된 장치가 없습니다. 연결 후 다시 확인하세요.","没有已连接设备，请连接后重试。"));return;}
        LinearLayout list=column();final Dialog[] holder=new Dialog[1];
        for(int i=0;i<labels.length;i++){final int index=i;TextView option=GameUi.action(activity,(i==selected?"●  ":"○  ")+labels[i],false,()->{if(holder[0]==null||!holder[0].isShowing())return;holder[0].dismiss();action.accept(index);});option.setGravity(Gravity.CENTER_VERTICAL);option.setTextSize(14);option.setTextColor(i==selected?GameUi.BLUE:GameUi.INK);option.setBackground(GameUi.ripple(activity,i==selected?GameUi.SLATE:0xeeffffff,24));option.setTextColor(i==selected?GameUi.GOLD:GameUi.INK);list.addView(option,new LinearLayout.LayoutParams(-1,-2));GameUi.space(list,4);}
        holder[0]=sheet(title,list,tr("취소","取消"));
    }
    private Dialog sheet(String title,LinearLayout contents,String dismissLabel){
        LinearLayout root=column();root.setPadding(dp(22),dp(22),dp(22),dp(18));GameUi.surface(root);
        TextView heading=GameUi.text(activity,title,21,GameUi.INK,true);root.addView(heading);GameUi.space(root,16);
        ScrollView sc=new ScrollView(activity);sc.setFillViewport(false);sc.addView(contents);root.addView(sc,new LinearLayout.LayoutParams(-1,0,1));GameUi.space(root,12);
        Dialog d=new Dialog(activity,android.R.style.Theme_Material_Light_NoActionBar);TextView dismiss=GameUi.action(activity,dismissLabel,false,d::dismiss);root.addView(dismiss);d.setContentView(root);
        protect(d,()->{if(learningDialog==d){owner.learning=-1;learningDialog=null;}});show(d,false);
        int width=Math.min(activity.getResources().getDisplayMetrics().widthPixels-dp(24),dp(540));
        contents.measure(View.MeasureSpec.makeMeasureSpec(width-dp(44),View.MeasureSpec.EXACTLY),View.MeasureSpec.makeMeasureSpec(0,View.MeasureSpec.UNSPECIFIED));
        int exact=View.MeasureSpec.makeMeasureSpec(width-dp(44),View.MeasureSpec.EXACTLY),free=View.MeasureSpec.makeMeasureSpec(0,View.MeasureSpec.UNSPECIFIED);
        heading.measure(exact,free);dismiss.measure(exact,free);
        int chrome=dp(68)+heading.getMeasuredHeight()+dismiss.getMeasuredHeight();
        int height=Math.max(dp(200),Math.min(Math.min(activity.getResources().getDisplayMetrics().heightPixels-dp(48),dp(760)),contents.getMeasuredHeight()+chrome));
        d.getWindow().setLayout(width,height);GameUi.appear(root);return d;
    }
    void show(Dialog d,boolean full){
        Window w=d.getWindow();w.setBackgroundDrawable(new ColorDrawable(Color.TRANSPARENT));w.addFlags(WindowManager.LayoutParams.FLAG_DIM_BEHIND);w.setDimAmount(.38f);w.setSoftInputMode(WindowManager.LayoutParams.SOFT_INPUT_ADJUST_RESIZE);
        w.getDecorView().setSystemUiVisibility(5894);d.show();int width=activity.getResources().getDisplayMetrics().widthPixels,height=activity.getResources().getDisplayMetrics().heightPixels;
        w.setLayout(full?WindowManager.LayoutParams.MATCH_PARENT:Math.min(width-dp(24),dp(540)),full?WindowManager.LayoutParams.MATCH_PARENT:Math.min(height-dp(48),dp(760)));
    }
    void protect(Dialog d,Runnable dismissed){
        openDialogs.add(d);dialogs++;owner.state.panel(true);d.setOnKeyListener((which,code,event)->OniimaiController.handleKey(event));
        d.setOnDismissListener(which->{if(!openDialogs.remove(d))return;dialogs--;if(dismissed!=null)dismissed.run();owner.state.panel(dialogs>0);});
    }
    LinearLayout column(){return GameUi.column(activity);}
    TextView text(LinearLayout body,String value){TextView t=GameUi.text(activity,value,13,GameUi.MUTED,false);t.setPadding(0,dp(6),0,dp(10));body.addView(t);return t;}
    void button(LinearLayout body,String label,Runnable action){button(body,label,action,false);}
    void button(LinearLayout body,String label,Runnable action,boolean primary){body.addView(GameUi.action(activity,label,primary,action),new LinearLayout.LayoutParams(-1,-2));}
    private void pair(LinearLayout body,String a,Runnable aa,String b,Runnable bb){LinearLayout row=new LinearLayout(activity);row.addView(GameUi.action(activity,a,false,aa),new LinearLayout.LayoutParams(0,-2,1));LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(0,-2,1);lp.leftMargin=dp(8);row.addView(GameUi.action(activity,b,false,bb),lp);body.addView(row);}
    void toggle(LinearLayout body,String label,String key,boolean fallback,Toggle action){GameUi.toggle(body,label,null,prefs.getBoolean(key,fallback),(v,checked)->{prefs.edit().putBoolean(key,checked).apply();action.accept(checked);});}
    void watchRow(LinearLayout body,String label,Value value,Runnable action){TextView detail=GameUi.row(body,label,value.get(),action);bindings.add(()->GameUi.setText(detail,value.get()));}
    void slider(LinearLayout body,String label,String key,int fallback,int min,int max,Runnable action){
        int value=Math.max(min,Math.min(max,prefs.getInt(key,fallback)));String suffix=key.equals("brightness")?"%":"";
        TextView title=GameUi.text(activity,label+"   "+value+suffix,14,GameUi.INK,true);title.setPadding(0,dp(10),0,0);body.addView(title);
        SeekBar seek=new SeekBar(new ContextThemeWrapper(activity,android.R.style.Theme_Material_Light_NoActionBar));seek.setProgressTintList(android.content.res.ColorStateList.valueOf(GameUi.BLUE));seek.setThumbTintList(android.content.res.ColorStateList.valueOf(GameUi.BLUE));seek.setMax(max-min);seek.setProgress(value-min);seek.setContentDescription(label);body.addView(seek,new LinearLayout.LayoutParams(-1,dp(48)));
        seek.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener(){public void onProgressChanged(SeekBar s,int p,boolean user){title.setText(label+"   "+(p+min)+suffix);if(user){prefs.edit().putInt(key,p+min).apply();action.run();}}public void onStartTrackingTouch(SeekBar s){}public void onStopTrackingTouch(SeekBar s){}});
    }
    private void flash(String message){GameUi.setText(notice,message);Toast.makeText(activity,message,Toast.LENGTH_SHORT).show();}
    int dp(int n){return GameUi.dp(activity,n);}
    void destroy(){closeAll();if(entry!=null&&entry.getParent() instanceof ViewGroup)((ViewGroup)entry.getParent()).removeView(entry);}
    private void closeAll(){for(Dialog d:new ArrayList<>(openDialogs))d.dismiss();}
}
