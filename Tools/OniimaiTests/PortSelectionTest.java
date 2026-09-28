package net.majdata.majdataplay.oniimai;

public final class PortSelectionTest {
    private static int checks;
    private static void eq(int expected,int actual) { checks++; if(expected!=actual) throw new AssertionError(expected+" != "+actual); }
    public static void main(String[] args) {
        String[] names={"onii-mai NFC","onii-mai Touch","onii-mai LED","onii-mai Command","onii-mai Keyboard","CDC Communications Control"};
        boolean[] hid={false,false,false,false,true,false};
        eq(3,PortSelection.named(names,hid,PortSelection.COMMAND));
        eq(1,PortSelection.named(names,hid,PortSelection.TOUCH));
        eq(2,PortSelection.named(names,hid,PortSelection.LED));
        eq(1,PortSelection.automaticTouch(names,hid)); // Touch wins even when Command is present.
        eq(-1,PortSelection.automaticTouch(new String[]{"onii-mai Command"},new boolean[1]));
        eq(-2,PortSelection.automaticTouch(new String[]{"onii-mai Touch","onii-mai Command","onii-mai Touch"},new boolean[3]));
        eq(1,PortSelection.protocol("onii-mai Touch",false,0));
        eq(0,PortSelection.protocol("onii-mai Command",false,1));
        eq(1,PortSelection.protocol("CDC-ACM IF4",false,1));
        eq(0,PortSelection.protocol("CDC-ACM IF4",false,0));
        eq(0,PortSelection.role("Generic LED",false));
        eq(0,PortSelection.role("onii-mai LED",true));
        eq(0,PortSelection.role(null,false));
        eq(1,PortSelection.role(" ONII-MAI COMMAND ",false));
        eq(-2,PortSelection.named(new String[]{"onii-mai LED","onii-mai LED"},new boolean[2],PortSelection.LED));
        eq(-1,PortSelection.named(new String[]{"onii-mai NFC"},new boolean[1],PortSelection.TOUCH));
        String[] ids={"100:200:3:ABC","100:200:5:ABC","100:200:3:XYZ"};
        eq(0,PortSelection.unique(ids,"100:200:3:ABC"));
        eq(2,PortSelection.unique(ids,"100:200:3:XYZ"));
        eq(-1,PortSelection.unique(ids,"100:200:3:OTHER"));
        eq(-2,PortSelection.unique(ids,"100:200:3:"));
        eq(1,PortSelection.unique(ids,"100:200:5:")); // chosen before permission: serial becomes available later
        eq(-1,PortSelection.unique(ids,""));
        eq(-1,PortSelection.unique(ids,"100:201:3:ABC"));
        eq(-2,PortSelection.unique(new String[]{ids[0],ids[0]},ids[0]));
        eq(0,PortSelection.unique(new String[]{ids[0]},ids[0])); // independent of /dev/bus address
        String io4="I/O CONTROL BD;15257;01;90;1831;6679A;00;GOUT=14_ADIN=8,E_ROTIN=4_COININ=2_SWIN=2,E_UQ1=41,6;";
        eq(PortSelection.IO4,PortSelection.role(io4,true));
        eq(PortSelection.NONE,PortSelection.role(io4,false)); // Never claim a serial interface as HID.
        eq(PortSelection.NONE,PortSelection.role("onii-mai Keyboard",true));
        eq(PortSelection.NONE,PortSelection.role("Generic HID",true));
        eq(PortSelection.IO4,PortSelection.role("onii-mai IO4",true));
        eq(PortSelection.IO4,PortSelection.role(" ONIIMAI IO4 HID ",true));
        eq(1,PortSelection.named(new String[]{"onii-mai Keyboard",io4,"onii-mai Touch"},new boolean[]{true,true,false},PortSelection.IO4));
        eq(-2,PortSelection.named(new String[]{io4,io4},new boolean[]{true,true},PortSelection.IO4));
        eq(-1,PortSelection.named(new String[]{"onii-mai Keyboard","Generic HID"},new boolean[]{true,true},PortSelection.IO4));
        System.out.println("PortSelection: "+checks+" checks passed");
    }
}
