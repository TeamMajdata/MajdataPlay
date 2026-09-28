package net.majdata.majdataplay.oniimai;

import android.content.SharedPreferences;
import android.graphics.*;
import android.graphics.drawable.Drawable;
import android.view.*;
import android.widget.*;
import java.util.*;

/** In-app widget board. Editing uses a private draft; only Save changes the stored board. */
final class DashboardView extends LinearLayout implements SharedPreferences.OnSharedPreferenceChangeListener {
    static final String KEY = "dashboardLayoutV1";
    private static final String DRAFT_KEY = KEY + "Draft";
    private static final String[] TYPES = {"song","score","judgments","timing","graph","sensors","connection","clock"};
    private final OniimaiController owner;
    private final boolean preview;
    private final Runnable close;
    private final LinearLayout header, toolbar;
    private final TextView hint;
    private final ScrollView scroll;
    private final Board board;
    private DashboardLayout layout;
    private boolean editing;
    private int viewportHeight;
    private int selected = -1;
    private String previousJson = "";
    private org.json.JSONObject statsFrame = new org.json.JSONObject();
    private final Runnable refresh = new Runnable() {
        public void run() { owner.display.updateDashboard(DashboardView.this); postOnAnimationDelayed(this, 150); }
    };

    DashboardView(OniimaiController owner, boolean preview, Runnable close) {
        super(owner.activity); this.owner=owner; this.preview=preview; this.close=close;
        editing=owner.prefs.contains(DRAFT_KEY);
        layout=DashboardLayout.parse(owner.prefs.getString(editing?DRAFT_KEY:KEY,""));
        setOrientation(VERTICAL); setBackground(new GameUi.Pattern()); setClickable(true);
        if(android.os.Build.VERSION.SDK_INT>=29)setForceDarkAllowed(false);
        if(android.os.Build.VERSION.SDK_INT>=30)setOnApplyWindowInsetsListener((v,insets)->{
            Insets safe=insets.getInsets(WindowInsets.Type.systemBars()|WindowInsets.Type.displayCutout());
            setPadding(safe.left,safe.top,safe.right,safe.bottom); return insets;
        });
        header=new LinearLayout(getContext());header.setGravity(Gravity.CENTER_VERTICAL);
        header.setPadding(dp(18),dp(14),dp(18),dp(10));addView(header);
        hint=GameUi.text(getContext(),"",12,GameUi.MUTED,false);
        hint.setPadding(dp(20),0,dp(20),dp(12));addView(hint);
        scroll=new ScrollView(getContext()){
            @Override protected void onMeasure(int ws,int hs){
                viewportHeight=Math.max(0,MeasureSpec.getSize(hs)-getPaddingTop()-getPaddingBottom());
                super.onMeasure(ws,hs);
            }
        };scroll.setFillViewport(true);scroll.setClipToPadding(true);
        scroll.setPadding(dp(16),dp(6),dp(16),dp(22));scroll.setVerticalScrollBarEnabled(true);
        scroll.setScrollBarStyle(View.SCROLLBARS_OUTSIDE_OVERLAY);
        scroll.setVerticalFadingEdgeEnabled(true);scroll.setFadingEdgeLength(dp(18));
        board=new Board();scroll.addView(board,new ScrollView.LayoutParams(-1,-2));
        addView(scroll,new LinearLayout.LayoutParams(-1,0,1));
        toolbar=new LinearLayout(getContext());toolbar.setGravity(Gravity.CENTER_VERTICAL);
        toolbar.setPadding(dp(16),dp(10),dp(16),dp(12));toolbar.setBackgroundColor(0xfafffaf0);addView(toolbar);
        chrome();board.rebuild();
    }
    private int dp(float n){return GameUi.dp(getContext(),n);}
    static String title(String type){switch(type){
        case "song":return GameUi.tr("곡 정보","歌曲信息");
        case "score":return GameUi.tr("점수 · 콤보","成绩 · 连击");
        case "judgments":return GameUi.tr("판정 통계","判定统计");
        case "timing":return "FAST / LATE";
        case "graph":return GameUi.tr("곡 진행 · 밀도","进度 · 密度");
        case "sensors":return GameUi.tr("터치 · 버튼","触摸 · 按钮");
        case "connection":return GameUi.tr("장치 상태","设备状态");
        default:return GameUi.tr("시계","时钟");
    }}
    private static String description(String type){switch(type){
        case "song":return GameUi.tr("재킷, 곡명, 아티스트와 난이도","封面、曲名、艺术家和难度");
        case "score":return GameUi.tr("달성률, 콤보와 잔여 DX 점수","达成率、连击和剩余 DX 分数");
        case "judgments":return GameUi.tr("Critical부터 Miss까지 실시간 판정","实时显示 Critical 至 Miss 判定");
        case "timing":return GameUi.tr("빠른 판정과 늦은 판정 비교","比较偏快和偏慢的判定");
        case "graph":return GameUi.tr("재생 시간과 채보 밀도 그래프","播放时间和谱面密度图");
        case "sensors":return GameUi.tr("실제 34개 터치 영역과 버튼 입력","34 个真实触摸区和按钮状态");
        case "connection":return GameUi.tr("USB 입력, LED와 외부 화면 상태","USB 输入、LED 和外接屏幕状态");
        default:return GameUi.tr("현재 시간과 날짜","当前时间和日期");
    }}
    private void chrome(){
        header.removeAllViews();toolbar.removeAllViews();
        LinearLayout words=GameUi.column(getContext());
        TextView caption=GameUi.text(getContext(),"MajdataPlay  /  Oniimai",12,GameUi.BLUE,true);caption.setPadding(0,0,0,dp(5));words.addView(caption);
        words.addView(GameUi.text(getContext(),editing?GameUi.tr("위젯 편집","编辑小组件"):GameUi.tr("내 대시보드","我的仪表盘"),26,GameUi.SLATE,true));
        header.addView(words,new LinearLayout.LayoutParams(0,-2,1));
        TextView action=GameUi.action(getContext(),editing?GameUi.tr("저장","保存"):GameUi.tr("편집","编辑"),true,()->{if(editing)save();else edit(-1);});
        action.setContentDescription(editing?GameUi.tr("위젯 배치 저장","保存小组件布局"):GameUi.tr("위젯 편집 시작","开始编辑小组件"));header.addView(action);
        hint.setText(editing?GameUi.tr("2×2 · 4×2 · 4×4  /  끌어서 이동 · 모서리로 크기 변경","2×2 · 4×2 · 4×4  /  拖动移动 · 拖角调整大小"):
            preview?GameUi.tr("미리보기 · 저장한 배치는 외부 모니터 연결 시에도 사용됩니다","预览 · 保存的布局也用于外接显示器模式"):
            GameUi.tr("길게 눌러 원하는 위젯을 배치하세요","长按即可自定义小组件布局"));
        hint.setVisibility(editing||preview?View.VISIBLE:View.GONE);
        if(editing){
            footer(GameUi.tr("＋ 위젯","＋ 小组件"),this::catalog,true);
            footer(GameUi.tr("배치","布局"),this::arrange,false);
            footer(GameUi.tr("취소","取消"),this::finish,false);
        }else{
            footer(preview?GameUi.tr("‹ 돌아가기","‹ 返回"):GameUi.tr("폰으로 전환","切换到手机"),close,false);
            if(!preview)footer(GameUi.tr("설정","设置"),owner.panel::open,false);
        }
    }
    private void footer(String label,Runnable action,boolean primary){
        TextView v=GameUi.action(getContext(),label,primary,action);v.setTextSize(14);v.setPadding(dp(8),dp(12),dp(8),dp(12));
        LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(0,-2,1);if(toolbar.getChildCount()>0)lp.leftMargin=dp(8);toolbar.addView(v,lp);
    }
    private void edit(int id){if(!editing){layout=layout.copy();editing=true;}selected=id;chrome();board.rebuild();}
    private void save(){owner.prefs.edit().putString(KEY,layout.encode()).apply();finish();toast(GameUi.tr("위젯 배치를 저장했습니다","已保存小组件布局"));}
    private void finish(){editing=false;owner.prefs.edit().remove(DRAFT_KEY).apply();selected=-1;layout=DashboardLayout.parse(owner.prefs.getString(KEY,""));chrome();board.rebuild();}
    boolean back(){
        if(!editing)return false;
        owner.panel.choose(GameUi.tr("편집을 마칠까요?","结束编辑？"),new String[]{GameUi.tr("저장하고 마치기","保存并结束"),GameUi.tr("변경 취소","放弃更改"),GameUi.tr("계속 편집","继续编辑")},-1,i->{if(i==0)save();else if(i==1)finish();});return true;
    }
    private void catalog(){
        String[] options=new String[TYPES.length];
        for(int i=0;i<TYPES.length;i++){
            StringBuilder dimensions=new StringBuilder();for(int[] size:DashboardLayout.sizes(TYPES[i])){if(dimensions.length()>0)dimensions.append("  ·  ");dimensions.append(size[0]).append("×").append(size[1]);}
            options[i]=title(TYPES[i])+"  ·  "+dimensions+"\n"+description(TYPES[i]);
        }
        owner.panel.choose(GameUi.tr("위젯 추가","添加小组件"),options,-1,i->{
            DashboardLayout.Item item=layout.add(TYPES[i]);
            if(item==null){toast(GameUi.tr("위젯은 최대 20개까지 배치할 수 있습니다","最多可放置 20 个小组件"));return;}
            selected=item.id;board.rebuild();board.post(()->scroll.smoothScrollTo(0,board.rowTop(item.y)));
        });
    }
    private void arrange(){
        owner.panel.choose(GameUi.tr("배치 도구","布局工具"),new String[]{GameUi.tr("전체 2×2 배치","全部设为 2×2"),GameUi.tr("빈 공간 정리","整理空白"),GameUi.tr("기본 배치","默认布局"),GameUi.tr("플레이 집중 배치","游玩专注布局"),GameUi.tr("입력 테스트 배치","输入测试布局"),GameUi.tr("모든 위젯 비우기","清空所有小组件")},-1,i->{
            if(i==0)layout.compact();
            else if(i==1)layout.pack();
            else if(i==2)layout=DashboardLayout.defaults();
            else{
                for(DashboardLayout.Item item:new ArrayList<>(layout.items()))layout.remove(item.id);
                String[] types=i==3?new String[]{"song","score","timing","judgments","graph"}:i==4?new String[]{"connection","sensors","clock"}:new String[0];
                for(String type:types)layout.add(type);
            }
            selected=-1;board.rebuild();scroll.smoothScrollTo(0,0);
        });
    }
    private void options(int id){
        DashboardLayout.Item item=layout.get(id);if(item==null)return;select(id);
        owner.panel.choose(title(item.type),new String[]{GameUi.tr("크기 변경","调整大小")+"  ·  "+item.w+" × "+item.h,GameUi.tr("앞으로 이동","向前移动"),GameUi.tr("뒤로 이동","向后移动"),GameUi.tr("하나 더 추가","再添加一个"),GameUi.tr("위젯 삭제","删除小组件")},-1,i->{
            if(i==0){sizes(id);return;}
            if(i==1||i==2){if(!layout.moveBy(id,i==1?-1:1))toast(GameUi.tr("더 이동할 수 없습니다","无法继续移动"));}
            else if(i==3){DashboardLayout.Item added=layout.add(item.type);if(added!=null)selected=added.id;else toast(GameUi.tr("위젯 개수 또는 공간이 부족합니다","小组件数量已达上限或空间不足"));}
            else layout.remove(id);
            board.rebuild();
        });
    }
    private void sizes(int id){
        DashboardLayout.Item item=layout.get(id);if(item==null)return;
        int[][] sizes=DashboardLayout.sizes(item.type);String[] names=new String[sizes.length];int checked=-1;
        for(int n=0;n<sizes.length;n++){int[] s=sizes[n];names[n]=s[0]+" × "+s[1]+"  ·  "+(s[0]!=s[1]?GameUi.tr("가로형","横向"):s[0]==4?GameUi.tr("큰 정사각형","大方形"):GameUi.tr("정사각형","方形"));if(item.w==s[0]&&item.h==s[1])checked=n;}
        owner.panel.choose(GameUi.tr("위젯 크기","小组件大小"),names,checked,i->{
            int[] size=sizes[i];if(!resizeAndPack(id,size[0],size[1]))toast(GameUi.tr("공간이 부족합니다. 다른 위젯을 이동해 주세요","空间不足，请移动其他小组件"));board.rebuild();
        });
    }
    private boolean resizeAndPack(int id,int w,int h){
        return layout.resizeAndPack(id,w,h);
    }
    private void toast(String message){Toast.makeText(getContext(),message,Toast.LENGTH_SHORT).show();}
    private void select(int id){selected=id;for(int n=0;n<board.getChildCount();n++)board.getChildAt(n).invalidate();}
    void update(String json,boolean stale,Bitmap cover,Bitmap graph){
        if(json==null)json="{}";
        if(!previousJson.equals(json)){
            try{statsFrame=new org.json.JSONObject(json);}catch(org.json.JSONException error){statsFrame=new org.json.JSONObject();}
            previousJson=json;
        }
        // Immutable for this update: every tile shares one parsed stats frame.
        for(int i=0;i<board.getChildCount();i++)((Tile)board.getChildAt(i)).content.update(statsFrame,stale,cover,graph);
    }
    @Override public void onSharedPreferenceChanged(SharedPreferences p,String key){if(KEY.equals(key)&&!editing){layout=DashboardLayout.parse(p.getString(KEY,""));board.rebuild();}}
    @Override protected void onAttachedToWindow(){super.onAttachedToWindow();owner.prefs.registerOnSharedPreferenceChangeListener(this);post(refresh);requestApplyInsets();}
    @Override protected void onDetachedFromWindow(){if(editing)owner.prefs.edit().putString(DRAFT_KEY,layout.encode()).apply();removeCallbacks(refresh);owner.prefs.unregisterOnSharedPreferenceChangeListener(this);super.onDetachedFromWindow();}

    private final class Board extends ViewGroup {
        private final Paint paint=new Paint(Paint.ANTI_ALIAS_FLAG);
        private float cellWidth,cellHeight,gridLeft;
        private final int gap=dp(10);
        private int ghostX,ghostY,ghostW,ghostH;
        private boolean ghost,valid;
        Board(){super(DashboardView.this.getContext());setWillNotDraw(false);setClipChildren(false);setClipToPadding(false);setOnLongClickListener(v->{edit(-1);return true;});}
        int rowTop(int row){return Math.round(row*(cellHeight+gap));}
        private boolean compactBoard(){
            java.util.List<DashboardLayout.Item> items=layout.items();
            if(items.isEmpty()||layout.rows()!=2*((items.size()+1)/2))return false;
            for(DashboardLayout.Item item:items)if(item.w!=2||item.h!=2||(item.x!=0&&item.x!=2)||item.y%2!=0)return false;
            return true;
        }
        void rebuild(){removeAllViews();ghost=false;for(DashboardLayout.Item item:layout.items())addView(new Tile(item));requestLayout();invalidate();owner.display.updateDashboard(DashboardView.this);}
        @Override protected void onMeasure(int ws,int hs){
            int width=MeasureSpec.getSize(ws);cellWidth=Math.max(1,(width-gap*3)/4f);
            gridLeft=0;
            // Recover the last whole row when only a small width reduction is needed.
            // Editing and deliberately spaced layouts retain their normal cell geometry.
            if(!editing&&compactBoard()&&viewportHeight>0){
                float fitCell=(viewportHeight-2-(layout.rows()-1)*gap)/(float)layout.rows();
                float fitWidth=fitCell*4+gap*3;
                if(fitCell<cellWidth&&fitWidth>=width*.88f&&fitCell*2+gap>=dp(148)){
                    cellWidth=fitCell;gridLeft=(width-fitWidth)/2;
                }
            }
            cellHeight=cellWidth;
            for(int i=0;i<getChildCount();i++){
                Tile tile=(Tile)getChildAt(i);DashboardLayout.Item item=layout.get(tile.id);
                tile.measure(MeasureSpec.makeMeasureSpec(Math.round(item.w*cellWidth+(item.w-1)*gap),MeasureSpec.EXACTLY),MeasureSpec.makeMeasureSpec(Math.round(item.h*cellHeight+(item.h-1)*gap),MeasureSpec.EXACTLY));
            }
            int rows=Math.max(editing?5:1,layout.rows()+(editing?4:0));
            setMeasuredDimension(width,Math.max(MeasureSpec.getSize(hs),rowTop(rows)-gap));
        }
        @Override protected void onLayout(boolean changed,int l,int t,int r,int b){
            for(int i=0;i<getChildCount();i++){Tile tile=(Tile)getChildAt(i);DashboardLayout.Item item=layout.get(tile.id);int x=Math.round(gridLeft+item.x*(cellWidth+gap)),y=rowTop(item.y);tile.layout(x,y,x+tile.getMeasuredWidth(),y+tile.getMeasuredHeight());}
        }
        @Override protected void onDraw(Canvas canvas){
            super.onDraw(canvas);
            if(editing){paint.setColor(0xffd1dbe4);for(float y=cellHeight/2;y<getHeight();y+=cellHeight+gap)for(float x=gridLeft+cellWidth/2;x<getWidth()-gridLeft;x+=cellWidth+gap)canvas.drawCircle(x,y,dp(2),paint);}
            if(getChildCount()==0){paint.setColor(GameUi.MUTED);paint.setTypeface(GameAssets.font(getContext()));paint.setTextSize(dp(16));paint.setTextAlign(Paint.Align.CENTER);String text=GameUi.tr("위젯을 추가해 나만의 화면을 만드세요","添加小组件，打造专属界面");float width=paint.measureText(text);if(width>getWidth()-dp(16))paint.setTextSize(paint.getTextSize()*(getWidth()-dp(16))/width);canvas.drawText(text,getWidth()/2f,dp(95),paint);}
        }
        @Override protected void dispatchDraw(Canvas canvas){
            super.dispatchDraw(canvas);if(!ghost)return;
            RectF target=new RectF(gridLeft+ghostX*(cellWidth+gap),rowTop(ghostY),gridLeft+ghostX*(cellWidth+gap)+ghostW*cellWidth+(ghostW-1)*gap,rowTop(ghostY)+ghostH*cellHeight+(ghostH-1)*gap);
            paint.setStyle(Paint.Style.FILL);paint.setColor(valid?0x30466f97:0x30b53d49);canvas.drawRoundRect(target,dp(22),dp(22),paint);
            paint.setStyle(Paint.Style.STROKE);paint.setStrokeWidth(dp(2));paint.setColor(valid?GameUi.BLUE:GameUi.ERROR);canvas.drawRoundRect(target,dp(22),dp(22),paint);paint.setStyle(Paint.Style.FILL);
        }
        void propose(int id,int x,int y,int w,int h,boolean resizing){
            ghost=true;ghostX=x;ghostY=y;ghostW=w;ghostH=h;
            DashboardLayout test=layout.copy();valid=resizing?test.resizeAndPack(id,w,h):test.move(id,x,y);
            if(valid&&resizing){DashboardLayout.Item target=test.get(id);ghostX=target.x;ghostY=target.y;}invalidate();
        }
    }
    private final class Tile extends FrameLayout {
        final int id;
        final DashboardWidget content;
        private final Paint pen=new Paint(Paint.ANTI_ALIAS_FLAG);
        private float downX,downY,rawX,rawY;
        private int initialScroll;
        private boolean dragging,resizing;
        private final int[] scrollLocation=new int[2];
        private final Runnable edgeScroll=new Runnable(){public void run(){
            if(!dragging||!isAttachedToWindow())return;
            scroll.getLocationOnScreen(scrollLocation);float y=rawY-scrollLocation[1];
            int before=scroll.getScrollY();if(y<dp(70))scroll.scrollBy(0,-dp(12));else if(y>scroll.getHeight()-dp(70))scroll.scrollBy(0,dp(12));
            if(before!=scroll.getScrollY())updateGesture();postDelayed(this,32);
        }};
        private void updateGesture(){
            DashboardLayout.Item item=layout.get(id);if(item==null)return;
            float dx=rawX-downX,dy=rawY-downY+scroll.getScrollY()-initialScroll;
            setAlpha(.65f);setElevation(dp(6));
            if(resizing){int[] size=DashboardLayout.nearestSize(item.type,item.w+dx/(board.cellWidth+board.gap),item.h+dy/(board.cellHeight+board.gap));board.propose(id,item.x,item.y,size[0],size[1],true);}
            else{int x=Math.max(0,Math.min(4-item.w,item.x+Math.round(dx/(board.cellWidth+board.gap))));int y=Math.max(0,Math.min(DashboardLayout.MAX_ROWS-item.h,item.y+Math.round(dy/(board.cellHeight+board.gap))));board.propose(id,x,y,item.w,item.h,false);setTranslationX(dx);setTranslationY(dy);}
        }
        Tile(DashboardLayout.Item item){
            super(DashboardView.this.getContext());id=item.id;setWillNotDraw(false);GameUi.surface(this);
            setBackground(new CardSkin(id));
            setElevation(dp(editing&&selected==id?3:1));
            TextView title=GameUi.text(getContext(),DashboardView.title(item.type),13,GameUi.TAB,true);title.setSingleLine();title.setEllipsize(android.text.TextUtils.TruncateAt.END);
            FrameLayout.LayoutParams titleLp=new FrameLayout.LayoutParams(-1,dp(38));titleLp.leftMargin=dp(14);titleLp.rightMargin=dp(editing?68:14);title.setGravity(Gravity.CENTER_VERTICAL);addView(title,titleLp);
            content=new DashboardWidget(getContext(),item.type,owner);content.setGridSize(item.w,item.h);FrameLayout.LayoutParams body=new FrameLayout.LayoutParams(-1,-1);body.setMargins(dp(14),dp(38),dp(14),dp(14));addView(content,body);
            setFocusable(true);setClickable(true);setContentDescription(DashboardView.title(item.type)+" · "+item.w+" × "+item.h);
            setOnLongClickListener(v->{edit(id);performHapticFeedback(HapticFeedbackConstants.LONG_PRESS);return true;});
            setOnClickListener(v->{if(editing)options(id);});
            setAccessibilityDelegate(new View.AccessibilityDelegate(){
                @Override public void onInitializeAccessibilityNodeInfo(View host,android.view.accessibility.AccessibilityNodeInfo info){super.onInitializeAccessibilityNodeInfo(host,info);info.addAction(new android.view.accessibility.AccessibilityNodeInfo.AccessibilityAction(android.view.accessibility.AccessibilityNodeInfo.ACTION_LONG_CLICK,GameUi.tr("위젯 편집","编辑小组件")));}
            });
        }
        @Override public boolean onInterceptTouchEvent(android.view.MotionEvent event){return editing||super.onInterceptTouchEvent(event);}
        @Override public boolean onTouchEvent(android.view.MotionEvent event){
            if(!editing)return super.onTouchEvent(event);
            DashboardLayout.Item item=layout.get(id);if(item==null)return true;
            switch(event.getActionMasked()){
                case MotionEvent.ACTION_DOWN:
                    removeCallbacks(edgeScroll);select(id);rawX=downX=event.getRawX();rawY=downY=event.getRawY();initialScroll=scroll.getScrollY();dragging=false;
                    resizing=event.getX()>getWidth()-dp(40)&&event.getY()>getHeight()-dp(40);
                    getParent().requestDisallowInterceptTouchEvent(true);return true;
                case MotionEvent.ACTION_MOVE:
                    rawX=event.getRawX();rawY=event.getRawY();float dx=rawX-downX,dy=rawY-downY+scroll.getScrollY()-initialScroll;
                    if(!dragging&&Math.hypot(dx,dy)<ViewConfiguration.get(getContext()).getScaledTouchSlop())return true;
                    if(!dragging){dragging=true;post(edgeScroll);}updateGesture();
                    return true;
                case MotionEvent.ACTION_UP:
                    removeCallbacks(edgeScroll);getParent().requestDisallowInterceptTouchEvent(false);
                    if(dragging){boolean done=board.valid&&(resizing?layout.resizeAndPack(id,board.ghostW,board.ghostH):layout.move(id,board.ghostX,board.ghostY));if(!done)toast(GameUi.tr("빈칸에 놓거나 위젯 옵션에서 크기·순서를 바꿔 주세요","请放到空白处，或在选项中调整大小和顺序"));board.rebuild();}
                    else performClick();return true;
                case MotionEvent.ACTION_CANCEL:
                    removeCallbacks(edgeScroll);dragging=false;resizing=false;getParent().requestDisallowInterceptTouchEvent(false);setAlpha(1);setElevation(dp(1));setTranslationX(0);setTranslationY(0);board.ghost=false;board.invalidate();return true;
                default:return true;
            }
        }
        @Override public boolean performClick(){return super.performClick();}
        @Override protected void onDetachedFromWindow(){removeCallbacks(edgeScroll);super.onDetachedFromWindow();}
        @Override protected void dispatchDraw(Canvas canvas){
            super.dispatchDraw(canvas);if(!editing)return;
            pen.setColor(GameUi.BLUE);pen.setStrokeWidth(dp(2));pen.setStrokeCap(Paint.Cap.ROUND);
            float r=getWidth()-dp(13),b=getHeight()-dp(13);canvas.drawLine(r-dp(13),b,r,b-dp(13),pen);canvas.drawLine(r-dp(6),b,r,b-dp(6),pen);
            for(int n=0;n<3;n++)canvas.drawCircle(getWidth()-dp(17),dp(12+n*5),dp(1.3f),pen);
            DashboardLayout.Item item=layout.get(id);if(item!=null){pen.setTypeface(GameAssets.font(getContext()));pen.setTextSize(dp(11));pen.setTextAlign(Paint.Align.RIGHT);canvas.drawText(item.w+"×"+item.h,getWidth()-dp(31),dp(24),pen);}
        }
    }
    /** Cream panel and stitched inset echo the game's rectangular settings cards. */
    private final class CardSkin extends Drawable {
        private final Paint p=new Paint(Paint.ANTI_ALIAS_FLAG);
        private final int id;
        CardSkin(int id){this.id=id;}
        @Override public void draw(Canvas canvas){
            boolean selected=editing&&DashboardView.this.selected==id;
            Rect b=getBounds();float r=dp(22);p.setStyle(Paint.Style.FILL);p.setColor(0xfffffaf0);
            canvas.drawRoundRect(b.left,b.top,b.right,b.bottom,r,r,p);
            p.setStyle(Paint.Style.STROKE);p.setStrokeWidth(dp(selected?2:1));p.setColor(selected?GameUi.BLUE:0xffe4dccb);
            canvas.drawRoundRect(b.left+dp(1),b.top+dp(1),b.right-dp(1),b.bottom-dp(1),r,r,p);
            p.setColor(0xffe9d8b6);p.setStrokeWidth(dp(1));p.setPathEffect(new DashPathEffect(new float[]{dp(5),dp(5)},0));
            canvas.drawRoundRect(b.left+dp(7),b.top+dp(7),b.right-dp(7),b.bottom-dp(7),dp(16),dp(16),p);p.setPathEffect(null);
        }
        @Override public void setAlpha(int a){} @Override public void setColorFilter(ColorFilter f){} @Override public int getOpacity(){return PixelFormat.TRANSLUCENT;}
    }
}
