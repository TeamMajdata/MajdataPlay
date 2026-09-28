package net.majdata.majdataplay.oniimai;

import android.content.Context;
import android.graphics.*;
import android.view.*;
import android.widget.*;
import android.util.TypedValue;

/** Photo-matched sensor layout inside the game's circular cabinet frame. */
final class SensorCabinet extends FrameLayout {
    private final SensorBoard board;
    private final TextView title,settings,phone;
    private final Paint paint=new Paint(Paint.ANTI_ALIAS_FLAG|Paint.FILTER_BITMAP_FLAG);
    private final GameUi.Pattern pattern=new GameUi.Pattern();
    SensorCabinet(Context c,InputSnapshot state,Runnable open,Runnable returnToPhone){
        super(c);setWillNotDraw(false);pattern.setBounds(0,0,1080,1080);
        board=new SensorBoard(c,state);board.setBackgroundColor(Color.TRANSPARENT);addView(board);
        title=GameUi.text(c,GameUi.tr("컨트롤러 입력","控制器输入"),20,GameUi.INK,true);title.setGravity(Gravity.CENTER);addView(title);
        settings=GameUi.action(c,GameUi.tr("설정  ›","设置  ›"),true,open);addView(settings);
        phone=GameUi.action(c,GameUi.tr("↶  폰 화면","↶  手机画面"),true,returnToPhone);addView(phone);
    }
    @Override protected void onMeasure(int ws,int hs){int w=MeasureSpec.getSize(ws);setMeasuredDimension(w,w);float s=w/1080f;
        measure(board,800,812,s);measure(title,630,70,s);measure(settings,270,100,s);measure(phone,270,100,s);
        title.setTextSize(TypedValue.COMPLEX_UNIT_PX,42*s);settings.setTextSize(TypedValue.COMPLEX_UNIT_PX,32*s);phone.setTextSize(TypedValue.COMPLEX_UNIT_PX,32*s);
    }
    private void measure(View v,int w,int h,float s){v.measure(MeasureSpec.makeMeasureSpec(Math.round(w*s),MeasureSpec.EXACTLY),MeasureSpec.makeMeasureSpec(Math.round(h*s),MeasureSpec.EXACTLY));}
    private void place(View v,int x,int y){float s=getWidth()/1080f;int l=Math.round(x*s),t=Math.round(y*s);v.layout(l,t,l+v.getMeasuredWidth(),t+v.getMeasuredHeight());}
    @Override protected void onLayout(boolean changed,int l,int t,int r,int b){place(title,225,66);place(board,140,130);place(phone,208,945);place(settings,602,945);}
    @Override protected void onDraw(Canvas c){c.save();c.scale(getWidth()/1080f,getHeight()/1080f);Path p=new Path();p.addCircle(540,540,539,Path.Direction.CW);c.clipPath(p);GameAssets.draw(c,getContext(),10,74,74,1006,1006,paint);c.restore();}
}
