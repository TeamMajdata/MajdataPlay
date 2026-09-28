package net.majdata.majdataplay.oniimai;

import android.app.Dialog;
import android.content.SharedPreferences;
import android.graphics.Color;
import android.view.*;
import android.widget.*;
import java.util.*;
import java.util.function.Supplier;

/** Resumable first-launch guide; all changes use the same native controls and saved settings. */
final class InitialSetup {
    private final OniimaiController owner;
    private final OniimaiPanel ui;
    private final SharedPreferences prefs;
    private final Dialog dialog;
    private final LinearLayout body;
    private final ScrollView scroll;
    private final TextView progress,title,next,back;
    private final List<Runnable> bindings=new ArrayList<>();
    private int step;
    private boolean closed;
    private final Runnable tick=new Runnable(){public void run(){if(closed)return;for(Runnable update:new ArrayList<>(bindings))update.run();owner.main.postDelayed(this,200);}};
    InitialSetup(OniimaiController owner,OniimaiPanel ui){
        this.owner=owner;this.ui=ui;prefs=owner.prefs;step=Math.max(0,Math.min(3,prefs.getInt("setupStep",0)));
        LinearLayout root=GameUi.column(owner.activity);root.setBackgroundColor(GameUi.PAGE);
        if(android.os.Build.VERSION.SDK_INT>=30)root.setOnApplyWindowInsetsListener((v,insets)->{android.graphics.Insets safe=insets.getInsets(WindowInsets.Type.systemBars()|WindowInsets.Type.displayCutout());root.setPadding(safe.left,safe.top,safe.right,safe.bottom);return insets;});
        LinearLayout header=GameUi.column(owner.activity);header.setPadding(dp(20),dp(16),dp(20),dp(16));
        LinearLayout nav=new LinearLayout(owner.activity);nav.setGravity(Gravity.CENTER_VERTICAL);
        progress=GameUi.text(owner.activity,"",13,GameUi.BLUE,true);nav.addView(progress,new LinearLayout.LayoutParams(0,-2,1));nav.addView(GameUi.action(owner.activity,"한국어 / 中文",false,ui::language));header.addView(nav);GameUi.space(header,10);
        title=GameUi.text(owner.activity,"",25,GameUi.INK,true);header.addView(title);root.addView(header);
        scroll=new ScrollView(owner.activity);scroll.setFillViewport(false);body=GameUi.column(owner.activity);body.setPadding(dp(20),0,dp(20),dp(12));scroll.addView(body);root.addView(scroll,new LinearLayout.LayoutParams(-1,0,1));
        LinearLayout footer=new LinearLayout(owner.activity);footer.setPadding(dp(20),dp(12),dp(20),dp(16));footer.setBackgroundColor(GameUi.PAPER);
        back=GameUi.action(owner.activity,"",false,this::back);footer.addView(back,new LinearLayout.LayoutParams(0,-2,1));
        next=GameUi.action(owner.activity,"",true,this::next);LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(0,-2,1);lp.leftMargin=dp(10);footer.addView(next,lp);root.addView(footer);
        dialog=new Dialog(owner.activity,android.R.style.Theme_Material_Light_NoActionBar){@Override public void onBackPressed(){back();}};dialog.setContentView(root);
        ui.protect(dialog,()->{closed=true;owner.main.removeCallbacks(tick);ui.setupClosed(this);});
    }
    void show(){ui.show(dialog,true);dialog.getWindow().getDecorView().requestApplyInsets();render();owner.main.post(tick);}
    private int dp(int n){return GameUi.dp(owner.activity,n);}
    private String tr(String ko,String zh){return GameUi.tr(ko,zh);}
    private void back(){if(step>0){step--;render();}else dialog.dismiss();}
    private void next(){if(step<3){step++;render();}else{prefs.edit().putBoolean("setupComplete",true).putInt("setupStep",0).apply();dialog.dismiss();Toast.makeText(owner.activity,tr("초기 설정을 마쳤습니다","初始设置已完成"),Toast.LENGTH_SHORT).show();}}
    private void render(){
        bindings.clear();body.removeAllViews();prefs.edit().putInt("setupStep",step).apply();
        progress.setText(tr("초기 설정  ","初始设置  ")+(step+1)+" / 4");
        title.setText(step==0?"언어 선택 / 选择语言":step==1?tr("컨트롤러 연결","连接控制器"):step==2?tr("모니터 방향 맞추기","调整显示方向"):tr("LED와 부가 설정","LED 与更多设置"));
        back.setText(step==0?tr("나중에","稍后"):tr("‹  이전","‹  上一步"));next.setText(step==3?tr("설정 완료","完成设置"):tr("다음  ›","下一步  ›"));
        if(step==0)language();else if(step==1)connection();else if(step==2)monitor();else extras();scroll.post(()->scroll.scrollTo(0,0));GameUi.appear(body);
    }
    private void language(){
        LinearLayout c=card("한국어 · 简体中文",tr("설정과 폰 스탯창에서 사용할 언어를 선택하세요.","请选择设置与手机状态界面使用的语言。"));
        c.addView(GameUi.action(owner.activity,"한국어","ko".equals(UiText.language()),()->language("ko")));GameUi.space(c,12);
        c.addView(GameUi.action(owner.activity,"简体中文","zh-Hans".equals(UiText.language()),()->language("zh-Hans")));
        ui.text(body,tr("언어를 고른 후 다음을 누르면 컨트롤러 연결을 확인합니다.","选择语言后，点击下一步检查控制器连接。"));
    }
    private void language(String value){UiText.language(value);prefs.edit().putString("language",value).apply();owner.display.refreshLanguage();render();}
    private LinearLayout card(String title,String hint){return GameUi.card(body,title,hint);}
    private void live(LinearLayout parent,Supplier<String> value){TextView label=GameUi.text(owner.activity,value.get(),14,GameUi.MUTED,false);parent.addView(label);bindings.add(()->GameUi.setText(label,value.get()));}
    private void row(LinearLayout parent,String name,Supplier<String> value,Runnable action){TextView label=GameUi.row(parent,name,value.get(),action);bindings.add(()->GameUi.setText(label,value.get()));}
    private void flag(LinearLayout parent,String name,String hint,String key,boolean fallback,OniimaiPanel.Toggle action){
        GameUi.toggle(parent,name,hint,prefs.getBoolean(key,fallback),(v,on)->{prefs.edit().putBoolean(key,on).apply();action.accept(on);});
    }
    private void connection(){
        LinearLayout c=card(tr("연결 상태","连接状态"),tr("Touch · IO4 HID · LED 포트를 이름으로 찾습니다.","按名称查找 Touch、IO4 HID 与 LED 端口。"));live(c,owner::connectionDescription);GameUi.space(c,12);
        TextView connect=GameUi.action(owner.activity,tr("포트 자동 선택 · 다시 연결","自动选择端口并重连"),true,()->{if(!owner.uiBusy()){owner.enableInput(true);owner.automaticPorts();}});c.addView(connect);bindings.add(()->GameUi.enabled(connect,!owner.uiBusy()));
        row(c,tr("USB 권한","USB 权限"),()->tr("장치 확인 · 연결 허용","查看设备并允许连接"),ui::permissions);
        c=card(tr("입력과 포트","输入与端口"),tr("감도 설정용 Command 포트는 터치 입력과 별도로 사용합니다.","灵敏度设置单独使用 Command 端口。"));
        row(c,tr("터치 포트","触摸端口"),()->ui.portName("touchPort"),()->ui.ports("touchPort",false));
        live(c,()->tr("터치 프로토콜: ","触摸协议：")+(prefs.getInt("touchMode",1)==1?"Mai2 Touch · 9600":"Command · 115200"));
        row(c,tr("LED 포트","LED 端口"),()->ui.portName("ledPort"),()->ui.ports("ledPort",false));
        String[] modes={tr("사용 안 함","关闭"),"USB Keyboard","IO4 HID"};
        row(c,tr("버튼 입력 방식","按钮输入方式"),()->modes[Math.max(0,Math.min(2,prefs.getInt("buttonMode",2)))],()->ui.choose(tr("버튼 입력 방식","按钮输入方式"),modes,prefs.getInt("buttonMode",2),i->{prefs.edit().putInt("buttonMode",i).apply();owner.configurationChanged();render();}));
        if(prefs.getInt("buttonMode",2)==2)row(c,"IO4 HID",()->ui.portName("hidPort"),()->ui.ports("hidPort",true));
        flag(c,tr("컨트롤러 입력 사용","启用控制器输入"),null,"input",false,owner::enableInput);
        ui.text(body,tr("설정창에서는 컨트롤러 입력이 게임에 전달되지 않습니다. 다음 단계에서 모니터 방향을 맞춰주세요.","设置期间不会向游戏传递控制器输入。下一步调整显示器方向。"));
    }
    private void monitor(){
        LinearLayout c=card(tr("외부 화면","外接屏幕"),tr("모니터는 가로 16:9, 게임은 회전된 세로 9:16으로 표시합니다.","显示器横向 16:9，游戏旋转后以竖向 9:16 显示。"));
        flag(c,tr("외부 모니터로 게임 출력","在外接显示器上显示游戏"),null,"external",true,on->owner.display.reconcile());
        row(c,tr("출력 장치","输出设备"),owner.display::deviceName,()->owner.display.chooseMonitor(ui));
        if(!owner.display.hasMonitor())ui.text(c,tr("USB-C/HDMI 모니터를 연결하면 여기서 확인할 수 있습니다. 모니터 없이도 다음 단계로 진행할 수 있습니다.","连接 USB-C/HDMI 显示器后可在此查看。也可以稍后再连接。"));
        c=card(tr("게임 방향","游戏方向"),tr("실제 모니터를 보면서 올바른 방향을 선택하세요.","查看实际显示器并选择正确方向。"));
        c.addView(new DisplayPreview(owner.activity,()->prefs.getBoolean("displayReverse",false)),new LinearLayout.LayoutParams(-1,-2));
        boolean reverse=prefs.getBoolean("displayReverse",false);LinearLayout choices=new LinearLayout(owner.activity);
        choices.addView(GameUi.action(owner.activity,tr("90° · 기본","90° · 默认"),!reverse,()->direction(false)),new LinearLayout.LayoutParams(0,-2,1));LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(0,-2,1);lp.leftMargin=dp(8);choices.addView(GameUi.action(owner.activity,tr("270° · 반대","270° · 反向"),reverse,()->direction(true)),lp);c.addView(choices);GameUi.space(c,12);live(c,owner.display::outputDescription);
    }
    private void direction(boolean reverse){prefs.edit().putBoolean("displayReverse",reverse).apply();owner.display.reconcile();render();}
    private void extras(){
        LinearLayout c=card(tr("조명","灯光"),tr("LED 연동은 처음 설치할 때 기본으로 켜집니다.","首次安装默认启用 LED 联动。"));
        flag(c,tr("LED 연동","LED 联动"),null,"led",true,on->{if(on)owner.startLeds();else owner.leds.stop();});live(c,owner.leds::brief);
        ui.slider(c,tr("밝기","亮度"),"brightness",100,0,100,owner::updateLedSettings);
        flag(c,tr("캐비닛 조명","机柜灯光"),tr("컨트롤러의 보조 조명 출력을 사용합니다","使用控制器的辅助灯光输出"),"ring",false,on->owner.updateLedSettings());
        flag(c,tr("LED 순서 반전","反转 LED 顺序"),null,"ledReverse",false,on->owner.updateLedSettings());
        c=card(tr("다음 실행과 P1","下次启动与 P1"),null);
        flag(c,tr("다음에도 자동 연결","下次自动连接"),null,"autoConnect",true,on->owner.refresh());
        String[] actions={tr("정렬 · 검색","排序 · 搜索"),tr("시작 · 확인","开始 · 确认")};
        row(c,tr("P1 버튼 동작","P1 按钮功能"),()->actions[prefs.getBoolean("p1Start",false)?1:0],()->ui.choose("P1 / START",actions,prefs.getBoolean("p1Start",false)?1:0,i->prefs.edit().putBoolean("p1Start",i==1).apply()));
        ui.text(body,tr("설정 완료를 누르면 다음 실행부터 이 창은 자동으로 뜨지 않습니다. 설정 → 연결 → 초기 설정에서 다시 열 수 있습니다.","完成后下次启动不会自动弹出。可在设置 → 连接 → 初始设置中重新打开。"));
    }
}
