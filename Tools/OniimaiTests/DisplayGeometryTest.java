package net.majdata.majdataplay.oniimai;

public final class DisplayGeometryTest {
    private static int checks;
    private static void near(float a, float b) {
        checks++; if (Math.abs(a-b) > .002f) throw new AssertionError(a+" != "+b);
    }
    private static float[] map(float[] m, float x, float y) {
        return new float[]{m[0]*x+m[1]*y+m[2], m[3]*x+m[4]*y+m[5]};
    }
    public static void main(String[] args) {
        for (int[] size : new int[][]{{1920,1080},{3840,2160},{2560,1440},{1280,720},{1024,768},{3440,1440}}) {
            int w=size[0], h=size[1];
            for (boolean reverse : new boolean[]{false,true}) {
                float[] surface=DisplayGeometry.surfaceMatrix(w,h,reverse);
                float[] texture=DisplayGeometry.textureMatrix(w,h,reverse);
                float[][] expected=reverse ? new float[][]{{0,h},{0,0},{w,0},{w,h},{w/2f,h/2f}}
                        : new float[][]{{w,0},{w,h},{0,h},{0,0},{w/2f,h/2f}};
                int[][] source={{0,0},{1080,0},{1080,1920},{0,1920},{540,960}};
                for (int i=0;i<source.length;i++) {
                    float[] point=map(surface,source[i][0],source[i][1]);
                    near(expected[i][0],point[0]); near(expected[i][1],point[1]);
                }
                // A route fallback must not move a note or sensor position.
                for (float x : new float[]{0,90,540,991,1080}) for (float y : new float[]{0,203,960,1701,1920}) {
                    float[] direct=map(surface,x,y), fallback=map(texture,x*w/1080f,y*h/1920f);
                    near(direct[0],fallback[0]); near(direct[1],fallback[1]);
                }
                if (w*9==h*16) near(Math.abs(surface[1]),Math.abs(surface[3]));
            }
        }
        for (int[] size : new int[][]{{0,1080},{1920,0},{-1,1080},{1920,-1}}) {
            try { DisplayGeometry.surfaceMatrix(size[0],size[1],false); throw new AssertionError("invalid display"); }
            catch (IllegalArgumentException expected) { checks++; }
        }
        System.out.println("PASS: "+checks+" direct/fallback display geometry checks");
    }
}
