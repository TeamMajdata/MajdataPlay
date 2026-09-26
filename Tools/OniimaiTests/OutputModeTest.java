package net.majdata.majdataplay.oniimai;
public final class OutputModeTest {
    private static int checks;
    private static void check(boolean ok){checks++;if(!ok)throw new AssertionError("check "+checks);}
    private static OutputMode.Mode mode(int id,int w,int h,float rate){return new OutputMode.Mode(id,w,h,rate);}
    public static void main(String[] args){
        OutputMode.Mode uhd60=mode(1,3840,2160,59.94f),fhd120=mode(2,1920,1080,119.88f);
        OutputMode.Mode uhd120=mode(3,3840,2160,120),fhd60=mode(4,1920,1080,60);
        check(OutputMode.choose(uhd60,new OutputMode.Mode[]{fhd60,uhd60,fhd120},60)==uhd60);
        check(OutputMode.choose(uhd60,new OutputMode.Mode[]{uhd60,fhd120},120)==fhd120);
        check(OutputMode.choose(uhd60,new OutputMode.Mode[]{fhd120,uhd120},120)==uhd120);
        check(OutputMode.choose(fhd60,new OutputMode.Mode[]{uhd120,fhd120},120)==fhd120);
        check(OutputMode.choose(uhd60,new OutputMode.Mode[]{uhd60,fhd60},120)==null);
        check(OutputMode.choose(uhd60,new OutputMode.Mode[]{mode(5,1600,1200,120)},120)==null);
        check(OutputMode.choose(uhd60,new OutputMode.Mode[]{mode(6,1920,1080,Float.NaN)},120)==null);
        check(OutputMode.choose(uhd60,new OutputMode.Mode[]{mode(7,3840,0,120)},120)==null);
        check(OutputMode.choose(uhd60,new OutputMode.Mode[0],60)==null);
        check(OutputMode.choose(fhd120,new OutputMode.Mode[]{fhd60,uhd60},60)==fhd60);
        check(OutputMode.frameRate(120)==120);
        for(int saved:new int[]{-1,0,30,60,90,144,Integer.MAX_VALUE})check(OutputMode.frameRate(saved)==60);
        check(OutputMode.matches(119.88f,120));check(!OutputMode.matches(60,120));
        check(!OutputMode.matches(Float.POSITIVE_INFINITY,60));
        System.out.println("OutputMode: "+checks+" checks passed");
    }
}
