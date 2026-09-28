package net.majdata.majdataplay.oniimai;

import android.content.Context;
import android.os.SystemClock;
import android.view.*;
import android.widget.*;

/** Read-only physical input card; native labels remain legible with larger system text. */
final class PhoneInputCard extends LinearLayout {
    private final InputSnapshot input;
    private final TextView touch,buttons,p1,active;
    private long lastTouch=-1,lastButtons=-1,lastFlags=-1;
    private final Runnable refresh=new Runnable(){public void run(){
        long[] f=input.diagnostic(SystemClock.uptimeMillis());
        if(f[0]!=lastButtons||f[1]!=lastTouch||f[3]!=lastFlags){
            lastButtons=f[0];lastTouch=f[1];lastFlags=f[3];
            GameUi.setText(touch,GameUi.tr("터치","触摸")+"  "+Long.bitCount(f[1])+"/34");
            GameUi.setText(buttons,GameUi.tr("버튼","按钮")+"  "+Long.bitCount(f[0]&255)+"/8");
            boolean down=(f[0]&256)!=0;GameUi.setText(p1,"P1  "+(down?"ON":"OFF"));p1.setTextColor(down?GameUi.BLUE:GameUi.MUTED);
            StringBuilder names=new StringBuilder();for(int i=0;i<34;i++)if((f[1]&(1L<<i))!=0){if(names.length()>0)names.append(" · ");names.append(Protocol.ZONES[i]);}
            for(int i=0;i<9;i++)if((f[0]&(1<<i))!=0){if(names.length()>0)names.append(" · ");names.append(i==8?"P1":GameUi.tr("버튼 ","按钮 ")+(i+1));}
            GameUi.setText(active,names.length()>0?names.toString():(f[3]&3)==0?GameUi.tr("컨트롤러 연결을 기다리고 있어요","正在等待控制器连接"):GameUi.tr("센서를 터치하거나 버튼을 눌러보세요","触摸传感器或按下按钮进行检查"));
        }
        postDelayed(this,80);
    }};
    PhoneInputCard(Context c,InputSnapshot input){
        super(c);this.input=input;setOrientation(VERTICAL);setPadding(dp(18),dp(20),dp(18),dp(18));GameUi.surface(this);
        addView(GameUi.text(c,GameUi.tr("컨트롤러 입력","控制器输入"),20,GameUi.INK,true));GameUi.space(this,5);
        addView(GameUi.text(c,GameUi.tr("터치 34개 · 버튼 8개 · P1","34 个触摸区 · 8 个按钮 · P1"),12,GameUi.MUTED,false));GameUi.space(this,12);
        SensorBoard board=new SensorBoard(c,input).withoutFooter();board.setImportantForAccessibility(IMPORTANT_FOR_ACCESSIBILITY_NO);addView(board,new LayoutParams(-1,-2));GameUi.space(this,10);
        LinearLayout counts=new LinearLayout(c);touch=chip(counts,"");buttons=chip(counts,"");p1=chip(counts,"");addView(counts);GameUi.space(this,12);
        active=GameUi.text(c,"",13,GameUi.MUTED,false);active.setGravity(Gravity.CENTER);active.setMinLines(2);active.setMaxLines(3);active.setEllipsize(android.text.TextUtils.TruncateAt.END);addView(active);
    }
    private TextView chip(LinearLayout parent,String label){TextView t=GameUi.text(getContext(),label,13,GameUi.INK,true);t.setGravity(Gravity.CENTER);t.setPadding(dp(3),dp(10),dp(3),dp(10));t.setBackground(GameUi.shape(getContext(),GameUi.PAGE,12));LayoutParams lp=new LayoutParams(0,-2,1);if(parent.getChildCount()>0)lp.leftMargin=dp(6);parent.addView(t,lp);return t;}
    private int dp(int n){return GameUi.dp(getContext(),n);}
    @Override protected void onAttachedToWindow(){super.onAttachedToWindow();post(refresh);}
    @Override protected void onDetachedFromWindow(){removeCallbacks(refresh);super.onDetachedFromWindow();}
}
