package net.majdata.majdataplay.oniimai;
import java.io.*;
import java.util.*;
import java.util.concurrent.*;

public final class LedTest {
    static int checks;
    static void check(boolean value,String label){checks++;if(!value)throw new AssertionError(label);}
    static byte[] ack(int address,int command,int status,int report,byte[] payload){
        ByteArrayOutputStream out=new ByteArrayOutputStream();out.write(0xe0);
        byte[] body=new byte[payload.length+6];body[0]=1;body[1]=(byte)address;body[2]=(byte)(payload.length+3);body[3]=(byte)status;body[4]=(byte)command;body[5]=(byte)report;
        System.arraycopy(payload,0,body,6,payload.length);int sum=0;
        for(byte b:body){sum=(sum+(b&255))&255;escape(out,b&255);}escape(out,sum);return out.toByteArray();
    }
    static void escape(ByteArrayOutputStream out,int value){if(value==0xe0||value==0xd0){out.write(0xd0);out.write(value-1);}else out.write(value);}
    static byte[] decode(byte[] packet){ByteArrayOutputStream out=new ByteArrayOutputStream();for(int i=1;i<packet.length;i++){int n=packet[i]&255;if(n==0xd0)n=((packet[++i]&255)+1)&255;out.write(n);}return out.toByteArray();}
    public static void main(String[] args)throws Exception{
        int[] colors={0xff0000,0x00ff00,0x0000ff,0x123456,0xd0e001,0xffffff,0,0xabcdef,0x805030};
        check(Arrays.equals(Arrays.copyOf(LedFrames.physical(colors,100,0,false),8),Arrays.copyOf(colors,8)),"default button ordering");
        for(boolean reverse:new boolean[]{false,true})for(int rotation=0;rotation<8;rotation++){
            int[] frame=LedFrames.physical(colors,100,rotation,reverse);
            for(int i=0;i<8;i++)check(frame[Math.floorMod(rotation+(reverse?-i:i),8)]==colors[i],"mapping bijection");
        }
        check(LedFrames.scale(0xff8040,50)==0x804020,"rounded brightness");check(LedFrames.scale(0xffffff,-1)==0,"clamp dark");check(LedFrames.scale(0xabcdef,101)==0xabcdef,"clamp bright");
        check(LedFrames.physical(colors,50,0,false)[8]==64,"ring luminance to FET");
        for(int i=0;i<32;i++)check((LedFrames.color(i,0xd0e0ff)[0]&255)==i,"valid indices");
        try{LedFrames.color(32,0);throw new AssertionError();}catch(IllegalArgumentException expected){check(true,"invalid LED range");}
        UsbIo.Cdc port=new UsbIo.Cdc();LedChannel channel=new LedChannel(port,17,100);
        port.onWrite=packet->{port.receive(ack(18,0xf0,1,1,new byte[]{1}));port.receive(ack(17,0x31,1,1,new byte[]{2}));byte[] a=ack(17,0xf0,1,1,new byte[]{(byte)0xd0,(byte)0xe0});for(byte b:a)port.receive(new byte[]{b});};
        check(Arrays.equals(channel.request(0xf0),new byte[]{(byte)0xd0,(byte)0xe0}),"fragmentation, escaping, wrong node and command ignored");channel.close();
        UsbIo.Cdc errorPort=new UsbIo.Cdc();LedChannel error=new LedChannel(errorPort,17,100);
        errorPort.onWrite=p->errorPort.receive(ack(17,0x31,1,4,new byte[0]));
        try{error.request(0x31);throw new AssertionError();}catch(IOException expected){check(error.isClosed()&&errorPort.closed,"NAK closes LED channel");}
        UsbIo.Cdc silent=new UsbIo.Cdc();LedChannel timeout=new LedChannel(silent,17,30);
        try{timeout.request(0x31);throw new AssertionError();}catch(IOException expected){check(timeout.isClosed()&&silent.closed,"timeout poisons LED channel");}
        silent.receive(ack(17,0x31,1,1,new byte[0]));try{timeout.request(0x31);throw new AssertionError();}catch(IOException expected){check(true,"late ACK cannot revive closed channel");}
        UsbIo.Cdc cancelPort=new UsbIo.Cdc();LedChannel cancel=new LedChannel(cancelPort,17,2000);CountDownLatch sent=new CountDownLatch(1);cancelPort.onWrite=p->sent.countDown();ExecutorService worker=Executors.newSingleThreadExecutor();
        Future<Boolean> pending=worker.submit(()->{try{cancel.request(0xf0);return false;}catch(IOException expected){return true;}});
        check(sent.await(1,TimeUnit.SECONDS),"request started");cancel.close();check(pending.get(1,TimeUnit.SECONDS),"close wakes pending request");worker.shutdownNow();
        UsbIo.Cdc brokenPort=new UsbIo.Cdc();LedChannel broken=new LedChannel(brokenPort,17,100);brokenPort.onWrite=p->brokenPort.failure.accept("unplugged");
        try{broken.request(0xf0);throw new AssertionError();}catch(IOException expected){check(broken.isClosed(),"reader failure interrupts request");}
        System.out.println("PASS: "+checks+" LED mapping and ACK checks");
    }
}
