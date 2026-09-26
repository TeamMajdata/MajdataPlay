package net.majdata.majdataplay.oniimai;

import java.util.*;

/** First-install defaults and upgrade migration, without overwriting explicit choices. */
final class SetupDefaults {
    static Map<String,Object> missing(Map<String,?> saved){
        Map<String,Object> patch=new LinkedHashMap<>();
        if(!saved.containsKey("setupComplete"))patch.put("setupComplete",!saved.isEmpty());
        if(!saved.containsKey("touchMode"))patch.put("touchMode",1);
        if(!saved.containsKey("buttonMode"))patch.put("buttonMode",2);
        if(!saved.containsKey("led"))patch.put("led",true);
        if(!saved.containsKey("external"))patch.put("external",true);
        if(!saved.containsKey("frameRate"))patch.put("frameRate",60);
        if(saved.isEmpty()){patch.put("input",true);patch.put("autoConnect",true);}
        return patch;
    }
}
