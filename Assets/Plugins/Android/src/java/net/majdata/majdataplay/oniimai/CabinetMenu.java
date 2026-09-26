package net.majdata.majdataplay.oniimai;

import android.content.Context;
import android.graphics.*;
import android.graphics.drawable.*;
import android.content.res.ColorStateList;
import android.util.TypedValue;
import android.view.*;
import android.widget.*;
import java.util.*;

/** The game's circular settings composition, with real accessible Android controls. */
final class CabinetMenu extends FrameLayout {
    interface Host {void category(int index);void move(int direction);void adjust(int direction);void activate();void close();void overview();}
    private final Host host;
    private final Paint paint=new Paint(Paint.ANTI_ALIAS_FLAG|Paint.FILTER_BITMAP_FLAG);
    private final Path circle=new Path();
    private final GameUi.Pattern pattern=new GameUi.Pattern();
    private final ArrayList<Item> items=new ArrayList<>();
    private final TextView[] tabs=new TextView[4];
    private final TextView title,value,hint,count,left,right;
    private final View minus,plus,open;
    private int previousCategory=-1,previousItem=-1;
    private static final class Item {final View view;final float x,y,w,h,size;Item(View v,float x,float y,float w,float h,float s){view=v;this.x=x;this.y=y;this.w=w;this.h=h;size=s;}}
    CabinetMenu(Context c,Host host) {
        super(c);this.host=host;setWillNotDraw(false);setClipChildren(true);
        pattern.setBounds(0,0,1080,1080);
        String[] names={GameUi.tr("연결","连接"),GameUi.tr("입력","输入"),"LED",GameUi.tr("화면","屏幕")};
        for(int i=0;i<4;i++){final int n=i;tabs[i]=text(names[i],31,GameUi.INK);tabs[i].setSingleLine();tabs[i].setOnClickListener(v->host.category(n));GameUi.buttonRole(tabs[i]);place(tabs[i],166+i*187,254,178,78,31);}
        title=text("",40,GameUi.GOLD);place(title,323,433,434,80,40);
        value=text("",84,GameUi.GOLD);place(value,328,522,424,143,84);
        open=new View(c);open.setBackground(new RippleDrawable(ColorStateList.valueOf(0x22ffffff),null,GameUi.shape(c,Color.WHITE,50)));GameUi.buttonRole(open);open.setOnClickListener(v->host.activate());place(open,305,390,470,312,0);
        left=text("",24,GameUi.INK);place(left,116,460,179,171,24);
        right=text("",24,GameUi.INK);place(right,785,460,179,171,24);
        minus=artButton(18,GameUi.tr("값 줄이기","减小数值"),()->host.adjust(-1));place(minus,290,682,165,110,0);
        plus=artButton(17,GameUi.tr("값 늘리기","增大数值"),()->host.adjust(1));place(plus,625,682,165,110,0);
        hint=text("",27,GameUi.INK);place(hint,162,804,756,96,27);
        count=text("",23,GameUi.INK);place(count,415,946,250,42,23);
        edge("left",GameUi.tr("이전 분류","上一分类"),5,264,144,150,()->host.category(-1));
        edge("right",GameUi.tr("다음 분류","下一分类"),931,264,144,150,()->host.category(-2));
        edge("left",GameUi.tr("이전 항목","上一项目"),5,660,144,150,()->host.move(-1));
        edge("right",GameUi.tr("다음 항목","下一项目"),931,660,144,150,()->host.move(1));
    }
    private TextView text(String value,int size,int color){TextView t=GameUi.text(getContext(),value,size,color,false);t.setGravity(Gravity.CENTER);t.setLineSpacing(0,1);return t;}
    private View artButton(int id,String description,Runnable click){
        ImageView b=new ImageView(getContext());b.setImageBitmap(GameAssets.sprite(getContext(),id));b.setScaleType(ImageView.ScaleType.FIT_XY);b.setContentDescription(description);b.setFocusable(true);b.setOnClickListener(v->click.run());
        GameUi.buttonRole(b);b.setForeground(GameUi.ripple(getContext(),Color.TRANSPARENT,40));return b;
    }
    private void edge(String symbol,String description,float x,float y,float w,float h,Runnable action){
        View icon=new View(getContext()){
            final Paint p=new Paint(Paint.ANTI_ALIAS_FLAG);
            @Override protected void onDraw(Canvas c){super.onDraw(c);c.save();float s=getWidth()/144f;c.translate(getWidth()/2f-29*s,getHeight()/2f-29*s);c.scale(s,s);p.setColor(GameUi.GOLD);p.setStrokeWidth(4);p.setStrokeCap(Paint.Cap.ROUND);p.setStrokeJoin(Paint.Join.ROUND);p.setStyle(Paint.Style.STROKE);Path path=new Path();
                if(symbol.equals("done")){path.moveTo(12,31);path.lineTo(24,43);path.lineTo(46,13);}
                else if(symbol.equals("back")){path.moveTo(27,10);path.lineTo(12,21);path.lineTo(27,30);path.moveTo(13,21);path.cubicTo(58,14,56,56,23,44);}
                else {if(symbol.equals("right")){c.translate(58,0);c.scale(-1,1);}path.moveTo(43,29);path.lineTo(13,29);path.moveTo(27,13);path.lineTo(11,29);path.lineTo(27,45);}
                c.drawPath(path,p);c.restore();}
        };icon.setContentDescription(description);GameUi.buttonRole(icon);icon.setOnClickListener(v->action.run());icon.setBackground(GameUi.ripple(getContext(),Color.TRANSPARENT,50));place(icon,x,y,w,h,0);
    }
    private void place(View v,float x,float y,float w,float h,float size){items.add(new Item(v,x,y,w,h,size));addView(v);}
    void show(int category,int index,int total,String name,String current,String description,String previous,String next,boolean adjustable){
        for(int i=0;i<4;i++){tabs[i].setBackground(GameUi.ripple(getContext(),i==category?GameUi.TAB:0xe6e3e8e9,40));tabs[i].setTextColor(i==category?Color.WHITE:GameUi.INK);tabs[i].setSelected(i==category);}
        GameUi.setText(title,name);GameUi.setText(value,current);GameUi.setText(hint,description);GameUi.setText(count,(index+1)+" / "+total+"  ·  ONIIMAI");
        GameUi.setText(left,previous);GameUi.setText(right,next);open.setContentDescription(name+": "+current+". "+description);
        minus.setVisibility(adjustable?VISIBLE:INVISIBLE);plus.setVisibility(adjustable?VISIBLE:INVISIBLE);
        value.setTextSize(TypedValue.COMPLEX_UNIT_PX,getWidth()/1080f*fontScale()*(current.length()>18?28:current.length()>9?36:current.length()>5?48:72));
        if(previousCategory!=category||previousItem!=index){GameUi.appear(title);GameUi.appear(value);GameUi.appear(hint);}previousCategory=category;previousItem=index;
        invalidate();
    }
    void busy(boolean busy){GameUi.enabled(open,!busy);GameUi.enabled(minus,!busy);GameUi.enabled(plus,!busy);}
    private float fontScale(){return Math.max(1f,Math.min(1.5f,getResources().getConfiguration().fontScale));}
    @Override protected void onMeasure(int ws,int hs){int size=MeasureSpec.getSize(ws);setMeasuredDimension(size,size);float s=size/1080f;
        for(Item i:items){int min=i.view.isClickable()?GameUi.dp(getContext(),GameUi.TOUCH_DP):0;
            if(i.view instanceof TextView&&i.view!=value){TextView label=(TextView)i.view;int max=Math.max(2,Math.round(i.size*s*fontScale()));label.setAutoSizeTextTypeUniformWithConfiguration(Math.max(1,Math.round(i.size*s*.8f)),max,1,TypedValue.COMPLEX_UNIT_PX);}
            i.view.measure(MeasureSpec.makeMeasureSpec(Math.max(min,Math.round(i.w*s)),MeasureSpec.EXACTLY),MeasureSpec.makeMeasureSpec(Math.max(min,Math.round(i.h*s)),MeasureSpec.EXACTLY));}
        String current=value.getText().toString();value.setTextSize(TypedValue.COMPLEX_UNIT_PX,s*fontScale()*(current.length()>18?28:current.length()>9?36:current.length()>5?48:72));
    }
    @Override protected void onLayout(boolean changed,int l,int t,int r,int b){float s=getWidth()/1080f;for(Item i:items){int w=i.view.getMeasuredWidth(),h=i.view.getMeasuredHeight();int x=Math.max(0,Math.min(getWidth()-w,Math.round((i.x+i.w/2)*s-w/2f))),y=Math.max(0,Math.min(getHeight()-h,Math.round((i.y+i.h/2)*s-h/2f)));i.view.layout(x,y,x+w,y+h);}}
    @Override protected void onDraw(Canvas c){super.onDraw(c);c.save();c.scale(getWidth()/1080f,getHeight()/1080f);circle.reset();circle.addCircle(540,540,539,Path.Direction.CW);c.clipPath(circle);
        GameAssets.draw(c,getContext(),22,92,245,988,824,paint);
        paint.setColor(0x99fffdf4);c.drawRect(115,250,965,820,paint);
        paint.setColor(0xffffe6ca);paint.setStrokeWidth(3);paint.setPathEffect(new DashPathEffect(new float[]{11,9},0));c.drawLine(111,350,969,350,paint);c.drawLine(111,751,969,751,paint);paint.setPathEffect(null);
        GameAssets.draw(c,getContext(),10,146,146,934,934,paint);
        GameAssets.draw(c,getContext(),16,295,376,785,723,paint);
        pad(c,0,340);pad(c,1080,340);pad(c,0,740);pad(c,1080,740);
        c.restore();
    }
    private void pad(Canvas c,float x,float y){GameAssets.draw(c,getContext(),20,x-132,y-132,x+132,y+132,paint);}
    @Override protected void dispatchDraw(Canvas c){c.save();Path p=new Path();p.addCircle(getWidth()/2f,getHeight()/2f,getWidth()/2f,Path.Direction.CW);c.clipPath(p);super.dispatchDraw(c);c.restore();}
}
