package net.majdata.majdataplay.oniimai;
import java.io.IOException;
import java.util.function.Consumer;

/** JVM-only fake. This file is never compiled into the Android app. */
public final class UsbIo {
    public interface Bytes {void accept(byte[] data,int length);}
    public interface Failure {void accept(String message);}
    public static final class Port {}
    public static final class Hid {
        public volatile int rgb=-1, writes;
        public volatile boolean fail, closed;
        public void writeCeiling(int rgb) throws IOException {
            if (fail || closed) throw new IOException("output unavailable");
            this.rgb=rgb; writes++;
        }
    }
    public static final class Cdc {
        public static volatile Consumer<Cdc> opened;
        public Cdc(){}
        public Cdc(android.hardware.usb.UsbManager manager,Port port,int baud){if(opened!=null)opened.accept(this);}
        public Consumer<byte[]> onWrite;public Bytes incoming;public Failure failure;public boolean closed;
        public void start(Bytes bytes,Failure fail){incoming=bytes;failure=fail;}
        public void write(byte[] bytes) throws IOException {if(closed)throw new IOException("closed");if(onWrite!=null)onWrite.accept(bytes);}
        public void receive(byte[] bytes){incoming.accept(bytes,bytes.length);}
        public void close(){closed=true;}
    }
}
