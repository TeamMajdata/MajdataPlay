package net.majdata.majdataplay.oniimai;

/** Physical output conversion, independent of USB and Android. RGB packed as 0xRRGGBB. */
final class LedFrames {
    // MajdataPlay has no separate RGB ceiling channel. Mix its eight game
    // button colours; cabinet brightness remains the independent white/FET output.
    static int ceiling(int[] colors) {
        if (colors == null || colors.length != 9) throw new IllegalArgumentException("9 LED colors required");
        int r = 0, g = 0, b = 0;
        for (int i = 0; i < 8; i++) { r += colors[i] >>> 16 & 255; g += colors[i] >>> 8 & 255; b += colors[i] & 255; }
        return ((r + 4) / 8 << 16) | ((g + 4) / 8 << 8) | (b + 4) / 8;
    }
    static int scale(int rgb,int percent){
        percent=Math.max(0,Math.min(100,percent));
        return (((rgb>>>16&255)*percent+50)/100<<16)|(((rgb>>>8&255)*percent+50)/100<<8)|((rgb&255)*percent+50)/100;
    }
    static int[] physical(int[] colors,int brightness,int rotation,boolean reverse){
        if(colors==null||colors.length!=9)throw new IllegalArgumentException("9 LED colors required");
        int[] result=new int[9];rotation=Math.floorMod(rotation,8);
        for(int i=0;i<8;i++)result[Math.floorMod(rotation+(reverse?-i:i),8)]=scale(colors[i],brightness);
        int ring=scale(colors[8],brightness);result[8]=Math.max(ring>>>16&255,Math.max(ring>>>8&255,ring&255));return result;
    }
    static byte[] color(int index,int rgb){
        if(index<0||index>31)throw new IllegalArgumentException("LED index 0..31");
        return new byte[]{(byte)index,(byte)(rgb>>16),(byte)(rgb>>8),(byte)rgb};
    }
}
