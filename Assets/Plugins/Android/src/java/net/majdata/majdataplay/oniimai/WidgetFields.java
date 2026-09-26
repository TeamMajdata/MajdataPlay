package net.majdata.majdataplay.oniimai;

import java.util.Objects;

/** A time tick must not invalidate song artwork, judgments, connection status or the clock. */
final class WidgetFields {
    interface Values { Object get(String key); }
    private static final String[] SONG = {"title", "artist", "level"};
    private static final String[] SCORE = {"achievement", "combo", "dx"};
    private static final String[] JUDGMENTS = {"critical", "perfect", "great", "good", "miss"};
    private static final String[] TIMING = {"fast", "late"};
    private static final String[] GRAPH = {"seconds", "length"};
    static boolean changed(String type, Values before, Values after) {
        String[] fields;
        switch (type) {
            case "song": fields = SONG; break;
            case "score": fields = SCORE; break;
            case "judgments": fields = JUDGMENTS; break;
            case "timing": fields = TIMING; break;
            case "graph": fields = GRAPH; break;
            default: return false;
        }
        if (!Objects.equals(before.get("game"), after.get("game"))
                || !Objects.equals(before.get("scene"), after.get("scene"))) return true;
        for (String key : fields) if (!Objects.equals(before.get(key), after.get(key))) return true;
        return false;
    }
}
