package net.majdata.majdataplay.oniimai;

import android.content.Context;
import android.graphics.*;
import android.view.View;
import java.util.function.BooleanSupplier;

/** Read-only orientation preview; the real output is handled by the Presentation. */
final class DisplayPreview extends View {
    private final BooleanSupplier reverse;
    private final Paint p=new Paint(Paint.ANTI_ALIAS_FLAG);
    DisplayPreview(Context c,BooleanSupplier reverse){super(c);this.reverse=reverse;}
    @Override protected void onMeasure(int width,int height){int w=MeasureSpec.getSize(width);setMeasuredDimension(w,resolveSize(Math.round(w*.57f),height));}
    @Override protected void onDraw(Canvas c){
        float w=getWidth(),h=getHeight();float mw=w*.85f,mh=mw*9/16,cx=w/2,cy=h/2-GameUi.dp(getContext(),8);
        p.setColor(GameUi.LINE);c.drawRoundRect(cx-mw/2-5,cy-mh/2-5,cx+mw/2+5,cy+mh/2+5,14,14,p);
        c.save();c.translate(cx,cy);c.rotate(reverse.getAsBoolean()?270:90);float gw=mh,gh=mw;
        p.setColor(0xffeaf3fc);c.drawRect(-gw/2,-gh/2,gw/2,gh/2,p);
        p.setColor(Color.WHITE);c.drawRoundRect(-gw*.45f,-gh*.45f,gw*.45f,-gh*.25f,7,7,p);
        p.setColor(GameUi.PINK);c.drawRoundRect(-gw*.37f,-gh*.39f,gw*.2f,-gh*.36f,3,3,p);
        p.setColor(Color.WHITE);c.drawCircle(0,gh*.19f,gw*.43f,p);p.setStyle(Paint.Style.STROKE);p.setStrokeWidth(4);p.setColor(GameUi.BLUE);c.drawCircle(0,gh*.19f,gw*.35f,p);p.setStyle(Paint.Style.FILL);
        p.setColor(GameUi.ORANGE);c.drawCircle(0,gh*.19f-gw*.35f,6,p);
        c.restore();p.setTextAlign(Paint.Align.CENTER);p.setTypeface(Typeface.create("sans-serif-medium",0));p.setTextSize(GameUi.dp(getContext(),11));p.setColor(GameUi.MUTED);
        c.drawText(GameUi.tr("가로 모니터 16:9","横向显示器 16:9"),cx,h-3,p);
        setContentDescription(GameUi.tr("게임 화면 회전 미리보기 ","游戏旋转预览 ")+(reverse.getAsBoolean()?"270°":"90°"));
    }
}
