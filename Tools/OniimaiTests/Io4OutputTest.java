package net.majdata.majdataplay.oniimai;
import java.util.Arrays;

public final class Io4OutputTest {
    private static int checks;
    private static void check(boolean valid,String name){if(!valid)throw new AssertionError(name);checks++;}
    public static void main(String[] args){
        byte[] frame=Io4Output.ceiling(0x12d0e0);
        check(frame.length==64,"IO4 report ID plus 63 descriptor bytes");
        check(Arrays.equals(Arrays.copyOf(frame,10),new byte[]{0x10,0x41,(byte)0xfc,0,0x12,0x12,(byte)0xd0,(byte)0xd0,(byte)0xe0,(byte)0xe0}),"published RGB report layout, both players, no CDC escaping");
        for(int i=10;i<64;i++)check(frame[i]==0,"reserved byte "+i);
        check(Arrays.equals(frame,Io4Output.ceiling(0xff12d0e0)),"ARGB alpha ignored");
        for(int value=0;value<256;value++){
            byte[] r=Io4Output.ceiling(value<<16),g=Io4Output.ceiling(value<<8),b=Io4Output.ceiling(value);
            check((r[4]&255)==value&&(r[5]&255)==value&&r[6]==0&&r[8]==0,"red "+value);
            check((g[6]&255)==value&&(g[7]&255)==value&&g[4]==0&&g[8]==0,"green "+value);
            check((b[8]&255)==value&&(b[9]&255)==value&&b[4]==0&&b[6]==0,"blue "+value);
        }
        frame[0]=0;check(Io4Output.ceiling(0)[0]==16,"caller cannot alter later reports");
        System.out.println("PASS: "+checks+" IO4 ceiling report checks");
    }
}
