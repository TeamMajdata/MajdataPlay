package net.majdata.majdataplay.oniimai;

import java.util.HashMap;
import java.util.Map;

public final class WidgetFieldsTest {
    private static int checks;
    private static void check(boolean ok) { checks++; if(!ok)throw new AssertionError("check "+checks); }
    private static boolean changed(String type,Map<String,Object> a,Map<String,Object> b){return WidgetFields.changed(type,a::get,b::get);}
    public static void main(String[] args){
        Map<String,Object> before=new HashMap<>();before.put("game",true);before.put("scene","Game");before.put("seconds",0.0);
        String[] stationary={"song","score","judgments","timing","connection","clock","sensors"};
        for(int i=1;i<=600;i++){
            Map<String,Object> next=new HashMap<>(before);next.put("seconds",i/10.0);
            check(changed("graph",before,next));
            for(String type:stationary)check(!changed(type,before,next));
            before=next;
        }
        String[][] fields={{"song","title","artist","level"},{"score","achievement","combo","dx"},
            {"judgments","critical","perfect","great","good","miss"},{"timing","fast","late"},{"graph","seconds","length"}};
        for(String[] group:fields){
            for(int i=1;i<group.length;i++){
                Map<String,Object> next=new HashMap<>(before);next.put(group[i],"changed");
                check(changed(group[0],before,next));
                check(!changed(group[0],next,new HashMap<>(next)));
            }
            Map<String,Object> result=new HashMap<>(before);result.put("scene","Result");
            check(changed(group[0],before,result));
            check(changed(group[0],result,Map.of("game",false,"scene","List")));
        }
        System.out.println("PASS: "+checks+" widget invalidation checks");
    }
}
