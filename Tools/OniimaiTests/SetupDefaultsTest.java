package net.majdata.majdataplay.oniimai;
import java.util.*;
public final class SetupDefaultsTest {
    private static int checks;
    private static void check(boolean ok){checks++;if(!ok)throw new AssertionError("check "+checks);}
    public static void main(String[] args){
        Map<String,Object> fresh=SetupDefaults.missing(Collections.emptyMap());
        check(Boolean.FALSE.equals(fresh.get("setupComplete")));
        check(Boolean.TRUE.equals(fresh.get("led")));
        check(Boolean.TRUE.equals(fresh.get("input")));
        check(Boolean.TRUE.equals(fresh.get("autoConnect")));
        check(Integer.valueOf(1).equals(fresh.get("touchMode")));
        check(Integer.valueOf(2).equals(fresh.get("buttonMode")));
        check(Boolean.TRUE.equals(fresh.get("external")));
        check(Integer.valueOf(60).equals(fresh.get("frameRate")));
        check(SetupDefaults.missing(fresh).isEmpty()); // A deferred first run remains pending.
        Map<String,Object> legacy=new HashMap<>();legacy.put("led",false);legacy.put("touchMode",0);legacy.put("touchPort","user-command-port");legacy.put("input",false);legacy.put("buttonMode",1);legacy.put("external",false);
        Map<String,Object> patch=SetupDefaults.missing(legacy);
        check(Boolean.TRUE.equals(patch.get("setupComplete")));
        check(patch.size()==2); // Only missing setup state and frame-rate choice are added.
        check(Integer.valueOf(60).equals(patch.get("frameRate")));
        legacy.put("frameRate",120);
        check(!SetupDefaults.missing(legacy).containsKey("frameRate"));
        legacy.putAll(patch);check(SetupDefaults.missing(legacy).isEmpty());
        check(Integer.valueOf(1).equals(legacy.get("buttonMode"))); // Preserve an explicit keyboard choice.
        check(Boolean.FALSE.equals(legacy.get("external"))); // Explicit phone-only output is not reset.
        legacy.remove("buttonMode");check(Integer.valueOf(2).equals(SetupDefaults.missing(legacy).get("buttonMode")));
        Map<String,Object> pending=new HashMap<>(fresh);pending.put("setupStep",2);pending.put("led",false);
        check(SetupDefaults.missing(pending).isEmpty());
        pending.put("setupComplete",true);check(SetupDefaults.missing(pending).isEmpty());
        Map<String,Object> partial=new HashMap<>();partial.put("language","zh-Hans");
        patch=SetupDefaults.missing(partial);check(Boolean.TRUE.equals(patch.get("led")));check(Integer.valueOf(1).equals(patch.get("touchMode")));check(!patch.containsKey("language"));
        check(Integer.valueOf(2).equals(patch.get("buttonMode")));
        check(Boolean.TRUE.equals(patch.get("external")));
        check(Integer.valueOf(60).equals(patch.get("frameRate")));
        System.out.println("SetupDefaults: "+checks+" checks passed");
    }
}
