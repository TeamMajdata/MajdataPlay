package net.majdata.majdataplay.oniimai;

import java.io.ByteArrayOutputStream;
import java.util.Arrays;

/** Wire formats recovered from the supplied Assistant 1.0.1; no original app code is bundled. */
public final class Protocol {
    private Protocol() {}
    public static final String[] ZONES = {"A1","A2","A3","A4","A5","A6","A7","A8","B1","B2","B3","B4","B5","B6","B7","B8","C1","C2","D1","D2","D3","D4","D5","D6","D7","D8","E1","E2","E3","E4","E5","E6","E7","E8"};
    public static final int INFO=0, DEBUG_START=0x10, DEBUG_STOP=0x11, DEBUG_DATA=0x12,
        TOUCH_RESTART=0x13, CONFIG_GET=0x20, CONFIG_SET=0x21, CONFIG_SAVE=0x23;
    public static int u(byte b) { return b & 255; }
    public static int u16(byte[] b,int o) { return u(b[o]) | u(b[o+1])<<8; }
    public static long u32(byte[] b,int o) { return (long)u16(b,o) | (long)u16(b,o+2)<<16; }
    public static void range(int value,int min,int max) {
        if(value<min || value>max) throw new IllegalArgumentException(UiText.t("범위: ")+min+"–"+max);
    }
    public static byte[] command(int cmd, byte[] payload) {
        range(cmd,0,255); range(payload.length,0,255);
        ByteArrayOutputStream out=new ByteArrayOutputStream(); out.write(0x53);
        escaped(out,cmd,0x53,0x7c,false); escaped(out,payload.length,0x53,0x7c,false);
        for(byte b:payload) escaped(out,u(b),0x53,0x7c,false);
        return out.toByteArray();
    }
    private static void escaped(ByteArrayOutputStream out,int b,int sync,int esc,boolean decrement) {
        if(b==sync || b==esc) { out.write(esc); if(decrement) b=(b-1)&255; }
        out.write(b);
    }
    public interface Reply { void accept(int command,int status,byte[] payload); }
    public static final class CommandParser {
        private final byte[] buffer=new byte[258]; private int size; private boolean active,escape;
        private final Reply listener;
        public CommandParser(Reply listener) { this.listener=listener; }
        public void feed(byte[] bytes,int length) {
            for(int i=0;i<length;i++) {
                int b=u(bytes[i]);
                if(!escape && b==0x53) { size=0;active=true;escape=false;continue; }
                if(!active) continue;
                if(!escape && b==0x7c) { escape=true;continue; }
                escape=false;
                if(size>=buffer.length) { active=false;size=0;continue; }
                buffer[size++]=(byte)b;
                if(size>=3 && size==3+u(buffer[2])) {
                    byte[] data=Arrays.copyOfRange(buffer,3,size);
                    int cmd=u(buffer[0]),status=u(buffer[1]);active=false;size=0;
                    listener.accept(cmd,status,data);
                }
            }
        }
    }
    public static final class TouchDebug {
        public final boolean[] pressed=new boolean[34];
        public final int[] raw=new int[34],baseline=new int[34];
        public TouchDebug(byte[] b) {
            if(b.length!=144) throw new IllegalArgumentException(UiText.t("터치 디버그 길이: ")+b.length+UiText.t(" (144 필요)"));
            for(int i=0;i<34;i++) {
                pressed[i]=(u(b[i/8])&(1<<(i%8)))!=0;
                raw[i]=u16(b,8+2*i);baseline[i]=u16(b,76+2*i);
            }
        }
    }
    public interface TouchListener { void accept(boolean[] pressed); }
    public static final class TouchParser {
        private final byte[] body=new byte[7]; private int size=-1; private final TouchListener listener;
        public TouchParser(TouchListener listener) { this.listener=listener; }
        public void feed(byte[] bytes,int length) {
            for(int i=0;i<length;i++) {
                int b=u(bytes[i]);
                if(b==40) {size=0;continue;}
                if(b==41) {
                    if(size==7) { boolean[] p=new boolean[34];for(int j=0;j<34;j++) p[j]=(body[j/5]&(1<<(j%5)))!=0;listener.accept(p); }
                    size=-1;continue;
                }
                if(size>=0) {if(size<7 && b<=31) body[size++]=(byte)b;else size=-1;}
            }
        }
    }
    public static final class Config {
        private final byte[] bytes;
        public Config(byte[] bytes) {
            if(bytes.length!=156) throw new IllegalArgumentException(UiText.t("설정 형식이 다릅니다: ")+bytes.length+UiText.t(" bytes. 쓰기를 중단합니다."));
            this.bytes=bytes.clone();
        }
        public int finger(int zone) {range(zone,0,33);return u16(bytes,zone*4);}
        public int noise(int zone) {range(zone,0,33);return u(bytes[zone*4+2]);}
        public int hysteresis(int zone) {range(zone,0,33);return u(bytes[zone*4+3]);}
        public void sensitivity(int zone,int finger,int noise,int hysteresis) {
            range(zone,0,33);range(finger,0,512);range(noise,0,255);range(hysteresis,0,128);
            int i=zone*4;bytes[i]=(byte)finger;bytes[i+1]=(byte)(finger>>8);bytes[i+2]=(byte)noise;bytes[i+3]=(byte)hysteresis;
        }
        public int get(int offset) {return u(bytes[offset]);}
        public void brightness(int front,int external) {range(front,0,255);range(external,0,255);bytes[139]=(byte)external;bytes[140]=(byte)front;}
        public byte[] bytes() {return bytes.clone();}
    }
    /** Mai2LED / BD15070-04: corroborated by onii.cc and Sucareto/Mai2Touch. */
    public static byte[] led(int destination,int cmd,byte[] payload) {
        range(destination,1,255);range(payload.length,0,254);
        ByteArrayOutputStream out=new ByteArrayOutputStream();out.write(0xe0);
        byte[] body=new byte[4+payload.length];body[0]=(byte)destination;body[1]=1;body[2]=(byte)(payload.length+1);body[3]=(byte)cmd;
        System.arraycopy(payload,0,body,4,payload.length);int sum=0;
        for(byte b:body) {sum=(sum+u(b))&255;escaped(out,u(b),0xe0,0xd0,true);}
        escaped(out,sum,0xe0,0xd0,true);return out.toByteArray();
    }
    public interface LedReply {void accept(int source,int command,int status,int report,byte[] payload);}
    public static final class LedParser {
        private final byte[] buf=new byte[260]; private int n;private boolean active,escape;private final LedReply listener;
        public LedParser(LedReply listener) {this.listener=listener;}
        public void feed(byte[] bytes,int length) {
            for(int i=0;i<length;i++) {
                int b=u(bytes[i]);
                if(!escape && b==0xe0) {active=true;escape=false;n=0;continue;}
                if(!active) continue;
                if(!escape && b==0xd0) {escape=true;continue;}
                if(escape) b=(b+1)&255;escape=false;
                if(n>=buf.length) {active=false;continue;}
                buf[n++]=(byte)b;
                if(n>=3 && n==u(buf[2])+4) {
                    active=false;int sum=0;for(int j=0;j<n-1;j++)sum=(sum+u(buf[j]))&255;
                    if(n>=7 && sum==u(buf[n-1]) && u(buf[0])==1)
                        listener.accept(u(buf[1]),u(buf[4]),u(buf[3]),u(buf[5]),Arrays.copyOfRange(buf,6,n-1));
                }
            }
        }
    }
    public static String hex(byte[] b) {StringBuilder s=new StringBuilder();for(byte v:b)s.append(String.format("%02X",u(v)));return s.toString();}
    public static boolean[] io4(byte[] report,int player) {
        // SEGA IO4 report ID 1, 63-byte payload. Button banks begin at payload offset 28.
        if(report.length!=64 || u(report[0])!=1) throw new IllegalArgumentException("IO4 report ID/length mismatch");
        range(player,0,1);int word=u16(report,29+player*2);int[] bit={2,3,0,15,14,13,12,11,9,6,1};
        boolean[] states=new boolean[11];for(int i=0;i<11;i++)states[i]=((word&(1<<bit[i]))!=0)==(i>=8);return states;
    }
    public static boolean io4P1(byte[] report) {
        if(report.length!=64 || u(report[0])!=1) throw new IllegalArgumentException("IO4 report ID/length mismatch");
        // MajdataPlay GeneralHIDDevice: payload byte 28, bit 1, active high.
        // P1 is a function switch independent of the selected ring-button bank.
        return (u(report[29]) & 2) != 0;
    }
}
