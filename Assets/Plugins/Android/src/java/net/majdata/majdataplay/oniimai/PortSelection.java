package net.majdata.majdataplay.oniimai;

import java.util.Locale;

/** Matches persistent identities and named firmware interfaces, never USB bus addresses. */
final class PortSelection {
    static final int NONE=0, COMMAND=1, TOUCH=2, LED=3, IO4=4;
    static int role(String name, boolean hid) {
        if (name == null) return NONE;
        String n=name.trim().toLowerCase(Locale.ROOT);
        // Actual onii-mai IF0 uses the SEGA IO4 board identity; IF1 is a separate keyboard.
        if(hid && n.startsWith("i/o control bd;15257;"))return IO4;
        if (!n.startsWith("onii-mai ") && !n.startsWith("oniimai ")) return NONE;
        if(hid)return n.endsWith(" io4")||n.endsWith(" io4 hid")?IO4:NONE;
        if (n.endsWith(" command")) return COMMAND;
        if (n.endsWith(" touch")) return TOUCH;
        if (n.endsWith(" led")) return LED;
        return NONE;
    }
    static String base(String id) {
        int a=id.indexOf(':'), b=id.indexOf(':',a+1), c=id.indexOf(':',b+1);
        return a<0 || b<0 || c<0 ? "" : id.substring(0,c+1);
    }
    static boolean samePort(String saved, String current) {
        return !saved.isEmpty() && (saved.equals(current) ||
                (saved.equals(base(saved)) && saved.equals(base(current))));
    }
    static int unique(String[] ids, String saved) {
        int found=-1;
        for(int i=0;i<ids.length;i++) if(samePort(saved,ids[i])) {
            if(found>=0) return -2;
            found=i;
        }
        return found;
    }
    static int named(String[] names, boolean[] hid, int role) {
        int found=-1;
        for(int i=0;i<names.length;i++) if(role(names[i],hid[i])==role) {
            if(found>=0) return -2;
            found=i;
        }
        return found;
    }
    static int automaticTouch(String[] names,boolean[] hid){return named(names,hid,TOUCH);}
    static int protocol(String name,boolean hid,int fallback){int role=role(name,hid);return role==TOUCH?1:role==COMMAND?0:fallback;}
}
