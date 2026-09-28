package net.majdata.majdataplay.oniimai;

import android.app.Activity;
import android.app.PendingIntent;
import android.content.*;
import android.hardware.input.InputManager;
import android.hardware.usb.*;
import android.os.*;
import android.view.InputDevice;
import android.view.KeyEvent;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicInteger;

/** Activity-owned USB host; Unity only reads snapshots and submits RGB frames. No root or hooks. */
public final class OniimaiController implements InputManager.InputDeviceListener {
    private static volatile OniimaiController instance;
    final Activity activity;
    final SharedPreferences prefs;
    final Handler main = new Handler(Looper.getMainLooper());
    final InputSnapshot state = new InputSnapshot();
    final KeyboardState keyboard = new KeyboardState();
    final LedOutput leds;
    final CeilingOutput ceiling;
    final OniimaiPanel panel;
    final OniimaiDisplay display;
    final UsbManager usb;
    private final ExecutorService io = Executors.newSingleThreadExecutor();
    private final AtomicInteger generation = new AtomicInteger();
    private final InputManager inputManager;
    private final String permissionAction;
    volatile List<UsbIo.Port> ports = Collections.emptyList();
    volatile String status = "USB 연결 대기";
    volatile int learning = -1;
    private volatile boolean destroyed, connecting, connected;
    private boolean manuallyDisconnected, resumed = true, permissionPending;
    private float uiFontScale;
    private int uiDensity;
    private final Set<String> permissionAsked = new HashSet<>();
    private volatile CommandChannel command,configCommand;
    private boolean setupDeferred;
    private UsbIo.Cdc serial;
    private volatile UsbIo.Hid hid;
    private final BroadcastReceiver receiver = new BroadcastReceiver() {
        @Override public void onReceive(Context context, Intent intent) {
            String action = intent.getAction();
            if (UsbManager.ACTION_USB_DEVICE_DETACHED.equals(action)) {
                // Release first. The neutral-input gate also applies to automatic reconnects.
                boolean manual = manuallyDisconnected;
                disconnect(); manuallyDisconnected = manual; permissionAsked.clear(); permissionPending = false; refresh();
            } else if (permissionAction.equals(action)) {
                UsbDevice device = intent.getParcelableExtra(UsbManager.EXTRA_DEVICE);
                permissionPending = false;
                status = device != null && usb.hasPermission(device) ? "USB 권한 허용됨 · 저장된 포트 연결 중" : "USB 권한이 없습니다";
                refresh();
            } else if (UsbManager.ACTION_USB_DEVICE_ATTACHED.equals(action)) refresh();
        }
    };

    private OniimaiController(Activity activity) {
        this.activity = activity;
        uiFontScale=activity.getResources().getConfiguration().fontScale;uiDensity=activity.getResources().getConfiguration().densityDpi;
        prefs = activity.getSharedPreferences("oniimai", Context.MODE_PRIVATE);
        SharedPreferences.Editor defaults=prefs.edit();
        for(Map.Entry<String,Object> e:SetupDefaults.missing(prefs.getAll()).entrySet()){
            if(e.getValue() instanceof Boolean)defaults.putBoolean(e.getKey(),(Boolean)e.getValue());
            else defaults.putInt(e.getKey(),(Integer)e.getValue());
        }
        defaults.apply();
        UiText.language(prefs.getString("language", "ko"));
        usb = (UsbManager) activity.getSystemService(Context.USB_SERVICE);
        inputManager = (InputManager) activity.getSystemService(Context.INPUT_SERVICE);
        permissionAction = activity.getPackageName() + ".ONIIMAI_USB_PERMISSION";
        synchronized (keyboard) {
            for (int i = 0; i < keyboard.map.length; i++) keyboard.map[i] = prefs.getInt("key" + i, keyboard.map[i]);
        }
        state.enabled(prefs.getBoolean("input", false));
        leds = new LedOutput(usb);
        ceiling = new CeilingOutput(() -> connected ? hid : null);
        updateLedSettings();
        panel = new OniimaiPanel(this);
        display = new OniimaiDisplay(this);
        IntentFilter filter = new IntentFilter(permissionAction);
        filter.addAction(UsbManager.ACTION_USB_DEVICE_ATTACHED);
        filter.addAction(UsbManager.ACTION_USB_DEVICE_DETACHED);
        if (Build.VERSION.SDK_INT >= 33) activity.registerReceiver(receiver, filter, Context.RECEIVER_NOT_EXPORTED);
        else activity.registerReceiver(receiver, filter);
        inputManager.registerInputDeviceListener(this, main);
        updateKeyboardConnection(); refresh();
    }
    public static void attach(Activity activity) { detach(); instance = new OniimaiController(activity); }
    public static void textConfigurationChanged(){
        OniimaiController c=instance;if(c==null||c.destroyed)return;
        android.content.res.Configuration config=c.activity.getResources().getConfiguration();
        if(c.uiFontScale==config.fontScale&&c.uiDensity==config.densityDpi)return;
        c.uiFontScale=config.fontScale;c.uiDensity=config.densityDpi;
        c.main.post(()->{if(!c.destroyed){c.display.refreshLanguage();c.panel.textConfigurationChanged();}});
    }
    public static void detach() {
        OniimaiController c = instance; instance = null;
        if (c == null) return;
        c.destroyed = true; c.generation.incrementAndGet(); c.state.disconnect();
        c.activity.unregisterReceiver(c.receiver); c.inputManager.unregisterInputDeviceListener(c);
        c.main.removeCallbacksAndMessages(null); c.panel.destroy(); c.display.destroy(); c.leds.destroy(); c.ceiling.destroy();
        c.io.execute(c::closeInputs); c.io.shutdown();
    }
    public static long[] poll() {
        OniimaiController c = instance;
        if (c == null) return new long[5];
        long[] frame = c.state.read(SystemClock.uptimeMillis());
        if (c.display.active()) frame[4] |= 4;
        if (c.prefs.getBoolean("p1Start", false)) frame[4] |= 8;
        if (OutputMode.frameRate(c.prefs.getInt("frameRate",60)) == 120) frame[4] |= 16;
        return frame;
    }
    public static void stats(String text) { OniimaiController c = instance; if (c != null) c.display.stats(text); }
    public static void artwork(int kind, byte[] png) {
        OniimaiController c=instance;
        if(c!=null) c.main.post(() -> { if(!c.destroyed) c.display.artwork(kind,png); });
    }
    public static void lights(int[] rgb) { OniimaiController c = instance; if (c != null) { c.leds.submit(rgb); c.ceiling.submit(rgb); } }
    public static void settingsVisible(boolean value) {
        OniimaiController c = instance;
        if (c != null) c.main.post(() -> { if (!c.destroyed) c.panel.entry(value); });
    }
    public static void resume() {
        OniimaiController c = instance;
        if (c != null) { c.resumed = true; c.state.foreground(true); c.updateKeyboardConnection(); c.leds.foreground(true); c.ceiling.foreground(true); c.display.resume(true); c.refresh(); c.main.post(c::maybeInitialSetup); }
    }
    public static void pause() {
        OniimaiController c = instance;
        if (c != null) { c.resumed = false; c.clearKeys(); c.state.foreground(false); c.leds.foreground(false); c.ceiling.foreground(false); c.display.resume(false); }
    }
    public static void focus(boolean value) {
        OniimaiController c = instance;
        if (c != null) { if (!value) c.clearKeys(); c.state.foreground(value);if(value)c.main.post(c::maybeInitialSetup); }
    }
    public static boolean dashboardBack() { OniimaiController c=instance; return c!=null && c.display!=null && c.display.dashboardBack(); }
    public static boolean handleKey(KeyEvent event) {
        OniimaiController c = instance;
        return c != null && c.key(event);
    }
    boolean key(KeyEvent event) {
        InputDevice device = event.getDevice();
        int code = event.getKeyCode();
        if (device == null || device.isVirtual() || event.getDeviceId() <= 0
                || (event.getFlags() & KeyEvent.FLAG_SOFT_KEYBOARD) != 0) return false;
        if (code == KeyEvent.KEYCODE_POWER || code == KeyEvent.KEYCODE_VOLUME_UP
                || code == KeyEvent.KEYCODE_VOLUME_DOWN || code == KeyEvent.KEYCODE_VOLUME_MUTE) return false;
        boolean external = Build.VERSION.SDK_INT >= 29 ? device.isExternal()
                : device.getVendorId() != 0 || device.getProductId() != 0;
        if (!external) return false;
        if (learning >= 0) {
            if (event.getAction() == KeyEvent.ACTION_DOWN && event.getRepeatCount() == 0) {
                synchronized (keyboard) {
                    keyboard.learn(learning, code); keyboard.clear();
                    SharedPreferences.Editor e = prefs.edit().putString("keyboardDevice", device.getDescriptor()).putInt("buttonMode", 1);
                    for (int i = 0; i < keyboard.map.length; i++) e.putInt("key" + i, keyboard.map[i]);
                    e.apply(); status = (learning == 8 ? "P1 / START" : "A" + (learning + 1)) + " = " + KeyEvent.keyCodeToString(code);
                    learning = -1; state.keyboard(0, true);
                }
            }
            return true;
        }
        boolean selected = prefs.getInt("buttonMode", 2) == 1
                && device.getDescriptor().equals(prefs.getString("keyboardDevice", ""));
        // While our settings are open, external keyboards cannot drive Android's menu focus.
        if (!selected) return panel.isOpen();
        synchronized (keyboard) {
            if (event.getAction() == KeyEvent.ACTION_DOWN || event.getAction() == KeyEvent.ACTION_UP) {
                keyboard.event(event.getDeviceId(), code, event.getAction() == KeyEvent.ACTION_DOWN, event.getRepeatCount());
                state.keyboard(keyboard.mask(), true);
            }
        }
        return true;
    }
    void clearKeys() { synchronized (keyboard) { keyboard.clear(); state.keyboard(0, false); } }
    void enableInput(boolean value) { prefs.edit().putBoolean("input", value).apply(); state.enabled(value); if (value) { manuallyDisconnected = false; refresh(); } }
    String connectionDescription() {
        if (permissionPending) return GameUi.tr("Android의 USB 권한 창에서 허용해 주세요.","请在 Android 的 USB 授权窗口中允许访问。");
        if (connecting) return GameUi.tr("컨트롤러 연결 중…","正在连接控制器…");
        if (status.contains("실패") || status.contains("오류") || status.startsWith("LED:") || status.contains("끊김")) return UiText.message(status);
        if (!prefs.getBoolean("input",false)) return GameUi.tr("입력 꺼짐 · 켜면 컨트롤러로 게임을 조작합니다.","输入已关闭 · 启用后可用控制器操作游戏。");
        if (manuallyDisconnected) return GameUi.tr("연결을 해제했습니다. 다시 연결을 누르세요.","连接已断开，请点击重新连接。");
        long[] raw=state.diagnostic(SystemClock.uptimeMillis());
        if ((raw[3]&3)==3) return GameUi.tr("터치와 버튼 준비 완료","触摸和按钮已就绪");
        if ((raw[3]&1)!=0) return GameUi.tr("터치 연결됨 · 입력 탭에서 버튼을 등록하세요.","触摸已连接 · 请在输入页映射按钮。");
        if ((raw[3]&2)!=0) return GameUi.tr("버튼 연결됨 · 터치 포트를 확인하세요.","按钮已连接 · 请检查触摸端口。");
        return GameUi.tr("USB 연결 대기 · 포트와 권한을 확인하세요.","等待 USB 连接 · 请检查端口和权限。");
    }
    void updateLedSettings() {
        leds.settings(prefs.getInt("brightness", 100), prefs.getInt("ledRotation", 0),
                prefs.getBoolean("ledReverse", false), prefs.getBoolean("ring", false));
        ceiling.settings(prefs.getBoolean("led", true) && prefs.getBoolean("ceiling", true), prefs.getInt("brightness", 100));
    }
    // A USB address can change on every plug-in. Match VID/PID/interface/serial instead.
    static String portId(UsbIo.Port p) {
        String serial = "";
        try { serial = p.device.getSerialNumber(); } catch (SecurityException ignored) {}
        return p.device.getVendorId() + ":" + p.device.getProductId() + ":" + p.controlId + ":" + (serial == null ? "" : serial);
    }
    UsbIo.Port selected(String key) throws IOException {
        String id = prefs.getString(key, "");
        if (id.isEmpty()) return null;
        List<UsbIo.Port> snapshot = ports;
        String[] ids = new String[snapshot.size()];
        for (int i=0;i<ids.length;i++) ids[i]=portId(snapshot.get(i));
        int index = PortSelection.unique(ids,id);
        if (index == -2) throw new IOException("동일한 USB 장치가 여러 개입니다. 하나만 연결하세요.");
        if (index < 0) throw new IOException("저장된 포트 연결 대기: " + prefs.getString(key + "Name", key));
        UsbIo.Port found = snapshot.get(index);
        if (usb.hasPermission(found.device) && !id.equals(ids[index])) prefs.edit().putString(key, ids[index]).apply();
        return found;
    }
    void selectPort(String key, UsbIo.Port port) {
        disconnect(); manuallyDisconnected = false;
        SharedPreferences.Editor edit=prefs.edit().putString(key, port == null ? "" : portId(port))
                .putString(key + "Name", port == null ? "사용 안 함 / 关闭" : port.name);
        if(key.equals("touchPort")&&port!=null)edit.putInt("touchMode",PortSelection.protocol(port.name,port.hid,prefs.getInt("touchMode",1)));
        edit.apply();
        if (port != null && !usb.hasPermission(port.device)) permission(port.device);
        refresh();
    }
    void configurationChanged() { disconnect(); manuallyDisconnected = false; refresh(); }
    void automaticPorts() {
        disconnect(); manuallyDisconnected = false;
        prefs.edit().remove("touchPort").remove("ledPort").remove("hidPort").putBoolean("autoConnect",true).apply();
        refresh();
    }
    private void autoSelectPorts() {
        List<UsbIo.Port> snapshot=ports;
        String[] names=new String[snapshot.size()]; boolean[] hidFlags=new boolean[names.length];
        for(int i=0;i<names.length;i++) { names[i]=snapshot.get(i).name; hidFlags[i]=snapshot.get(i).hid; }
        SharedPreferences.Editor edit=prefs.edit();
        for(String key:new String[]{"touchPort","ledPort","hidPort"}) {
            // An explicit manual selection (including Disabled) always wins.
            if(prefs.contains(key)) continue;
            if(key.equals("hidPort")&&prefs.getInt("buttonMode",2)!=2)continue;
            int index=key.equals("touchPort")?PortSelection.automaticTouch(names,hidFlags):PortSelection.named(names,hidFlags,key.equals("hidPort")?PortSelection.IO4:PortSelection.LED);
            if(index==-2) { status="동일한 이름의 포트가 여러 개입니다. 직접 선택하세요."; continue; }
            if(index>=0) {
                UsbIo.Port p=snapshot.get(index);
                edit.putString(key,portId(p)).putString(key+"Name",p.name);
                android.util.Log.i("OniimaiUsb", "Auto selected " + key + " = " + p.name + " IF" + p.controlId);
                if(key.equals("touchPort")) edit.putInt("touchMode",1);
            }
        }
        edit.apply();
        autoSelectKeyboard();
    }
    private void autoSelectKeyboard(){
        if(prefs.contains("keyboardDevice")||prefs.getInt("buttonMode",2)!=1)return;
        InputDevice found=null;
        for(int id:InputDevice.getDeviceIds()){
            InputDevice d=InputDevice.getDevice(id);if(d==null||d.isVirtual()||d.getKeyboardType()==InputDevice.KEYBOARD_TYPE_NONE)continue;
            boolean ours=false;for(UsbIo.Port p:ports)if(PortSelection.role(p.name,p.hid)!=PortSelection.NONE&&d.getVendorId()==p.device.getVendorId()&&d.getProductId()==p.device.getProductId())ours=true;
            if(ours){if(found!=null)return;found=d;}
        }
        if(found!=null){prefs.edit().putString("keyboardDevice",found.getDescriptor()).apply();updateKeyboardConnection();}
    }
    private void maybeInitialSetup(){
        if(destroyed||!resumed||permissionPending||setupDeferred||prefs.getBoolean("setupComplete",true)||panel.isOpen()||!activity.hasWindowFocus())return;
        // Onboarding belongs to the first app launch, even with no USB device or auto-connect disabled.
        setupDeferred=true;panel.openSetup();
    }
    private void reconnectSaved() {
        if(destroyed || !resumed || manuallyDisconnected || connecting || !prefs.getBoolean("autoConnect",true)) return;
        autoSelectPorts();
        main.post(this::maybeInitialSetup);
        if(ports.isEmpty())return;
        boolean input=prefs.getBoolean("input",false), led=prefs.getBoolean("led",true);
        if(!input && !led) return;
        List<String> keys=new ArrayList<>();
        if(input) { keys.add("touchPort"); if(prefs.getInt("buttonMode",2)==2) keys.add("hidPort"); }
        if(led) keys.add("ledPort");
        for(String key:keys) {
            String id=prefs.getString(key,""); if(id.isEmpty()) continue;
            for(UsbIo.Port p:ports) {
                if(!usb.hasPermission(p.device) && PortSelection.base(id).equals(PortSelection.base(portId(p)))) {
                    if(!permissionPending && !permissionAsked.contains(p.device.getDeviceName())) permission(p.device);
                    return;
                }
            }
            try { selected(key); } catch(IOException ex) { status=ex.getMessage(); return; }
        }
        if(input && !connected) connect();
        else if(led && !leds.running()) startLeds();
    }
    void refresh() {
        if (destroyed) return;
        io.execute(() -> {
            try { ports = Collections.unmodifiableList(UsbIo.ports(usb)); main.post(this::reconnectSaved); }
            catch (Exception ex) { status = ex.getMessage(); }
        });
    }
    void permission(UsbDevice device) {
        permissionAsked.add(device.getDeviceName()); permissionPending = true;
        int flags = PendingIntent.FLAG_UPDATE_CURRENT;
        if (Build.VERSION.SDK_INT >= 31) flags |= PendingIntent.FLAG_MUTABLE;
        Intent reply = new Intent(permissionAction).setPackage(activity.getPackageName());
        usb.requestPermission(device, PendingIntent.getBroadcast(activity, device.getDeviceId(), reply, flags));
    }
    void connect() {
        if (destroyed || connecting) return;
        manuallyDisconnected = false; connected = false;
        int gen = generation.incrementAndGet(); connecting = true; clearKeys(); state.disconnect();
        state.awaitStreams(!prefs.getString("touchPort", "").isEmpty(), prefs.getInt("buttonMode", 2) == 2);
        status = "연결 중…";
        io.execute(() -> {
            try {
                closeInputs(); if (gen != generation.get() || destroyed) return;
                ports = Collections.unmodifiableList(UsbIo.ports(usb));
                UsbIo.Port touch = selected("touchPort");
                if (touch != null) {
                    if (touch.hid) throw new IOException("터치에는 CDC 포트를 선택하세요.");
                    if (prefs.getInt("touchMode", 1) == 0) {
                        command = new CommandChannel(new UsbIo.Cdc(usb, touch, 115200), (data, n) -> {
                            if (gen != generation.get()) return;
                            try { acceptTouch(new Protocol.TouchDebug(data).pressed); }
                            catch (IllegalArgumentException ex) { status = ex.getMessage(); }
                        }, message -> {}, message -> inputFailure(gen, message));
                        new Protocol.Config(command.request(Protocol.CONFIG_GET));
                        command.request(Protocol.DEBUG_START);
                    } else {
                        serial = new UsbIo.Cdc(usb, touch, 9600);
                        Protocol.TouchParser parser = new Protocol.TouchParser(pressed -> {
                            if (gen == generation.get()) acceptTouch(pressed);
                        });
                        serial.start(parser::feed, message -> inputFailure(gen, message));
                        serial.write("{HALT}{RSET}{STAT}".getBytes(StandardCharsets.US_ASCII));
                    }
                }
                if (prefs.getInt("buttonMode", 2) == 2) {
                    UsbIo.Port button = selected("hidPort");
                    if (button == null || !button.hid) throw new IOException("IO4 HID 포트를 선택하세요.");
                    final int bank = prefs.getInt("bank", 0);
                    hid = new UsbIo.Hid(usb, button, (data, n) -> {
                        if (gen != generation.get()) return;
                        try {
                            boolean[] bits = Protocol.io4(data, bank); int mask = 0;
                            for (int i = 0; i < 8; i++) if (bits[i]) mask |= 1 << i;
                            if (Protocol.io4P1(data)) mask |= 256;
                            state.hid(mask, SystemClock.uptimeMillis());
                        } catch (IllegalArgumentException ex) { inputFailure(gen, "IO4 형식 오류: " + n + " bytes"); }
                    }, message -> inputFailure(gen, message));
                }
                if (gen != generation.get() || destroyed) { closeInputs(); return; }
                connected = true; status = "연결됨 · 모든 버튼과 터치에서 손을 떼세요";
                android.util.Log.i("OniimaiUsb", "Saved inputs connected; release gate armed");
                main.post(this::updateKeyboardConnection);
            } catch (Exception ex) {
                closeInputs(); if (gen == generation.get()) { state.disconnect(); status = "연결 실패: " + ex.getMessage(); }
            } finally { connecting = false; }
        });
        startLeds();
    }
    private void acceptTouch(boolean[] bits) {
        long mask = 0; for (int i = 0; i < 34; i++) if (bits[i]) mask |= 1L << i;
        state.touch(mask, SystemClock.uptimeMillis());
    }
    private void inputFailure(int gen, String message) {
        if (generation.compareAndSet(gen, gen + 1)) {
            connected = false;
            state.disconnect(); status = "입력 연결 끊김: " + message;
            if (!destroyed) io.execute(this::closeInputs);
        }
    }
    void startLeds() {
        updateLedSettings();
        if (!prefs.getBoolean("led", true)) { leds.stop(); return; }
        try {
            UsbIo.Port led = selected("ledPort");
            if (led == null) throw new IOException("LED 포트를 선택하세요.");
            if (portId(led).equals(prefs.getString("touchPort", ""))) throw new IOException("터치와 LED는 서로 다른 포트여야 합니다.");
            updateLedSettings(); leds.start(led, prefs.getInt("address", 17), prefs.getInt("base", 0));
        } catch (Exception ex) { status = "LED: " + ex.getMessage(); }
    }
    void disconnect() {
        manuallyDisconnected = true; connected = false;
        generation.incrementAndGet(); state.disconnect(); clearKeys(); leds.stop();
        status = "연결 해제됨"; if (!destroyed) io.execute(this::closeInputs);
    }
    private final java.util.concurrent.atomic.AtomicBoolean sensitivityBusy=new java.util.concurrent.atomic.AtomicBoolean();
    boolean uiBusy(){return connecting||sensitivityBusy.get();}
    boolean sensitivityBusy(){return sensitivityBusy.get();}
    void sensitivity(int zone, java.util.function.Consumer<Protocol.Config> callback) {
        if (destroyed || !sensitivityBusy.compareAndSet(false,true)) return;
        status="감도 읽는 중…";
        io.execute(() -> {
            try {
                CommandChannel c = configurationChannel();
                Protocol.Config config = new Protocol.Config(c.request(Protocol.CONFIG_GET));
                status="감도를 읽었습니다";
                main.post(() -> { if (!destroyed && panel.isOpen()) callback.accept(config); });
            } catch (Exception ex) { status = "감도 읽기 실패: " + ex.getMessage(); }
            finally {sensitivityBusy.set(false);}
        });
    }
    void saveSensitivity(int zone, int finger, int noise, int hysteresis, Runnable onSaved) {
        if (destroyed || !sensitivityBusy.compareAndSet(false,true)) return;
        status="감도 저장 중…";
        io.execute(() -> {
            try {
                CommandChannel c = configurationChannel();
                Protocol.Config config = new Protocol.Config(c.request(Protocol.CONFIG_GET));
                for (int i = 0; i < 34; i++) if (zone == 34 || zone == i) config.sensitivity(i, finger, noise, hysteresis);
                c.request(Protocol.CONFIG_SET, config.bytes());
                Protocol.Config verified = new Protocol.Config(c.request(Protocol.CONFIG_GET));
                for (int i = 0; i < 34; i++) if (zone == 34 || zone == i)
                    if (verified.finger(i) != finger || verified.noise(i) != noise || verified.hysteresis(i) != hysteresis)
                        throw new IOException("장치의 감도 읽기 결과가 다릅니다. 저장을 중단합니다.");
                c.request(Protocol.CONFIG_SAVE); status = "컨트롤러에 감도를 저장했습니다";
                main.post(()->{if(!destroyed)onSaved.run();});
            } catch (Exception ex) { status = "감도 저장 실패: " + ex.getMessage(); }
            finally {sensitivityBusy.set(false);}
        });
    }
    private CommandChannel configurationChannel() throws IOException {
        if(command!=null&&!command.isClosed())return command;
        if(configCommand!=null&&!configCommand.isClosed())return configCommand;
        if(configCommand!=null){configCommand.close();configCommand=null;}
        UsbIo.Port touch=selected("touchPort"),found=null;
        for(UsbIo.Port p:ports)if(PortSelection.role(p.name,p.hid)==PortSelection.COMMAND&&(touch==null||p.device.getDeviceName().equals(touch.device.getDeviceName()))){
            if(found!=null)throw new IOException("Command 포트가 여러 개입니다. 컨트롤러 하나만 연결하세요.");found=p;
        }
        if(found==null)throw new IOException("감도 설정용 Command 포트를 찾지 못했습니다.");
        // Configuration uses its own interface. Never start debug input on the Touch stream.
        configCommand=new CommandChannel(new UsbIo.Cdc(usb,found,115200),(data,n)->{},message->{},message->{});
        return configCommand;
    }
    private void closeInputs() {
        if(configCommand!=null){configCommand.close();configCommand=null;}
        if (command != null) {
            try { if (!command.isClosed()) command.request(Protocol.DEBUG_STOP); } catch (Exception ignored) {}
            command.close(); command = null;
        }
        if (serial != null) {
            try { serial.write("{HALT}".getBytes(StandardCharsets.US_ASCII)); } catch (Exception ignored) {}
            serial.close(); serial = null;
        }
        UsbIo.Hid oldHid = hid; hid = null;
        if (oldHid != null) {
            try { oldHid.writeCeiling(0); } catch (IOException ignored) {}
            oldHid.close();
        }
    }
    private void updateKeyboardConnection() {
        boolean found = false;
        String selected = prefs.getString("keyboardDevice", "");
        if (prefs.getInt("buttonMode", 2) == 1) for (int id : InputDevice.getDeviceIds()) {
            InputDevice d = InputDevice.getDevice(id);
            if (d != null && selected.equals(d.getDescriptor())) found = true;
        }
        synchronized (keyboard) { if (!found) keyboard.clear(); state.keyboard(keyboard.mask(), found); }
    }
    @Override public void onInputDeviceAdded(int id) { updateKeyboardConnection(); }
    @Override public void onInputDeviceChanged(int id) { clearKeys(); updateKeyboardConnection(); }
    @Override public void onInputDeviceRemoved(int id) { clearKeys(); updateKeyboardConnection(); }
}
