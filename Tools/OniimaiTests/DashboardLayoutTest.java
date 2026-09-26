package net.majdata.majdataplay.oniimai;

import java.util.List;

public final class DashboardLayoutTest {
    private static int checks;

    private static void check(boolean condition, String message) {
        checks++;
        if (!condition) throw new AssertionError(message);
    }

    private static void same(String expected, String actual, String message) {
        check(expected.equals(actual), message + "\nExpected: " + expected + "\nActual: " + actual);
    }

    private static String data(String records) { return "oniimai-dashboard-v2|1\n" + records; }
    private static String legacy(String records) { return "oniimai-dashboard-v1|1\n" + records; }

    private static void size(String type, float width, float height, int expectedW, int expectedH) {
        int[] size = DashboardLayout.nearestSize(type, width, height);
        check(size != null && size[0] == expectedW && size[1] == expectedH,
                "Snap " + type + " " + width + "x" + height + " to " + expectedW + "x" + expectedH);
    }

    private static void valid(DashboardLayout layout) {
        List<DashboardLayout.Item> items = layout.items();
        for (int i = 0; i < items.size(); i++) {
            DashboardLayout.Item a = items.get(i);
            check(DashboardLayout.validSize(a.type, a.w, a.h) && a.x >= 0 && a.y >= 0
                    && a.x + a.w <= DashboardLayout.COLS && a.y + a.h <= DashboardLayout.MAX_ROWS,
                    "Widget " + a.id + " has supported size and in-bounds placement");
            for (int j = 0; j < i; j++) {
                DashboardLayout.Item b = items.get(j);
                check(a.id != b.id && (a.x + a.w <= b.x || b.x + b.w <= a.x
                        || a.y + a.h <= b.y || b.y + b.h <= a.y), "Distinct widgets do not overlap");
            }
        }
    }

    public static void main(String[] args) {
        DashboardLayout layout = DashboardLayout.defaults();
        String defaults = layout.encode();
        check(layout.items().size() == 8 && layout.rows() == 10, "Photo dashboard uses eight widgets and ten square-cell rows");
        String photoDefaults = "oniimai-dashboard-v2|9\n1|song|0|0|2|2\n2|score|2|0|2|2\n"
                + "3|graph|0|2|2|2\n4|judgments|2|2|2|2\n5|sensors|0|4|2|2\n6|timing|2|4|2|2\n"
                + "7|connection|0|6|4|2\n8|clock|0|8|4|2";
        same(photoDefaults, defaults, "Default arrangement matches the supplied photo");
        same(photoDefaults, DashboardLayout.parse(null).encode(), "First launch uses photo arrangement");
        check(layout.items().stream().map(item -> item.type).distinct().count() == DashboardLayout.types().size(),
                "Photo defaults include all eight widget types exactly once");
        String previousPhoto = "oniimai-dashboard-v2|8\n1|song|0|0|4|2\n2|graph|0|2|2|2\n"
                + "3|judgments|2|2|2|2\n4|sensors|0|4|2|2\n5|timing|2|4|2|2\n"
                + "6|connection|0|6|4|2\n7|clock|0|8|4|2";
        same(previousPhoto, DashboardLayout.parse(previousPhoto).encode(),
                "Saved seven-widget photo arrangement is not replaced by the new eight-widget defaults");
        String previousDefaults = "oniimai-dashboard-v2|7\n1|song|0|0|2|2\n2|score|2|0|2|2\n"
                + "3|timing|0|2|2|2\n4|judgments|2|2|2|2\n5|sensors|0|4|2|2\n6|connection|2|4|2|2";
        same(previousDefaults, DashboardLayout.parse(previousDefaults).encode(),
                "Saved previous default or custom arrangement is not overwritten by new defaults");
        check(defaults.startsWith("oniimai-dashboard-v2|"), "New layouts persist as version 2");
        same(defaults, DashboardLayout.parse(defaults).encode(), "Persist every default coordinate and size");
        check(DashboardLayout.types().size() == 8, "Eight available widget types");
        for (String type : DashboardLayout.types()) {
            check(DashboardLayout.validSize(type, DashboardLayout.defaultWidth(type), DashboardLayout.defaultHeight(type)),
                    "Supported default size for " + type);
            check(DashboardLayout.validSize(type, 2, 2), "Every widget supports 2x2: " + type);
            DashboardLayout.Item added = DashboardLayout.empty().add(type);
            check(added != null && added.w == 2 && added.h == 2, "New widgets all start at 2x2: " + type);
            for (int[] supported : DashboardLayout.sizes(type))
                check(DashboardLayout.validSize(type, supported[0], supported[1]), "Size picker only offers supported dimensions");
            check(!DashboardLayout.validSize(type, 3, 3) && !DashboardLayout.validSize(type, 4, 3)
                    && !DashboardLayout.validSize(type, 2, 4), "Arbitrary dimensions rejected for " + type);
        }
        check(DashboardLayout.sizes(DashboardLayout.SENSORS).length == 2
                && DashboardLayout.validSize(DashboardLayout.SENSORS, 2, 2)
                && !DashboardLayout.validSize(DashboardLayout.SENSORS, 4, 2), "Sensor board offers only square 2x2 and 4x4 sizes");
        check(DashboardLayout.validSize(DashboardLayout.JUDGMENTS, 4, 4)
                && !DashboardLayout.validSize(DashboardLayout.SCORE, 4, 4), "Only judgments and sensors offer large square cards");
        int[][] mutableSizes = DashboardLayout.sizes(DashboardLayout.SENSORS);
        mutableSizes[0][0] = 1;
        check(DashboardLayout.sizes(DashboardLayout.SENSORS)[0][0] == 2, "Size option arrays are defensive copies");
        check(DashboardLayout.minWidth("unknown") == 0 && !DashboardLayout.knownType(null)
                && DashboardLayout.sizes(null).length == 0 && DashboardLayout.nearestSize(null, 2, 2) == null,
                "Unknown types have no dimensions or nearest size");
        size(DashboardLayout.SCORE, 2.4f, 3.6f, 2, 2);
        size(DashboardLayout.SCORE, 3.6f, 3.6f, 4, 2);
        size(DashboardLayout.JUDGMENTS, 4, 3, 4, 2);
        size(DashboardLayout.JUDGMENTS, 4, 3.01f, 4, 4);
        size(DashboardLayout.SENSORS, 2, 2, 2, 2);
        size(DashboardLayout.SENSORS, 3, 3, 2, 2);
        size(DashboardLayout.SENSORS, 4, 5, 4, 4);
        size(DashboardLayout.CLOCK, Float.NaN, 2, 2, 2);
        size(DashboardLayout.SONG, 4, Float.POSITIVE_INFINITY, 2, 2);
        valid(layout);

        check(!layout.move(1, 0, 2), "Moving over another widget is rejected");
        check(!layout.resize(3, 4, 2), "Resizing over adjacent judgment widget is rejected");
        check(!layout.resize(5, 2, 3), "Sensor map rejects non-square sizes");
        check(!layout.move(1, Integer.MAX_VALUE, 0) && !layout.move(1, 0, Integer.MAX_VALUE), "Huge coordinates cannot overflow bounds");
        check(!layout.move(1, -1, 0) && !layout.move(1, 0, -1), "Negative coordinates rejected");
        check(!layout.resize(1, Integer.MAX_VALUE, Integer.MAX_VALUE), "Huge sizes cannot overflow bounds");
        check(!layout.move(999, 0, 0) && !layout.resize(999, 2, 2) && !layout.remove(999), "Missing widget mutations rejected");
        same(defaults, layout.encode(), "Rejected mutations preserve exact layout");
        check(layout.move(1, 0, 20), "Freeform widget move succeeds");
        check(layout.rows() == 22, "Rows include intentionally empty vertical space");
        check(layout.resize(1, 2, 2), "Supported resize succeeds in unoccupied space");
        String positioned = layout.encode();
        DashboardLayout recreated = DashboardLayout.parse(positioned);
        same(positioned, recreated.encode(), "Device rotation or Activity recreation preserves logical cells without pixel dimensions");
        check(recreated.get(1).x == 0 && recreated.get(1).y == 20 && recreated.get(1).w == 2
                && recreated.get(1).h == 2, "Persisted custom position and canonical size restored");

        List<DashboardLayout.Item> snapshot = layout.items();
        DashboardLayout copy = layout.copy();
        check(copy.remove(1) && layout.get(1) != null, "Editing a copy cannot mutate original saved layout");
        boolean immutable = false;
        try { snapshot.clear(); } catch (UnsupportedOperationException expected) { immutable = true; }
        check(immutable, "Caller cannot mutate exposed item list");
        layout.remove(2);
        check(snapshot.size() == 8 && layout.items().size() == 7, "Published list remains a stable snapshot");

        DashboardLayout holes = DashboardLayout.defaults();
        check(holes.remove(3), "Delete graph widget");
        DashboardLayout.Item replacement = holes.add(DashboardLayout.CLOCK);
        check(replacement != null && replacement.x == 0 && replacement.y == 2, "Add fills first available hole, not the bottom");
        check(replacement.id > 8, "Deleted IDs are not immediately reused");
        DashboardLayout.Item secondClock = holes.add(DashboardLayout.CLOCK);
        check(secondClock != null && secondClock.id != replacement.id, "Duplicate widget types retain distinct stable IDs");
        holes.remove(secondClock.id);
        DashboardLayout restored = DashboardLayout.parse(holes.encode());
        check(restored.add(DashboardLayout.CLOCK).id > secondClock.id, "Persistence remembers IDs even after most recent widget was removed");
        check(holes.add("not-a-widget") == null && holes.add(null) == null, "Cannot add unknown widget types");

        DashboardLayout empty = DashboardLayout.empty();
        check(empty.rows() == 0 && DashboardLayout.parse(empty.encode()).items().isEmpty(), "Deliberately empty dashboard remains empty");
        check(DashboardLayout.parse("oniimai-dashboard-v1|25").items().isEmpty(), "Intentionally empty version 1 layout remains empty");
        empty.add(DashboardLayout.CLOCK);
        empty.clear();
        check(DashboardLayout.parse(empty.encode()).add(DashboardLayout.CLOCK).id == 2, "Clear does not rewind stable IDs");
        empty.reset();
        same(defaults, empty.encode(), "Explicit reset restores all defaults");

        for (String invalid : new String[] {null, "", "garbage", "oniimai-dashboard-v3|1", "oniimai-dashboard-v1|0",
                "oniimai-dashboard-v1|-1", "oniimai-dashboard-v1|9223372036854775807", "oniimai-dashboard-v1|nope",
                data("1|unknown|0|0|4|2"), data("1|song|0|0|0|2"), data("1|song|0|0|4|999999999999999999999"),
                data("1|song|0|0|3|2"), legacy("1|sensors|0|0|2|3")}) {
            same(defaults, DashboardLayout.parse(invalid).encode(), "Wholly corrupt saved data recovers defaults");
        }
        String mixed = data("1|song|0|0|4|2\n"
                + "1|clock|0|10|2|2\n" // Duplicate identity.
                + "2|score|0|0|2|2\n" // Overlapping geometry.
                + "3|unknown|0|4|2|2\n"
                + "4|clock|-1|4|2|2\n"
                + "5|clock|0|63|2|2\n"
                + "6|clock|0|4|2|NaN\n"
                + "-7|clock|0|4|2|2\n"
                + "8|clock|0|4|2|2\n"
                + "9|clock|0|10|2|3\n");
        DashboardLayout recovered = DashboardLayout.parse(mixed);
        check(recovered.items().size() == 2 && recovered.get(1) != null && recovered.get(8) != null,
                "Mixed corruption keeps valid non-overlapping widgets only");
        check(recovered.add(DashboardLayout.CLOCK).id == 9, "Stale next ID repaired from restored IDs");
        same(defaults, DashboardLayout.parse(defaults.replace("\n", "\r\n")).encode(), "Windows line endings accepted");
        same(defaults, DashboardLayout.parse("x".repeat(16385)).encode(), "Overlarge preference data is bounded");

        String oldDefaults = "oniimai-dashboard-v1|7\n1|song|0|0|4|2\n2|score|0|2|2|2\n"
                + "3|timing|2|2|2|2\n4|judgments|0|4|4|3\n5|sensors|0|7|4|5\n6|connection|0|12|4|2";
        DashboardLayout migrated = DashboardLayout.parse(oldDefaults);
        check(migrated.items().size() == 6 && migrated.get(4).h == 2 && migrated.get(5).h == 4,
                "Existing 0.2.4 defaults migrate judgment and sensor sizes without losing widgets");
        check(migrated.get(5).y == 7 && migrated.get(6).y == 12, "Migration preserves saved positions when resized widgets already fit");
        check(migrated.add(DashboardLayout.CLOCK).id == 7, "Migration preserves the next identity");
        valid(migrated);
        String compatible = data("8|clock|1|19|2|2\n10|score|0|29|4|2");
        same(DashboardLayout.parse(compatible).encode(), DashboardLayout.parse(compatible.replace("v2|", "v1|")).encode(),
                "Compatible legacy positions including odd cells and intentional gaps are restored exactly");
        DashboardLayout shiftedSensor = DashboardLayout.parse(legacy("51|sensors|1|60|3|4"));
        check(shiftedSensor.items().size() == 1 && shiftedSensor.get(51).x == 0 && shiftedSensor.get(51).y == 0
                && shiftedSensor.get(51).w == 4 && shiftedSensor.get(51).h == 4,
                "Legacy 3-column sensor snaps to 4x4 and repacks when expansion would leave the grid");
        String mixedLegacy = "oniimai-dashboard-v1|99\n1|song|0|0|4|3\n2|score|0|3|2|3\n"
                + "3|timing|2|3|2|3\n4|judgments|0|6|4|3\n5|sensors|1|9|3|5\n"
                + "6|clock|0|14|2|3\n7|clock|2|14|2|3";
        DashboardLayout migratedPacked = DashboardLayout.parse(mixedLegacy);
        check(migratedPacked.items().size() == 7 && migratedPacked.rows() == 12,
                "Mixed arbitrary legacy sizes snap and repack into a complete canonical layout");
        for (int id = 1; id <= 7; id++)
            check(migratedPacked.get(id) != null && migratedPacked.items().get(id - 1).id == id, "Migration retains stable ID and stored order " + id);
        check(DashboardLayout.CLOCK.equals(migratedPacked.get(6).type) && DashboardLayout.CLOCK.equals(migratedPacked.get(7).type),
                "Migration retains duplicate widget types independently");
        valid(migratedPacked);
        same(migratedPacked.encode(), DashboardLayout.parse(migratedPacked.encode()).encode(), "Migrated v2 layout remains stable across reloads");
        check(migratedPacked.add(DashboardLayout.CLOCK).id == 99, "Migration preserves previously removed widget IDs");
        DashboardLayout legacyCorruption = DashboardLayout.parse(legacy("2|clock|0|0|2|3\n2|clock|2|0|2|3\n"
                + "3|clock|0|0|2|3\n4|clock|2|0|2|3\n5|sensors|0|20|2|4"));
        check(legacyCorruption.items().size() == 2 && legacyCorruption.get(2) != null && legacyCorruption.get(4) != null,
                "Migration still rejects duplicate IDs, original overlaps, and invalid legacy geometry");

        DashboardLayout limit = DashboardLayout.empty();
        for (int i = 0; i < DashboardLayout.MAX_ITEMS; i++) check(limit.add(DashboardLayout.CLOCK) != null, "Add up to widget limit " + i);
        String atLimit = limit.encode();
        check(limit.add(DashboardLayout.CLOCK) == null, "Widget count limit enforced");
        same(atLimit, limit.encode(), "Failed add does not consume an ID");
        check(DashboardLayout.parse(atLimit + "\n100|clock|0|40|2|2").items().size() == DashboardLayout.MAX_ITEMS,
                "Parser also enforces widget count limit");
        DashboardLayout full = DashboardLayout.empty();
        for (int i = 0; i < 16; i++) {
            DashboardLayout.Item sensor = full.add(DashboardLayout.SENSORS);
            check(sensor != null && full.resizeAndPack(sensor.id, 4, 4), "Fill sensor grid with explicit 4x4 tiles " + i);
        }
        String fullBefore = full.encode();
        check(full.add(DashboardLayout.CLOCK) == null, "Adding to completely occupied grid fails without overlap");
        same(fullBefore, full.encode(), "Adding beyond grid bounds preserves exact layout and next ID");
        DashboardLayout exhausted = DashboardLayout.parse(data("2147483647|clock|0|0|2|2"));
        check(exhausted.get(Integer.MAX_VALUE) != null && exhausted.add(DashboardLayout.CLOCK) == null,
                "Exhausted positive ID range cannot wrap or duplicate");
        same(exhausted.encode(), DashboardLayout.parse(exhausted.encode()).encode(), "Maximum identity persists safely");

        DashboardLayout packed = DashboardLayout.defaults();
        packed.move(1, 0, 30);
        check(packed.pack(), "Compaction succeeds for displaced defaults");
        same(defaults, packed.encode(), "Compaction uses stable order and original sizes");
        check(packed.moveBy(4, -1), "Accessible reorder moves widget earlier");
        check(packed.items().get(2).id == 4 && packed.get(4).x == 0 && packed.get(4).y == 2 && packed.get(3).x == 2 && packed.get(3).y == 2,
                "Accessible reorder exchanges side-by-side widgets and repacks coordinates");
        check(!packed.moveBy(1, -1) && !packed.moveBy(8, 1) && !packed.moveBy(2, 0) && !packed.moveBy(99, 1),
                "Invalid and boundary reorder requests are rejected");
        check(packed.moveBy(4, 1), "Accessible reorder can be reversed");
        same(defaults, packed.encode(), "Reversed reorder restores layout");

        String existingLarge = "oniimai-dashboard-v2|51\n9|sensors|0|0|4|4\n20|song|0|5|4|2\n"
                + "25|sensors|0|10|4|4\n41|clock|2|20|2|2";
        DashboardLayout compacted = DashboardLayout.parse(existingLarge);
        same(existingLarge, compacted.encode(), "Existing v2 large widgets retain saved sizes and positions");
        check(compacted.compact(), "Explicit compact action resizes and packs all existing widgets");
        check(compacted.items().size() == 4 && compacted.rows() == 4, "Compact layout uses two 2x2 tiles per row");
        int[] compactIds = {9, 20, 25, 41};
        String[] compactTypes = {DashboardLayout.SENSORS, DashboardLayout.SONG, DashboardLayout.SENSORS, DashboardLayout.CLOCK};
        for (int i = 0; i < compactIds.length; i++) {
            DashboardLayout.Item item = compacted.items().get(i);
            check(item.id == compactIds[i] && item.type.equals(compactTypes[i]) && item.w == 2 && item.h == 2
                    && item.x == (i % 2) * 2 && item.y == (i / 2) * 2,
                    "Compaction preserves identity, type, duplicate widgets, and stored order " + i);
        }
        String compactOnce = compacted.encode();
        check(compacted.compact(), "Repeated compaction succeeds");
        same(compactOnce, compacted.encode(), "Compaction is idempotent");
        same(compactOnce, DashboardLayout.parse(compactOnce).encode(), "Compact sensor sizes persist across reloads");
        check(compacted.add(DashboardLayout.SENSORS).id == 51, "Compaction preserves next identity");
        DashboardLayout compactEmpty = DashboardLayout.parse("oniimai-dashboard-v2|19");
        check(compactEmpty.compact() && compactEmpty.items().isEmpty(), "Compact preserves intentionally empty dashboard");
        check(compactEmpty.add(DashboardLayout.SONG).id == 19, "Empty compaction preserves next identity");
        valid(compacted);

        DashboardLayout resizePacked = DashboardLayout.parse(data("1|score|0|0|2|2\n2|timing|2|0|2|2"));
        check(!resizePacked.resize(1, 4, 2), "Wider replacement overlaps current neighbor");
        check(resizePacked.resizeAndPack(1, 4, 2), "Resize packs the desired dimensions without requiring temporary room for old dimensions");
        check(resizePacked.get(1).w == 4 && resizePacked.get(1).h == 2 && resizePacked.get(1).y == 0
                && resizePacked.get(2).y == 2 && resizePacked.get(2).x == 0, "Atomic resize preserves identities and moves the neighboring widget");
        valid(resizePacked);
        String resized = resizePacked.encode();
        check(!resizePacked.resizeAndPack(1, 4, 3), "Resize to non-canonical dimensions is rejected");
        same(resized, resizePacked.encode(), "Failed resize and pack preserves every rectangle");
        check(!resizePacked.resizeAndPack(999, 4, 2) && !resizePacked.resizeAndPack(1, 1, 2)
                && !resizePacked.resizeAndPack(1, Integer.MAX_VALUE, 2), "Atomic resize validates identity, readability, and bounds");
        same(resized, resizePacked.encode(), "Invalid atomic resize has no side effects");
        DashboardLayout resizeInPlace = DashboardLayout.empty();
        int resizeId = resizeInPlace.add(DashboardLayout.SCORE).id;
        resizeInPlace.move(resizeId, 0, 20);
        check(resizeInPlace.resizeAndPack(resizeId, 4, 2) && resizeInPlace.get(resizeId).x == 0
                && resizeInPlace.get(resizeId).y == 20, "Non-overlapping resize preserves user placement rather than packing unnecessarily");

        StringBuilder occupied = new StringBuilder();
        for (int id = 1; id <= 15; id++) occupied.append(id).append("|sensors|0|").append((id - 1) * 4).append("|4|4\n");
        occupied.append("16|judgments|0|60|2|2\n17|judgments|2|60|2|2\n18|judgments|0|62|2|2\n19|judgments|2|62|2|2");
        DashboardLayout resizeFailure = DashboardLayout.parse(data(occupied.toString()));
        check(resizeFailure.rows() == 64 && resizeFailure.items().size() == 19, "Canonical widgets can fill the entire grid");
        String resizeBefore = resizeFailure.encode();
        check(!resizeFailure.resizeAndPack(16, 4, 4), "Resize rejects dimensions that cannot fit all existing widgets");
        same(resizeBefore, resizeFailure.encode(), "Capacity failure preserves all identities, coordinates, sizes, order, and next ID atomically");
        resizeFailure.remove(18);
        resizeFailure.remove(19);
        check(resizeFailure.resizeAndPack(16, 4, 2) && resizeFailure.get(16).y == 60 && resizeFailure.get(17).y == 62,
                "Freeing enough space permits the same overlapping resize without losing other widgets");
        valid(resizeFailure);
        System.out.println("DashboardLayout: " + checks + " checks passed");
    }
}
