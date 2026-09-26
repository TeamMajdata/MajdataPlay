package net.majdata.majdataplay.oniimai;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.DashPathEffect;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RectF;
import android.graphics.Typeface;
import android.os.SystemClock;
import android.text.Layout;
import android.text.StaticLayout;
import android.text.TextPaint;
import android.text.TextUtils;
import android.view.Gravity;
import android.view.View;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;
import org.json.JSONObject;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Locale;

/** Read-only, size-independent content for the phone's customizable dashboard tiles. */
final class DashboardWidget extends FrameLayout {
    private static final String DASH="—";
    private static final int FOOTER_HEIGHT_DP=22;
    private static final String[] JUDGMENT_KEYS={"critical","perfect","great","good","miss"};
    private static final String[] JUDGMENT_NAMES={"Critical","Perfect","Great","Good","Miss"};
    private final String type;
    private final OniimaiController owner;
    private final Content content;
    private TextView sensorFooter;
    private SensorBoard sensorBoard;
    private JSONObject frame=new JSONObject();
    private String inputStatus="", ledStatus="", outputStatus="";
    private Bitmap cover, graph;
    private boolean stale=true;
    private int gridWidth=2,gridHeight=2;
    private long minute=-1, inputFlags;

    DashboardWidget(Context context,String type,OniimaiController owner) {
        super(context); this.type=type;this.owner=owner;
        setClipChildren(true);setClipToPadding(true);
        if("sensors".equals(type)) {
            content=null;
            LinearLayout body=GameUi.column(context);
            sensorBoard=new SensorBoard(context,owner.state).withoutFooter().gameTheme().compact(true);
            body.addView(sensorBoard,new LinearLayout.LayoutParams(-1,0,1));
            sensorFooter=GameUi.text(context,"",11,GameUi.MUTED,false);
            sensorFooter.setTypeface(GameAssets.font(context));
            sensorFooter.setBackground(GameUi.shape(context,0xffe7ecec,14));
            sensorFooter.setGravity(Gravity.CENTER);sensorFooter.setSingleLine();
            sensorFooter.setIncludeFontPadding(false);sensorFooter.setPadding(GameUi.dp(context,4),0,GameUi.dp(context,4),0);
            sensorFooter.setAutoSizeTextTypeUniformWithConfiguration(9,11,1,android.util.TypedValue.COMPLEX_UNIT_SP);
            sensorFooter.setEllipsize(TextUtils.TruncateAt.END);
            LinearLayout.LayoutParams footerParams=new LinearLayout.LayoutParams(-1,GameUi.dp(context,FOOTER_HEIGHT_DP));
            footerParams.topMargin=GameUi.dp(context,6);body.addView(sensorFooter,footerParams);
            addView(body,new FrameLayout.LayoutParams(-1,-1));
        } else {
            content=new Content(context);
            content.setImportantForAccessibility(IMPORTANT_FOR_ACCESSIBILITY_NO);
            addView(content,new FrameLayout.LayoutParams(-1,-1));
        }
        setImportantForAccessibility(IMPORTANT_FOR_ACCESSIBILITY_YES);
        update(new JSONObject(),true,null,null);
    }

    void setGridSize(int width,int height) {
        if(gridWidth==width&&gridHeight==height)return;
        gridWidth=width;gridHeight=height;
        if(sensorBoard!=null) {
            sensorBoard.compact(width==2);
            LinearLayout.LayoutParams footer=(LinearLayout.LayoutParams)sensorFooter.getLayoutParams();
            footer.height=GameUi.dp(getContext(),width==2?FOOTER_HEIGHT_DP:28);sensorFooter.setLayoutParams(footer);
        }
        if(content!=null)content.invalidate();
    }

    void update(JSONObject next,boolean stale,Bitmap cover,Bitmap graph) {
        boolean dirty=this.stale!=stale
                || ("song".equals(type)&&this.cover!=cover) || ("graph".equals(type)&&this.graph!=graph)
                || (frame!=next&&WidgetFields.changed(type,frame::opt,next::opt));
        frame=next;
        this.stale=stale;this.cover=cover;this.graph=graph;
        if("sensors".equals(type)) {
            // diagnostic() does not consume edges queued for the game.
            long[] input=owner.state.diagnostic(SystemClock.uptimeMillis());
            String detail=GameUi.tr("터치 ","触摸 ")+Long.bitCount(input[1])+"/34   ·   "+
                GameUi.tr("버튼 ","按钮 ")+Integer.bitCount((int)input[0]&255)+"/8   ·   P1 "+((input[0]&256)!=0?"ON":"OFF");
            String summary=gridWidth==2?"T"+Long.bitCount(input[1])+"/34 B"+Integer.bitCount((int)input[0]&255)+"/8 P1"+((input[0]&256)!=0?"ON":"OFF"):detail;
            GameUi.setText(sensorFooter,summary);
            sensorFooter.setContentDescription(detail);
            setContentDescription(GameUi.tr("컨트롤러 입력. ","控制器输入。")+detail+". "+owner.connectionDescription());
            return;
        }
        if("connection".equals(type)) {
            String input=owner.connectionDescription(),led=owner.leds.brief(),output=owner.display.outputDescription();
            long flags=owner.state.diagnostic(SystemClock.uptimeMillis())[3];
            dirty|=!inputStatus.equals(input)||!ledStatus.equals(led)||!outputStatus.equals(output)||inputFlags!=flags;
            inputStatus=input;ledStatus=led;outputStatus=output;inputFlags=flags;
        }
        if("clock".equals(type)) {
            long now=System.currentTimeMillis()/60000;
            dirty|=minute!=now;minute=now;
        }
        if(dirty) { content.postInvalidateOnAnimation();setContentDescription(description()); }
    }

    private boolean live(){return !stale&&frame.optBoolean("game",false);}
    private String waiting(){return stale?GameUi.tr("통계 연결 대기","等待统计连接"):GameUi.tr("곡 시작 대기","等待曲目开始");}
    private String count(String key){return live()&&frame.has(key)&&!frame.isNull(key)?String.valueOf(frame.optLong(key)):DASH;}
    private String achievement(){double v=frame.optDouble("achievement",Double.NaN);return live()&&Double.isFinite(v)?String.format(Locale.US,"%.4f%%",v):DASH;}
    private String title(){return live()?frame.optString("title",GameUi.tr("재생 중","正在播放")):scene(frame.optString("scene"));}
    private String artist(){return live()?frame.optString("artist"):waiting();}
    private String level(){String value=live()?frame.optString("level","").trim():"";return value.isEmpty()?DASH:value;}
    private static String elapsed(double value){if(!Double.isFinite(value)||value<0)return DASH;int sec=(int)value;return String.format(Locale.US,"%d:%02d",sec/60,sec%60);}
    private String times(){return live()?elapsed(frame.optDouble("seconds",Double.NaN))+" / "+elapsed(frame.optDouble("length",Double.NaN)):DASH+" / "+DASH;}
    private String scene(String name) {
        switch(name){case "Title":return GameUi.tr("타이틀 화면","标题画面");case "List":return GameUi.tr("곡 선택","选择曲目");
            case "SortFind":return GameUi.tr("정렬 · 검색","排序 · 搜索");case "Setting":return GameUi.tr("게임 설정","游戏设置");
            case "Result":return GameUi.tr("플레이 결과","游玩结果");case "Login":return GameUi.tr("로그인","登录");
            default:return GameUi.tr("플레이 대기","等待游玩");}
    }
    private Locale locale(){return "zh-Hans".equals(UiText.language())?Locale.SIMPLIFIED_CHINESE:Locale.KOREAN;}
    private String localTime(){return new SimpleDateFormat(android.text.format.DateFormat.is24HourFormat(getContext())?"HH:mm":"h:mm",locale()).format(new Date());}
    private String localDate(){return new SimpleDateFormat("M.d E",locale()).format(new Date());}
    private String description() {
        switch(type) {
            case "song":return title()+". "+artist()+". "+GameUi.tr("레벨 ","等级 ")+level();
            case "score":return "Achievement "+achievement()+". Combo "+count("combo")+". "+GameUi.tr("잔여 DX 점수 ","剩余 DX 分数 ")+count("dx")+(!live()?". "+waiting():"");
            case "timing":return "Fast "+count("fast")+". Late "+count("late");
            case "judgments":StringBuilder value=new StringBuilder();for(int i=0;i<5;i++)value.append(JUDGMENT_NAMES[i]).append(' ').append(count(JUDGMENT_KEYS[i])).append(". ");return value.toString();
            case "graph":return GameUi.tr("곡 진행 ","曲目进度 ")+times();
            case "connection":return inputStatus+". "+ledStatus+". "+outputStatus;
            case "clock":return localDate()+" "+localTime();
            default:return "";
        }
    }

    private final class Content extends View {
        private final Paint paint=new Paint(Paint.ANTI_ALIAS_FLAG|Paint.FILTER_BITMAP_FLAG);
        private final TextPaint textPaint=new TextPaint(Paint.ANTI_ALIAS_FLAG);
        private final Typeface regular=GameAssets.font(getContext());
        private final int pale=0xffe7ecec, blue=0xff5383a5, pink=0xffd95691;
        private final DashPathEffect stitch=new DashPathEffect(new float[]{dp(4),dp(4)},0);
        private final RectF rect=new RectF();
        private final Path clip=new Path();
        Content(Context c){super(c);}
        private float dp(float n){return n*getResources().getDisplayMetrics().density;}
        private float sp(float n){return n*getResources().getDisplayMetrics().scaledDensity;}
        @Override protected void onDraw(Canvas canvas) {
            super.onDraw(canvas);float w=getWidth(),h=getHeight();if(w<1||h<1)return;
            switch(type) {
                case "song":song(canvas,w,h);break;
                case "score":score(canvas,w,h);break;
                case "judgments":judgments(canvas,w,h);break;
                case "timing":timing(canvas,w,h);break;
                case "graph":graph(canvas,w,h);break;
                case "connection":connection(canvas,w,h);break;
                case "clock":clock(canvas,w,h);break;
            }
        }
        /** All single-line labels use the same font-metric vertical alignment. */
        private void line(Canvas c,String value,float x,float y,float width,float height,float size,int color,Paint.Align align,boolean fitWidth) {
            if(width<=0||height<=0||size<=0)return;
            value=value==null?"":value;
            textPaint.setTypeface(regular);textPaint.setTextSize(size);textPaint.setColor(color);
            float lineHeight=textPaint.descent()-textPaint.ascent();
            size*=Math.min(1,height/Math.max(1,lineHeight));textPaint.setTextSize(size);
            if(fitWidth) {
                float measured=textPaint.measureText(value);
                textPaint.setTextSize(size*Math.min(1,width/Math.max(1,measured)));
            } else value=TextUtils.ellipsize(value,textPaint,width,TextUtils.TruncateAt.END).toString();
            float measured=textPaint.measureText(value);
            float left=align==Paint.Align.RIGHT?x+width-measured:align==Paint.Align.CENTER?x+(width-measured)/2:x;
            float baseline=y+(height-textPaint.descent()-textPaint.ascent())/2;
            c.save();c.clipRect(x,y,x+width,y+height);c.drawText(value,left,baseline,textPaint);c.restore();
        }
        /** A bounded title area prevents long names from colliding with artist/level text. */
        private void paragraph(Canvas c,String value,float x,float y,float width,float height,float size,int color,boolean center,int lines) {
            if(width<1||height<1||size<1)return;
            value=value==null?"":value;if(value.length()>512)value=value.substring(0,512);
            textPaint.setTypeface(regular);textPaint.setColor(color);textPaint.setTextSize(size);
            if(value.indexOf('\n')<0&&textPaint.measureText(value)<=width) {
                line(c,value,x,y,width,height,size,color,center?Paint.Align.CENTER:Paint.Align.LEFT,false);return;
            }
            StaticLayout layout=fittedParagraph(value,width,height,size,color,center,lines);
            c.save();c.clipRect(x,y,x+width,y+height);c.translate(x,y+Math.max(0,(height-layout.getHeight())/2));layout.draw(c);c.restore();
        }
        /** Measure the actual wrapped lines. Use a private paint so drawing another label cannot change this layout. */
        private StaticLayout fittedParagraph(String value,float width,float height,float size,int color,boolean center,int lines) {
            value=value==null?"":value;if(value.length()>512)value=value.substring(0,512);
            TextPaint paragraphPaint=new TextPaint(textPaint);paragraphPaint.setTypeface(regular);
            paragraphPaint.setColor(color);paragraphPaint.setTextSize(size);
            StaticLayout layout=null;
            for(int pass=0;pass<3;pass++) {
                layout=StaticLayout.Builder.obtain(value,0,value.length(),paragraphPaint,Math.max(1,(int)width))
                    .setAlignment(center?Layout.Alignment.ALIGN_CENTER:Layout.Alignment.ALIGN_NORMAL).setIncludePad(false)
                    .setMaxLines(lines).setEllipsize(TextUtils.TruncateAt.END).setLineSpacing(dp(1),1).build();
                if(layout.getHeight()<=height||pass==2)break;
                paragraphPaint.setTextSize(Math.max(1,paragraphPaint.getTextSize()*Math.max(1,height-1)/layout.getHeight()));
            }
            return layout;
        }
        private void round(Canvas c,float x,float y,float width,float height,float radius,int color) {
            if(width<=0||height<=0)return;
            paint.setColor(color);paint.setStyle(Paint.Style.FILL);rect.set(x,y,x+width,y+height);c.drawRoundRect(rect,radius,radius,paint);
        }
        private boolean wide(){return gridWidth>gridHeight;}
        private void centered(Canvas c,String value,float x,float y,float width,float height,float size,int color) {
            line(c,value,x,y,width,height,size,color,Paint.Align.CENTER,true);
        }
        private void centeredLine(Canvas c,String value,float x,float y,float width,float height,float size,int color) {
            line(c,value,x,y,width,height,size,color,Paint.Align.CENTER,false);
        }
        private void stitched(Canvas c,float x,float y,float width,float height) {
            round(c,x,y,width,height,Math.min(dp(24),height*.28f),GameUi.SLATE);
            float inset=Math.min(dp(6),height*.10f);
            rect.set(x+inset,y+inset,x+width-inset,y+height-inset);
            paint.setColor(GameUi.GOLD);paint.setStyle(Paint.Style.STROKE);paint.setStrokeWidth(dp(1));paint.setPathEffect(stitch);
            c.drawRoundRect(rect,Math.min(dp(19),height*.22f),Math.min(dp(19),height*.22f),paint);
            paint.setPathEffect(null);paint.setStyle(Paint.Style.FILL);
        }
        private void artwork(Canvas c,Bitmap bitmap,float x,float y,float size,boolean crop) {
            if(size<1)return;
            paint.setStyle(Paint.Style.FILL);paint.setColor(Color.WHITE);c.drawCircle(x+size/2,y+size/2,size/2,paint);
            float inset=Math.min(dp(5),size*.06f),inner=size-2*inset;
            paint.setColor(live()?GameUi.GOLD:pale);paint.setStyle(Paint.Style.STROKE);paint.setStrokeWidth(inset);
            c.drawCircle(x+size/2,y+size/2,(size-inset)/2,paint);paint.setStyle(Paint.Style.FILL);
            if(bitmap==null||bitmap.isRecycled())return;
            float bw=bitmap.getWidth(),bh=bitmap.getHeight();if(bw<1||bh<1)return;
            float scale=crop?Math.max(inner/bw,inner/bh):Math.min(inner/bw,inner/bh);
            clip.reset();clip.addCircle(x+size/2,y+size/2,inner/2,Path.Direction.CW);
            c.save();c.clipPath(clip);paint.setColor(Color.WHITE);c.drawBitmap(bitmap,null,
                new RectF(x+(size-bw*scale)/2,y+(size-bh*scale)/2,x+(size+bw*scale)/2,y+(size+bh*scale)/2),paint);c.restore();
        }
        private void song(Canvas c,float w,float h) {
            Bitmap art=live()?cover:GameAssets.mascot(getContext());
            if(wide()) {
                float edge=Math.min(h*.91f,w*.32f),x=edge+dp(14),ww=w-x;
                artwork(c,art,0,(h-edge)/2,edge,live());
                float gap=Math.min(dp(8),h*.045f),labelHeight=Math.min(dp(26),h*.19f);
                float artistHeight=Math.min(dp(22),h*.17f);
                StaticLayout titleLayout=fittedParagraph(title(),ww,h*.42f,Math.min(sp(27),h*.19f),GameUi.INK,false,2);
                float titleHeight=titleLayout.getHeight();
                float blockHeight=labelHeight+titleHeight+artistHeight+gap*2,y=(h-blockHeight)/2;
                float labelWidth=Math.min(ww,dp(128));
                round(c,x,y,labelWidth,labelHeight,labelHeight/2,pale);
                centered(c,live()?"Lv. "+level():"MAJDATA PLAY",x+dp(8),y,labelWidth-dp(16),labelHeight,Math.min(sp(12),h*.10f),GameUi.TAB);
                // A one-line title reserves one line, while a wrapped title reserves its actual two lines.
                float titleY=y+labelHeight+gap;
                c.save();c.clipRect(x,titleY,x+ww,titleY+titleHeight);c.translate(x,titleY);titleLayout.draw(c);c.restore();
                line(c,artist(),x,titleY+titleHeight+gap,ww,artistHeight,Math.min(sp(14),h*.11f),GameUi.MUTED,Paint.Align.LEFT,false);
            } else {
                float top=h*.45f,gap=Math.min(dp(10),w*.06f),edge=Math.min(w*.46f,top),badgeX=edge+gap,badgeW=w-badgeX;
                artwork(c,art,0,(top-edge)/2,edge,live());
                float badgeH=top*.82f,badgeY=(top-badgeH)/2;
                stitched(c,badgeX,badgeY,badgeW,badgeH);
                centered(c,"LEVEL",badgeX+dp(5),badgeY+badgeH*.20f,badgeW-dp(10),badgeH*.20f,Math.min(sp(11),badgeH*.21f),GameUi.GOLD);
                centered(c,level(),badgeX+dp(5),badgeY+badgeH*.46f,badgeW-dp(10),badgeH*.38f,Math.min(sp(30),badgeH*.40f),GameUi.GOLD);
                paragraph(c,title(),0,h*.49f,w,h*.34f,Math.min(sp(21),h*.135f),GameUi.INK,true,2);
                centeredLine(c,artist(),0,h*.87f,w,h*.13f,Math.min(sp(12),h*.095f),GameUi.MUTED);
            }
        }
        private void metric(Canvas c,String label,String value,float x,float y,float width,float height,int color) {
            round(c,x,y,width,height,Math.min(dp(17),height*.25f),pale);
            boolean shortRow=height<dp(48);
            float labelY=shortRow?.05f:.13f,labelHeight=shortRow?.32f:.22f;
            float valueY=shortRow?.47f:.43f,valueHeight=shortRow?.46f:.43f;
            centeredLine(c,label,x+dp(5),y+height*labelY,width-dp(10),height*labelHeight,Math.min(sp(13),height*labelHeight),GameUi.MUTED);
            centered(c,value,x+dp(5),y+height*valueY,width-dp(10),height*valueHeight,Math.min(sp(32),height*(shortRow?.43f:.38f)),color);
        }
        private void score(Canvas c,float w,float h) {
            float gap=dp(8),mainW=wide()?w*.60f:w,mainH=wide()?h:h*.61f;
            stitched(c,0,0,mainW,mainH);
            centered(c,"Achievement",dp(8),mainH*.14f,mainW-dp(16),mainH*.18f,Math.min(sp(17),mainH*.15f),GameUi.GOLD);
            centered(c,achievement(),dp(10),mainH*.37f,mainW-dp(20),mainH*.45f,Math.min(sp(48),mainH*.36f),GameUi.GOLD);
            if(wide()) {
                float x=mainW+gap,ww=w-x,hh=(h-gap)/2;
                metric(c,"Combo",count("combo"),x,0,ww,hh,GameUi.INK);
                metric(c,GameUi.tr("DX 잔여","剩余 DX"),count("dx"),x,hh+gap,ww,hh,blue);
            } else {
                float y=mainH+gap,ww=(w-gap)/2,hh=h-y;
                metric(c,"Combo",count("combo"),0,y,ww,hh,GameUi.INK);
                metric(c,GameUi.tr("DX 잔여","剩余 DX"),count("dx"),ww+gap,y,ww,hh,blue);
            }
        }
        private void judgments(Canvas c,float w,float h) {
            if(gridWidth==2) {
                float rh=h/5,gutter=Math.min(dp(10),w*.055f),labelX=gutter+dp(11),valueX=w*.64f;
                float rowSize=Math.min(sp(18),rh*.45f);
                for(int i=0;i<5;i++) {
                    float y=i*rh,mark=Math.min(dp(5),rh*.10f);
                    if(i%2==0)round(c,0,y,w,rh,Math.min(dp(10),rh*.22f),0xfff1eee7);
                    paint.setColor(GameUi.JUDGMENT[i]);c.drawCircle(gutter,y+rh/2,mark,paint);
                    line(c,JUDGMENT_NAMES[i],labelX,y,valueX-labelX-dp(6),rh,rowSize,GameUi.JUDGMENT[i],Paint.Align.LEFT,false);
                    line(c,count(JUDGMENT_KEYS[i]),valueX,y,w-valueX-gutter,rh,rowSize,GameUi.INK,Paint.Align.RIGHT,true);
                }
                return;
            }
            float gap=Math.min(dp(9),h*.055f),cw=(w-2*gap)/3,rh=(h-gap)/2;
            long total=0;boolean totalKnown=live();for(String key:JUDGMENT_KEYS){totalKnown&=frame.has(key)&&!frame.isNull(key);total+=frame.optLong(key);}
            for(int i=0;i<6;i++) {
                float x=(i%3)*(cw+gap),y=(i/3)*(rh+gap);int color=i<5?GameUi.JUDGMENT[i]:blue;
                round(c,x,y,cw,rh,Math.min(dp(17),rh*.25f),i==5?GameUi.SLATE:0xfff1eee7);
                String name=i<5?JUDGMENT_NAMES[i]:"Total",value=i<5?count(JUDGMENT_KEYS[i]):totalKnown?String.valueOf(total):DASH;
                centered(c,name,x+dp(4),y+rh*.13f,cw-dp(8),rh*.22f,Math.min(sp(16),rh*.18f),i==5?GameUi.GOLD:color);
                centered(c,value,x+dp(4),y+rh*.42f,cw-dp(8),rh*.43f,Math.min(sp(44),rh*.35f),i==5?GameUi.GOLD:GameUi.INK);
            }
        }
        private void timing(Canvas c,float w,float h) {
            float gap=Math.min(dp(8),w*.055f),cw=(w-gap)/2,footer=Math.min(dp(FOOTER_HEIGHT_DP),h*.19f),bottom=h-footer-dp(6);
            metric(c,"Fast",count("fast"),0,0,cw,bottom,blue);
            metric(c,"Late",count("late"),cw+gap,0,cw,bottom,pink);
            float bar=Math.min(dp(4),footer*.20f),y=h-footer/2-bar/2;round(c,0,y,w,bar,bar/2,pale);
            long fast=frame.optLong("fast"),late=frame.optLong("late");
            if(live()&&fast+late>0) {
                float fraction=(float)fast/(fast+late),split=fast>0&&late>0?dp(2):0;
                round(c,0,y,Math.max(0,w*fraction-split/2),bar,bar/2,blue);
                round(c,w*fraction+split/2,y,Math.max(0,w*(1-fraction)-split/2),bar,bar/2,pink);
            }
        }
        private void graph(Canvas c,float w,float h) {
            float footer=Math.min(dp(FOOTER_HEIGHT_DP),h*.19f),gap=Math.min(dp(6),h*.045f);
            float barH=Math.min(dp(4),h*.035f),barY=h-footer-gap-barH;
            float bottom=barY-gap,pad=Math.min(dp(9),h*.055f);
            round(c,0,0,w,bottom,dp(15),Color.WHITE);
            if(live()&&graph!=null&&!graph.isRecycled()) {
                rect.set(pad,pad,w-pad,bottom-pad);clip.reset();clip.addRoundRect(rect,dp(9),dp(9),Path.Direction.CW);
                c.save();c.clipPath(clip);paint.setColor(Color.WHITE);c.drawBitmap(graph,null,rect,paint);c.restore();
            } else {
                centered(c,live()?GameUi.tr("밀도 그래프 없음","暂无密度图"):waiting(),pad,bottom*.30f,w-2*pad,bottom*.27f,Math.min(sp(15),h*.11f),GameUi.MUTED);
                round(c,pad,bottom*.82f,w-2*pad,dp(2),dp(1),pale);
            }
            double length=frame.optDouble("length",0),seconds=frame.optDouble("seconds",0);
            round(c,0,barY,w,barH,barH/2,pale);
            if(live()&&length>0&&Double.isFinite(length)&&Double.isFinite(seconds)) {
                float progress=(float)Math.max(0,Math.min(1,seconds/length));
                paint.setColor(pink);paint.setStrokeWidth(dp(2));c.drawLine(pad+progress*(w-2*pad),pad,pad+progress*(w-2*pad),bottom-pad,paint);
                round(c,0,barY,w*progress,barH,barH/2,pink);
            }
            centered(c,times(),0,h-footer,w,footer,Math.min(sp(12),footer*.68f),GameUi.TAB);
        }
        private void connection(Canvas c,float w,float h) {
            String[] names={GameUi.tr("입력","输入"),"LED",GameUi.tr("화면","屏幕")};
            String[] values={inputStatus,ledStatus,outputStatus};
            if(!wide()){
                if(values[0].equals(GameUi.tr("터치와 버튼 준비 완료","触摸和按钮已就绪")))values[0]=GameUi.tr("준비 완료","已就绪");
                if(values[1].startsWith("LED "))values[1]=values[1].substring(4);
                int arrow=values[2].indexOf(" → ");
                if(arrow>=0){
                    values[2]=values[2].substring(arrow+3);int divider=values[2].indexOf(" · ");
                    if(divider>=0)values[2]=values[2].substring(0,divider)+"\n"+values[2].substring(divider+3);
                }
            }
            int ready=0xff257965;
            int[] colors={(inputFlags&3)==3?ready:GameUi.MUTED,owner.prefs.getBoolean("led",true)?blue:GameUi.MUTED,owner.display.active()?ready:GameUi.MUTED};
            float gap=Math.min(dp(6),h*.035f),rh=(h-2*gap)/3;
            for(int i=0;i<3;i++) {
                float y=i*(rh+gap),tagWidth=Math.min(dp(61),w*.31f);
                round(c,0,y,w,rh,Math.min(dp(13),rh*.30f),pale);
                float inset=Math.min(dp(10),w*.065f),rowText=Math.min(sp(13),rh*.38f);
                line(c,names[i],inset,y,tagWidth-inset,rh,rowText,colors[i],Paint.Align.LEFT,false);
                float left=tagWidth+dp(4),available=w-left-dp(6);
                if(wide()) {
                    line(c,values[i],left,y,available,rh,rowText,GameUi.INK,Paint.Align.LEFT,false);
                } else {
                    paragraph(c,values[i],left,y+rh*.08f,available,rh*.84f,Math.min(rh*.34f,Math.min(sp(12),Math.max(sp(10),rh*.29f))),GameUi.INK,false,2);
                }
            }
        }
        private void clock(Canvas c,float w,float h) {
            float timeWidth=wide()?w*.62f:w,timeHeight=wide()?h:h*.69f;
            stitched(c,0,0,timeWidth,timeHeight);
            centered(c,localTime(),dp(10),0,timeWidth-dp(20),timeHeight,Math.min(sp(72),h*.40f),GameUi.GOLD);
            String period=android.text.format.DateFormat.is24HourFormat(getContext())?"":new SimpleDateFormat("a",locale()).format(new Date());
            if(wide()) {
                float x=timeWidth+dp(10),ww=w-x,dateHeight=h*.23f,periodHeight=period.isEmpty()?0:h*.15f;
                float gap=period.isEmpty()?0:Math.min(dp(8),h*.06f),y=(h-dateHeight-periodHeight-gap)/2;
                centered(c,localDate(),x,y,ww,dateHeight,Math.min(sp(22),h*.17f),GameUi.INK);
                if(!period.isEmpty())centered(c,period,x,y+dateHeight+gap,ww,periodHeight,Math.min(sp(14),h*.12f),blue);
            } else {
                float y=timeHeight+dp(6);
                centered(c,localDate()+(period.isEmpty()?"":" · "+period),0,y,w,h-y,Math.min(sp(16),h*.13f),GameUi.INK);
            }
        }
    }
}
