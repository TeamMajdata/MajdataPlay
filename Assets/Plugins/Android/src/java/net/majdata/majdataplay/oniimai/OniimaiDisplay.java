package net.majdata.majdataplay.oniimai;

import android.app.Presentation;
import android.content.Context;
import android.graphics.Color;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Canvas;
import android.graphics.Matrix;
import android.graphics.SurfaceTexture;
import android.graphics.drawable.GradientDrawable;
import android.hardware.display.DisplayManager;
import android.os.Bundle;
import android.os.Build;
import android.os.SystemClock;
import android.util.Log;
import android.view.*;
import android.widget.*;
import net.majdata.majdataplay.MajdataPlayActivity;
import java.lang.reflect.Method;

/** Routes Unity display 0 directly to a monitor surface; no PixelCopy, projection, or Vulkan hooks. */
final class OniimaiDisplay implements DisplayManager.DisplayListener {
    private static final String TAG = "OniimaiDisplay";
    private final OniimaiController owner;
    private final DisplayManager displays;
    private Monitor monitor;
    private DashboardView dashboard, previewDashboard;
    private android.app.Dialog previewDialog;
    private TextView outputShortcut;
    private Toast outputHint;
    private Bitmap coverArt, graphArt;
    private volatile String latestStats = "{}";
    private volatile long statsAt;
    private volatile boolean active;
    private boolean resumed = true, destroyed, overrideRequested;
    private int rejectedHighRateDisplay = -1;
    private String outputStatus = "외부 출력 꺼짐 / 外接输出关闭";
    OniimaiDisplay(OniimaiController owner) {
        this.owner = owner;
        displays = (DisplayManager)owner.activity.getSystemService(Context.DISPLAY_SERVICE);
        displays.registerDisplayListener(this, owner.main);
        owner.main.post(this::reconcile);
    }
    boolean active() { return active; }
    boolean hasMonitor(){return selectedDisplay()!=null;}
    int selectedFrameRate(){return OutputMode.frameRate(owner.prefs.getInt("frameRate",60));}
    String frameRateDescription(){
        Display d=selectedDisplay();
        return selectedFrameRate()+"fps"+(d==null?"":GameUi.tr(" · 실제 "," · 实际 ")+Math.round(d.getRefreshRate())+"Hz");
    }
    void chooseFrameRate(OniimaiPanel ui){
        ui.choose(GameUi.tr("프레임 고정","固定帧率"),new String[]{GameUi.tr("60fps · 기본","60fps · 默认"),"120fps"},selectedFrameRate()==120?1:0,i->{
            owner.prefs.edit().putInt("frameRate",i==1?120:60).apply();
            rejectedHighRateDisplay=-1;
            if(monitor!=null)monitor.applyMode(true);
        });
    }
    void refreshLanguage() {
        if(dashboard!=null) { ((ViewGroup)dashboard.getParent()).removeView(dashboard);dashboard=null;showDashboard(); }
        removeOutputShortcut();
        refreshOutputShortcut();
    }
    String outputDescription() {
        if(active)return outputStatus;
        return owner.prefs.getBoolean("external",true)?GameUi.tr("외부 모니터 연결 대기","等待外接显示器"):GameUi.tr("휴대폰에서 게임 출력","在手机上显示游戏");
    }
    void stats(String text) { latestStats = text; statsAt = SystemClock.uptimeMillis(); }
    void artwork(int kind, byte[] png) {
        Bitmap image = png == null || png.length == 0 || png.length > 2_000_000 ? null : BitmapFactory.decodeByteArray(png,0,png.length);
        if(kind==0) coverArt=image; else if(kind==1) graphArt=image;
        if(dashboard!=null)updateDashboard(dashboard);
        if(previewDashboard!=null)updateDashboard(previewDashboard);
    }
    void resume(boolean value) { resumed = value; reconcile(); }
    private Display selectedDisplay() {
        Display[] available = displays.getDisplays(DisplayManager.DISPLAY_CATEGORY_PRESENTATION);
        String selected = owner.prefs.getString("displayName", "");
        for (Display d : available) {
            if (d.getDisplayId() != Display.DEFAULT_DISPLAY && (selected.isEmpty() || selected.equals(d.getName()))) return d;
        }
        return null;
    }
    void reconcile() {
        if (destroyed) return;
        Display d = selectedDisplay();
        boolean enabled = owner.prefs.getBoolean("external", true) && resumed;
        if (!enabled || d == null) {
            closeMonitor();
            outputStatus = enabled ? "외부 출력 대기 · USB-C/HDMI 디스플레이 연결을 확인하세요" : "외부 출력 꺼짐 / 外接输出关闭";
            refreshOutputShortcut();
            return;
        }
        if (monitor != null && monitor.getDisplay().getDisplayId() == d.getDisplayId()) { monitor.rotate(); refreshOutputShortcut(); return; }
        closeMonitor();
        try {
            monitor = new Monitor(d);
            monitor.setOnDismissListener(dialog -> {
                if (monitor == dialog) { closeMonitor(); owner.main.post(this::reconcile); }
            });
            monitor.show();
            Log.i(TAG, "Presentation display=" + d.getDisplayId() + " " + d.getName() + " mode=" + d.getMode());
        } catch (RuntimeException ex) { closeMonitor(); outputStatus = "외부 출력 실패: " + ex.getMessage(); Log.e(TAG, outputStatus, ex); }
        refreshOutputShortcut();
    }
    private void bind(Surface surface) {
        overrideRequested = true;
        if (!((MajdataPlayActivity)owner.activity).oniimaiSurface(surface))
            throw new IllegalStateException("Unity surface not ready");
        active = true; owner.panel.refreshEntry(); showDashboard();
        refreshOutputShortcut();
    }
    private void restore() {
        if (overrideRequested) {
            ((MajdataPlayActivity)owner.activity).oniimaiSurface(null);
            overrideRequested = false;
            Log.i(TAG, "Unity surface restored to device");
        }
        active = false;
        if(!destroyed)owner.panel.refreshEntry();
        if (dashboard != null) { ((ViewGroup)dashboard.getParent()).removeView(dashboard); dashboard = null; }
        refreshOutputShortcut();
    }
    private void closeMonitor() {
        Monitor old = monitor; monitor = null;
        // Detach Unity's EGL producer before releasing either consumer route.
        restore();
        if (old != null) { old.releaseSurface(); old.dismiss(); }
    }
    private void restart() { closeMonitor(); reconcile(); }
    String deviceName(){Display d=selectedDisplay();return d==null?GameUi.tr("연결된 모니터 없음","未连接显示器"):d.getName();}
    void chooseMonitor(OniimaiPanel ui){
        Display[] list=displays.getDisplays(DisplayManager.DISPLAY_CATEGORY_PRESENTATION);String[] names=new String[list.length];int selected=-1;
        for(int i=0;i<list.length;i++){names[i]=list[i].getName();if(names[i].equals(owner.prefs.getString("displayName","")))selected=i;}
        ui.choose(GameUi.tr("외부 모니터 선택","选择外接显示器"),names,selected,i->{owner.prefs.edit().putString("displayName",names[i]).apply();restart();});
    }
    void updateDashboard(DashboardView view) {
        view.update(latestStats,SystemClock.uptimeMillis()-statsAt>2500,coverArt,graphArt);
    }
    boolean dashboardBack() { return dashboard!=null && dashboard.back(); }
    void previewDashboard() {
        if(previewDialog!=null)return;
        android.app.Dialog d=new android.app.Dialog(owner.activity,android.R.style.Theme_Material_Light_NoActionBar){
            @Override public void onBackPressed(){if(previewDashboard==null||!previewDashboard.back())super.onBackPressed();}
        };
        previewDialog=d;previewDashboard=new DashboardView(owner,true,d::dismiss);d.setContentView(previewDashboard);
        owner.panel.protect(d,()->{previewDialog=null;previewDashboard=null;});owner.panel.show(d,true);
    }
    private void showDashboard() {
        if(dashboard!=null)return;
        dashboard=new DashboardView(owner,false,this::switchToPhone);
        owner.activity.addContentView(dashboard,new ViewGroup.LayoutParams(-1,-1));dashboard.requestApplyInsets();
    }
    private void switchToPhone() {
        owner.prefs.edit().putBoolean("external",false).apply();
        reconcile();
        hint(GameUi.tr("‘외부 화면 켜기’를 누르면 모니터로 돌아갑니다. 버튼을 끌어서 옮길 수 있어요.",
                "点击“开启外接屏幕”即可返回显示器。拖动按钮可调整位置。"));
    }
    private void switchToMonitor() {
        Log.i(TAG,"Display shortcut requested: resumed="+resumed+" active="+active+" connecting="+(monitor!=null));
        if(destroyed || !resumed || active || monitor!=null)return;
        if(outputHint!=null)outputHint.cancel();
        owner.prefs.edit().putBoolean("external",true).apply();
        reconcile();
        if(selectedDisplay()==null)hint(GameUi.tr("외부 출력을 켰습니다. 모니터를 연결하면 자동으로 전환됩니다.",
                "已开启外接输出。连接显示器后将自动切换。"));
        else if(monitor==null)hint(GameUi.tr("외부 화면을 열지 못했습니다. 모니터 연결을 확인하고 다시 눌러 주세요.",
                "无法开启外接屏幕。请检查显示器连接后重试。"));
    }
    private void hint(String message) {
        if(outputHint!=null)outputHint.cancel();
        outputHint=Toast.makeText(owner.activity,message,Toast.LENGTH_LONG);
        outputHint.show();
    }
    private void refreshOutputShortcut() {
        boolean visible=!destroyed && resumed && !active
                && (!owner.prefs.getBoolean("external",true) || selectedDisplay()!=null);
        if(!visible) { if(outputShortcut!=null)outputShortcut.setVisibility(View.GONE); return; }
        if(outputShortcut==null) {
            outputShortcut=GameUi.action(owner.activity,"",true,this::switchToMonitor);
            outputShortcut.setSingleLine(true);
            outputShortcut.setMinHeight(dp(GameUi.TOUCH_DP));
            outputShortcut.setEllipsize(android.text.TextUtils.TruncateAt.END);
            outputShortcut.setElevation(dp(4));
            // Only this button owns touch events; the rest of the screen remains Unity's.
            // A separate row leaves room for the game's centered Oniimai settings entry.
            FrameLayout.LayoutParams lp=new FrameLayout.LayoutParams(-2,-2,Gravity.TOP|Gravity.RIGHT);
            lp.topMargin=dp(80);lp.rightMargin=dp(12);
            owner.activity.addContentView(outputShortcut,lp);
            FloatingShortcut.attach(outputShortcut,owner.prefs);
            outputShortcut.requestApplyInsets();
        }
        boolean connecting=monitor!=null;
        String title=connecting?GameUi.tr("외부 화면 연결 중…","正在连接外接屏幕…")
                :GameUi.tr("외부 화면 켜기","开启外接屏幕");
        GameUi.setText(outputShortcut,title);outputShortcut.setContentDescription(title);
        GameUi.enabled(outputShortcut,!connecting);
        outputShortcut.setVisibility(View.VISIBLE);outputShortcut.bringToFront();
    }
    private void removeOutputShortcut() {
        if(outputShortcut==null)return;
        if(outputShortcut.getParent() instanceof ViewGroup)((ViewGroup)outputShortcut.getParent()).removeView(outputShortcut);
        outputShortcut=null;
    }
    private int dp(int n) { return Math.round(n * owner.activity.getResources().getDisplayMetrics().density); }
    private TextView label(LinearLayout parent, String text, int size) {
        TextView t = new TextView(owner.activity); t.setText(text); t.setTextSize(size); t.setTextColor(Color.rgb(29, 33, 43)); t.setPadding(0, dp(10), 0, dp(14)); parent.addView(t); return t;
    }
    private void card(TextView text) {
        GradientDrawable bg = new GradientDrawable(); bg.setColor(Color.WHITE); bg.setCornerRadius(dp(22)); text.setBackground(bg); text.setPadding(dp(20), dp(20), dp(20), dp(20));
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(-1, -2); lp.setMargins(0, dp(14), 0, dp(12)); text.setLayoutParams(lp);
    }
    void addSettings(OniimaiPanel ui, LinearLayout body) {
        LinearLayout c=GameUi.card(body,ui.tr("외부 모니터","外接显示器"),ui.tr("가로 모니터에 세로 게임 화면을 회전해 출력합니다.","将竖屏游戏旋转后铺满横向显示器。"));
        ui.toggle(c, ui.tr("외부 화면으로 게임 출력", "将游戏输出到外接屏幕"), "external", true, value -> reconcile());
        ui.watchRow(c,ui.tr("출력 장치","输出设备"),()->{Display d=selectedDisplay();return d==null?ui.tr("연결된 모니터 없음","未连接显示器"):d.getName();}, () -> {
            Display[] list = displays.getDisplays(DisplayManager.DISPLAY_CATEGORY_PRESENTATION);
            String[] names = new String[list.length]; for (int i = 0; i < list.length; i++) names[i] = list[i].getName();
            ui.choose(ui.tr("외부 모니터 선택","选择外接显示器"), names, -1, i -> { owner.prefs.edit().putString("displayName", names[i]).apply(); restart(); });
        });
        c=GameUi.card(body,ui.tr("게임 방향","游戏方向"),"1080 × 1920  →  16:9");
        final DisplayPreview preview=new DisplayPreview(owner.activity,()->owner.prefs.getBoolean("displayReverse",false));c.addView(preview,new LinearLayout.LayoutParams(-1,-2));
        ui.watchRow(c,ui.tr("회전 방향","旋转方向"),()->owner.prefs.getBoolean("displayReverse",false)?ui.tr("역방향 세로 · 270°","反向竖屏 · 270°"):ui.tr("세로 · 90°","竖屏 · 90°"), () -> ui.choose(ui.tr("회전 방향","旋转方向"),
                new String[]{ui.tr("세로 · 90°", "竖屏 · 90°"), ui.tr("역방향 세로 · 270°", "反向竖屏 · 270°")}, owner.prefs.getBoolean("displayReverse", false) ? 1 : 0,
                i -> { owner.prefs.edit().putBoolean("displayReverse", i == 1).apply(); if (monitor != null) monitor.rotate();preview.invalidate(); }));
        ui.text(c,ui.tr("방향은 즉시 적용되고 저장됩니다. 폰에는 스탯과 센서 상태가 표시됩니다.","方向立即生效并保存，手机显示统计和传感器状态。"));
        c=GameUi.card(body,ui.tr("현재 출력","当前输出"),null);
        ui.watchRow(c,ui.tr("해상도 · 주사율","分辨率 · 刷新率"),this::outputDescription,()->ui.info(ui.tr("출력 해상도","输出分辨率"),ui.tr("게임은 1080×1920으로 렌더링합니다. HDMI 해상도와 주사율은 폰·허브·모니터가 함께 지원하는 모드를 사용합니다. 16:9 모니터에서 전체 영상을 채웁니다.","游戏以 1080×1920 渲染。HDMI 分辨率和刷新率取决于手机、扩展坞和显示器共同支持的模式，完整画面铺满 16:9 显示器。")));
    }
    private final class Monitor extends Presentation implements TextureView.SurfaceTextureListener, SurfaceHolder.Callback2 {
        private FrameLayout root;
        private TextureView texture;
        private SurfaceView anchor;
        private SurfaceControl gameLayer;
        private Method layerMatrix, layerPosition;
        private boolean direct = Build.VERSION.SDK_INT >= 29, fallbackPending;
        private Surface unitySurface;
        private boolean firstFrame;
        private int appliedWidth, appliedHeight;
        private boolean appliedReverse;
        private float requestedRate = Float.NaN;
        private int modeRequest = -1;
        private Runnable modeCheck;
        private String lastGeometry = "";
        Monitor(Display display) { super(owner.activity, display, android.R.style.Theme_Material_NoActionBar_Fullscreen); }
        @Override protected void onCreate(Bundle bundle) {
            super.onCreate(bundle);
            requestWindowFeature(Window.FEATURE_NO_TITLE);
            Window window = getWindow();
            window.setTitle("MajdataPlay Oniimai External");
            window.setBackgroundDrawableResource(android.R.color.black);
            window.getDecorView().setPadding(0, 0, 0, 0);
            window.addFlags(WindowManager.LayoutParams.FLAG_HARDWARE_ACCELERATED | WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON | WindowManager.LayoutParams.FLAG_FULLSCREEN | WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE);
            window.getDecorView().setSystemUiVisibility(5894); // Fullscreen + hide navigation + immersive sticky.
            if (Build.VERSION.SDK_INT >= 30) {
                window.setDecorFitsSystemWindows(false);
                WindowInsetsController bars = window.getInsetsController();
                if (bars != null) {
                    bars.hide(WindowInsets.Type.systemBars());
                    bars.setSystemBarsBehavior(WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE);
                }
            }
            root = new FrameLayout(getContext()); root.setBackgroundColor(Color.BLACK);
            if (direct) {
                // Android owns the unrotated anchor. Only our child layer receives
                // Unity buffers, bypassing TextureView and the UI render thread.
                anchor = new SurfaceView(getContext()); anchor.setZOrderOnTop(true);
                anchor.getHolder().addCallback(this);
                root.addView(anchor, new FrameLayout.LayoutParams(-1, -1));
            } else installTexture();
            root.addOnLayoutChangeListener((v,l,t,r,b,ol,ot,or,ob) -> rotate());
            setContentView(root); window.setLayout(-1, -1); applyMode(false); rotate();
        }
        private OutputMode.Mode mode(Display.Mode m){
            return new OutputMode.Mode(m.getModeId(),m.getPhysicalWidth(),m.getPhysicalHeight(),m.getRefreshRate());
        }
        private OutputMode.Mode findMode(int rate){
            Display.Mode[] supported=getDisplay().getSupportedModes();
            OutputMode.Mode[] modes=new OutputMode.Mode[supported.length];
            for(int i=0;i<supported.length;i++)modes[i]=mode(supported[i]);
            return OutputMode.choose(mode(getDisplay().getMode()),modes,rate);
        }
        private void setMode(OutputMode.Mode mode){
            WindowManager.LayoutParams attrs=getWindow().getAttributes();
            if(attrs.preferredDisplayModeId==mode.id && Float.compare(attrs.preferredRefreshRate,mode.rate)==0)return;
            attrs.preferredDisplayModeId=mode.id; attrs.preferredRefreshRate=mode.rate;
            getWindow().setAttributes(attrs);
            Log.i(TAG,"External mode requested: "+mode.width+"x"+mode.height+" @ "+mode.rate+" Hz");
        }
        void applyMode(boolean force){
            int requested=selectedFrameRate();
            if(!force && modeRequest==requested)return;
            modeRequest=requested;
            if(modeCheck!=null)owner.main.removeCallbacks(modeCheck);
            int effective=requested==120 && rejectedHighRateDisplay==getDisplay().getDisplayId()?60:requested;
            OutputMode.Mode chosen=findMode(effective);
            if(chosen==null){
                if(requested==120){
                    rejectedHighRateDisplay=getDisplay().getDisplayId();
                    chosen=findMode(60);
                }
                hint(GameUi.tr("선택한 주사율을 지원하는 외부 출력 모드가 없습니다. 실제 주사율을 확인하세요.","没有支持所选刷新率的外接模式，请查看实际刷新率。"));
            }
            if(chosen!=null)setMode(chosen);
            modeCheck=()->{
                if(monitor!=this || destroyed || !resumed)return;
                float actual=getDisplay().getRefreshRate();
                if(!OutputMode.matches(actual,requested)){
                    Log.i(TAG,"External mode not applied: requested="+requested+" actual="+actual);
                    if(requested==120 && rejectedHighRateDisplay!=getDisplay().getDisplayId()){
                        rejectedHighRateDisplay=getDisplay().getDisplayId();
                        OutputMode.Mode fallback=findMode(60);if(fallback!=null)setMode(fallback);
                        hint(GameUi.tr("120Hz가 적용되지 않아 60Hz 모드로 복귀합니다. 프레임 선택은 저장됩니다.","120Hz 未生效，返回 60Hz 模式。保留帧率选择。"));
                    }
                }
                rotate();
            };
            owner.main.postDelayed(modeCheck,5000);
        }
        private void installTexture() {
            texture = new TextureView(getContext());
            texture.setOpaque(true);
            texture.setSurfaceTextureListener(this);
            root.addView(texture, new FrameLayout.LayoutParams(-1, -1));
            texture.addOnLayoutChangeListener((v,l,t,r,b,ol,ot,or,ob) -> rotate());
        }
        void rotate() {
            if (monitor != this || root == null || root.getWidth() <= 0 || root.getHeight() <= 0) return;
            boolean reverse = owner.prefs.getBoolean("displayReverse", false);
            int width = direct ? root.getWidth() : texture == null ? 0 : texture.getWidth();
            int height = direct ? root.getHeight() : texture == null ? 0 : texture.getHeight();
            if (width <= 0 || height <= 0) return;
            boolean geometryChanged = appliedWidth != width || appliedHeight != height || appliedReverse != reverse;
            if (direct) {
                if (gameLayer == null || !gameLayer.isValid() || fallbackPending) return;
                if (geometryChanged) {
                    float[] m = DisplayGeometry.surfaceMatrix(width, height, reverse);
                    try (SurfaceControl.Transaction tx = new SurfaceControl.Transaction()) {
                        // Only changed geometry needs a compositor transaction.
                        // Resolve these non-SDK APIs once when creating the layer.
                        layerMatrix.invoke(tx, gameLayer, m[0], m[3], m[1], m[4]);
                        layerPosition.invoke(tx, gameLayer, m[2], m[5]);
                        tx.setLayer(gameLayer, 1).setVisibility(gameLayer, true).apply();
                    } catch (ReflectiveOperationException | RuntimeException error) { fallbackToTexture(error); return; }
                }
            } else if (geometryChanged) {
                Matrix matrix = new Matrix();
                matrix.setValues(DisplayGeometry.textureMatrix(width, height, reverse));
                texture.setTransform(matrix);
            }
            appliedWidth = width; appliedHeight = height; appliedReverse = reverse;
            requestFrameRate();
            outputStatus = "1080×1920 → " + root.getWidth() + "×" + root.getHeight()
                    + " · " + DisplayGeometry.angle(reverse) + "° · " + Math.round(getDisplay().getMode().getRefreshRate()) + " Hz";
            String geometry = outputStatus + (direct ? " SurfaceControl direct" : " TextureView fallback");
            if (!lastGeometry.equals(geometry)) {
                lastGeometry = geometry;
                Log.i(TAG, geometry);
            }
        }
        private void requestFrameRate() {
            if (Build.VERSION.SDK_INT >= 30 && unitySurface != null && unitySurface.isValid()) {
                float rate = getDisplay().getRefreshRate();
                if (Float.compare(rate, requestedRate) == 0) return;
                try {
                    unitySurface.setFrameRate(rate, Surface.FRAME_RATE_COMPATIBILITY_DEFAULT);
                    requestedRate = rate;
                }
                catch (RuntimeException error) { Log.w(TAG, "Display frame-rate hint unavailable", error); }
            }
        }
        @Override public void surfaceCreated(SurfaceHolder holder) {
            if (monitor != this || !resumed || destroyed || !direct) return;
            try {
                layerMatrix = SurfaceControl.Transaction.class.getMethod("setMatrix", SurfaceControl.class, float.class, float.class, float.class, float.class);
                layerPosition = SurfaceControl.Transaction.class.getMethod("setPosition", SurfaceControl.class, float.class, float.class);
                gameLayer = new SurfaceControl.Builder().setName("MajdataPlay Oniimai Unity External")
                        .setParent(anchor.getSurfaceControl()).setBufferSize(DisplayGeometry.BUFFER_WIDTH, DisplayGeometry.BUFFER_HEIGHT)
                        .setOpaque(true).setHidden(true).build();
                unitySurface = new Surface(gameLayer);
                rotate();
                if (fallbackPending) return;
                bind(unitySurface);
                Log.i(TAG, "Unity attached to direct portrait surface 1080x1920");
            } catch (ReflectiveOperationException | RuntimeException error) { fallbackToTexture(error); }
        }
        private void fallbackToTexture(Exception error) {
            if (fallbackPending || monitor != this || destroyed) return;
            fallbackPending = true;
            Log.w(TAG, "Direct surface unavailable; using TextureView", error);
            // Replace after the current SurfaceHolder/layout callback returns.
            owner.main.post(() -> {
                if (monitor != this || destroyed) return;
                try {
                    restore();
                    direct = false;
                    anchor.getHolder().removeCallback(this);
                    releaseSurface(); root.removeView(anchor); anchor = null;
                    installTexture(); applyMode(true);
                } catch (RuntimeException failure) {
                    Log.e(TAG, "External fallback failed", failure); closeMonitor();
                }
            });
        }
        @Override public void surfaceChanged(SurfaceHolder holder, int format, int width, int height) { rotate(); }
        @Override public void surfaceRedrawNeeded(SurfaceHolder holder) {
            if (monitor != this || destroyed || !holder.getSurface().isValid()) return;
            // A black buffer completes window relayout, without a per-frame UI copy.
            Canvas canvas = null;
            try { canvas = holder.lockCanvas(); if (canvas != null) canvas.drawColor(Color.BLACK); }
            finally { if (canvas != null) holder.unlockCanvasAndPost(canvas); }
        }
        @Override public void surfaceDestroyed(SurfaceHolder holder) {
            if (monitor == this) { closeMonitor(); owner.main.post(OniimaiDisplay.this::reconcile); }
            releaseSurface();
        }
        private void attachTexture(SurfaceTexture buffer) {
            if (monitor != this || !resumed || destroyed) return;
            // TextureView resets its default to landscape bounds on resize.
            buffer.setDefaultBufferSize(DisplayGeometry.BUFFER_WIDTH, DisplayGeometry.BUFFER_HEIGHT);
            rotate();
            // Geometry changes must not recreate Unity's EGL surface.
            if (unitySurface == null) {
                try {
                    unitySurface = new Surface(buffer); requestFrameRate(); bind(unitySurface);
                    Log.i(TAG, "Unity attached to fallback portrait texture 1080x1920");
                } catch (RuntimeException error) { Log.e(TAG, "Texture output failed", error); closeMonitor(); }
            }
        }
        public void onSurfaceTextureAvailable(SurfaceTexture buffer, int width, int height) { attachTexture(buffer); }
        public void onSurfaceTextureSizeChanged(SurfaceTexture buffer, int width, int height) {
            attachTexture(buffer);
        }
        public boolean onSurfaceTextureDestroyed(SurfaceTexture buffer) {
            if (monitor == this) { closeMonitor(); owner.main.post(OniimaiDisplay.this::reconcile); }
            releaseSurface();
            return true;
        }
        void releaseSurface() {
            if(modeCheck!=null){owner.main.removeCallbacks(modeCheck);modeCheck=null;}
            appliedWidth = appliedHeight = 0;
            requestedRate = Float.NaN;
            if (unitySurface != null) { unitySurface.release(); unitySurface = null; }
            if (gameLayer != null) {
                try (SurfaceControl.Transaction tx = new SurfaceControl.Transaction()) {
                    if (gameLayer.isValid()) tx.reparent(gameLayer, null).apply();
                } catch (RuntimeException error) { Log.w(TAG, "External layer already disconnected", error); }
                finally { gameLayer.release(); gameLayer = null; }
            }
        }
        public void onSurfaceTextureUpdated(SurfaceTexture buffer) {
            if (!firstFrame) { firstFrame = true; Log.i(TAG, "First external game frame presented"); }
        }
    }
    @Override public void onDisplayAdded(int id) { reconcile(); }
    @Override public void onDisplayRemoved(int id) {
        if(rejectedHighRateDisplay==id)rejectedHighRateDisplay=-1;
        reconcile();
    }
    @Override public void onDisplayChanged(int id) {
        // Phone brightness/refresh events do not change the external surface.
        if (monitor == null || monitor.getDisplay().getDisplayId() == id) reconcile();
    }
    void destroy() { destroyed = true; displays.unregisterDisplayListener(this); if(previewDialog!=null)previewDialog.dismiss(); closeMonitor(); removeOutputShortcut(); if(outputHint!=null)outputHint.cancel(); }
}
