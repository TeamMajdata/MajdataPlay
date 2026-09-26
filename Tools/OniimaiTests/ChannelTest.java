package net.majdata.majdataplay.oniimai;
import java.io.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicInteger;

public final class ChannelTest {
    static int checks;
    static void check(boolean value,String label){checks++;if(!value)throw new AssertionError(label);}
    static byte[] reply(int cmd,int status,byte[] data){ByteArrayOutputStream out=new ByteArrayOutputStream();out.write(0x53);byte[] body=new byte[data.length+3];body[0]=(byte)cmd;body[1]=(byte)status;body[2]=(byte)data.length;System.arraycopy(data,0,body,3,data.length);for(byte b:body){if(b==0x53||b==0x7c)out.write(0x7c);out.write(b);}return out.toByteArray();}
    public static void main(String[] args)throws Exception {
        UsbIo.Cdc port=new UsbIo.Cdc();AtomicInteger debug=new AtomicInteger();
        CommandChannel channel=new CommandChannel(port,(b,n)->debug.incrementAndGet(),s->{},s->{});
        port.onWrite=packet->{port.receive(reply(0x12,0,new byte[144]));port.receive(reply(0x77,0,new byte[]{7}));port.receive(reply(0x20,0,new byte[]{83,124}));};
        check(Arrays.equals(channel.request(0x20),new byte[]{83,124}),"Debug and unrelated replies cannot complete configuration request");
        check(debug.get()==1,"Debug independently dispatched");
        port.onWrite=packet->port.receive(reply(0x21,3,new byte[0]));
        try{channel.request(0x21);throw new AssertionError("Device error must throw");}catch(IOException expected){check(expected.getMessage().contains("3"),"Device status propagated");}
        port.onWrite=packet->port.receive(reply(0x20,0,new byte[]{1}));check(channel.request(0x20)[0]==1,"Can retry after explicit error response");channel.close();
        ExecutorService executor=Executors.newSingleThreadExecutor();
        UsbIo.Cdc cancelled=new UsbIo.Cdc();CommandChannel closeable=new CommandChannel(cancelled,(b,n)->{},s->{},s->{});CountDownLatch written=new CountDownLatch(1);cancelled.onWrite=packet->written.countDown();
        Future<Boolean> pending=executor.submit(()->{try{closeable.request(0x20);return false;}catch(IOException e){return true;}});
        check(written.await(1,TimeUnit.SECONDS),"Cancellation test request started");closeable.close();check(pending.get(1,TimeUnit.SECONDS),"Disconnect unblocks pending request promptly");
        UsbIo.Cdc silent=new UsbIo.Cdc();CommandChannel timed=new CommandChannel(silent,(b,n)->{},s->{},s->{});
        try{timed.request(0x20);throw new AssertionError("Must time out");}catch(IOException expected){check(timed.isClosed()&&silent.closed,"Timeout closes and poisons transport");}
        silent.receive(reply(0x20,0,new byte[]{2}));
        try{timed.request(0x20);throw new AssertionError("Late ACK must not be reused");}catch(IOException expected){check(timed.isClosed(),"Late response cannot complete a later request");}
        UsbIo.Cdc failed=new UsbIo.Cdc();CommandChannel broken=new CommandChannel(failed,(b2,n)->{},s->{},s->{});
        failed.onWrite=packet->failed.failure.accept("USB unplugged");
        try{broken.request(0x20);throw new AssertionError("Reader failure must throw");}catch(IOException expected){check(broken.isClosed()&&failed.closed,"Reader failure closes transport and unblocks request");}
        executor.shutdownNow();System.out.println("PASS: "+checks+" command-channel checks");
    }
}
