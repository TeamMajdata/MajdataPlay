package net.majdata.majdataplay.oniimai;

import java.util.Arrays;
import java.util.concurrent.atomic.AtomicReference;

public final class CeilingOutputTest {
    static int checks;
    static void check(boolean ok, String label) { checks++; if (!ok) throw new AssertionError(label); }
    static void await(java.util.function.BooleanSupplier condition, String label) throws Exception {
        long end=System.currentTimeMillis()+3000;
        while (!condition.getAsBoolean() && System.currentTimeMillis()<end) Thread.sleep(5);
        check(condition.getAsBoolean(),label);
    }
    public static void main(String[] args) throws Exception {
        int[] frame=new int[9]; Arrays.fill(frame,0xff0000); frame[8]=0xffffff;
        check(LedFrames.ceiling(frame)==0xff0000,"cabinet white does not wash out speaker RGB");
        for(int i=4;i<8;i++)frame[i]=0x0000ff;
        check(LedFrames.ceiling(frame)==0x800080,"equal red and blue lamps mix to purple");
        Arrays.fill(frame,0); frame[8]=0xffffff;
        check(LedFrames.ceiling(frame)==0,"all button lamps off clears RGB independently of FET");
        Arrays.fill(frame,0x204080);
        AtomicReference<UsbIo.Hid> port=new AtomicReference<>(new UsbIo.Hid());
        CeilingOutput output=new CeilingOutput(port::get);
        java.util.concurrent.ScheduledExecutorService producer=java.util.concurrent.Executors.newSingleThreadScheduledExecutor();
        producer.scheduleWithFixedDelay(()->output.submit(frame),0,100,java.util.concurrent.TimeUnit.MILLISECONDS);
        try {
            output.settings(true,50);
            await(()->port.get().rgb==0x102040,"game colour + brightness reaches IO4");
            int n=port.get().writes; Thread.sleep(140); check(n==port.get().writes,"unchanged colours not resent");
            output.test(0xff0000); await(()->port.get().rgb==0x800000,"two-second speaker-only test");
            await(()->port.get().rgb==0x102040,"test automatically returns to current game");
            output.foreground(false); await(()->port.get().rgb==0,"background clears speaker");
            output.foreground(true); await(()->port.get().rgb==0x102040,"foreground restores latest colour");
            output.settings(false,50); await(()->port.get().rgb==0,"master/speaker OFF clears output");
            output.settings(true,100); await(()->port.get().rgb==0x204080,"re-enable uses saved brightness");
            producer.shutdownNow(); await(()->port.get().rgb==0,"stale game data clears speaker");
            UsbIo.Hid failed=port.get(); failed.fail=true; output.submit(frame);
            await(()->output.summary().contains("확인 필요"),"output failure is reported");
            check(!failed.closed,"output failure never closes button input");
            UsbIo.Hid replacement=new UsbIo.Hid(); port.set(replacement); output.submit(frame);
            await(()->replacement.rgb==0x204080,"new IO4 handle restores latest colour");
            output.destroy(); await(()->replacement.rgb==0,"destroy clears RGB");
            check(!replacement.closed,"ceiling worker does not own input handle");
        } finally { producer.shutdownNow(); output.destroy(); }
        System.out.println("PASS: "+checks+" speaker RGB lifecycle checks");
    }
}
