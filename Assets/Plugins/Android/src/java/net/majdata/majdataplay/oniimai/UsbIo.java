package net.majdata.majdataplay.oniimai;

import android.hardware.usb.*;
import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;

/** CDC-ACM implementation for the STM32 composite device, with independent handles per interface. */
public final class UsbIo {
    public interface Bytes {void accept(byte[] data,int length);}
    public interface Failure {void accept(String message);}
    public static final class Port {
        public final UsbDevice device; public final int controlId;public final boolean hid;
        public String name;
        Port(UsbDevice d,UsbInterface i,String name) {device=d;controlId=i.getId();hid=i.getInterfaceClass()==3;this.name=name;}
        public String key() {return device.getDeviceName()+"#"+controlId;}
    }
    public static List<Port> ports(UsbManager manager) {
        List<Port> result=new ArrayList<>();
        for(UsbDevice d:manager.getDeviceList().values()) {
            UsbDeviceConnection conn=manager.hasPermission(d)?manager.openDevice(d):null;
            try {
                Map<Integer,String> names=conn==null?new HashMap<>():names(conn);
                for(int n=0;n<d.getInterfaceCount();n++) {
                    UsbInterface i=d.getInterface(n);
                    if(i.getAlternateSetting()!=0) continue;
                    if((i.getInterfaceClass()==2 && i.getInterfaceSubclass()==2) || i.getInterfaceClass()==3) {
                        String name=names.get(i.getId());
                        if(name==null || name.isEmpty()) name=i.getName();
                        if(name==null || name.isEmpty()) name=(i.getInterfaceClass()==3?"HID":"CDC-ACM")+" IF"+i.getId();
                        result.add(new Port(d,i,name));
                    }
                }
            } finally {if(conn!=null)conn.close();}
        }
        result.sort(Comparator.comparing(Port::key));return result;
    }
    private static Map<Integer,String> names(UsbDeviceConnection conn) {
        Map<Integer,String> result=new HashMap<>();byte[] raw=conn.getRawDescriptors();
        if(raw==null)return result;
        byte[] languages=new byte[255];int count=conn.controlTransfer(0x80,6,0x300,0,languages,languages.length,300);
        int language=count>=4?Protocol.u16(languages,2):0x0409;
        for(int p=0;p+2<=raw.length;) {
            int len=Protocol.u(raw[p]),type=Protocol.u(raw[p+1]);if(len<2 || p+len>raw.length)break;
            if((type==4 && len>=9) || (type==11 && len>=8)) {
                int id=Protocol.u(raw[p+2]),index=Protocol.u(raw[p+(type==4?8:7)]);
                if(index>0) {
                    byte[] str=new byte[255];int n=conn.controlTransfer(0x80,6,0x300|index,language,str,str.length,300);
                    if(n>=2) {int end=Math.min(n,Protocol.u(str[0]));if(end>=2)result.put(id,new String(str,2,(end-2)&~1,StandardCharsets.UTF_16LE));}
                }
            }
            p+=len;
        }
        return result;
    }
    private static UsbInterface byId(UsbDevice d,int id) throws IOException {
        for(int n=0;n<d.getInterfaceCount();n++)if(d.getInterface(n).getId()==id && d.getInterface(n).getAlternateSetting()==0)return d.getInterface(n);
        throw new IOException(UiText.t("USB 인터페이스를 찾을 수 없습니다: ")+id);
    }
    public static final class Cdc implements AutoCloseable {
        private final UsbDeviceConnection conn;private UsbInterface control,data;private UsbEndpoint in,out;
        private volatile boolean open=true;private Thread reader;
        public Cdc(UsbManager manager,Port port,int baud) throws IOException {
            if(port.hid)throw new IOException(UiText.t("시리얼 포트가 아닙니다."));
            if(!manager.hasPermission(port.device))throw new IOException(UiText.t("USB 권한을 먼저 허용하세요."));
            conn=manager.openDevice(port.device);if(conn==null)throw new IOException(UiText.t("USB 열기 실패"));
            try {
                control=byId(port.device,port.controlId);int dataId=-1;byte[] raw=conn.getRawDescriptors();int current=-1;
                if(raw!=null)for(int p=0;p+2<=raw.length;) {
                    int len=Protocol.u(raw[p]),type=Protocol.u(raw[p+1]);if(len<2 || p+len>raw.length)break;
                    if(type==4 && len>=9)current=Protocol.u(raw[p+2]);
                    if(type==0x24 && len>=5 && Protocol.u(raw[p+2])==6 && current==control.getId() && Protocol.u(raw[p+3])==control.getId()) dataId=Protocol.u(raw[p+4]);
                    p+=len;
                }
                if(dataId<0)dataId=control.getId()+1;
                data=byId(port.device,dataId);if(data.getInterfaceClass()!=10)throw new IOException(UiText.t("CDC 데이터 인터페이스가 아닙니다."));
                if(!conn.claimInterface(control,true) || !conn.claimInterface(data,true))throw new IOException(UiText.t("USB 포트 사용 중 또는 권한 오류"));
                for(int n=0;n<data.getEndpointCount();n++) {
                    UsbEndpoint e=data.getEndpoint(n);if(e.getType()!=UsbConstants.USB_ENDPOINT_XFER_BULK)continue;
                    if(e.getDirection()==UsbConstants.USB_DIR_IN)in=e;else out=e;
                }
                if(in==null || out==null)throw new IOException(UiText.t("CDC bulk endpoint 없음"));
                byte[] coding={(byte)baud,(byte)(baud>>8),(byte)(baud>>16),(byte)(baud>>24),0,0,8};
                if(conn.controlTransfer(0x21,0x20,0,control.getId(),coding,coding.length,1000)!=7)throw new IOException(UiText.t("115200/9600 8N1 설정 실패"));
                if(conn.controlTransfer(0x21,0x22,3,control.getId(),null,0,1000)<0)throw new IOException(UiText.t("DTR/RTS 설정 실패"));
            } catch(Exception e) {close();throw e instanceof IOException?(IOException)e:new IOException(e);}
        }
        public void start(Bytes callback,Failure failure) {
            reader=new Thread(()->{
                byte[] b=new byte[1024];
                try {while(open) {int n=conn.bulkTransfer(in,b,b.length,200);if(n>0)callback.accept(b,n);}}
                catch(Exception e) {if(open)failure.accept(e.toString());}
            },"Oniimai-CDC-"+control.getId());reader.start();
        }
        public synchronized void write(byte[] bytes) throws IOException {
            if(!open)throw new IOException(UiText.t("포트가 닫혔습니다."));
            for(int offset=0;offset<bytes.length;) {
                int count=Math.min(32,bytes.length-offset);
                int n=conn.bulkTransfer(out,bytes,offset,count,1000);
                if(n<=0)throw new IOException(UiText.t("USB 전송 실패 (")+offset+"/"+bytes.length+")");offset+=n;
            }
        }
        public void close() {
            open=false;
            try {if(control!=null)conn.controlTransfer(0x21,0x22,0,control.getId(),null,0,100);}catch(Exception ignored){}
            try {if(data!=null)conn.releaseInterface(data);if(control!=null)conn.releaseInterface(control);}catch(Exception ignored){}
            conn.close();
            if(reader!=null && Thread.currentThread()!=reader)try {reader.join(500);}catch(InterruptedException e){Thread.currentThread().interrupt();}
        }
    }
    public static final class Hid implements AutoCloseable {
        private final UsbDeviceConnection conn;private UsbInterface intf;private final UsbRequest request=new UsbRequest();
        private final UsbRequest outputRequest=new UsbRequest();private boolean interruptOutput;
        private final Object submissionLock=new Object();
        private volatile boolean open=true;private Thread reader;
        private final AtomicBoolean closed=new AtomicBoolean();
        private volatile boolean outputFailed;
        private volatile Output pendingOutput;
        private static final class Output {
            final ByteBuffer buffer=ByteBuffer.allocateDirect(Io4Output.REPORT_BYTES);
            final CompletableFuture<Integer> completion=new CompletableFuture<>();
            Output(byte[] report){buffer.put(report).flip();}
        }
        public Hid(UsbManager manager,Port port,Bytes listener,Failure failure) throws IOException {
            if(!manager.hasPermission(port.device))throw new IOException(UiText.t("HID USB 권한을 허용하세요."));
            conn=manager.openDevice(port.device);if(conn==null)throw new IOException(UiText.t("HID 열기 실패"));
            try {
                intf=byId(port.device,port.controlId);
                if(!port.hid || !conn.claimInterface(intf,true))throw new IOException(UiText.t("HID 인터페이스 사용 중"));
                UsbEndpoint ep=null,out=null;
                for(int n=0;n<intf.getEndpointCount();n++){
                    UsbEndpoint e=intf.getEndpoint(n);if(e.getType()!=3)continue;
                    if(e.getDirection()==128)ep=e;else out=e;
                }
                if(ep==null || !request.initialize(conn,ep))throw new IOException(UiText.t("HID interrupt endpoint 없음"));
                // A missing OUT endpoint uses the standard HID SET_REPORT path.
                // Failure to initialise an existing endpoint does not guess another transport.
                if(out!=null){interruptOutput=outputRequest.initialize(conn,out);outputFailed=!interruptOutput;}
                final int capacity=Math.max(64,ep.getMaxPacketSize());
                reader=new Thread(()->{
                    ByteBuffer buffer=ByteBuffer.allocateDirect(capacity);boolean queued=false;
                    try {while(open) {
                        if(!queued){
                            synchronized(submissionLock){
                                if(!open)break;buffer.clear();
                                if(!request.queue(buffer))throw new IOException(UiText.t("HID queue 실패"));queued=true;
                            }
                        }
                        UsbRequest done;try {done=conn.requestWait(1000);}catch(TimeoutException timeout){continue;}
                        if(!open)break;
                        // requestWait returns completions for every endpoint on this handle.
                        // Only this thread drains them, so output never steals a button report.
                        if(done==outputRequest){Output output=pendingOutput;if(output!=null)output.completion.complete(output.buffer.position());continue;}
                        if(done!=request)throw new IOException(UiText.t("HID 연결 종료"));queued=false;
                        int n=buffer.position();buffer.flip();byte[] bytes=new byte[n];buffer.get(bytes);if(n>0)listener.accept(bytes,n);
                    }}catch(Exception e){
                        outputFailed=true;Output output=pendingOutput;if(output!=null)output.completion.completeExceptionally(e);
                        if(open)failure.accept(e.toString());
                    }
                },"Oniimai-HID");reader.start();
            } catch(Exception e) {close();throw e instanceof IOException?(IOException)e:new IOException(e);}
        }
        /** Blocking, bounded output: call from the LED worker, never the UI or HID input callback.
         * An output error disables further writes on this handle without closing button input.
         */
        public synchronized void writeCeiling(int rgb)throws IOException {
            if(Thread.currentThread()==reader)throw new IOException("IO4 output cannot run on its input reader");
            if(!open||closed.get())throw new IOException("IO4 handle is closed");
            if(outputFailed)throw new IOException("IO4 ceiling output unavailable");
            byte[] report=Io4Output.ceiling(rgb);
            if(!interruptOutput){
                int sent;
                synchronized(submissionLock){
                    if(!open||closed.get())throw new IOException("IO4 handle is closed");
                    try{sent=conn.controlTransfer(0x21,0x09,0x0200|Io4Output.REPORT_ID,intf.getId(),report,report.length,300);}
                    catch(RuntimeException e){outputFailed=true;throw new IOException("IO4 SET_REPORT failed",e);}
                }
                if(sent!=report.length){outputFailed=true;throw new IOException("IO4 SET_REPORT incomplete");}
                if(!open)throw new IOException("IO4 output canceled");
                return;
            }
            Output output=new Output(report);
            try {
                synchronized(submissionLock){
                    if(!open||closed.get())throw new IOException("IO4 handle is closed");
                    pendingOutput=output;
                    if(!outputRequest.queue(output.buffer))throw new IOException("IO4 output queue failed");
                }
                if(output.completion.get(500,TimeUnit.MILLISECONDS)!=report.length)throw new IOException("IO4 output report incomplete");
                if(!open)throw new IOException("IO4 output canceled");
                pendingOutput=null;
            }catch(InterruptedException e){Thread.currentThread().interrupt();disableOutput();throw new IOException("IO4 output interrupted",e);}
            catch(ExecutionException|TimeoutException e){disableOutput();throw new IOException("IO4 output did not complete",e);}
            catch(IOException|RuntimeException e){disableOutput();throw e instanceof IOException?(IOException)e:new IOException(e);}
        }
        private void disableOutput(){
            outputFailed=true;
            // Never reuse a canceled request: a late completion can still reach requestWait.
            // Its buffer stays strongly referenced until this HID handle is closed.
            try{outputRequest.cancel();}catch(Exception ignored){}
        }
        public void close(){
            if(!closed.compareAndSet(false,true))return;
            synchronized(submissionLock){
                open=false;
                Output output=pendingOutput;if(output!=null)output.completion.completeExceptionally(new IOException("IO4 handle closed"));
                try{outputRequest.cancel();}catch(Exception ignored){}
            }
            try{request.cancel();}catch(Exception ignored){}
            // A control transfer may already be running. Do not close its fd underneath it.
            synchronized(this){conn.close();}
            if(reader!=null&&Thread.currentThread()!=reader)try{reader.join(500);}catch(InterruptedException e){Thread.currentThread().interrupt();}
            try{request.close();}catch(Exception ignored){}
            try{outputRequest.close();}catch(Exception ignored){}
            pendingOutput=null;
        }
    }
}
