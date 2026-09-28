package net.majdata.majdataplay.oniimai;

import java.io.IOException;
import java.util.concurrent.*;

/** A single LED request in flight. A timeout closes the port so late ACKs cannot acknowledge later frames. */
final class LedChannel implements AutoCloseable {
    private final UsbIo.Cdc port;
    private final int address,timeout;
    private volatile CompletableFuture<byte[]> pending;
    private volatile int expected=-1;
    private volatile boolean closed;
    LedChannel(UsbIo.Cdc port,int address){this(port,address,700);}
    LedChannel(UsbIo.Cdc port,int address,int timeout){
        this.port=port;this.address=address;this.timeout=timeout;
        Protocol.LedParser parser=new Protocol.LedParser((source,cmd,status,report,data)->{
            CompletableFuture<byte[]> p=pending;
            if(p!=null&&source==address&&cmd==expected){
                if(status==1&&report==1)p.complete(data);
                else p.completeExceptionally(new IOException(UiText.t("LED 응답 오류 ")+status+" / "+report));
            }
        });
        port.start(parser::feed,error->{close();});
    }
    synchronized void enableReplies() throws IOException {port.write(Protocol.led(address,0x7d,new byte[0]));}
    synchronized byte[] request(int command,byte[] payload) throws IOException {
        if(closed)throw new IOException(UiText.t("LED 포트가 닫혔습니다. 다시 연결하세요."));
        CompletableFuture<byte[]> p=new CompletableFuture<>();expected=command;pending=p;
        try{
            port.write(Protocol.led(address,command,payload));return p.get(timeout,TimeUnit.MILLISECONDS);
        }catch(TimeoutException e){close();throw new IOException(UiText.t("LED 응답 없음 — 포트·노드 주소를 확인하세요."));}
        catch(InterruptedException e){Thread.currentThread().interrupt();close();throw new IOException(UiText.t("LED 요청 취소"),e);}
        catch(ExecutionException e){close();throw new IOException(e.getCause().getMessage(),e.getCause());}
        catch(IOException e){close();throw e;}
        finally{pending=null;expected=-1;}
    }
    byte[] request(int command) throws IOException {return request(command,new byte[0]);}
    boolean isClosed(){return closed;}
    public void close(){
        if(closed)return;closed=true;
        CompletableFuture<byte[]> p=pending;if(p!=null)p.completeExceptionally(new IOException(UiText.t("LED 연결 종료")));port.close();
    }
}
