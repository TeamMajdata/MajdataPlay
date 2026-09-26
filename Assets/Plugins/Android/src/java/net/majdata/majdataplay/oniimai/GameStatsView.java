package net.majdata.majdataplay.oniimai;

import android.content.Context;
import android.graphics.*;
import android.view.*;
import android.widget.*;
import org.json.JSONObject;
import java.util.Locale;

/** Native, font-scaled companion stats. Empty states never show invented zero scores. */
final class GameStatsView extends LinearLayout {
    private final TextView state,title,subtitle,achievement,combo,time;
    private final TextView[] judgments=new TextView[7];
    private final LinearLayout playing;
    private final ImageView coverView;
    private final Graph graphView;
    private JSONObject frame=new JSONObject();
    private String previous="";
    private Bitmap cover,graph;
    private boolean stale;
    private static final String[] KEYS={"critical","perfect","great","good","miss","fast","late"};
    private static final String[] LABELS={"Critical","Perfect","Great","Good","Miss","Fast","Late"};
    GameStatsView(Context c) {
        super(c);setOrientation(VERTICAL);setPadding(dp(20),dp(20),dp(20),dp(20));GameUi.surface(this);
        LinearLayout heading=new LinearLayout(c);heading.setGravity(Gravity.CENTER_VERTICAL);
        LinearLayout words=GameUi.column(c);
        state=GameUi.text(c,"",12,GameUi.BLUE,true);words.addView(state);GameUi.space(words,7);
        title=GameUi.text(c,"",21,GameUi.INK,true);title.setMaxLines(2);title.setEllipsize(android.text.TextUtils.TruncateAt.END);words.addView(title);
        subtitle=GameUi.text(c,"",13,GameUi.MUTED,false);subtitle.setPadding(0,dp(7),dp(12),0);words.addView(subtitle);
        heading.addView(words,new LayoutParams(0,-2,1));
        coverView=new ImageView(c);coverView.setScaleType(ImageView.ScaleType.CENTER_CROP);coverView.setImportantForAccessibility(IMPORTANT_FOR_ACCESSIBILITY_NO);heading.addView(coverView,new LayoutParams(dp(64),dp(64)));addView(heading);
        playing=GameUi.column(c);GameUi.space(playing,18);
        achievement=GameUi.text(c,"",34,GameUi.BLUE,true);playing.addView(achievement);
        combo=GameUi.text(c,"",13,GameUi.MUTED,false);combo.setPadding(0,dp(4),0,dp(16));playing.addView(combo);GameUi.divider(playing);GameUi.space(playing,14);
        for(int row=0;row<2;row++){
            LinearLayout line=new LinearLayout(c);int start=row==0?0:4,end=row==0?4:7;
            for(int i=start;i<end;i++){
                LinearLayout cell=GameUi.column(c);cell.addView(GameUi.text(c,LABELS[i],12,GameUi.JUDGMENT[i],true));GameUi.space(cell,5);
                judgments[i]=GameUi.text(c,"",21,GameUi.INK,true);cell.addView(judgments[i]);line.addView(cell,new LayoutParams(0,-2,1));
            }
            playing.addView(line);GameUi.space(playing,12);
        }
        time=GameUi.text(c,"",12,GameUi.MUTED,false);playing.addView(time);GameUi.space(playing,8);
        graphView=new Graph(c);playing.addView(graphView,new LayoutParams(-1,dp(42)));addView(playing);playing.setVisibility(GONE);
    }
    private int dp(int n){return GameUi.dp(getContext(),n);}
    void artwork(Bitmap cover,Bitmap graph){this.cover=cover;this.graph=graph;coverView.setImageBitmap(frame.optBoolean("game")?cover:GameAssets.mascot(getContext()));graphView.invalidate();}
    void update(String json,boolean stale){
        if(previous.equals(json)&&this.stale==stale)return;
        try{frame=new JSONObject(json);previous=json;}catch(Exception ignored){frame=new JSONObject();}
        this.stale=stale;boolean game=frame.optBoolean("game",false);
        GameUi.setText(state,stale?GameUi.tr("통계 연결 대기","等待统计连接"):game?GameUi.tr("지금 플레이 중","正在游玩"):GameUi.tr("플레이 대기","等待游玩"));
        GameUi.setText(title,game?frame.optString("title"):scene(frame.optString("scene","Waiting")));
        GameUi.setText(subtitle,game?frame.optString("artist")+" · "+frame.optString("level"):GameUi.tr("곡을 시작하면 점수와 판정을 보여드려요.","开始曲目后显示成绩和判定。"));
        playing.setVisibility(game?VISIBLE:GONE);coverView.setImageBitmap(game?cover:GameAssets.mascot(getContext()));
        coverView.setScaleType(game?ImageView.ScaleType.CENTER_CROP:ImageView.ScaleType.FIT_CENTER);
        if(game){
            GameUi.setText(achievement,String.format(Locale.US,"%.4f%%",frame.optDouble("achievement",0)));
            GameUi.setText(combo,"COMBO  "+frame.optLong("combo")+"     DX  "+frame.optLong("dx"));
            for(int i=0;i<7;i++)GameUi.setText(judgments[i],String.valueOf(frame.optLong(KEYS[i])));
            GameUi.setText(time,time(frame.optDouble("seconds",0))+" / "+time(frame.optDouble("length",0)));
            graphView.invalidate();
        }
        setContentDescription(readableDetails());
    }
    private static String time(double value){int sec=Math.max(0,(int)value);return String.format(Locale.US,"%d:%02d",sec/60,sec%60);}
    String readableDetails(){
        if(!frame.optBoolean("game",false))return scene(frame.optString("scene","Waiting"))+"\n\n"+GameUi.tr("게임을 시작하면 곡 정보와 판정 통계가 표시됩니다.","开始游戏后显示歌曲信息和判定统计。");
        StringBuilder d=new StringBuilder(frame.optString("title")).append('\n').append(frame.optString("artist")).append("\n\n");
        d.append("ACHIEVEMENT  ").append(String.format(Locale.US,"%.4f%%",frame.optDouble("achievement",0))).append("\nCOMBO  ").append(frame.optLong("combo")).append("\nDX  ").append(frame.optLong("dx")).append("\n\n");
        for(int i=0;i<7;i++)d.append(LABELS[i]).append("  ").append(frame.optLong(KEYS[i])).append('\n');
        return d.append("\n").append(time(frame.optDouble("seconds",0))).append(" / ").append(time(frame.optDouble("length",0))).toString();
    }
    private String scene(String name){switch(name){case "Title":return GameUi.tr("타이틀 화면","标题画面");case "List":return GameUi.tr("곡 선택","选择曲目");case "SortFind":return GameUi.tr("정렬 · 검색","排序 · 搜索");case "Setting":return GameUi.tr("게임 설정","游戏设置");case "Result":return GameUi.tr("플레이 결과","游玩结果");case "Login":return GameUi.tr("로그인","登录");default:return GameUi.tr("게임 대기","等待游戏");}}
    private final class Graph extends View{
        final Paint p=new Paint(Paint.ANTI_ALIAS_FLAG|Paint.FILTER_BITMAP_FLAG);
        Graph(Context c){super(c);setImportantForAccessibility(IMPORTANT_FOR_ACCESSIBILITY_NO);}
        @Override protected void onDraw(Canvas c){super.onDraw(c);p.setColor(GameUi.LINE);
            if(graph!=null)c.drawBitmap(graph,null,new RectF(0,0,getWidth(),getHeight()),p);
            else c.drawRoundRect(0,getHeight()/2f-2,getWidth(),getHeight()/2f+2,2,2,p);
            double length=frame.optDouble("length",0);float progress=(float)Math.max(0,Math.min(1,length>0?frame.optDouble("seconds",0)/length:0));
            p.setColor(GameUi.PINK);p.setStrokeWidth(dp(2));c.drawLine(progress*getWidth(),0,progress*getWidth(),getHeight(),p);
        }
    }
}
