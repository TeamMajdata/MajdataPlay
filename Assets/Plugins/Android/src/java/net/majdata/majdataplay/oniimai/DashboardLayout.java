package net.majdata.majdataplay.oniimai;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.List;

/** Persisted logical cells, independent of pixels, orientation, and the Android view lifecycle. */
final class DashboardLayout {
    static final int COLS = 4;
    static final int MAX_ROWS = 64;
    static final int MAX_ITEMS = 20;
    static final String SONG = "song", SCORE = "score", JUDGMENTS = "judgments",
            TIMING = "timing", GRAPH = "graph", SENSORS = "sensors",
            CONNECTION = "connection", CLOCK = "clock";
    private static final String HEADER = "oniimai-dashboard-v2";
    private static final String LEGACY_HEADER = "oniimai-dashboard-v1";
    private static final List<String> TYPES = Collections.unmodifiableList(Arrays.asList(
            SONG, SCORE, JUDGMENTS, TIMING, GRAPH, SENSORS, CONNECTION, CLOCK));

    static final class Item {
        final int id, x, y, w, h;
        final String type;

        Item(int id, String type, int x, int y, int w, int h) {
            this.id = id;
            this.type = type;
            this.x = x;
            this.y = y;
            this.w = w;
            this.h = h;
        }
    }

    private final ArrayList<Item> entries = new ArrayList<>();
    // A long prevents overflow when reading an otherwise valid highest possible integer ID.
    private long nextId = 1;

    private DashboardLayout() {}

    static DashboardLayout empty() { return new DashboardLayout(); }

    static DashboardLayout defaults() {
        DashboardLayout layout = empty();
        layout.entries.add(new Item(1, SONG, 0, 0, 2, 2));
        layout.entries.add(new Item(2, SCORE, 2, 0, 2, 2));
        layout.entries.add(new Item(3, GRAPH, 0, 2, 2, 2));
        layout.entries.add(new Item(4, JUDGMENTS, 2, 2, 2, 2));
        layout.entries.add(new Item(5, SENSORS, 0, 4, 2, 2));
        layout.entries.add(new Item(6, TIMING, 2, 4, 2, 2));
        layout.entries.add(new Item(7, CONNECTION, 0, 6, 4, 2));
        layout.entries.add(new Item(8, CLOCK, 0, 8, 4, 2));
        layout.nextId = 9;
        return layout;
    }

    static List<String> types() { return TYPES; }
    static boolean knownType(String type) { return TYPES.contains(type); }
    /** Fresh arrays keep callers from changing the supported widget sizes. */
    static int[][] sizes(String type) {
        if (!knownType(type)) return new int[0][];
        if (SENSORS.equals(type)) return new int[][] {{2, 2}, {4, 4}};
        if (JUDGMENTS.equals(type)) return new int[][] {{2, 2}, {4, 2}, {4, 4}};
        return new int[][] {{2, 2}, {4, 2}};
    }

    static boolean validSize(String type, int width, int height) {
        if (!knownType(type)) return false;
        if (SENSORS.equals(type)) return (width == 2 && height == 2) || (width == 4 && height == 4);
        return (width == 2 && height == 2) || (width == 4 && height == 2)
                || (JUDGMENTS.equals(type) && width == 4 && height == 4);
    }

    /** Snap a gesture or legacy rectangle to the closest supported size, preferring smaller ties. */
    static int[] nearestSize(String type, float width, float height) {
        int[][] available = sizes(type);
        if (available.length == 0) return null;
        if (Float.isNaN(width) || Float.isInfinite(width) || Float.isNaN(height) || Float.isInfinite(height))
            return new int[] {defaultWidth(type), defaultHeight(type)};
        int[] nearest = available[0];
        double distance = Double.POSITIVE_INFINITY;
        for (int[] size : available) {
            double dx = (double)width - size[0], dy = (double)height - size[1];
            double candidate = dx * dx + dy * dy;
            if (candidate < distance) {
                nearest = size;
                distance = candidate;
            }
        }
        return nearest;
    }

    static int minWidth(String type) { return !knownType(type) ? 0 : 2; }
    static int minHeight(String type) { return !knownType(type) ? 0 : 2; }
    static int defaultWidth(String type) { return minWidth(type); }
    static int defaultHeight(String type) { return minHeight(type); }

    /** Invalid records are skipped without losing good widgets. A wholly corrupt layout resets. */
    static DashboardLayout parse(String saved) {
        if (saved == null || saved.length() > 16384) return defaults();
        String[] lines = saved.trim().split("\\r?\\n", -1);
        String[] header = lines[0].split("\\|", -1);
        if (header.length != 2 || (!HEADER.equals(header[0]) && !LEGACY_HEADER.equals(header[0]))) return defaults();
        boolean legacy = LEGACY_HEADER.equals(header[0]);
        DashboardLayout layout = empty();
        try {
            layout.nextId = Long.parseLong(header[1]);
            if (layout.nextId < 1 || layout.nextId > (long)Integer.MAX_VALUE + 1) return defaults();
        } catch (NumberFormatException error) {
            return defaults();
        }
        boolean hadRecords = false;
        for (int line = 1; line < lines.length; line++) {
            if (lines[line].trim().isEmpty()) continue;
            hadRecords = true;
            String[] fields = lines[line].split("\\|", -1);
            if (fields.length != 6 || layout.entries.size() >= MAX_ITEMS) continue;
            try {
                Item item = new Item(Integer.parseInt(fields[0]), fields[1],
                        Integer.parseInt(fields[2]), Integer.parseInt(fields[3]),
                        Integer.parseInt(fields[4]), Integer.parseInt(fields[5]));
                if (item.id <= 0 || layout.get(item.id) != null
                        || !(legacy ? fitsLegacy(item, layout.entries) : fits(item, layout.entries, -1))) continue;
                layout.entries.add(item);
                layout.nextId = Math.max(layout.nextId, (long)item.id + 1);
            } catch (NumberFormatException ignored) {
                // Keep the remaining good records after a damaged preference value.
            }
        }
        if (hadRecords && layout.entries.isEmpty()) return defaults();
        if (legacy) layout.migrateSizes();
        return layout;
    }

    /** Preserve valid saved positions; only a collision or new bounds violation requires packing. */
    private void migrateSizes() {
        ArrayList<Item> migrated = new ArrayList<>();
        boolean needsPacking = false;
        for (Item item : entries) {
            int[] size = nearestSize(item.type, item.w, item.h);
            Item next = new Item(item.id, item.type, item.x, item.y, size[0], size[1]);
            if (!fits(next, migrated, -1)) needsPacking = true;
            migrated.add(next);
        }
        if (needsPacking && packInOrder(migrated)) return;
        entries.clear();
        entries.addAll(migrated);
    }

    String encode() {
        StringBuilder output = new StringBuilder(HEADER).append('|').append(nextId);
        for (Item item : entries) {
            output.append('\n').append(item.id).append('|').append(item.type).append('|')
                    .append(item.x).append('|').append(item.y).append('|')
                    .append(item.w).append('|').append(item.h);
        }
        return output.toString();
    }

    DashboardLayout copy() {
        DashboardLayout copy = empty();
        copy.entries.addAll(entries); // Items are immutable, so editors cannot mutate the saved layout.
        copy.nextId = nextId;
        return copy;
    }

    List<Item> items() { return Collections.unmodifiableList(new ArrayList<>(entries)); }

    Item get(int id) {
        for (Item item : entries) if (item.id == id) return item;
        return null;
    }

    Item add(String type) {
        if (!knownType(type) || entries.size() >= MAX_ITEMS || nextId > Integer.MAX_VALUE) return null;
        Item item = firstFree((int)nextId, type, defaultWidth(type), defaultHeight(type), entries);
        if (item == null) return null;
        entries.add(item);
        nextId++;
        return item;
    }

    boolean remove(int id) {
        Item item = get(id);
        return item != null && entries.remove(item);
    }

    boolean move(int id, int x, int y) {
        Item previous = get(id);
        return previous != null && replace(previous, new Item(id, previous.type, x, y, previous.w, previous.h));
    }

    boolean resize(int id, int width, int height) {
        Item previous = get(id);
        return previous != null && replace(previous, new Item(id, previous.type, previous.x, previous.y, width, height));
    }

    /** Resize in place when possible, otherwise atomically pack the desired rectangles in order. */
    boolean resizeAndPack(int id, int width, int height) {
        Item previous = get(id);
        if (previous == null || !fits(new Item(id, previous.type, 0, 0, width, height), Collections.emptyList(), -1)) return false;
        if (resize(id, width, height)) return true;
        ArrayList<Item> ordered = new ArrayList<>(entries);
        ordered.set(entries.indexOf(previous), new Item(id, previous.type, previous.x, previous.y, width, height));
        return packInOrder(ordered);
    }

    private boolean replace(Item previous, Item next) {
        if (!fits(next, entries, previous.id)) return false;
        entries.set(entries.indexOf(previous), next);
        return true;
    }

    /** Pack in stored order; commit only if all existing widgets fit with their current sizes. */
    boolean pack() { return packInOrder(entries); }

    /** Explicit compact preset; existing saved sizes change only when the user requests it. */
    boolean compact() {
        ArrayList<Item> compacted = new ArrayList<>();
        for (Item item : entries)
            compacted.add(new Item(item.id, item.type, item.x, item.y, 2, 2));
        return packInOrder(compacted);
    }

    /** Accessible one-step reorder; drag uses move() and preserves the stored order. */
    boolean moveBy(int id, int direction) {
        Item item = get(id);
        if (item == null || direction == 0) return false;
        int index = entries.indexOf(item);
        int destination = index + (direction < 0 ? -1 : 1);
        if (destination < 0 || destination >= entries.size()) return false;
        ArrayList<Item> ordered = new ArrayList<>(entries);
        Collections.swap(ordered, index, destination);
        return packInOrder(ordered);
    }

    private boolean packInOrder(List<Item> ordered) {
        ArrayList<Item> packed = new ArrayList<>();
        for (Item item : ordered) {
            Item next = firstFree(item.id, item.type, item.w, item.h, packed);
            if (next == null) return false;
            packed.add(next);
        }
        entries.clear();
        entries.addAll(packed);
        return true;
    }

    void clear() { entries.clear(); }

    void reset() {
        DashboardLayout initial = defaults();
        entries.clear();
        entries.addAll(initial.entries);
        nextId = initial.nextId;
    }

    int rows() {
        int rows = 0;
        for (Item item : entries) rows = Math.max(rows, item.y + item.h);
        return rows;
    }

    private static Item firstFree(int id, String type, int width, int height, List<Item> occupied) {
        if (!validSize(type, width, height)) return null;
        for (int y = 0; y <= MAX_ROWS - height; y++) {
            for (int x = 0; x <= COLS - width; x++) {
                Item item = new Item(id, type, x, y, width, height);
                if (fits(item, occupied, -1)) return item;
            }
        }
        return null;
    }

    private static boolean fits(Item item, List<Item> occupied, int ignoredId) {
        if (!validSize(item.type, item.w, item.h)
                || item.x < 0 || item.x > COLS - item.w || item.y < 0 || item.y > MAX_ROWS - item.h) return false;
        return doesNotOverlap(item, occupied, ignoredId);
    }

    /** Validate old data before resizing it, so corruption cannot turn into phantom widgets. */
    private static boolean fitsLegacy(Item item, List<Item> occupied) {
        int minW = SENSORS.equals(item.type) ? 3 : 2;
        int minH = SENSORS.equals(item.type) ? 4 : JUDGMENTS.equals(item.type) ? 3 : 2;
        if (!knownType(item.type) || item.w < minW || item.w > COLS || item.h < minH || item.h > MAX_ROWS
                || item.x < 0 || item.x > COLS - item.w || item.y < 0 || item.y > MAX_ROWS - item.h) return false;
        return doesNotOverlap(item, occupied, -1);
    }

    private static boolean doesNotOverlap(Item item, List<Item> occupied, int ignoredId) {
        for (Item other : occupied) {
            if (other.id != ignoredId && item.x < other.x + other.w && item.x + item.w > other.x
                    && item.y < other.y + other.h && item.y + item.h > other.y) return false;
        }
        return true;
    }
}
