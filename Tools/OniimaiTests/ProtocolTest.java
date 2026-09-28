package net.majdata.majdataplay.oniimai;
import java.util.*;
import java.io.ByteArrayOutputStream;

public final class ProtocolTest {
    static int checks;
    static void check(boolean b,String name){checks++;if(!b)throw new AssertionError(name);}
    static void invalid(Runnable f,String name){try{f.run();throw new AssertionError(name);}catch(IllegalArgumentException expected){checks++;}}
    static byte[] reply(int cmd,int status,byte[] data){ByteArrayOutputStream out=new ByteArrayOutputStream();out.write(0x53);byte[] body=new byte[data.length+3];body[0]=(byte)cmd;body[1]=(byte)status;body[2]=(byte)data.length;System.arraycopy(data,0,body,3,data.length);for(byte b:body){if(b==0x53||b==0x7c)out.write(0x7c);out.write(b);}return out.toByteArray();}
    public static void main(String[] args){
        check(Protocol.hex(Protocol.command(0x20,new byte[0])).equals("532000"),"GET config known vector");
        check(Protocol.hex(Protocol.command(0x21,new byte[]{0x53,0x7c,0})).equals("5321037C537C7C00"),"Command escaping known vector");
        byte[] data=new byte[144];data[0]=1;data[4]=2;data[8]=0x34;data[9]=0x12;data[76]=0x7c;data[77]=0x53;
        Protocol.TouchDebug debug=new Protocol.TouchDebug(data);check(debug.pressed[0]&&debug.pressed[33],"34-bit touch map");check(debug.raw[0]==0x1234&&debug.baseline[0]==0x537c,"unsigned LE readings");
        invalid(()->new Protocol.TouchDebug(new byte[143]),"Short debug rejected");
        byte[] encoded=reply(0x12,0,data);
        for(int split=0;split<=encoded.length;split++){
            List<byte[]> got=new ArrayList<>();Protocol.CommandParser parser=new Protocol.CommandParser((cmd,status,payload)->got.add(payload));
            parser.feed(Arrays.copyOfRange(encoded,0,split),split);parser.feed(Arrays.copyOfRange(encoded,split,encoded.length),encoded.length-split);
            check(got.size()==1&&Arrays.equals(got.get(0),data),"Split command at "+split);
        }
        for(int value=0;value<256;value++){
            byte[] payload={(byte)value,0x53,0x7c};List<byte[]> got=new ArrayList<>();Protocol.CommandParser parser=new Protocol.CommandParser((c,s,p)->got.add(p));
            byte[] b=reply(0x20,0,payload);for(byte v:b)parser.feed(new byte[]{v},1);
            check(got.size()==1&&Arrays.equals(got.get(0),payload),"All octets including escaped final byte "+value);
        }
        List<Integer> cmds=new ArrayList<>();Protocol.CommandParser multi=new Protocol.CommandParser((c,s,p)->cmds.add(c));
        byte[] a=reply(0x20,0,new byte[]{1}),b=reply(0x12,0,data),c=reply(0x23,0,new byte[0]);ByteArrayOutputStream joined=new ByteArrayOutputStream();for(byte[] part:new byte[][]{new byte[]{5,8,0x53,8,7,90},a,b,c})joined.write(part,0,part.length);byte[] combined=joined.toByteArray();multi.feed(combined,combined.length);
        check(cmds.equals(Arrays.asList(0x20,0x12,0x23)),"Noise, resync, debug interleave, empty ACK");
        byte[] original=new byte[156];new Random(2).nextBytes(original);Protocol.Config cfg=new Protocol.Config(original);cfg.sensitivity(33,512,255,128);cfg.brightness(50,60);byte[] modified=cfg.bytes();
        for(int i=0;i<156;i++)if(!(i>=132&&i<136)&&i!=139&&i!=140)check(original[i]==modified[i],"Preserve unrelated config byte "+i);
        check(cfg.finger(33)==512&&cfg.noise(33)==255&&cfg.hysteresis(33)==128,"Config LE fields");
        modified[0]^=1;check(!Arrays.equals(modified,cfg.bytes()),"Defensive config copy");
        invalid(()->new Protocol.Config(new byte[155]),"Unknown config sizes rejected");invalid(()->cfg.sensitivity(34,1,1,1),"Zone bound");invalid(()->cfg.sensitivity(0,513,0,0),"Sensitivity bound");
        for(int zone=0;zone<34;zone++){
            List<boolean[]> got=new ArrayList<>();Protocol.TouchParser parser=new Protocol.TouchParser(got::add);byte[] packet=new byte[9];packet[0]=40;packet[8]=41;packet[1+zone/5]=(byte)(1<<(zone%5));for(byte v:packet)parser.feed(new byte[]{v},1);
            check(got.size()==1&&got.get(0)[zone],"Touch serial zone "+zone);
        }
        List<boolean[]> malformed=new ArrayList<>();Protocol.TouchParser tp=new Protocol.TouchParser(malformed::add);tp.feed(new byte[]{40,1,2,41,40,32,0,0,0,0,0,0,41},13);check(malformed.isEmpty(),"Malformed touch rejected");
        check(Protocol.hex(Protocol.led(17,0x31,new byte[]{3,(byte)255,(byte)255,(byte)255})).equals("E01101053103FFFFFF48"),"Published Mai2LED single LED vector");
        check(Protocol.hex(Protocol.led(17,0x39,new byte[]{(byte)0xd0,(byte)0xd0,(byte)255})).equals("E011010439D0CFD0CFFFEE"),"Published Mai2LED escape vector");
        check(Protocol.hex(Protocol.led(17,0x3c,new byte[0])).equals("E01101013C4F"),"Published LED update vector");
        byte[] ack={(byte)0xe0,1,17,3,1,0x3c,1,0x53};List<Integer> gotAck=new ArrayList<>();Protocol.LedParser lp=new Protocol.LedParser((source,cmd,status,report,p)->gotAck.add(cmd));for(byte v:ack)lp.feed(new byte[]{v},1);check(gotAck.equals(Arrays.asList(0x3c)),"LED ACK checksum");ack[7]=0;lp.feed(ack,ack.length);check(gotAck.size()==1,"LED bad checksum rejected");
        byte[] io4=new byte[64];io4[0]=1;int idle=0xf80d;io4[29]=(byte)idle;io4[30]=(byte)(idle>>8);boolean[] idleButtons=Protocol.io4(io4,0);check(!idleButtons[0]&&!idleButtons[7],"IO4 active-low idle");io4[29]&=~4;check(Protocol.io4(io4,0)[0],"IO4 button 1");invalid(()->Protocol.io4(new byte[16],0),"Unknown HID report not guessed");
        check(!Protocol.io4P1(io4),"IO4 P1 idle");
        io4[29]|=2; check(Protocol.io4P1(io4),"IO4 P1 active high independent of button bank");
        io4[29]&=~2; check(!Protocol.io4P1(io4),"IO4 P1 release");
        io4[29]|=64; check(!Protocol.io4P1(io4),"IO4 P2 is not P1");
        invalid(()->Protocol.io4P1(new byte[16]),"P1 rejects unknown report");
        System.out.println("PASS: "+checks+" protocol checks");
    }
}
