package net.majdata.majdataplay.oniimai;

/** IO4 HID output report, following whowechina/mai_pico firmware/src/hid.c. */
public final class Io4Output {
    public static final int REPORT_ID=0x10, REPORT_BYTES=64;
    private Io4Output(){}
    /** Both player channels receive the same colour; the controller selects its player. */
    public static byte[] ceiling(int rgb){
        byte[] report=new byte[REPORT_BYTES];
        report[0]=REPORT_ID;report[1]=0x41;report[2]=(byte)0xfc;
        report[4]=report[5]=(byte)(rgb>>16);
        report[6]=report[7]=(byte)(rgb>>8);
        report[8]=report[9]=(byte)rgb;
        return report;
    }
}
