package net.majdata.majdataplay.oniimai;

/** Module-owned text only: no changes to the game's Resources or system locale. */
final class UiText {
    private static volatile String language="ko";
    static void language(String value){language="zh-Hans".equals(value)?"zh-Hans":"ko";}
    static String language(){return language;}
    static String message(String value){
        if(!"zh-Hans".equals(language))return value;
        java.util.ArrayList<String> keys=new java.util.ArrayList<>(UiTextCatalog.CHINESE.keySet());
        keys.sort((a,b)->Integer.compare(b.length(),a.length()));
        for(String key:keys)value=value.replace(key,UiTextCatalog.CHINESE.get(key));
        return value;
    }
    static String t(String korean){
        if(!"zh-Hans".equals(language))return korean;
        String translated=UiTextCatalog.CHINESE.get(korean);
        return translated==null?korean:translated;
    }
}
