package net.majdata.majdataplay.oniimai;

import android.content.Context;
import android.graphics.*;
import android.os.SystemClock;
import android.view.View;

/** Read-only physical input monitor. It never drains Unity's queued input edges. */
final class SensorBoard extends View {
    private final InputSnapshot input;
    private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Path[] paths = new Path[34];
    private final Path[] buttonPaths = new Path[8];
    private final float[] labelX = new float[34], labelY = new float[34];
    private long touches = -1, flags;
    private int buttons = -1;
    private boolean footer=true;
    private boolean gameTheme;
    private boolean compact;
    SensorBoard withoutFooter(){footer=false;requestLayout();return this;}
    SensorBoard gameTheme(){gameTheme=true;setBackgroundColor(Color.TRANSPARENT);invalidate();return this;}
    SensorBoard compact(boolean value){compact=value;invalidate();return this;}
    private final Runnable refresh = new Runnable() {
        public void run() {
            long[] frame=input.diagnostic(SystemClock.uptimeMillis());
            if(buttons!=(int)frame[0] || touches!=frame[1] || flags!=frame[3]) {
                buttons=(int)frame[0]; touches=frame[1]; flags=frame[3];
                setContentDescription("Touch " + Long.bitCount(touches) + "/34; Buttons " + Integer.bitCount(buttons&255) + "/8; P1 " + ((buttons&256)!=0));
                postInvalidateOnAnimation();
            }
            postDelayed(this,33);
        }
    };
    SensorBoard(Context context,InputSnapshot input) {
        super(context); this.input=input;
        setBackgroundColor(Color.WHITE);
        for(int i=0;i<8;i++)buttonPaths[i]=sector(161,188,i*45-88,41);
        for(int i=0;i<34;i++) {
            String name=Protocol.ZONES[i];
            if(i==16 || i==17) {
                // The actual centre electrode is an octagon split vertically.
                float side=i==16?1:-1;
                paths[i]=polygon(new float[]{side,-29,12*side,-29,29*side,-12,29*side,12,12*side,29,side,29});
                labelX[i]=side*14; labelY[i]=0;
            } else {
                char group=name.charAt(0); int n=name.charAt(1)-'1';
                float angle=n*45+(group=='A'||group=='B'?22.5f:0)-90;
                float radius=group=='A'?127:group=='B'?61:group=='D'?131:90;
                labelX[i]=x(radius,angle); labelY[i]=y(radius,angle);
                if(group=='B') paths[i]=regular(labelX[i],labelY[i],23,8,22.5f);
                else if(group=='E') paths[i]=regular(labelX[i],labelY[i],21,4,angle);
                else if(group=='D') {
                    // D's inner edge follows the neighbouring E diamond, as in the supplied diagram.
                    Path p=new Path();p.moveTo(x(154,-97.2f),y(154,-97.2f));
                    p.arcTo(new RectF(-154,-154,154,154),-97.2f,14.4f);
                    p.lineTo(11.8f,-101);p.lineTo(0,-113);p.lineTo(-11.8f,-101);p.close();
                    Matrix rotation=new Matrix();rotation.setRotate(n*45);p.transform(rotation);paths[i]=p;
                }
                else {
                    // Trace A1 from the user's 980px reference (also Sprites/Default_Background.png).
                    // The stepped inner edge borders E1's diamond and E2's square; it is not a wedge.
                    Path p=new Path();p.moveTo(x(154,-81.6f),y(154,-81.6f));
                    p.arcTo(new RectF(-154,-154,154,154),-81.6f,28.2f);
                    p.lineTo(59.4f,-80.3f);p.lineTo(44.8f,-80.3f);p.lineTo(25.4f,-88.6f);p.lineTo(15.6f,-98.8f);p.close();
                    Matrix rotation=new Matrix();rotation.setRotate(n*45);p.transform(rotation);paths[i]=p;
                }
            }
        }
    }
    private static float x(float r,float angle) { return r*(float)Math.cos(Math.toRadians(angle)); }
    private static float y(float r,float angle) { return r*(float)Math.sin(Math.toRadians(angle)); }
    private static Path polygon(float[] points) {
        Path p=new Path(); p.moveTo(points[0],points[1]);
        for(int i=2;i<points.length;i+=2) p.lineTo(points[i],points[i+1]); p.close(); return p;
    }
    private static Path regular(float cx,float cy,float radius,int count,float start) {
        float[] points=new float[count*2];
        for(int i=0;i<count;i++) { points[i*2]=cx+x(radius,start+i*360f/count); points[i*2+1]=cy+y(radius,start+i*360f/count); }
        return polygon(points);
    }
    private static Path sector(float inner,float outer,float start,float sweep) {
        Path p=new Path(); p.moveTo(x(outer,start),y(outer,start));
        p.arcTo(new RectF(-outer,-outer,outer,outer),start,sweep);
        p.lineTo(x(inner,start+sweep),y(inner,start+sweep));
        p.arcTo(new RectF(-inner,-inner,inner,inner),start+sweep,-sweep); p.close(); return p;
    }
    @Override protected void onAttachedToWindow() { super.onAttachedToWindow(); post(refresh); }
    @Override protected void onDetachedFromWindow() { removeCallbacks(refresh); super.onDetachedFromWindow(); }
    @Override protected void onMeasure(int widthSpec,int heightSpec) {
        int width=MeasureSpec.getSize(widthSpec);
        int height=Math.round(Math.min(width,GameUi.dp(getContext(),450))*(footer?438f:400f)/400f);
        setMeasuredDimension(width,resolveSize(height,heightSpec));
    }
    @Override protected void onDraw(Canvas canvas) {
        super.onDraw(canvas);
        float extent=compact&&!footer?388f:400f;
        float scale=Math.min(getWidth()/extent,getHeight()/(footer?438f:extent));
        if(scale<=0)return;
        // Keep every electrode in the compact view; use the available label width within each path.
        float labelSize=compact?Math.max(13,Math.min(21,GameUi.dp(getContext(),7)/scale)):13;
        canvas.save(); canvas.translate(getWidth()/2f,getHeight()/2f-(footer?12:0)*scale); canvas.scale(scale,scale);
        paint.setTypeface(GameAssets.font(getContext())); paint.setTextAlign(Paint.Align.CENTER);
        for(int i=0;i<34;i++) {
            boolean pressed=(touches>=0 && (touches & (1L<<i))!=0);
            paint.setStyle(Paint.Style.FILL); paint.setColor(pressed?(gameTheme?0xff5383a5:0xff286adf):(gameTheme?0xffe7e8e4:0xffe3e9ec)); canvas.drawPath(paths[i],paint);
            paint.setStyle(Paint.Style.STROKE); paint.setStrokeWidth(1); paint.setColor(gameTheme?0xfffffaf0:Color.WHITE); canvas.drawPath(paths[i],paint);
            paint.setStyle(Paint.Style.FILL); paint.setColor(pressed?Color.WHITE:GameUi.INK); paint.setTextSize(labelSize);
            canvas.drawText(Protocol.ZONES[i],labelX[i],labelY[i]-(paint.ascent()+paint.descent())/2,paint);
        }
        for(int i=0;i<8;i++) {
            float angle=i*45-67.5f, bx=x(174,angle), by=y(174,angle);
            boolean pressed=buttons>=0 && (buttons & (1<<i))!=0;
            paint.setColor(pressed?(gameTheme?GameUi.GOLD:Color.rgb(255,145,40)):(gameTheme?GameUi.SLATE:GameUi.TAB)); canvas.drawPath(buttonPaths[i],paint);
            paint.setColor(pressed?0xff342c28:(gameTheme?GameUi.GOLD:Color.WHITE)); paint.setTextSize(labelSize); canvas.drawText(""+(i+1),bx,by-(paint.ascent()+paint.descent())/2,paint);
        }
        if(footer){
        paint.setTextSize(12); paint.setColor(GameUi.INK);
        canvas.drawText(GameUi.tr("터치 ","触摸 ") + Long.bitCount(Math.max(0,touches)) + "/34   ·   "+GameUi.tr("버튼 ","按钮 ") + Integer.bitCount(Math.max(0,buttons)&255) + "/8   ·   P1 " + ((buttons>=0&&(buttons&256)!=0)?"ON":"OFF"),0,209,paint);
        paint.setTextSize(10);
        canvas.drawText((flags&1)==0?GameUi.tr("터치 데이터 대기","等待触摸数据"):GameUi.tr("LIVE · 센서 상태는 읽기 전용입니다","LIVE · 传感器状态为只读"),0,226,paint);
        }
        canvas.restore();
    }
}
