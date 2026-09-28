package net.majdata.majdataplay.oniimai;

import android.hardware.usb.UsbManager;
import android.os.SystemClock;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicInteger;

/** USB LED traffic never runs on the Unity, UI, or sensor-reader threads. */
final class LedOutput {
    private final UsbManager usb;
    private volatile int[] latestFrame = new int[9];
    private volatile long frameAt;
    private final ScheduledExecutorService io=Executors.newSingleThreadScheduledExecutor();
    private final AtomicInteger generation=new AtomicInteger();
    private volatile boolean running,destroyed,foreground=true;
    private volatile java.util.function.Supplier<String> message=()->UiText.t("LED 연결 대기");
    private volatile long sent,events;
    private volatile int seen,nativeStatus;
    private volatile int brightness=100,rotation,testColor=-1;
    private volatile long testUntil;
    private volatile boolean reverse,ring;
    // Owned exclusively by the LED worker.
    private LedChannel channel;
    private ScheduledFuture<?> pump,retry;
    private UsbIo.Port selectedPort;
    private int selectedAddress,failures;
    private int base;
    private int[] last;
    private boolean lastRing,blank;
    private long lastPing;
    LedOutput(UsbManager usb){this.usb=usb;}
    void submit(int[] frame){
        if(frame==null||frame.length!=9)return;
        latestFrame=frame.clone();frameAt=SystemClock.uptimeMillis();
    }
    boolean running(){return running;}
    String brief(){return message.get();}
    String summary(){return message.get()+UiText.t("\n게임 LED ")+(nativeStatus==255?UiText.t("준비 8/8"):UiText.t("상태 ")+nativeStatus)+UiText.t(" · 버튼 신호 ")+Integer.bitCount(seen&255)+UiText.t("/8 · 송신 ")+sent;}
    String diagnostic(){return summary()+" / Events: "+events+" / Ring seen: "+((seen&256)!=0);}
    void settings(int brightness,int rotation,boolean reverse,boolean ring){
        this.brightness=Math.max(0,Math.min(100,brightness));this.rotation=rotation;this.reverse=reverse;this.ring=ring;
    }
    void foreground(boolean value){foreground=value;if(!value)testColor=-1;}
    void test(int rgb){if(running){testColor=rgb&0xffffff;testUntil=SystemClock.uptimeMillis()+2000;}}
    void start(UsbIo.Port port,int address,int base){
        if(destroyed||running)return;
        if(address<1||address>255||base<0||base>24)throw new IllegalArgumentException(UiText.t("LED 범위 오류"));
        int gen=generation.incrementAndGet();running=true;sent=0;message=()->UiText.t("LED 보드 확인 중…");
        io.execute(()->{
            closeCurrent();if(gen!=generation.get()||destroyed)return;
            selectedPort=port;selectedAddress=address;this.base=base;failures=0;
            open(gen);
        });
    }
    private void open(int gen){
        if(gen!=generation.get()||destroyed)return;
        // Keep the enabled session alive without opening USB handles in the background.
        if(!foreground){retry=io.schedule(()->open(gen),1000,TimeUnit.MILLISECONDS);return;}
        try{
                channel=new LedChannel(new UsbIo.Cdc(usb,selectedPort,115200),selectedAddress);
                channel.enableReplies();byte[] info=channel.request(0xf0);
                if(info.length<8||!new String(info,0,8,StandardCharsets.US_ASCII).equals("15070-04"))throw new IOException(UiText.t("지원하는 LED 보드 응답이 아닙니다."));
                if(gen!=generation.get()||destroyed){closeCurrent();return;}
                last=null;lastRing=false;blank=false;lastPing=0;
                pump=io.scheduleWithFixedDelay(()->poll(gen),0,33,TimeUnit.MILLISECONDS);
        }catch(Exception e){fail(gen,e);}
    }
    private void poll(int gen){
        if(gen!=generation.get()||destroyed)return;
        try{
            if(!foreground){if(!blank){blackout();blank=true;}message=()->UiText.t("앱 백그라운드 · LED 꺼짐");return;}
            if(blank){last=null;blank=false;}
            long now=SystemClock.uptimeMillis();int[] colors=new int[9];
            if(now-frameAt<1000){colors=latestFrame.clone();nativeStatus=255;seen=511;events++;}
            else {nativeStatus=0;seen=0;}
            boolean test=testColor>=0&&now<testUntil;if(test)Arrays.fill(colors,testColor);
            else testColor=-1;
            int[] frame=LedFrames.physical(colors,brightness,rotation,reverse);
            send(frame,ring);
            // Confirm a quiet/static connection too. Failure only closes this LED port.
            if(now-lastPing>=2000){channel.request(0xf0);lastPing=now;}
            failures=0;
            message=()->test?UiText.t("색상 테스트 · 2초 후 게임 연동"):UiText.t("LED 연동 중");
            if(!test&&(nativeStatus!=255||(seen&255)==0))message=()->UiText.t("연결됨 · 게임 LED 신호 대기 (게임 조명 명령 대기)");
        }catch(Exception e){fail(gen,e);}
    }
    private void send(int[] frame,boolean useRing)throws IOException{
        boolean changed=false;
        for(int i=0;i<8;i++)if(last==null||last[i]!=frame[i]){channel.request(0x31,LedFrames.color(base+i,frame[i]));changed=true;}
        if(changed){channel.request(0x3c);sent++;}
        if(useRing&&(last==null||!lastRing||last[8]!=frame[8]))channel.request(0x39,new byte[]{(byte)frame[8],(byte)frame[8],(byte)frame[8]});
        else if(!useRing&&lastRing)channel.request(0x39,new byte[3]);
        last=frame;lastRing=useRing;
    }
    private void blackout()throws IOException{
        if(channel==null||channel.isClosed())return;
        for(int i=0;i<8;i++)channel.request(0x31,LedFrames.color(base+i,0));
        channel.request(0x3c);if(lastRing)channel.request(0x39,new byte[3]);last=null;lastRing=false;
    }
    private void closeCurrent(){
        closeCurrent(true);
    }
    private void closeCurrent(boolean clear){
        if(pump!=null){pump.cancel(false);pump=null;}
        if(retry!=null){retry.cancel(false);retry=null;}
        if(clear)try{blackout();}catch(Exception ignored){}
        if(channel!=null){channel.close();channel=null;}
    }
    private void fail(int gen,Exception error){
        closeCurrent(false);
        if(gen!=generation.get()||destroyed)return;
        int seconds=1<<Math.min(failures++,3);
        message=()->UiText.t("LED 재연결 대기: ")+seconds+UiText.t("초 · ")+error.getMessage();
        System.err.println("Oniimai LED retry in "+seconds+"s: "+error.getMessage());
        // running remains true while enabled, preventing UI refresh from starting a second session.
        retry=io.schedule(()->open(gen),seconds,TimeUnit.SECONDS);
    }
    void stop(){
        if(destroyed)return;generation.incrementAndGet();running=false;testColor=-1;message=()->UiText.t("LED 연동 OFF");io.execute(this::closeCurrent);
    }
    void destroy(){
        if(destroyed)return;destroyed=true;generation.incrementAndGet();running=false;io.execute(this::closeCurrent);io.shutdown();
    }
}
