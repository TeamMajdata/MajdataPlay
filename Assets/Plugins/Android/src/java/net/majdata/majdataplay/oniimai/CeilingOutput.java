package net.majdata.majdataplay.oniimai;

import android.os.SystemClock;
import java.util.concurrent.*;
import java.util.function.Supplier;

/** Shares the IO4 input handle; USB output never runs on Unity or the UI thread. */
final class CeilingOutput {
    private final Supplier<UsbIo.Hid> transport;
    private final ScheduledExecutorService worker = Executors.newSingleThreadScheduledExecutor();
    private volatile boolean enabled = true, foreground = true, destroyed;
    private volatile int brightness = 100, testColor = -1, state;
    private volatile long testUntil, sent;
    private volatile Frame latest;
    private UsbIo.Hid current;
    private int last = -1;
    private long retryAt;
    private static final class Frame {
        final int rgb; final long time;
        Frame(int rgb) { this.rgb = rgb; time = SystemClock.uptimeMillis(); }
    }
    CeilingOutput(Supplier<UsbIo.Hid> transport) {
        this.transport = transport;
        worker.scheduleWithFixedDelay(this::poll, 0, 33, TimeUnit.MILLISECONDS);
    }
    void submit(int[] colors) {
        if (colors != null && colors.length == 9) latest = new Frame(LedFrames.ceiling(colors));
    }
    void settings(boolean enabled, int brightness) {
        this.enabled = enabled; this.brightness = Math.max(0, Math.min(100, brightness));
        if (!enabled) testColor = -1;
    }
    void foreground(boolean value) { foreground = value; if (!value) testColor = -1; }
    void test(int rgb) { testColor = rgb & 0xffffff; testUntil = SystemClock.uptimeMillis() + 2000; }
    private static String tr(String ko, String zh) { return "zh-Hans".equals(UiText.language()) ? zh : ko; }
    String summary() {
        if (!enabled) return tr("스피커 RGB OFF", "扬声器 RGB 已关闭");
        if (state == 0) return tr("스피커 RGB · IO4 연결 대기", "扬声器 RGB · 等待 IO4 连接");
        if (state == 3) return tr("스피커 RGB · 출력 확인 필요", "扬声器 RGB · 请检查输出");
        if (!foreground) return tr("스피커 RGB · 백그라운드 소등", "扬声器 RGB · 后台熄灯");
        return state == 2 ? tr("스피커 RGB · 게임 연동 중", "扬声器 RGB · 游戏联动中")
                : tr("스피커 RGB · 게임 조명 대기", "扬声器 RGB · 等待游戏灯光");
    }
    String diagnostic() { return summary() + " / HID sent: " + sent; }
    private void poll() {
        if (destroyed) return;
        UsbIo.Hid port = transport.get();
        if (port != current) { current = port; last = -1; retryAt = 0; }
        if (port == null) { state = 0; return; }
        long now = SystemClock.uptimeMillis();
        if (now < retryAt) return;
        int rgb = 0; boolean seen = false;
        if (enabled && foreground) {
            Frame frame = latest;
            if (frame != null && now - frame.time < 1000) { rgb = frame.rgb; seen = true; }
            if (testColor >= 0 && now < testUntil) { rgb = testColor; seen = true; }
            else testColor = -1;
            rgb = LedFrames.scale(rgb, brightness);
        }
        try {
            if (last != rgb) { port.writeCeiling(rgb); last = rgb; sent++; }
            state = seen ? 2 : 1;
        } catch (Exception error) {
            // Output failures must not disconnect buttons/touch or queue old colours.
            state = 3; retryAt = now + 2000; last = -1;
        }
    }
    void destroy() {
        if (destroyed) return;
        destroyed = true;
        worker.execute(() -> {
            UsbIo.Hid port = transport.get();
            if (port != null) try { port.writeCeiling(0); } catch (Exception ignored) {}
        });
        worker.shutdown();
    }
}
